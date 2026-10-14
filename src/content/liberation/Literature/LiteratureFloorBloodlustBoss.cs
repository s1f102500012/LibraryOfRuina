using LibraryLib.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using LibraryOfRuina.content.liberation.History;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.framework.audio;
using LibraryOfRuina.framework.combat;
using LibraryOfRuina.framework.intents;
using LibraryOfRuina.framework.monsters;
using LibraryOfRuina.framework.visuals;
using LibraryOfRuina.patches;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace LibraryOfRuina.content.liberation.Literature;

public sealed class LiteratureFloorBloodlustBoss :
    LiberationPhaseBossMonster
{
    public const int Phase = 3;
    public const string PersistenceMoveId = "PERSISTENCE";
    public const string ObsessionMoveId = "OBSESSION";
    public const string DesireBurstMoveId = "DESIRE_BURST";
    public const string UnbearableMoveId = "UNBEARABLE";

    public const int PersistenceHits = 2; // 执著：攻击次数（单次伤害见 PersistenceDamage），各进阶相同。
    public const int ObsessionHits = 1; // 执念：攻击次数（单次伤害见 ObsessionDamage），各进阶相同。
    public const int DesireBurstHits = 3; // 欲望迸发：攻击次数（单次伤害见 DesireBurstDamage），各进阶相同。
    public const int DesireBurstHealMultiplier = 3; // 欲望迸发：回复生命 = 招式开始时玩家最高流血层数 × 此倍率，各进阶相同。
    public const int UnbearableHits = 4; // 无法忍受！！！：无差别攻击次数（单次伤害见 UnbearableDamage），各进阶相同。
    public const int UnbearableFinisherHits = 1; // 无法忍受！！！：对每名存活玩家的终结攻击次数，各进阶相同。
    public const int UnbearableBleedMultiplier = 2; // 无法忍受！！！：终结攻击每层流血追加的伤害，各进阶相同。
    public const int ChainsApplied = 2; // 执念：攻击后给予所有存活玩家的魂缚锁链层数，各进阶相同。

    public const string IdleTexturePath = LiteratureFloorAssets.LiberationBloodlustMonsterRoot + "bloodlust_idle.png";
    public const string StrikeTexturePath = LiteratureFloorAssets.LiberationBloodlustMonsterRoot + "bloodlust_strike.png";
    public const string SlashTexturePath = LiteratureFloorAssets.LiberationBloodlustMonsterRoot + "bloodlust_slash.png";
    public const string S1TexturePath = LiteratureFloorAssets.LiberationBloodlustMonsterRoot + "bloodlust_s1.png";
    public const string S2TexturePath = LiteratureFloorAssets.LiberationBloodlustMonsterRoot + "bloodlust_s2.png";
    public const string EvadeTexturePath = LiteratureFloorAssets.LiberationBloodlustMonsterRoot + "bloodlust_evade.png";
    public const string HitTexturePath = LiteratureFloorAssets.LiberationBloodlustMonsterRoot + "bloodlust_hit.png";

    public const string AttackSfxPath =
        LiteratureFloorAssets.LiberationBloodlustSfxRoot + "red_shoes_attack.ogg";
    public const string HorizontalSfxPath =
        LiteratureFloorAssets.LiberationBloodlustSfxRoot + "red_shoes_horizontal.ogg";
    public const string StrongHorizontalSfxPath =
        LiteratureFloorAssets.LiberationBloodlustSfxRoot + "red_shoes_strong_horizontal.ogg";
    public const string StrongVerticalSfxPath =
        LiteratureFloorAssets.LiberationBloodlustSfxRoot + "red_shoes_strong_vertical.ogg";
    public const string StrongFinishSfxPath =
        LiteratureFloorAssets.LiberationBloodlustSfxRoot + "red_shoes_strong_finish.ogg";

    private MoveState? _persistenceState;
    private MoveState? _obsessionState;
    private MoveState? _desireBurstState;
    private MoveState? _unbearableState;

    public override int LiberationPhase => Phase;

    /// <summary>死亡动画时长；转阶段的假死返回 0，见 <see cref="LayeredBossSpine.DeathLength"/>。</summary>
    public override float DeathAnimLengthOverride => LayeredBossSpine.DeathLength(this);

    private int PersistenceDamage =>
        AscensionHelper.GetValueIfAscension(
            AscensionLevel.DeadlyEnemies,
            7,
            6);

    private int ObsessionDamage =>
        AscensionHelper.GetValueIfAscension(
            AscensionLevel.DeadlyEnemies,
            17,
            16);

    private int DesireBurstDamage =>
        AscensionHelper.GetValueIfAscension(
            AscensionLevel.DeadlyEnemies,
            6,
            5);

    private int UnbearableDamage =>
        AscensionHelper.GetValueIfAscension(
            AscensionLevel.DeadlyEnemies,
            4,
            3);

    private int UnbearableFinisherBaseDamage =>
        AscensionHelper.GetValueIfAscension(
            AscensionLevel.DeadlyEnemies,
            15,
            14);

    internal static (int Min, int Max) DebugHpRange(bool toughEnemies) =>
        toughEnemies ? (244, 250) : (230, 238);

    internal static int DebugPersistenceDamage(bool deadlyEnemies) =>
        deadlyEnemies ? 10 : 8;

    internal static int DebugObsessionDamage(bool deadlyEnemies) =>
        deadlyEnemies ? 24 : 20;

    internal static int DebugDesireBurstDamage(bool deadlyEnemies) =>
        deadlyEnemies ? 7 : 5;

    internal static int DebugUnbearableDamage(bool deadlyEnemies) =>
        deadlyEnemies ? 5 : 3;

    internal static int DebugUnbearableFinisherBaseDamage(
        bool deadlyEnemies) => deadlyEnemies ? 16 : 14;

    internal static int CalculateUnbearableFinisherDamage(
        int baseDamage,
        int bleedStacks) =>
        baseDamage
        + Math.Max(0, bleedStacks) * UnbearableBleedMultiplier;

    public override int MinInitialHp =>
        AscensionHelper.GetValueIfAscension(
            AscensionLevel.ToughEnemies,
            200,
            180);

    public override int MaxInitialHp =>
        AscensionHelper.GetValueIfAscension(
            AscensionLevel.ToughEnemies,
            210,
            190);

    public override int DefaultChaoResistance => 80;

    public override bool ShouldDisappearFromDoom => false;

    public override LibraryCreatureResistanceData.Resistance?
        DefaultPhysicalResistanceData => new()
        {
            Slash = LibraryResistanceLevel.Endure,
            Pierce = LibraryResistanceLevel.Normal,
            Blunt = LibraryResistanceLevel.Normal
        };

    public override LibraryCreatureResistanceData.Resistance?
        DefaultChaoResistanceData => new()
        {
            Slash = LibraryResistanceLevel.Endure,
            Pierce = LibraryResistanceLevel.Endure,
            Blunt = LibraryResistanceLevel.Endure
        };

    public override IEnumerable<string> AssetPaths
    {
        get
        {
            var paths = new List<string>
            {
                LiteratureFloorBloodlustCreatureVisuals.ScenePath,
                IdleTexturePath,
                StrikeTexturePath,
                SlashTexturePath,
                S1TexturePath,
                S2TexturePath,
                EvadeTexturePath,
                HitTexturePath,
                AttackSfxPath,
                HorizontalSfxPath,
                StrongHorizontalSfxPath,
                StrongVerticalSfxPath,
                StrongFinishSfxPath,
                LiteratureFloorAssets.LibraryPassiveGreenIcon,
                LiteratureFloorAssets.HistoryFloorCorrosionPowerIcon,
                LiteratureFloorAssets.DeepWoundPowerIcon
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
        LiteratureFloorLiberationBackgroundController.SetPhaseBackground(
            Phase);
        EncounterBgmController.RegisterMonster(Creature);

        if (Creature.CombatState?.Encounter
            is LiteratureFloorLiberationEncounter encounter)
        {
            await encounter.EnsureControllerPowers(Creature.CombatState);
        }

        await PowerCmdCompat.Apply<HistoryFloorCorrosionPower>(
            Creature,
            1m,
            Creature,
            null,
            silent: true);
        await PowerCmdCompat.Apply<
            LiteratureFloorBloodlustGiantAxePassivePower>(
            Creature,
            1m,
            Creature,
            null,
            silent: true);
        await PowerCmdCompat.Apply<
            LiteratureFloorBloodlustGlitterPassivePower>(
            Creature,
            LiteratureFloorBloodlustGlitterPassivePower.Interval,
            Creature,
            null,
            silent: true);
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

        if (Creature.CombatState?.Encounter
            is LiteratureFloorLiberationEncounter encounter)
        {
            await encounter.OnPhaseBossDeath(
                this,
                wasRemovalPrevented,
                deathAnimLength);
        }
    }

    internal async Task<bool> TryQueueUnbearableMove()
    {
        if (_unbearableState == null
            || Creature.IsDead
            || Creature.IsStunned
            || NextMove.StateId == UnbearableMoveId
            || NextMove.StateId == ReviveAndEmpowerMoveId
            || NextMove.StateId == stunnedMoveId
            || Creature.CombatState?.Encounter
                is not LiteratureFloorLiberationEncounter
                {
                    CurrentPhase: Phase,
                    PhaseComplete: false,
                    TransitionPending: false
                })
        {
            return false;
        }

        SetMoveImmediate(_unbearableState, forceTransition: true);
        if (CombatQueries.CreatureNodeOf(this) is { } node)
        {
            await node.RefreshIntents();
        }

        return true;
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        _persistenceState = new MoveState(
            PersistenceMoveId,
            PersistenceMove,
            new MultiAttackIntent(PersistenceDamage, PersistenceHits));
        _obsessionState = new MoveState(
            ObsessionMoveId,
            ObsessionMove,
            new CombinedAttackDebuffIntent(
                ObsessionDamage,
                ObsessionHits,
                "LITERATURE_FLOOR_BLOODLUST_OBSESSION.description",
                IntentBadge.FromPower<ChainsOfBindingPower>(
                    ChainsApplied)),
            new CardDebuffIntent());
        _desireBurstState = new MoveState(
            DesireBurstMoveId,
            DesireBurstMove,
            new MultiAttackIntent(DesireBurstDamage, DesireBurstHits),
            new HealIntent());
        var unbearableIntent = new IndiscriminateAttackIntent(
            () => UnbearableDamage,
            () => UnbearableHits,
            "LITERATURE_FLOOR_BLOODLUST_UNBEARABLE.description");
        _unbearableState = new MoveState(
            UnbearableMoveId,
            unbearableIntent.WithPreAttackBlockBreak(this, UnbearableMove),
            unbearableIntent,
            new LocalPreviewAttackIntent(
                (_, previewTarget) =>
                    CalculateProjectedUnbearableFinisherDamage(
                        previewTarget),
                () => UnbearableFinisherHits,
                "LITERATURE_FLOOR_BLOODLUST_UNBEARABLE_FINISHER.description"));
        MoveState reviveAndEmpower = CreateReviveAndEmpowerState();

        var normalRandom = new RandomBranchState("NORMAL_RANDOM");
        normalRandom.AddBranch(
            _persistenceState,
            MoveRepeatType.CannotRepeat);
        normalRandom.AddBranch(
            _obsessionState,
            MoveRepeatType.CannotRepeat);
        normalRandom.AddBranch(
            _desireBurstState,
            MoveRepeatType.CannotRepeat);

        _persistenceState.FollowUpState = normalRandom;
        _obsessionState.FollowUpState = normalRandom;
        _desireBurstState.FollowUpState = normalRandom;
        _unbearableState.FollowUpState = normalRandom;
        reviveAndEmpower.FollowUpState = normalRandom;

        return new MonsterMoveStateMachine(
            [
                _persistenceState,
                _obsessionState,
                _desireBurstState,
                _unbearableState,
                reviveAndEmpower,
                normalRandom
            ],
            normalRandom);
    }

    private Task PersistenceMove(IReadOnlyList<Creature> targets) =>
        ExecuteTimedMultiAttack(
            PersistenceDamage,
            PersistenceHits,
            "Persistence",
            LiteratureFloorBloodlustAnimationContract
                .PersistenceHitFrameTimesSeconds,
            LiteratureFloorBloodlustAnimationContract
                .PersistenceDurationSeconds,
            [AttackSfxPath, HorizontalSfxPath]);

    private async Task ObsessionMove(IReadOnlyList<Creature> targets)
    {
        await ExecuteTimedMultiAttack(
            ObsessionDamage,
            ObsessionHits,
            "Obsession",
            LiteratureFloorBloodlustAnimationContract
                .ObsessionHitFrameTimesSeconds,
            LiteratureFloorBloodlustAnimationContract
                .ObsessionDurationSeconds,
            [HorizontalSfxPath]);

        Creature[] players = LivingPlayers();
        if (players.Length > 0)
        {
            await PowerCmdCompat.ApplyDebuff<ChainsOfBindingPower>(
                players,
                ChainsApplied,
                Creature,
                null);
        }
    }

    private async Task DesireBurstMove(IReadOnlyList<Creature> targets)
    {
        int maxBleedAtMoveStart = LivingPlayers()
            .Select(GetBleedStacks)
            .DefaultIfEmpty(0)
            .Max();

        await ExecuteTimedMultiAttack(
            DesireBurstDamage,
            DesireBurstHits,
            "DesireBurst",
            LiteratureFloorBloodlustAnimationContract
                .DesireBurstHitFrameTimesSeconds,
            LiteratureFloorBloodlustAnimationContract
                .DesireBurstDurationSeconds,
            [AttackSfxPath, HorizontalSfxPath, AttackSfxPath]);

        int heal = maxBleedAtMoveStart * DesireBurstHealMultiplier;
        if (heal > 0)
        {
            await CreatureCmd.Heal(
                Creature,
                MultiplayerScalingPatchHelper.ScaleMonsterHealAmount(
                    Creature,
                    heal));
        }
    }

    private async Task UnbearableMove(IReadOnlyList<Creature> targets)
    {
        Creature.GetPower<LiteratureFloorBloodlustGlitterPassivePower>()
            ?.MarkSpecialUsed();

        await CreatureCmd.TriggerAnim(Creature, "Unbearable", 0f);
        float previousHitTime = 0f;
        for (int hitIndex = 0; hitIndex < UnbearableHits; hitIndex++)
        {
            float hitTime = LiteratureFloorBloodlustAnimationContract
                .UnbearableHitFrameTimesSeconds[hitIndex];
            await Cmd.Wait(Math.Max(0f, hitTime - previousHitTime));
            previousHitTime = hitTime;

            LocalOggOneShotPlayer.Play(
                hitIndex % 2 == 0
                    ? StrongVerticalSfxPath
                    : StrongHorizontalSfxPath,
                -1.5f);

            await DamageCmd.Attack(UnbearableDamage)
                .FromMonster(this)
                .WithNoAttackerAnim()
                .WithHitFx("vfx/vfx_attack_slash")
                .Execute(null);
        }

        float finisherHitTime = LiteratureFloorBloodlustAnimationContract
            .UnbearableFinisherHitTimeSeconds;
        await Cmd.Wait(Math.Max(0f, finisherHitTime - previousHitTime));
        previousHitTime = finisherHitTime;
        LocalOggOneShotPlayer.Play(StrongFinishSfxPath, -1f);

        Creature[] finisherTargets = LivingPlayers();
        foreach (Creature target in finisherTargets)
        {
            int damage = CalculateUnbearableFinisherDamage(
                UnbearableFinisherBaseDamage,
                GetBleedStacks(target));
            using var forcedTargetScope =
                TargetedMonsterAttackHelper.ForceTargets(
                    Creature,
                    [target]);
            await DamageCmd.Attack(damage)
                .FromMonster(this)
                .WithNoAttackerAnim()
                .WithHitFx("vfx/vfx_attack_slash")
                .Execute(null);
        }

        foreach (Creature player in Creature.CombatState?.PlayerCreatures
                     .ToArray() ?? [])
        {
            if (player.GetPower<LibraryBleedingPower>() is { } bleed)
            {
                await PowerCmd.Remove(bleed);
            }
        }

        await Cmd.Wait(Math.Max(
            0f,
            LiteratureFloorBloodlustAnimationContract
                .UnbearableDurationSeconds - previousHitTime));
    }

    protected override Task PlayReviveAndEmpowerAnimation()
    {
        return CreatureCmd.TriggerAnim(
            Creature,
            "Cast",
            LiteratureFloorBloodlustAnimationContract
                .CastDurationSeconds);
    }

    protected override Task CompleteLiberationPhaseTransition() =>
        Creature.CombatState?.Encounter
            is LiteratureFloorLiberationEncounter encounter
            ? encounter.CompletePhaseTransition(this)
            : Task.CompletedTask;

    private async Task ExecuteTimedMultiAttack(
        int damage,
        int hitCount,
        string animation,
        IReadOnlyList<float> hitTimes,
        float totalDuration,
        IReadOnlyList<string> hitSfxPaths)
    {
        await CreatureCmd.TriggerAnim(Creature, animation, 0f);
        int hitIndex = 0;
        float previousHitTime = 0f;
        await DamageCmd.Attack(damage)
            .WithHitCount(hitCount)
            .FromMonster(this)
            .WithNoAttackerAnim()
            .WithHitFx("vfx/vfx_attack_slash")
            .BeforeDamage(async () =>
            {
                if (hitIndex >= hitTimes.Count)
                {
                    return;
                }

                float hitTime = hitTimes[hitIndex];
                await Cmd.Wait(Math.Max(0f, hitTime - previousHitTime));
                previousHitTime = hitTime;
                if (hitIndex < hitSfxPaths.Count)
                {
                    LocalOggOneShotPlayer.Play(
                        hitSfxPaths[hitIndex],
                        -1.5f);
                }
                hitIndex++;
            })
            .Execute(null);
        await Cmd.Wait(Math.Max(0f, totalDuration - previousHitTime));
    }

    private int CalculateProjectedUnbearableFinisherDamage(
        Creature? previewTarget)
    {
        return CalculateUnbearableFinisherDamage(
            UnbearableFinisherBaseDamage,
            previewTarget == null ? 0 : GetBleedStacks(previewTarget));
    }

    private Creature[] LivingPlayers() =>
        Creature.CombatState?.LivingPlayerCreatures()
            .OrderBy(static player => player.Player?.NetId ?? 0UL)
            .ToArray() ?? [];

    internal static int GetBleedStacks(Creature creature) =>
        creature.GetPower<LibraryBleedingPower>()?.Amount ?? 0;

    private IEnumerable<AbstractIntent> EnumerateIntentAssets()
    {
        MonsterMoveStateMachine machine = GenerateMoveStateMachine();
        foreach (MoveState move in machine.States.Values.OfType<MoveState>())
        {
            foreach (AbstractIntent intent in move.Intents)
            {
                yield return intent;
            }
        }
    }
}
