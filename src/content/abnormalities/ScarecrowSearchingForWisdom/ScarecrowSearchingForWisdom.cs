using System;
using System.Linq;
using System.Threading.Tasks;
using LibraryLib.Entities.Creatures;
using LibraryOfRuina.content.abnormalities.ScorchedGirl;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.framework.audio;
using LibraryOfRuina.framework.intents;
using LibraryOfRuina.framework.monsters;
using LibraryOfRuina.framework.relics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace LibraryOfRuina.content.abnormalities.ScarecrowSearchingForWisdom;

public sealed class ScarecrowSearchingForWisdom : LorMonsterModel
{
    private static readonly string[] BattleStartLines =
    {
        "SCARECROW_SEARCHING_FOR_WISDOM.dialogue.battleStart.0",
        "SCARECROW_SEARCHING_FOR_WISDOM.dialogue.battleStart.1",
        "SCARECROW_SEARCHING_FOR_WISDOM.dialogue.battleStart.2"
    };

    private const string CultivateMoveId = "CULTIVATE";
    private const string RakeOfWisdomMoveId = "RAKE_OF_WISDOM";
    private const string StruggleForWisdomMoveId = "STRUGGLE_FOR_WISDOM";
    private const string HarvestWisdomMoveId = "HARVEST_WISDOM";

    private const int CultivateMinDamage = 6;
    private const int CultivateMaxDamage = 9;
    private const int CultivateBlock = 45;
    private const int CultivateVulnerable = 3;
    private const int CultivateBind = 6;
    private const int RakeMinDamage = 9;
    private const int RakeMaxDamage = 12;
    private const int StruggleMinDamage = 9;
    private const int StruggleMaxDamage = 12;
    private const int StruggleHits = 2;
    private const int RakeWisdomCards = 1; // 求知的钉耙：命中后放到目标抽牌堆顶的智慧张数，各进阶相同。
    private const int StruggleWisdomCards = 1; // 求知的挣扎：命中后加入每名被命中玩家弃牌堆的智慧张数，各进阶相同。
    private const int OpeningWisdomCards = 1; // 战斗开始：由编队第三只稻草人放到每名存活玩家抽牌堆顶的智慧张数，各进阶相同。
    // 收割智慧：最低单次伤害。
    private const int HarvestMinDamage = 6;

    // 收割智慧：最高单次伤害。
    private const int HarvestMaxDamage = 10;

    // 收割智慧：攻击次数。
    private const int HarvestHits = 4;

    // 收割智慧：被动触发间隔回合数，每隔该回合数由一只稻草人使用。
    internal const int HarvestIntervalTurns = 2;

    // 收割智慧：命中后从目标抽牌堆消耗的最大张数。
    internal const int HarvestCardsToSteal = 4;

    // 收割智慧：使用后对自身施加的束缚层数。
    private const int HarvestBindSelf = 12;

    // 收割智慧：全部攻击结算后，若命中过玩家，恢复一次的最大体力百分比。
    internal const int HarvestHealPercent = 10;

    internal const string IdleTexturePath = "res://images/monsters/scarecrow_searching_for_wisdom/idle.png";
    internal const string HitTexturePath = "res://images/monsters/scarecrow_searching_for_wisdom/hit.png";
    internal const string StrikeTexturePath = "res://images/monsters/scarecrow_searching_for_wisdom/attack_strike.png";
    internal const string ThrustTexturePath = "res://images/monsters/scarecrow_searching_for_wisdom/attack_thrust.png";
    internal const string SpecialTexturePath = "res://images/monsters/scarecrow_searching_for_wisdom/special.png";
    // Spine 身体（tools/spine_from_sprite 生成）：骨骼与图集以原始文件打进 PCK，运行时按路径加载
    internal const string SpineAtlasPath = "res://images/monsters/scarecrow_searching_for_wisdom/scarecrow.atlas";
    internal const string SpineSkeletonPath = "res://images/monsters/scarecrow_searching_for_wisdom/scarecrow.spine-json";
    private const string AttackOneSfxPath = "res://audio/sfx/scarecrow_searching_for_wisdom/attack_1.ogg";
    private const string AttackTwoSfxPath = "res://audio/sfx/scarecrow_searching_for_wisdom/attack_2.ogg";
    private const string HarvestDrainSfxPath = "res://audio/sfx/scarecrow_searching_for_wisdom/harvest_drain.ogg";
    private const string HarvestSpecialSfxPath = "res://audio/sfx/scarecrow_searching_for_wisdom/harvest_special.ogg";

