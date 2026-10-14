using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LibraryOfRuina.content.abnormalities.QueenBee;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.framework.audio;
using LibraryOfRuina.framework.combat;
using LibraryOfRuina.framework.intents;
using LibraryOfRuina.framework.monsters;
using LibraryOfRuina.framework.relics;
using LibraryOfRuina.framework.visuals;
using LibraryOfRuina.infra.helpers;
using LibraryOfRuina.patches;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace LibraryOfRuina.content.abnormalities.Nosferatu;

public sealed class Nosferatu : LorMonsterModel
{
    public override float DeathAnimLengthOverride => LayeredBossSpine.DeathLength(this);

    private const string GracefulRestMoveId = "GRACEFUL_REST";
    private const string ElegantDinnerMoveId = "ELEGANT_DINNER";
    private const string ThirstMoveId = "THIRST";
    private const string CrimsonMealMoveId = "CRIMSON_MEAL";
    private const string BloodFeastMoveId = "BLOOD_FEAST";
    private const string OminousAuraMoveId = "OMINOUS_AURA";
    private const string ColdClawsMoveId = "COLD_CLAWS";
    private const string ViolentGestureMoveId = "VIOLENT_GESTURE";
    private const string UnbearableThirstMoveId = "UNBEARABLE_THIRST";
    private const string ExtremeBloodthirstMoveId = "EXTREME_BLOODTHIRST";

    private const int GracefulRestBlock = 12; // 高雅的休憩：所有存活敌人获得的格挡，各进阶相同。
    private const int ElegantDinnerHits = 2; // 精致的晚宴：攻击次数（单次伤害见 ElegantDinnerDamage），各进阶相同。
    private const int ElegantDinnerBloodPerUnblockedHit = 2; // 精致的晚宴：每段造成未被格挡伤害时自身获得的鲜血层数，各进阶相同。
    private const int ThirstHits = 1; // 干渴：攻击次数（单次伤害见 ThirstDamage），各进阶相同。
    private const int ThirstBlood = 3; // 干渴：造成未被格挡伤害时自身获得的鲜血层数，各进阶相同。
    private const int CrimsonMealBlock = 24; // 猩红的餐食：所有存活敌人获得的格挡，各进阶相同。
    private const int CrimsonMealQuickness = 5; // 猩红的餐食：所有存活敌人获得的下回合迅捷层数，各进阶相同。
    private const int CrimsonMealProtection = 5; // 猩红的餐食：所有存活敌人获得的守护层数，各进阶相同。
    private const int CrimsonMealProtectionTurns = 1; // 猩红的餐食：守护的持续回合数，各进阶相同。
    private const int BloodFeastHits = 3; // 血宴：无差别攻击次数（单次伤害见 BloodFeastDamage），各进阶相同。
    private const int BloodFeastHydrophobia = 2; // 血宴：给予每名被攻击玩家的恐水症层数，各进阶相同。
    private const int BloodFeastHealBase = 12; // 血宴：所有存活敌人回复生命的基础值，另加受到未被格挡伤害的玩家数，各进阶相同。
    private const int OminousAuraBlock = 18; // 阴森的气息：自身获得的格挡，各进阶相同。
    private const int OminousAuraWeak = 2; // 阴森的气息：给予所有存活玩家的虚弱层数，各进阶相同。
    private const int OminousAuraFrail = 2; // 阴森的气息：给予所有存活玩家的脆弱层数，各进阶相同。
    private const int ColdClawsHits = 12; // 冷酷的魔爪：攻击次数（单次伤害见 ColdClawsDamage），各进阶相同。
    private const int ColdClawsStrength = 1; // 冷酷的魔爪：攻击后自身获得的力量，各进阶相同。
    private const int ViolentGestureHits = 4; // 激烈的手势：攻击次数（单次伤害见 ViolentGestureDamage），各进阶相同。
    private const int ViolentGestureStrengthLoss = 1; // 激烈的手势：受到未被格挡伤害的玩家失去的力量，各进阶相同。
    private const int ViolentGestureDexterityLoss = 1; // 激烈的手势：受到未被格挡伤害的玩家失去的敏捷，各进阶相同。
    private const int ViolentGestureRapidWear = 8; // 激烈的手势：给予受到未被格挡伤害的玩家的易损层数，各进阶相同。
    private const int ViolentGestureRapidWearTurns = 1; // 激烈的手势：易损的持续回合数，各进阶相同。
    private const int UnbearableThirstBlood = 3; // 无法忍受的饥渴：自身获得的鲜血层数，各进阶相同。
    private const int UnbearableThirstHeal = 16; // 无法忍受的饥渴：自身回复生命的基础值（多人时按人数缩放），各进阶相同。
    private const int UnbearableThirstVulnerable = 1; // 无法忍受的饥渴：给予所有存活玩家的易伤层数，各进阶相同。
    private const int ExtremeBloodthirstHits = 1; // 极度渴血：无差别攻击次数（单次伤害见 ExtremeBloodthirstDamage），各进阶相同。
    private const int ExtremeBloodthirstHydrophobia = 2; // 极度渴血：给予受到未被格挡伤害的玩家的恐水症层数，各进阶相同。
    private const int ExtremeBloodthirstRapidWear = 2; // 极度渴血：给予受到未被格挡伤害的玩家的易损层数，各进阶相同。
    private const int ExtremeBloodthirstRapidWearTurns = 2; // 极度渴血：易损的持续回合数，各进阶相同。
    private const int ExtremeBloodthirstWeak = 2; // 极度渴血：给予受到未被格挡伤害的玩家的虚弱层数，各进阶相同。
    private const int ExtremeBloodthirstWeakTurns = 2; // 极度渴血：虚弱的持续回合数，各进阶相同。

