using System;
using System.Linq;
using System.Threading.Tasks;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.framework.audio;
using LibraryOfRuina.framework.intents;
using LibraryOfRuina.framework.monsters;
using LibraryOfRuina.framework.relics;
using LibraryOfRuina.infra.helpers;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;

namespace LibraryOfRuina.content.abnormalities.CosmicFragment;

public sealed class CosmicFragment : LorMonsterModel
{
    public const string IdleTexturePath = "res://images/monsters/cosmic_fragment/cosmic_fragment_idle.png";
    public const string AttackTexturePath = "res://images/monsters/cosmic_fragment/cosmic_fragment_attack.png";
    public const string Attack2TexturePath = "res://images/monsters/cosmic_fragment/cosmic_fragment_attack2.png";
    public const string HitTexturePath = "res://images/monsters/cosmic_fragment/cosmic_fragment_hit.png";
    // Spine 身体（tools/spine_from_sprite 生成）：骨骼与图集以原始文件打进 PCK，运行时按路径加载
    internal const string SpineAtlasPath = "res://images/monsters/cosmic_fragment/cosmic_fragment.atlas";
    internal const string SpineSkeletonPath = "res://images/monsters/cosmic_fragment/cosmic_fragment.spine-json";
    public const string AttackSfxPath = "res://audio/sfx/cosmic_fragment/cosmic_fragment_hit.ogg";
    public const string SingingSfxPath = "res://audio/sfx/cosmic_fragment/cosmic_fragment_singing.ogg";
    public const string EchoAttackSfxPath = "res://audio/sfx/cosmic_fragment/cosmic_fragment_echo_attack.ogg";

    private static readonly string CosmicFragmentPageRelicTitleLocKey =
        $"{ModelDb.GetId<CosmicFragmentPageRelic>().Entry}.title";

    private const int PenetrateEpiphany = 2; // 穿透：攻击后加入被攻击玩家抽牌堆的顿悟张数，各进阶相同。
    private const int EchoHits = 3; // 异界的回响：攻击次数（单次伤害见 EchoDmg），各进阶相同。
    private const int EchoInitialStrengthGain = 2; // 异界的回响：首次攻击后自身获得的力量，各进阶相同。
    private const long EchoStrengthGainMultiplier = 2; // 异界的回响：每次结算后下一次力量获得量的倍率，各进阶相同。