    private static readonly string PageRelicTitleLocKey = $"{ModelDb.GetId<ScarecrowPageRelic>().Entry}.title";

    private int _formationIndex = 1;

    private int FormationIndex => _formationIndex;

    private int _baseCycleIndex;
    private bool _hasPlayedBattleStartLine;
    private int? _cultivateDamageRoll;
    private int? _rakeDamageRoll;
    private int? _struggleDamageRoll;
    private int? _harvestDamageRoll;
    private MoveState? _cultivateState;
    private MoveState? _rakeState;
    private MoveState? _struggleState;
    private MoveState? _harvestState;

    public override int MinInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 283, 180);

    public override int MaxInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 290, 183);

    public override int DefaultChaoResistance => 160;

    public override LibraryCreatureResistanceData.Resistance DefaultPhysicalResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Vulnerable,
        Pierce = LibraryResistanceLevel.Endure,
        Blunt = LibraryResistanceLevel.Normal
    };

    public override LibraryCreatureResistanceData.Resistance DefaultChaoResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Endure,
        Pierce = LibraryResistanceLevel.Normal,
        Blunt = LibraryResistanceLevel.Vulnerable
    };

    protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/scarecrow_searching_for_wisdom");

    public override IEnumerable<string> AssetPaths
    {
        get
        {
            var paths = new List<string>(
                ScarecrowSearchingForWisdomCreatureVisuals
                    .Profile.AssetPaths);
            paths.AddRange(EnumerateIntentAssets().SelectMany(static intent => intent.AssetPaths));

            return paths.Distinct();
        }
    }

    public void SetFormationIndex(int index)
    {
        _formationIndex = Math.Clamp(index, 1, 3);
        _baseCycleIndex = _formationIndex - 1;
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        _baseCycleIndex = _formationIndex - 1;
        _hasPlayedBattleStartLine = false;
        EncounterBgmController.RegisterMonster(Creature);
        await PowerCmdCompat.Apply<ScarecrowHarvestWisdomPassivePower>(Creature, 1, Creature, null, silent: true);
    }

    public override async Task BeforeCombatStart()
    {
        await base.BeforeCombatStart();
        if (_formationIndex == 2)
        {
            if (!_hasPlayedBattleStartLine)
            {
                _hasPlayedBattleStartLine = true;
                ScorchedGirlDialogueHelper.SpeakRandom(this, BattleStartLines);
            }

            await AddOpeningWisdomToEachPlayer();
        }
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        _cultivateState = new MoveState(
            CultivateMoveId,
            CultivateMove,
            new SingleAttackIntent(() => GetCultivateDamageRoll()),
            new DebuffIntent(strong: true),
            new DefendIntent());

        _rakeState = new MoveState(
            RakeOfWisdomMoveId,
            RakeOfWisdomMove,
            new SingleAttackIntent(() => GetRakeDamageRoll()),
            new StatusIntent(RakeWisdomCards));

        _struggleState = new MoveState(
            StruggleForWisdomMoveId,
            StruggleForWisdomMove,
            new DynamicAttackIntent(() => GetStruggleDamageRoll(), () => StruggleHits),
            new StatusIntent(StruggleWisdomCards));

        _harvestState = new MoveState(
            HarvestWisdomMoveId,
            HarvestWisdomMove,
            new DynamicAttackIntent(() => GetHarvestIntentDamageRoll(), () => HarvestHits),
            new CardDebuffIntent());

        var chooser = new ConditionalBranchState("CHOOSER");
        chooser.AddState(_harvestState, ShouldUseHarvestWisdom);
        chooser.AddState(_cultivateState, () => ShouldUseBaseMove(0, EnsureCultivateDamageRoll));
        chooser.AddState(_rakeState, () => ShouldUseBaseMove(1, EnsureRakeDamageRoll));
        chooser.AddState(_struggleState, () => ShouldUseBaseMove(2, EnsureStruggleDamageRoll));

        _cultivateState.FollowUpState = chooser;
        _rakeState.FollowUpState = chooser;
        _struggleState.FollowUpState = chooser;
        _harvestState.FollowUpState = chooser;

        return new MonsterMoveStateMachine(
            [_cultivateState, _rakeState, _struggleState, _harvestState, chooser],
            chooser);
    }

    private bool ShouldUseHarvestWisdom()
    {
        int round = Creature?.CombatState?.RoundNumber ?? 0;
        if (round <= 0 || round % HarvestIntervalTurns != 0)
        {
            return false;
        }

        int expectedFormationIndex = GetHarvestFormationIndex(round / HarvestIntervalTurns - 1);

        // Fall-forward: if expected formation is dead, pick next alive in sequence (wrap around)
        List<int> aliveIndices = Creature?.CombatState?.Enemies
            .Where(static e => e.IsAlive && e.Monster is ScarecrowSearchingForWisdom)
            .Select(e => ((ScarecrowSearchingForWisdom)e.Monster!).FormationIndex)
            .OrderBy(static idx => idx)
            .ToList() ?? [];

        if (aliveIndices.Count == 0)
        {
            return false;
        }

        int actualFormation = aliveIndices
            .Where(idx => idx >= expectedFormationIndex)
            .DefaultIfEmpty(aliveIndices[0])
            .First();

        if (_formationIndex != actualFormation)
        {
            return false;
        }

        EnsureHarvestDamageRoll();
        return true;
    }

    private static int GetHarvestFormationIndex(int harvestIndex)
    {
        // 收割顺序固定为中、左、右站位轮流。
        switch (harvestIndex % 3)
        {
            case 0:
                return 2;
            case 1:
                return 1;
            default:
                return 3;
        }
    }

    internal bool IsHarvestWisdomMoveQueued()
    {
        return MoveStateMachine?.StateLog.LastOrDefault()?.Id == HarvestWisdomMoveId;
    }

    private bool ShouldUseBaseMove(int cycleIndex, Func<int> ensureDamageRoll)
    {
        if (_baseCycleIndex != cycleIndex)
        {
            return false;
        }

        ensureDamageRoll();
        return true;
    }

    private async Task CultivateMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(AttackOneSfxPath, -2f);
        int damage = GetCultivateDamageRoll();
        IReadOnlyList<DamageResult> results = AttackCommandCompat.Results(await DamageCmd.Attack(damage)
            .FromMonster(this)
            .WithAttackerAnim("AttackStrike", ScarecrowSearchingForWisdomCreatureVisuals.AttackImpactSeconds)
            .WithHitFx("vfx/vfx_attack_blunt")
            .Execute(null));
        _cultivateDamageRoll = null;

        IEnumerable<Creature> hitTargets = results
            .Where(static result => result.TotalDamage > 0)
            .Select(static result => result.Receiver)
            .Where(static target => target.IsAlive);

        foreach (Creature hitTarget in hitTargets.Distinct())
        {
            await LibraryPowerCmd.Apply<LibraryDisarmPower>(
                hitTarget,
                CultivateVulnerable,
                1,
                Creature,
                null);
            await LibraryPowerCmd.Apply<LibraryBindingPower>(
                hitTarget,
                CultivateBind,
                1,
                Creature,
                null);
        }

        await CreatureCmd.GainBlock(Creature, CultivateBlock, ValueProp.Move, null);
        AdvanceBaseCycle();
    }

    private async Task RakeOfWisdomMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(AttackTwoSfxPath, -2f);
        int damage = GetRakeDamageRoll();
        IReadOnlyList<DamageResult> results = AttackCommandCompat.Results(await DamageCmd.Attack(damage)
            .FromMonster(this)
            .WithAttackerAnim("AttackThrust", ScarecrowSearchingForWisdomCreatureVisuals.AttackImpactSeconds)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null));
        _rakeDamageRoll = null;

        foreach (Creature target in HitPlayers(results))
        {
            await CardPileCmdCompat.AddToCombatAndPreview<ScarecrowWisdomStatusCard>(
                target,
                PileType.Draw,
                RakeWisdomCards,
                addedByPlayer: false,
                CardPilePosition.Top);
        }

        AdvanceBaseCycle();
    }

    private async Task StruggleForWisdomMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(AttackOneSfxPath, -2f);
        int damage = GetStruggleDamageRoll();
        IReadOnlyList<DamageResult> results = AttackCommandCompat.Results(await DamageCmd.Attack(damage)
            .FromMonster(this)
            .WithHitCount(StruggleHits)
            .WithAttackerAnim("AttackStrike", ScarecrowSearchingForWisdomCreatureVisuals.AttackImpactSeconds)
            .WithHitFx("vfx/vfx_attack_blunt")
            .Execute(null));
        _struggleDamageRoll = null;

        foreach (Creature target in HitPlayers(results).Distinct())
        {
            await CardPileCmdCompat.AddToCombatAndPreview<ScarecrowWisdomStatusCard>(
                target,
                PileType.Discard,
                StruggleWisdomCards,
                addedByPlayer: false);
        }

        AdvanceBaseCycle();
    }

    private async Task HarvestWisdomMove(IReadOnlyList<Creature> targets)
    {
        IReadOnlyList<Creature> playerTargets = GetLivingPlayerTargets();
        if (playerTargets.Count == 0)
        {
            _harvestDamageRoll = null;
            return;
        }

        LocalOggOneShotPlayer.Play(HarvestSpecialSfxPath, -2f);
        using (TargetedMonsterAttackHelper.ForceTargets(Creature, playerTargets))
        {
            IReadOnlyList<DamageResult> results = AttackCommandCompat.Results(await DamageCmd.Attack(GetHarvestDamageRoll())
                .FromMonster(this)
                .WithHitCount(HarvestHits)
                .WithAttackerAnim("Special", ScarecrowSearchingForWisdomCreatureVisuals.HarvestHitSeconds)
                .WithHitFx("vfx/vfx_attack_slash")
                .Execute(null));

            List<Creature> hitPlayers = HitPlayers(results).Distinct().ToList();
            foreach (Creature target in hitPlayers)
            {
                await ResolveHarvestWisdomHit(new ThrowingPlayerChoiceContext(), target);
            }

            if (hitPlayers.Count > 0)
            {
                await HealAfterHarvest();
            }
        }

        _harvestDamageRoll = null;
        await PowerCmdCompat.Apply<LibraryBindingPower>(Creature, HarvestBindSelf, Creature, null);
    }

    private async Task ResolveHarvestWisdomHit(PlayerChoiceContext choiceContext, Creature target)
    {
        ScarecrowWisdomPower? wisdomPower = target.GetPower<ScarecrowWisdomPower>();
        if (wisdomPower is { Amount: > 0 })
        {
            await PowerCmd.Remove(wisdomPower);
        }

        if (target.Player == null)
        {
            return;
        }

        await StealCardsFromDrawPile(choiceContext, target.Player);
    }

    private async Task HealAfterHarvest()
    {
        decimal missingHp = Creature.MaxHp - Creature.CurrentHp;
        if (missingHp > 0m)
        {
            await CreatureCmd.Heal(
                Creature,
                Math.Min(missingHp, Math.Ceiling(Creature.MaxHp * HarvestHealPercent / 100m)));
        }
    }

    private static async Task StealCardsFromDrawPile(PlayerChoiceContext choiceContext, Player player)
    {
        CardPile drawPile = PileType.Draw.GetPile(player);
        if (drawPile.Cards.Count == 0)
        {
            return;
        }

        LocalOggOneShotPlayer.Play(HarvestDrainSfxPath, -2f);
        for (int i = 0; i < HarvestCardsToSteal && drawPile.Cards.Count > 0; i++)
        {
            CardModel? stolen = drawPile.Cards.OfType<ScarecrowWisdomStatusCard>().FirstOrDefault()
                                ?? player.RunState.Rng.CombatCardSelection.NextItem(drawPile.Cards);
            if (stolen == null)
            {
                break;
            }

            await CardCmd.Exhaust(choiceContext, stolen);
        }
    }

    private int GetHarvestIntentDamageRoll()
    {
        return GetHarvestDamageRoll();
    }

    private IReadOnlyList<Creature> GetLivingPlayerTargets()
    {
        IReadOnlyList<Creature>? playerCreatures = Creature?.CombatState?.PlayerCreatures;
        if (playerCreatures == null)
        {
            return Array.Empty<Creature>();
        }

        return playerCreatures
            .Where(static creature => creature.IsAlive)
            .OrderBy(static creature => creature.CombatId ?? 0u)
            .ToArray();
    }

    private static IEnumerable<Creature> HitPlayers(IEnumerable<DamageResult> results)
    {
        return results
            .Where(static result => result.TotalDamage > 0)
            .Select(static result => result.Receiver)
            .Where(static target => target.IsAlive && target.Player != null);
    }

    private async Task AddOpeningWisdomToEachPlayer()
    {
        if (Creature.CombatState == null)
        {
            return;
        }

        foreach (Creature player in Creature.CombatState.Players.Select(static p => p.Creature)
        .Where(static c => c.IsAlive))
        {
            await CardPileCmdCompat.AddToCombatWithoutPreview<ScarecrowWisdomStatusCard>(
                player,
                PileType.Draw,
                OpeningWisdomCards,
                addedByPlayer: false,
                CardPilePosition.Top);
        }
    }

    private void AdvanceBaseCycle()
    {
        _baseCycleIndex = (_baseCycleIndex + 1) % 3;
    }

    private int GetCultivateDamageRoll()
    {
        return GetOrRollDamage(ref _cultivateDamageRoll, CultivateMinDamage, CultivateMaxDamage);
    }

    private int GetRakeDamageRoll()
    {
        return GetOrRollDamage(ref _rakeDamageRoll, RakeMinDamage, RakeMaxDamage);
    }

    private int GetStruggleDamageRoll()
    {
        return GetOrRollDamage(ref _struggleDamageRoll, StruggleMinDamage, StruggleMaxDamage);
    }

    private int GetHarvestDamageRoll()
    {
        return GetOrRollDamage(ref _harvestDamageRoll, HarvestMinDamage, HarvestMaxDamage);
    }

    private int EnsureCultivateDamageRoll()
    {
        return EnsureDamageRoll(ref _cultivateDamageRoll, CultivateMinDamage, CultivateMaxDamage);
    }

    private int EnsureRakeDamageRoll()
    {
        return EnsureDamageRoll(ref _rakeDamageRoll, RakeMinDamage, RakeMaxDamage);
    }

    private int EnsureStruggleDamageRoll()
    {
        return EnsureDamageRoll(ref _struggleDamageRoll, StruggleMinDamage, StruggleMaxDamage);
    }

    private int EnsureHarvestDamageRoll()
    {
        return EnsureDamageRoll(ref _harvestDamageRoll, HarvestMinDamage, HarvestMaxDamage);
    }

    private IEnumerable<AbstractIntent> EnumerateIntentAssets()
    {
        yield return new SingleAttackIntent(CultivateMaxDamage);
        yield return new DebuffIntent(strong: true);
        yield return new DefendIntent();
        yield return new SingleAttackIntent(RakeMaxDamage);
        yield return new StatusIntent(RakeWisdomCards);
        yield return new MultiAttackIntent(StruggleMaxDamage, StruggleHits);
        yield return new StatusIntent(StruggleWisdomCards);
        yield return new MultiAttackIntent(HarvestMaxDamage, HarvestHits);
        yield return new CardDebuffIntent();
    }

    private void AddScarecrowPageRewardsFromDeathHook(Creature deadCreature)
    {
        if (deadCreature.CombatState?.RunState.CurrentRoom is not CombatRoom room 
            || !IsScarecrowEncounter(room))
        {
            return;
        }

        AbnormalityPageRewardHelper.AddPageRewardForEachPlayer<ScarecrowPageRelic>(room, PageRelicTitleLocKey);
    }

    private static bool IsScarecrowEncounter(CombatRoom room)
    {
        return room.Encounter.MonstersWithSlots.Any(pair => pair.Item1 is ScarecrowSearchingForWisdom);
    }

    public override Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature creature,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (wasRemovalPrevented || creature != Creature)
        {
            return Task.CompletedTask;
        }

        AddScarecrowPageRewardsFromDeathHook(creature);
        return Task.CompletedTask;
    }
}