    internal const string TextureRoot = NosferatuAssets.NosferatuMonsterRoot;
    private const float LocalSfxVolumeScale = 0.85f;
    private static readonly float LocalSfxVolumeDb = Mathf.LinearToDb(LocalSfxVolumeScale);

    private static readonly string[] NormalCycle =
    [
        ElegantDinnerMoveId,
        CrimsonMealMoveId,
        ThirstMoveId,
        BloodFeastMoveId
    ];

    private static readonly string[] TransformedCycle =
    [
        OminousAuraMoveId,
        ColdClawsMoveId,
        ViolentGestureMoveId,
        UnbearableThirstMoveId,
        ExtremeBloodthirstMoveId
    ];

    public static readonly string[] AssetPathsStatic =
        NosferatuCreatureVisuals.Profile.AssetPaths
            .Concat(
            [
                NosferatuAssets.NosferatuSfxRoot + "nosferatu_strong_attack.ogg",
                NosferatuAssets.NosferatuSfxRoot + "nosferatu_transform.ogg",
                NosferatuAssets.NosferatuSfxRoot + "nosferatu_transform_slash.ogg",
                NosferatuAssets.NosferatuSfxRoot + "nosferatu_transform_strike.ogg",
                NosferatuAssets.NosferatuSfxRoot + "nosferatu_transform_strong_attack_effect.ogg",
                NosferatuAssets.NosferatuSfxRoot + "nosferatu_transform_vampire.ogg",
                NosferatuAssets.NosferatuSfxRoot + "nosferatu_evade.ogg",
                NosferatuAssets.BloodPowerIcon,
                NosferatuAssets.HydrophobiaPassivePowerIcon,
                NosferatuAssets.HydrophobiaPowerIcon,
                NosferatuAssets.TransformPowerIcon,
                NosferatuAssets.FlowingBloodPowerIcon
            ])
            .ToArray();

    private static readonly string NosferatuPageRelicTitleLocKey =
        $"{ModelDb.GetId<NosferatuPageRelic>().Entry}.title";

    private bool _isTransformed;
    private int _cycleIndex;

    public bool IsTransformed => _isTransformed;

