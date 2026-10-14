using LibraryLib.Models;
using System.Linq;
using System.Threading.Tasks;
using LibraryOfRuina.content.abnormalities.CosmicFragment;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.framework.audio;
using LibraryOfRuina.framework.combat;
using LibraryOfRuina.framework.intents;
using LibraryOfRuina.framework.monsters;
using LibraryOfRuina.framework.visuals;
using LibraryOfRuina.infra.helpers;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Saves;

namespace LibraryOfRuina.content.liberation.Art;

public sealed class ArtFloorBeyondFragmentBoss : LiberationPhaseBossMonster
{
    private const int Phase = 2;
    private const string MelodiousSongMoveId = "MELODIOUS_SONG";
    private const string PenetrateMoveId = "PENETRATE";
    private const string BoundaryThornMoveId = "BOUNDARY_THORN";
    private const string OtherworldlyEchoMoveId = "OTHERWORLDLY_ECHO";
    private const string BeyondFragmentEgoMoveId = "BEYOND_FRAGMENT_EGO";
    private const float SegmentDelaySeconds = AbnormalityAnimHelper.DefaultAttackSegmentDelaySeconds;

    private const int MelodiousSongHits = 3; // 悠扬的歌声：攻击次数（单次伤害见 MelodiousSongDamage），各进阶相同。
    private const int MelodiousSongWeak = 1; // 悠扬的歌声：每段给予受到未被格挡伤害的玩家的虚弱层数，各进阶相同。
    private const int MelodiousSongWeakTurns = 1; // 悠扬的歌声：虚弱的持续回合数，各进阶相同。
    private const int MelodiousSongFlaw = 1; // 悠扬的歌声：每段给予受到未被格挡伤害的玩家的破绽层数，各进阶相同。
    private const int MelodiousSongFlawTurns = 1; // 悠扬的歌声：破绽的持续回合数，各进阶相同。
    private const int MelodiousSongRapidWear = 2; // 悠扬的歌声：每段给予受到未被格挡伤害的玩家的易损层数，各进阶相同。
    private const int MelodiousSongRapidWearTurns = 1; // 悠扬的歌声：易损的持续回合数，各进阶相同。
    private const int PenetrateEpiphany = 1; // 穿透：攻击后加入每名存活玩家抽牌堆的顿悟张数，各进阶相同。
    private const int BoundaryThornEpiphany = 1; // 境界之刺：攻击前加入每名存活玩家抽牌堆的顿悟张数，各进阶相同。
    private const int OtherworldlyEchoHits = 3; // 异界的回响：无差别攻击次数（单次伤害见 OtherworldlyEchoDamage），各进阶相同。
    private const int OtherworldlyEchoStrengthPerEpiphany = 3; // 异界的回响：每移除一张顿悟，自身获得的力量，各进阶相同。

    public const string Root = ArtFloorAssets.BeyondFragmentMonsterRoot;
    public const string IdleTexturePath = Root + "beyond_fragment_idle.png";
    public const string HitTexturePath = Root + "beyond_fragment_hit.png";
    public const string AttackTexturePath = Root + "beyond_fragment_attack.png";
    public const string Attack2TexturePath = Root + "beyond_fragment_attack2.png";
    public const string EgoTexturePath = Root + "beyond_fragment_ego.png";

    private int _baseCadenceIndex;
    private bool _egoQueued;
    private MoveState? _egoState;

    public override int LiberationPhase => Phase;

    /// <summary>死亡动画时长；转阶段的假死返回 0，见 <see cref="LayeredBossSpine.DeathLength"/>。</summary>
    public override float DeathAnimLengthOverride => LayeredBossSpine.DeathLength(this);

    public override bool ShouldDisappearFromDoom => false;

    public override int DefaultChaoResistance => 200;