    public override int MinInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 151, 149);

    public override int MaxInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 155, 152);

    private int _nextEchoStrengthGain = EchoInitialStrengthGain;

    public override int DefaultChaoResistance => 120;

    public override LibraryCreatureResistanceData.Resistance? DefaultPhysicalResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Endure,
        Pierce = LibraryResistanceLevel.Endure,
        Blunt = LibraryResistanceLevel.Endure
    };

    public override LibraryCreatureResistanceData.Resistance? DefaultChaoResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Vulnerable,
        Pierce = LibraryResistanceLevel.Vulnerable,
        Blunt = LibraryResistanceLevel.Vulnerable
    };

    private int PenetrateDmg =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 19, 17);

    private int EchoDmg =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 8, 6);

    public override IEnumerable<string> AssetPaths
    {
        get
        {
            var paths = new List<string>(
                CosmicFragmentCreatureVisuals.Profile.AssetPaths)
            {
                AttackSfxPath,
                SingingSfxPath,
                EchoAttackSfxPath
            };

            foreach (AbstractIntent intent in EnumerateIntentAssets())
            {
                paths.AddRange(intent.AssetPaths);
            }

            return paths.Distinct();
        }
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        SaveManager.Instance.MarkCardAsSeen(ModelDb.Card<CosmicFragmentEpiphanyCard>());
        EncounterBgmController.RegisterMonster(Creature);
    }

    public override async Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature creature,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (wasRemovalPrevented || creature != Creature)
        {
            return;
        }

        await ClearEpiphanyCards(creature);
        AddCosmicFragmentPageRewardsFromDeathHook(creature);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var penetrate = new MoveState(
            "PENETRATE",
            PenetrateMove,
            new SingleAttackIntent(PenetrateDmg),
            new DetailedStatusCardIntent<CosmicFragmentEpiphanyCard>(
                PenetrateEpiphany, PileType.Draw, showSingleTargetMarker: false));

        var echo = new MoveState(
            "OTHERWORLDLY_ECHO",
            OtherworldlyEchoMove,
            new MultiAttackIntent(EchoDmg, EchoHits),
            new BuffIntent());

        penetrate.FollowUpState = echo;
        echo.FollowUpState = penetrate;

        return new MonsterMoveStateMachine([penetrate, echo], penetrate);
    }

    private async Task PenetrateMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(AttackSfxPath, -2f);
        AttackCommand attack = await DamageCmd.Attack(PenetrateDmg)
            .FromMonster(this)
            .WithAttackerAnim("Attack", CosmicFragmentCreatureVisuals.AttackImpactSeconds)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);

        IReadOnlyList<Creature> hitTargets = AttackCommandCompat.Results(attack)
            .Where(static result => result.Receiver.IsPlayer)
            .Select(static result => result.Receiver)
            .Distinct()
            .ToList();

        await CardPileCmdCompat.AddToCombatAndPreview<CosmicFragmentEpiphanyCard>(
            hitTargets, PileType.Draw, PenetrateEpiphany, addedByPlayer: false);
        
    }

    private async Task OtherworldlyEchoMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(SingingSfxPath, -2f);

        List<CardModel> allEpiphanies = [];
        if (CombatState != null)
        {
            foreach (var player in CombatState.Players)
            {
                if (player.PlayerCombatState == null)
                {
                    continue;
                }

                allEpiphanies.AddRange(
                    player.PlayerCombatState.AllCards.Where(static c => c is CosmicFragmentEpiphanyCard));
            }
        }

        foreach (CardModel epiphany in allEpiphanies)
        {
            CosmicFragmentEpiphanyCard.UpgradeFromCosmicFragment(epiphany);
        }

        for (int i = 0; i < EchoHits; i++)
        {
            if (Creature.IsDead || Creature.CombatState == null)
            {
                return;
            }

            LocalOggOneShotPlayer.Play(EchoAttackSfxPath, -2f);
            await AbnormalityAnimHelper.ExecuteAttackSegment(
                this,
                EchoDmg,
                animId: "Attack2",
                // 回响只换一次图，第一段等到换图后的命中，后续各段落在人物鼓起的时刻（见 CosmicFragmentCreatureVisuals）
                delaySeconds: i == 0
                    ? CosmicFragmentCreatureVisuals.AttackImpactSeconds
                    : CosmicFragmentCreatureVisuals.EchoFollowUpSeconds);
            await Cmd.CustomScaledWait(0.04f, 0.08f);
        }
        
        if (Creature.IsDead || Creature.CombatState == null)
        {
            return;
        }

        await PowerCmdCompat.Apply<StrengthPower>(Creature, _nextEchoStrengthGain, Creature, null);
        // Match the vanilla PowerModel amount limit without overflowing the next gain.
        _nextEchoStrengthGain = (int)Math.Min(_nextEchoStrengthGain * EchoStrengthGainMultiplier, 999_999_999L);
    }

    private IEnumerable<AbstractIntent> EnumerateIntentAssets()
    {
        yield return new SingleAttackIntent(PenetrateDmg);
        yield return new DetailedStatusCardIntent<CosmicFragmentEpiphanyCard>(
            PenetrateEpiphany, PileType.Draw, showSingleTargetMarker: false);
        yield return new MultiAttackIntent(EchoDmg, EchoHits);
        yield return new DetailedBuffIntent<LibraryWeakPower>(99);
    }

    private static bool IsCosmicFragmentEncounter(CombatRoom room)
    {
        return room.Encounter.MonstersWithSlots.Any(pair => pair.Item1 is CosmicFragment);
    }

    private static async Task ClearEpiphanyCards(Creature deadCreature)
    {
        if (deadCreature.CombatState == null)
        {
            return;
        }

        foreach (Creature player in deadCreature.CombatState.PlayerCreatures)
        {
            if (player.Player?.PlayerCombatState == null)
            {
                continue;
            }

            IReadOnlyList<CardModel> visibleEpiphanies = player.Player.PlayerCombatState.AllPiles
                .Where(static pile => pile.Type is PileType.Hand or PileType.Play)
                .SelectMany(static pile => pile.Cards)
                .Where(static card => card is CosmicFragmentEpiphanyCard)
                .ToArray();
            if (visibleEpiphanies.Count > 0)
            {
                await CardPileCmd.RemoveFromCombat(visibleEpiphanies);
            }

            IReadOnlyList<CardModel> hiddenEpiphanies = player.Player.PlayerCombatState.AllCards
                .Where(static card => card is CosmicFragmentEpiphanyCard)
                .ToArray();
            if (hiddenEpiphanies.Count > 0)
            {
                await CardPileCmd.RemoveFromCombat(hiddenEpiphanies, skipVisuals: true);
            }
        }
    }

    private void AddCosmicFragmentPageRewardsFromDeathHook(Creature deadCreature)
    {
        if (deadCreature.CombatState?.RunState.CurrentRoom is not CombatRoom room || !IsCosmicFragmentEncounter(room))
        {
            return;
        }

        AbnormalityPageRewardHelper.AddPageRewardForEachPlayer<CosmicFragmentPageRelic>(room, CosmicFragmentPageRelicTitleLocKey);
    }
}