    public override bool ShouldDisappearFromDoom => _isTransformed;

    public override int MinInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 545, 441);

    public override int MaxInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 550, 444);

    public override int DefaultChaoResistance => 190;

    public override LibraryCreatureResistanceData.Resistance? DefaultPhysicalResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Endure,
        Pierce = LibraryResistanceLevel.Normal,
        Blunt = LibraryResistanceLevel.Endure
    };

    public override LibraryCreatureResistanceData.Resistance? DefaultChaoResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Endure,
        Pierce = LibraryResistanceLevel.Normal,
        Blunt = LibraryResistanceLevel.Endure
    };

    public override IEnumerable<string> AssetPaths =>
        AssetPathsStatic
            .Concat(BloodBat.AssetPathsStatic)
            .Concat(EnumerateIntentAssets().SelectMany(static intent => intent.AssetPaths))
            .Distinct();

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        _isTransformed = false;
        _cycleIndex = 0;
        EncounterBgmController.RegisterMonster(Creature);
        await PowerCmdCompat.Apply<NosferatuHydrophobiaPassivePower>(Creature, 1m, Creature, null, silent: true);
        await PowerCmdCompat.Apply<NosferatuTransformPower>(Creature, 1m, Creature, null, silent: true);
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

        AddPageRewardsFromDeathHook();
        return Task.CompletedTask;
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var gracefulRest = new MoveState(
            GracefulRestMoveId,
            GracefulRestMove,
            new SummonIntent(),
            new DefendIntent());
        var elegantDinner = new MoveState(
            ElegantDinnerMoveId,
            ElegantDinnerMove,
            new CombinedAttackBuffIntent(
                () => ElegantDinnerDamage,
                () => ElegantDinnerHits,
                "NOSFERATU_ELEGANT_DINNER.description",
                IntentBadge.FromPower<NosferatuBloodPower>(ElegantDinnerBloodPerUnblockedHit)));
        var thirst = new MoveState(
            ThirstMoveId,
            ThirstMove,
            new CombinedAttackBuffIntent(
                () => ThirstDamage,
                () => ThirstHits,
                "NOSFERATU_THIRST.description",
                IntentBadge.FromPower<NosferatuBloodPower>(ThirstBlood)));
        var crimsonMeal = new MoveState(
            CrimsonMealMoveId,
            CrimsonMealMove,
            new CombinedDefendBuffIntent(
                CrimsonMealBlock,
                "NOSFERATU_CRIMSON_MEAL.description",
                IntentBadge.FromPower<QueenBeeNextTurnQuicknessPower>(CrimsonMealQuickness),
                IntentBadge.FromPower<LibraryProtectionPower>(CrimsonMealProtection)));
        var bloodFeast = new MoveState(
            BloodFeastMoveId,
            BloodFeastMove,
            new IndiscriminateAttackIntent(
                () => BloodFeastDamage,
                () => BloodFeastHits,
                "NOSFERATU_BLOOD_FEAST.description",
                IntentBadge.FromPower<NosferatuHydrophobiaPower>(BloodFeastHydrophobia),
                IntentBadge.Heal(() => BloodFeastHealPreview)));

        var ominousAura = new MoveState(
            OminousAuraMoveId,
            OminousAuraMove,
            new CombinedDefendDebuffIntent(
                OminousAuraBlock,
                "NOSFERATU_OMINOUS_AURA.description",
                IntentBadge.Weak(OminousAuraWeak),
                IntentBadge.Frail(OminousAuraFrail)));
        var coldClaws = new MoveState(
            ColdClawsMoveId,
            ColdClawsMove,
            new CombinedAttackBuffIntent(
                () => ColdClawsDamage,
                () => ColdClawsHits,
                "NOSFERATU_COLD_CLAWS.description",
                IntentBadge.Strength(ColdClawsStrength)));
        var violentGesture = new MoveState(
            ViolentGestureMoveId,
            ViolentGestureMove,
            new CombinedAttackDebuffIntent(
                () => ViolentGestureDamage,
                () => ViolentGestureHits,
                "NOSFERATU_VIOLENT_GESTURE.description",
                IntentBadge.StrengthDown(ViolentGestureStrengthLoss),
                IntentBadge.FromPower<DexterityPower>(-ViolentGestureDexterityLoss),
                IntentBadge.FromPower<StrengthPower>(-ViolentGestureStrengthLoss),
                IntentBadge.RapidWear(ViolentGestureRapidWear, ViolentGestureRapidWearTurns)));
        var unbearableThirst = new MoveState(
            UnbearableThirstMoveId,
            UnbearableThirstMove,
            new BadgedBuffIntent(
                [
                    IntentBadge.FromPower<NosferatuBloodPower>(UnbearableThirstBlood),
                    IntentBadge.Heal(),
                ],
                amount: UnbearableThirstBlood,
                "NOSFERATU_UNBEARABLE_THIRST.description"),
            new DebuffIntent());
        var extremeBloodthirst = new MoveState(
            ExtremeBloodthirstMoveId,
            ExtremeBloodthirstMove,
            new IndiscriminateAttackIntent(
                () => ExtremeBloodthirstDamage,
                () => ExtremeBloodthirstHits,
                "NOSFERATU_EXTREME_BLOODTHIRST.description",
                IntentBadge.FromPower<NosferatuHydrophobiaPower>(ExtremeBloodthirstHydrophobia),
                IntentBadge.RapidWear(ExtremeBloodthirstRapidWear, ExtremeBloodthirstRapidWearTurns),
                IntentBadge.FromPower<LibraryWeakPower>(
                    ExtremeBloodthirstWeak,
                    ExtremeBloodthirstWeakTurns.ToString(),
                    ExtremeBloodthirstWeak.ToString())));

        var router = new DelegatingMonsterRouterState(
            "NOSFERATU_ROUTER",
            (_, _) => ResolvePlannedMoveId());
        foreach (MoveState state in new[]
                 {
                     gracefulRest, elegantDinner, thirst, crimsonMeal, bloodFeast,
                     ominousAura, coldClaws, violentGesture, unbearableThirst, extremeBloodthirst
                 })
        {
            state.FollowUpState = router;
        }

        return new MonsterMoveStateMachine(
            [
                gracefulRest, elegantDinner, thirst, crimsonMeal, bloodFeast,
                ominousAura, coldClaws, violentGesture, unbearableThirst, extremeBloodthirst, router
            ],
            router);
    }

    public async Task TransformToBloodfiend()
    {
        if (_isTransformed || Creature.IsDead)
        {
            return;
        }

        _isTransformed = true;
        _cycleIndex = 0;
        LocalOggOneShotPlayer.Play(NosferatuAssets.NosferatuSfxRoot + "nosferatu_transform.ogg", LocalSfxVolumeDb);

        if (CombatQueries.CreatureNodeOf(this)?.Visuals is NosferatuCreatureVisuals visuals)
        {
            visuals.SetBloodfiendForm(true);
        }

        await PowerCmdCompat.RemoveIfPresent<NosferatuTransformPower>(
            Creature);

        await PowerCmdCompat.Apply<NosferatuFlowingBloodPower>(Creature, 1m, Creature, null, silent: true);
    }

    internal string ResolvePlannedMoveId()
    {
        if (!_isTransformed && !HasLivingBloodBat() && TryGetBloodBatSlot(out _))
        {
            return GracefulRestMoveId;
        }

        string[] cycle = _isTransformed ? TransformedCycle : NormalCycle;
        return cycle[Math.Clamp(_cycleIndex, 0, cycle.Length - 1)];
    }

    private int ElegantDinnerDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 8, 5);

    private int ThirstDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 19, 16);

    private int BloodFeastDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 9, 7);

    private int ColdClawsDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 3, 2);

    private int ViolentGestureDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 8, 6);

    private int ExtremeBloodthirstDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 23, 18);

    private int BloodFeastHealPreview =>
        BloodFeastHealBase + Math.Max(1, Creature.CombatState?.PlayerCreatures.Count(static c => c.IsAlive) ?? 1);

    private async Task GracefulRestMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(NosferatuAssets.NosferatuSfxRoot + "nosferatu_evade.ogg", LocalSfxVolumeDb);
        await AbnormalityAnimHelper.TriggerCast(Creature);
        if (!HasLivingBloodBat() && TryGetBloodBatSlot(out string slot))
        {
            await CreatureCmd.Add(ModelDb.Monster<BloodBat>().ToMutable(), CombatState, CombatSide.Enemy, slot);
        }

        foreach (Creature enemy in LivingEnemies())
        {
            await CreatureCmd.GainBlock(enemy, GracefulRestBlock, ValueProp.Move, null);
        }

        AdvanceCycle();
    }

    private async Task ElegantDinnerMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(NosferatuAssets.NosferatuSfxRoot + "nosferatu_transform_strike.ogg", LocalSfxVolumeDb);
        GroupAttackOutcome attack = await ExecuteStandardAttackDetailed(ElegantDinnerDamage, hits: ElegantDinnerHits, "Attack");
        foreach (DamageResult _ in attack.UnblockedResults)
        {
            await NosferatuBloodPower.Change(Creature, ElegantDinnerBloodPerUnblockedHit, Creature);
        }

        AdvanceCycle();
    }

    private async Task ThirstMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(NosferatuAssets.NosferatuSfxRoot + "nosferatu_transform_strike.ogg", LocalSfxVolumeDb);
        GroupAttackOutcome attack = await ExecuteStandardAttackDetailed(ThirstDamage, hits: ThirstHits, "Attack");
        if (attack.AnyUnblocked)
        {
            await NosferatuBloodPower.Change(Creature, ThirstBlood, Creature);
        }

        AdvanceCycle();
    }

    private async Task CrimsonMealMove(IReadOnlyList<Creature> targets)
    {
        await AbnormalityAnimHelper.TriggerCast(Creature);
        foreach (Creature enemy in LivingEnemies())
        {
            await CreatureCmd.GainBlock(enemy, CrimsonMealBlock, ValueProp.Move, null);
            // await LibraryPowerCmd.Apply<LibraryQuicknessPower>(enemy,
            //     5,
            //     Creature,
            //     null);
            await PowerCmdCompat.Apply<QueenBeeNextTurnQuicknessPower>(enemy,
                CrimsonMealQuickness,
                Creature, null);
            await LibraryPowerCmd.Apply<LibraryProtectionPower>(
                enemy,
                CrimsonMealProtection,
                turns: CrimsonMealProtectionTurns,
                Creature,
                null);
        }

        AdvanceCycle();
    }

    private async Task BloodFeastMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(NosferatuAssets.NosferatuSfxRoot + "nosferatu_strong_attack.ogg", LocalSfxVolumeDb);
        GroupAttackOutcome attack = await ExecuteGroupAttackDetailed(BloodFeastDamage, hits: BloodFeastHits, "SpecialAttack");
        foreach (Creature target in attack.Targets.Distinct())
        {
            await PowerCmdCompat.ApplyDebuff<NosferatuHydrophobiaPower>(target, BloodFeastHydrophobia, Creature, null);
        }

        int heal = BloodFeastHealBase + attack.UnblockedTargets.Distinct().Count();
        foreach (Creature enemy in LivingEnemies())
        {
            await CreatureCmd.Heal(enemy, MultiplayerScalingPatchHelper.ScaleMonsterHealAmount(enemy, heal));
        }

        AdvanceCycle();
    }

    private async Task OminousAuraMove(IReadOnlyList<Creature> targets)
    {
        await AbnormalityAnimHelper.TriggerCast(Creature);
        await CreatureCmd.GainBlock(Creature, OminousAuraBlock, ValueProp.Move, null);
        foreach (Creature player in LivingPlayers())
        {
            await PowerCmdCompat.ApplyDebuff<WeakPower>(player, OminousAuraWeak, Creature, null);
            await PowerCmdCompat.ApplyDebuff<FrailPower>(player, OminousAuraFrail, Creature, null);
        }

        AdvanceCycle();
    }

    private async Task ColdClawsMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(NosferatuAssets.NosferatuSfxRoot + "nosferatu_transform_strike.ogg", LocalSfxVolumeDb);
        await ExecuteStandardAttackDetailed(ColdClawsDamage, hits: ColdClawsHits, "Attack");
        await PowerCmdCompat.Apply<StrengthPower>(Creature, ColdClawsStrength, Creature, null);
        AdvanceCycle();
    }

    private async Task ViolentGestureMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(NosferatuAssets.NosferatuSfxRoot + "nosferatu_transform_slash.ogg", LocalSfxVolumeDb);
        GroupAttackOutcome attack = await ExecuteStandardAttackDetailed(ViolentGestureDamage, hits: ViolentGestureHits, "Attack");
        foreach (Creature target in attack.UnblockedTargets.Distinct())
        {
            await PowerCmdCompat.Apply<StrengthPower>(target, -ViolentGestureStrengthLoss, Creature, null);
            await PowerCmdCompat.Apply<DexterityPower>(target, -ViolentGestureDexterityLoss, Creature, null);
            await LibraryPowerCmd.Apply<LibraryVulnerablePower>(
                target,
                ViolentGestureRapidWear,
                turns: ViolentGestureRapidWearTurns,
                Creature,
                null);
        }

        AdvanceCycle();
    }

    private async Task UnbearableThirstMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(NosferatuAssets.NosferatuSfxRoot + "nosferatu_transform_vampire.ogg", LocalSfxVolumeDb);
        await AbnormalityAnimHelper.TriggerCast(Creature);
        await NosferatuBloodPower.Change(Creature, UnbearableThirstBlood, Creature);
        await CreatureCmd.Heal(Creature, MultiplayerScalingPatchHelper.ScaleMonsterHealAmount(Creature, UnbearableThirstHeal));
        await PowerCmdCompat.ApplyDebuff<VulnerablePower>(LivingPlayers(), UnbearableThirstVulnerable, Creature, null);
        AdvanceCycle();
    }

    private async Task ExtremeBloodthirstMove(IReadOnlyList<Creature> targets)
    {
        GroupAttackOutcome attack = await ExecuteGroupAttackDetailed(ExtremeBloodthirstDamage, hits: ExtremeBloodthirstHits, "SpecialAttack");
        LocalOggOneShotPlayer.Play(NosferatuAssets.NosferatuSfxRoot + "nosferatu_transform_strong_attack_effect.ogg", LocalSfxVolumeDb);
        foreach (Creature target in attack.UnblockedTargets.Distinct())
        {
            await PowerCmdCompat.ApplyDebuff<NosferatuHydrophobiaPower>(target, ExtremeBloodthirstHydrophobia, Creature, null);
            await LibraryPowerCmd.Apply<LibraryVulnerablePower>(
                target,
                ExtremeBloodthirstRapidWear,
                turns: ExtremeBloodthirstRapidWearTurns,
                Creature,
                null);
            await LibraryPowerCmd.Apply<LibraryWeakPower>(
                target,
                ExtremeBloodthirstWeak,
                turns: ExtremeBloodthirstWeakTurns,
                Creature,
                null);
        }

        AdvanceCycle();
    }

    private async Task<GroupAttackOutcome> ExecuteStandardAttackDetailed(int damage, int hits, string anim)
    {
        AttackCommand attack = await DamageCmd.Attack(damage)
            .FromMonster(this)
            .WithHitCount(hits)
            .OnlyPlayAnimOnce()
            .WithAttackerAnim(anim, AbnormalityAnimHelper.DefaultAttackSegmentDelaySeconds)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);
        return GroupAttackOutcome.From(CombatState.LivingPlayerCreatures().ToArray(), AttackCommandCompat.Results(attack).ToArray());
    }

    private async Task<GroupAttackOutcome> ExecuteGroupAttackDetailed(int damage, int hits, string anim)
    {
        IReadOnlyList<Creature> players = LivingPlayers().ToArray();
        if (players.Count == 0)
        {
            return new GroupAttackOutcome(players, []);
        }

        List<DamageResult> results = [];
        for (int i = 0; i < hits; i++)
        {
            using (TargetedMonsterAttackHelper.ForceTargets(Creature, players))
            {
                await IndiscriminateAttackBlockBreaker.BreakBlockBeforeAttack(this, damage, players);
                AttackCommand attack = await DamageCmd.Attack(damage)
                    .FromMonster(this)
                    .WithAttackerAnim(anim, AbnormalityAnimHelper.DefaultAttackSegmentDelaySeconds)
                    .WithHitFx("vfx/vfx_attack_slash")
                    .SpawningHitVfxOnEachCreature()
                    .WithIndiscriminateBlockBreak(this, damage, players)
                    .Execute(null);
                results.AddRange(AttackCommandCompat.Results(attack));
            }
        }

        return GroupAttackOutcome.From(players, results);
    }

    private IEnumerable<Creature> LivingPlayers() =>
        Creature.CombatState?.LivingPlayerCreatures().ToArray() ?? [];

    private IEnumerable<Creature> LivingEnemies() =>
        Creature.CombatState?.LivingEnemies().ToArray() ?? [];

    private bool HasLivingBloodBat() =>
        Creature.CombatState?.Enemies.Any(static enemy => enemy.IsAlive && enemy.Monster is BloodBat) ?? false;

    private bool TryGetBloodBatSlot(out string slot)
    {
        slot = string.Empty;
        CombatStateLike? combatState = Creature.CombatState;
        if (combatState?.Encounter is not NosferatuElite)
        {
            return false;
        }

        foreach (string candidate in NosferatuElite.BatSlots)
        {
            bool occupied = combatState.Enemies.Any(enemy => enemy.IsAlive && enemy.SlotName == candidate);
            if (!occupied)
            {
                slot = candidate;
                return true;
            }
        }

        return false;
    }

    private void AdvanceCycle()
    {
        string[] cycle = _isTransformed ? TransformedCycle : NormalCycle;
        _cycleIndex = (_cycleIndex + 1) % cycle.Length;
    }

    private void AddPageRewardsFromDeathHook()
    {
        if (Creature.CombatState?.RunState.CurrentRoom is not CombatRoom room
            || room.Encounter is not NosferatuElite)
        {
            return;
        }

        AbnormalityPageRewardHelper.AddPageRewardForEachPlayer<NosferatuPageRelic>(room, NosferatuPageRelicTitleLocKey);
    }

    private IEnumerable<AbstractIntent> EnumerateIntentAssets()
    {
        MonsterMoveStateMachine stateMachine = GenerateMoveStateMachine();
        foreach (MonsterState state in stateMachine.States.Values)
        {
            if (state is MoveState moveState)
            {
                foreach (AbstractIntent intent in moveState.Intents)
                {
                    yield return intent;
                }
            }
        }
    }

    private sealed record GroupAttackOutcome(
        IReadOnlyList<Creature> Targets,
        IReadOnlyList<DamageResult> Results)
    {
        public bool AnyUnblocked => Results.Any(static result => result.UnblockedDamage > 0);

        public IEnumerable<DamageResult> UnblockedResults =>
            Results.Where(static result => result.UnblockedDamage > 0);

        public IEnumerable<Creature> UnblockedTargets =>
            UnblockedResults.Select(static result => result.Receiver);

        public static GroupAttackOutcome From(
            IReadOnlyList<Creature> targets,
            IReadOnlyList<DamageResult> results) =>
            new(targets, results);
    }
}