    public override LibraryCreatureResistanceData.Resistance? DefaultPhysicalResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Endure,
        Pierce = LibraryResistanceLevel.Immune,
        Blunt = LibraryResistanceLevel.Resist
    };

    public override LibraryCreatureResistanceData.Resistance? DefaultChaoResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Vulnerable,
        Pierce = LibraryResistanceLevel.Normal,
        Blunt = LibraryResistanceLevel.Normal
    };

    public override int MinInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 296, 213);

    public override int MaxInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 300, 215);

    private int MelodiousSongDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 8, 6);

    private int PenetrateDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 26, 20);

    private int BoundaryThornDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 14, 10);

    private int OtherworldlyEchoDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 13, 10);

    private int BeyondFragmentEgoDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, ArtFloorEgoNumbers.BeyondFragmentUpgradedDamage, ArtFloorEgoNumbers.BeyondFragmentDamage);

    public override IEnumerable<string> AssetPaths =>
        BeyondFragmentCreatureVisuals.Profile.AssetPaths
            .Concat(new[]
            {
                abnormalities.CosmicFragment.CosmicFragment.AttackSfxPath,
                abnormalities.CosmicFragment.CosmicFragment.SingingSfxPath,
                abnormalities.CosmicFragment.CosmicFragment.EchoAttackSfxPath
            })
            .Concat(EnumerateIntentAssets().SelectMany(static intent => intent.AssetPaths))
            .Distinct();

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        _baseCadenceIndex = 0;
        _egoQueued = false;
        SaveManager.Instance.MarkCardAsSeen(ModelDb.Card<CosmicFragmentEpiphanyCard>());
        SaveManager.Instance.MarkCardAsSeen(ModelDb.Card<BeyondFragmentEgoCard>());

        if (CombatState?.Encounter is ArtFloorLiberationEncounter encounter)
        {
            await encounter.EnsureControllerPowers(CombatState);
            encounter.MarkBossPhaseStarted(Phase);
        }

        EncounterBgmController.RegisterMonster(Creature);
        await PowerCmdCompat.Apply<ArtFloorErosionPower>(Creature, 1m, Creature, null, silent: true);
        await PowerCmdCompat.Apply<BeyondFragmentTentaclePower>(Creature, 1m, Creature, null, silent: true);
        await PowerCmdCompat.Apply<BeyondFragmentIncomprehensiblePower>(Creature, 1m, Creature, null, silent: true);
    }

    public override Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature creature,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (creature != Creature
            || Creature.CombatState?.Encounter is not ArtFloorLiberationEncounter encounter)
        {
            return Task.CompletedTask;
        }

        return encounter.OnPhaseBossDeath(this, wasRemovalPrevented, deathAnimLength);
    }

    public Task QueueBeyondFragmentEgo()
    {
        if (_egoQueued || Creature.IsDead || _egoState == null)
        {
            return Task.CompletedTask;
        }

        _egoQueued = true;
        SetMoveImmediate(_egoState, forceTransition: true);
        return CombatQueries.CreatureNodeOf(this)?.RefreshIntents() ?? Task.CompletedTask;
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState reviveAndEmpower = CreateReviveAndEmpowerState();

        var melodiousSong = new MoveState(
            MelodiousSongMoveId,
            MelodiousSongMove,
            new CombinedAttackDebuffIntent(() => MelodiousSongDamage, () => MelodiousSongHits)
            );

        var penetrate = new MoveState(
            PenetrateMoveId,
            PenetrateMove,
            new SingleAttackIntent(() => PenetrateDamage),
            new DetailedStatusCardIntent<CosmicFragmentEpiphanyCard>(
                PenetrateEpiphany, PileType.Draw, showSingleTargetMarker: false));

        var boundaryThorn = new MoveState(
            BoundaryThornMoveId,
            BoundaryThornMove,
            new DetailedStatusCardIntent<CosmicFragmentEpiphanyCard>(
                BoundaryThornEpiphany, PileType.Draw, showSingleTargetMarker: false),
            new CombinedAttackBuffIntent(() => BoundaryThornDamage)
            );

        var otherworldlyEcho = new MoveState(
            OtherworldlyEchoMoveId,
            OtherworldlyEchoMove,
            new IndiscriminateAttackIntent(() => OtherworldlyEchoDamage, () => OtherworldlyEchoHits, null),
            new BuffIntent());

        _egoState = new MoveState(
            BeyondFragmentEgoMoveId,
            BeyondFragmentEgoMove,
            CreateBeyondFragmentEgoIntent(),
            new DebuffIntent(strong: true));

        var chooser = new ConditionalBranchState("CHOOSER");
        chooser.AddState(_egoState, () => _egoQueued);
        chooser.AddState(melodiousSong, () => _baseCadenceIndex == 0);
        chooser.AddState(penetrate, () => _baseCadenceIndex == 1);
        chooser.AddState(boundaryThorn, () => _baseCadenceIndex == 2);
        chooser.AddState(otherworldlyEcho, () => true);

        melodiousSong.FollowUpState = chooser;
        penetrate.FollowUpState = chooser;
        boundaryThorn.FollowUpState = chooser;
        otherworldlyEcho.FollowUpState = chooser;
        _egoState.FollowUpState = chooser;
        reviveAndEmpower.FollowUpState = chooser;

        return new MonsterMoveStateMachine(
            new MonsterState[]
            {
                reviveAndEmpower,
                melodiousSong,
                penetrate,
                boundaryThorn,
                otherworldlyEcho,
                _egoState,
                chooser
            },
            chooser);
    }

    private async Task MelodiousSongMove(IReadOnlyList<Creature> targets)
    {
        for (int i = 0; i < MelodiousSongHits; i++)
        {
            LocalOggOneShotPlayer.Play(abnormalities.CosmicFragment.CosmicFragment.SingingSfxPath, -2f);
            AttackCommand attack = await ExecuteGroupAttack(MelodiousSongDamage, "Attack2", "vfx/vfx_attack_blunt");
            foreach (Creature target in GetUnblockedPlayerHitTargets(attack))
            {
                await LibraryPowerCmd.Apply<LibraryWeakPower>(target, MelodiousSongWeak, MelodiousSongWeakTurns, Creature, null);
                await LibraryPowerCmd.Apply<LibraryDisarmPower>(target, MelodiousSongFlaw, MelodiousSongFlawTurns, Creature, null);
                await LibraryPowerCmd.Apply<LibraryVulnerablePower>(
                    target,
                    MelodiousSongRapidWear,
                    MelodiousSongRapidWearTurns,
                    Creature,
                    null);
            }
        }

        AdvanceBaseCadence();
    }

    private async Task PenetrateMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(abnormalities.CosmicFragment.CosmicFragment.AttackSfxPath, -2f);
        await ExecuteGroupAttack(PenetrateDamage, "Attack", "vfx/vfx_attack_slash");
        await AddEpiphanyToPlayers(PenetrateEpiphany);
        AdvanceBaseCadence();
    }

    private async Task BoundaryThornMove(IReadOnlyList<Creature> targets)
    {
        await AddEpiphanyToPlayers(BoundaryThornEpiphany);

        LocalOggOneShotPlayer.Play(abnormalities.CosmicFragment.CosmicFragment.AttackSfxPath, -2f);
        await ExecuteGroupAttack(BoundaryThornDamage, "Attack", "vfx/vfx_attack_slash");

        foreach (CardModel epiphany in EnumerateEpiphanyCards())
        {
            CosmicFragmentEpiphanyCard.UpgradeFromCosmicFragment(epiphany);
        }

        AdvanceBaseCadence();
    }

    private async Task OtherworldlyEchoMove(IReadOnlyList<Creature> targets)
    {
        int consumed = 0;
        foreach (Creature player in CombatState.LivingPlayerCreatures())
        {
            CardModel? epiphany = player.Player?.PlayerCombatState?.AllCards
                .FirstOrDefault(static card => card is CosmicFragmentEpiphanyCard);
            if (epiphany == null)
            {
                continue;
            }

            await CardPileCmd.RemoveFromCombat(epiphany);
            consumed++;
        }

        for (int i = 0; i < OtherworldlyEchoHits; i++)
        {
            LocalOggOneShotPlayer.Play(abnormalities.CosmicFragment.CosmicFragment.EchoAttackSfxPath, -2f);
            await ExecuteGroupAttack(OtherworldlyEchoDamage, "Attack2", "vfx/vfx_attack_slash", indiscriminate: true);
            await Cmd.CustomScaledWait(0.04f, 0.08f);
        }

        if (consumed > 0)
        {
            await PowerCmdCompat.Apply<StrengthPower>(Creature, consumed * OtherworldlyEchoStrengthPerEpiphany, Creature, null);
        }

        AdvanceBaseCadence();
    }

    private async Task BeyondFragmentEgoMove(IReadOnlyList<Creature> targets)
    {
        _egoQueued = false;
        for (int i = 0; i < ArtFloorEgoNumbers.BeyondFragmentHitCount; i++)
        {
            LocalOggOneShotPlayer.Play(abnormalities.CosmicFragment.CosmicFragment.EchoAttackSfxPath, -2f);
            await ExecuteGroupAttack(BeyondFragmentEgoDamage, "Ego", "vfx/vfx_attack_slash");
            await Cmd.CustomScaledWait(0.04f, 0.08f);
        }

        foreach (Creature player in CombatState.LivingPlayerCreatures())
        {
            await PowerCmdCompat.Apply<StrengthPower>(player, -ArtFloorEgoNumbers.BeyondFragmentStrengthLoss, Creature, null);
            await PowerCmdCompat.Apply<DexterityPower>(player, -ArtFloorEgoNumbers.BeyondFragmentDexterityLoss, Creature, null);
        }
    }

    protected override Task CompleteLiberationPhaseTransition() =>
        Creature.CombatState?.Encounter is ArtFloorLiberationEncounter encounter
            ? encounter.CompletePhaseTransition(this)
            : Task.CompletedTask;

    private async Task<AttackCommand> ExecuteGroupAttack(
        int damage,
        string anim,
        string hitFx,
        bool indiscriminate = false)
    {
        IReadOnlyList<Creature> players = CombatState.LivingPlayerCreatures()
            .ToArray();

        if (indiscriminate)
        {
            await IndiscriminateAttackBlockBreaker.BreakBlockBeforeAttack(this, damage, players);
        }

        AttackCommand builder = DamageCmd.Attack(damage)
            .FromMonster(this)
            .WithAttackerAnim(anim, SegmentDelaySeconds)
            .WithHitFx(hitFx);

        if (indiscriminate)
        {
            builder = builder.WithIndiscriminateBlockBreak(this, damage, players);
        }

        return await builder.Execute(null);
    }

    private Task AddEpiphanyToPlayers(int count)
    {
        IReadOnlyList<Creature> players = CombatState.LivingPlayerCreatures()
            .ToArray();

        return CardPileCmdCompat.AddToCombatAndPreview<CosmicFragmentEpiphanyCard>(
            players,
            PileType.Draw,
            count,
            addedByPlayer: false);
    }

    private IEnumerable<CardModel> EnumerateEpiphanyCards()
    {
        foreach (Creature player in CombatState.PlayerCreatures)
        {
            if (player.Player?.PlayerCombatState == null)
            {
                continue;
            }

            foreach (CardModel card in player.Player.PlayerCombatState.AllCards)
            {
                if (card is CosmicFragmentEpiphanyCard)
                {
                    yield return card;
                }
            }
        }
    }

    private static IReadOnlyList<Creature> GetUnblockedPlayerHitTargets(AttackCommand attack)
    {
        return AttackCommandCompat.Results(attack)
            .Where(static result => result.Receiver.IsAlive && result.Receiver.IsPlayer && result.UnblockedDamage > 0)
            .Select(static result => result.Receiver)
            .Distinct()
            .ToArray();
    }

    private void AdvanceBaseCadence()
    {
        _baseCadenceIndex = (_baseCadenceIndex + 1) % 4;
    }

    private IEnumerable<AbstractIntent> EnumerateIntentAssets()
    {
        yield return new CombinedAttackDebuffIntent(() => MelodiousSongDamage, () => MelodiousSongHits);
        yield return new DebuffIntent(strong: true);
        yield return new SingleAttackIntent(() => PenetrateDamage);
        yield return new DetailedStatusCardIntent<CosmicFragmentEpiphanyCard>(
            PenetrateEpiphany, PileType.Draw, showSingleTargetMarker: false);
        yield return new CombinedAttackBuffIntent(() => BoundaryThornDamage);
        yield return new BuffIntent();
        yield return new IndiscriminateAttackIntent(() => OtherworldlyEchoDamage, () => OtherworldlyEchoHits, null);
        yield return new BuffIntent();
        yield return new HealIntent();
        yield return new DebuffIntent(strong: true);
        yield return CreateBeyondFragmentEgoIntent();
    }

    private PlayCardAttackIntent<BeyondFragmentEgoCard> CreateBeyondFragmentEgoIntent()
    {
        return new PlayCardAttackIntent<BeyondFragmentEgoCard>(
            "BEYOND_FRAGMENT_EGO_CARD",
            () => BeyondFragmentEgoDamage,
            static (card, _) => card.UpgradePreview(),
            () => ArtFloorEgoNumbers.BeyondFragmentHitCount,
            badges: [IntentBadge.StrengthDown(ArtFloorEgoNumbers.BeyondFragmentStrengthLoss), IntentBadge.FromPower<DexterityPower>(-ArtFloorEgoNumbers.BeyondFragmentDexterityLoss)]);
    }
}
