using System;
using System.Linq;
using System.Threading.Tasks;
using LibraryLib.Entities.Creatures;
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
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.ValueProps;

namespace LibraryOfRuina.content.abnormalities.QueenBee;

public sealed class QueenBeeWorker : LorMonsterModel
{
    public override float DeathAnimLengthOverride => LayeredBossSpine.DeathLength(this);

    private const string GuardQueenMoveId = "QUEEN_BEE_WORKER_GUARD_QUEEN";
    private const string CarryLarvaMoveId = "QUEEN_BEE_WORKER_CARRY_LARVA";
    private const string NutrientMixMoveId = "QUEEN_BEE_WORKER_NUTRIENT_MIX";
    private const string PromoteGrowthMoveId = "QUEEN_BEE_WORKER_PROMOTE_GROWTH";
    private const string DeathEmbraceMoveId = "QUEEN_BEE_WORKER_DEATH_EMBRACE";
    private const string CadenceStateId = "QUEEN_BEE_WORKER_CADENCE";
    internal const string ChooserStateId = "QUEEN_BEE_WORKER_COND";

    private const int CarryLarvaBlock = 8;
    private const int PromoteGrowthHits = 3; // 促进增长：攻击次数（单次伤害见 PromoteGrowthDamage），各进阶相同。
    private const int DeathEmbraceDamage = 15;
    private const int DeathEmbraceChao = 0;
    private const decimal NutrientMixHealPercent = 0.05m;
    private const float SegmentDelaySeconds = 0.78f;

    public const int StaggerResistance = 30;

    // 促进增长：每段攻击的动画触发与音效，按段数循环取用。
    private static readonly (string Trigger, string SfxPath)[] PromoteGrowthSegments =
    [
        ("Attack", HistoryFloorWorkerBee.AttackSlashSfxPath),
        ("Attack2", HistoryFloorWorkerBee.AttackSlashSfxPath),
        ("Attack", HistoryFloorWorkerBee.AttackThrustSfxPath)
    ];

    private MoveState? _deathEmbraceState;
    private MoveState? _promoteGrowthState;
    private int _cadenceIndex;

    public override int DefaultChaoResistance => StaggerResistance;

    public override LibraryCreatureResistanceData.Resistance? DefaultPhysicalResistanceData => new()
    {
        Blunt = LibraryResistanceLevel.Normal,
        Pierce = LibraryResistanceLevel.Normal,
        Slash = LibraryResistanceLevel.Vulnerable
    };

    public override LibraryCreatureResistanceData.Resistance? DefaultChaoResistanceData => new()
    {
        Blunt = LibraryResistanceLevel.Vulnerable,
        Pierce = LibraryResistanceLevel.Normal,
        Slash = LibraryResistanceLevel.Normal
    };

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 40, 27);

    public override int MaxInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 43, 29);

    private static int GuardQueenDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 9, 8);

    private static int PromoteGrowthDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 3, 2);

    public override IEnumerable<string> AssetPaths =>
        QueenBeeWorkerCreatureVisuals.Profile.AssetPaths
            .Concat(
            [
                HistoryFloorWorkerBee.AttackThrustSfxPath,
                HistoryFloorWorkerBee.AttackSlashSfxPath,
                HistoryFloorWorkerBee.DodgeSfxPath,
                HistoryFloorWorkerBee.SpawnSfxPath,
                HistoryFloorWorkerBee.SporeSfxPath
            ])
            .Concat(EnumerateIntentAssets().SelectMany(static intent => intent.AssetPaths))
            .Distinct();

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        await PowerCmdCompat.Apply<MinionPower>(
            Creature,
            1m,
            Creature,
            null,
            silent: true);
        await PowerCmdCompat.Apply<QueenBeeWorkerDeathEmbracePassivePower>(
            Creature,
            1m,
            Creature,
            null,
            silent: true);
    }

    public void ConfigurePattern(int startIndex)
    {
        AssertMutable();
        _cadenceIndex = startIndex;
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        _deathEmbraceState = null;
        _promoteGrowthState = null;

        var guardQueen = new MoveState(
            GuardQueenMoveId,
            GuardQueenMove,
            new SingleAttackIntent(GuardQueenDamage));

        var carryLarva = new MoveState(
            CarryLarvaMoveId,
            CarryLarvaMove,
            new DefendIntent());

        var nutrientMix = new MoveState(
            NutrientMixMoveId,
            NutrientMixMove,
            new HealIntent());

        var promoteGrowth = new MoveState(
            PromoteGrowthMoveId,
            PromoteGrowthMove,
            new MultiAttackIntent(PromoteGrowthDamage, PromoteGrowthHits));
        _promoteGrowthState = promoteGrowth;

        var deathEmbrace = new MoveState(
            DeathEmbraceMoveId,
            DeathEmbraceMove,
            new QueenBeeDeathEmbraceIntent(DeathEmbraceDamage));
        _deathEmbraceState = deathEmbrace;

        var cadence = new ConditionalBranchState(CadenceStateId);
        cadence.AddState(guardQueen, () => _cadenceIndex % 4 == 0);
        cadence.AddState(carryLarva, () => _cadenceIndex % 4 == 1);
        cadence.AddState(nutrientMix, () => _cadenceIndex % 4 == 2);
        cadence.AddState(promoteGrowth, static () => true);

        var chooser = new ConditionalBranchState(ChooserStateId);
        chooser.AddState(deathEmbrace, ShouldUseDeathEmbrace);
        chooser.AddState(cadence, static () => true);

        guardQueen.FollowUpState = chooser;
        carryLarva.FollowUpState = chooser;
        nutrientMix.FollowUpState = chooser;
        promoteGrowth.FollowUpState = chooser;
        deathEmbrace.FollowUpState = chooser;

        return new MonsterMoveStateMachine(
            new MonsterState[]
            {
                guardQueen,
                carryLarva,
                nutrientMix,
                promoteGrowth,
                deathEmbrace,
                cadence,
                chooser
            },
            chooser);
    }

    private bool ShouldUseDeathEmbrace()
    {
        Creature? queen = FindQueen();
        return queen is { IsAlive: true }
            && queen.CurrentHp <= queen.MaxHp * QueenBee.DeathEmbraceHpThresholdPercent;
    }

    public async Task ForceDeathEmbraceIfNeeded()
    {
        if (Creature.IsDead
            || !ShouldUseDeathEmbrace()
            || _deathEmbraceState == null)
        {
            return;
        }

        bool moveChanged = !ReferenceEquals(NextMove, _deathEmbraceState);
        if (moveChanged)
        {
            SetMoveImmediate(_deathEmbraceState, forceTransition: true);
        }

        NCreature? creatureNode = CombatQueries.CreatureNodeOf(this);
        if (creatureNode?.Visuals is QueenBeeWorkerCreatureVisuals visuals)
        {
            visuals.FaceQueen();
        }

        if (moveChanged && creatureNode != null)
        {
            await creatureNode.RefreshIntents();
        }
    }

    internal void ForcePromoteGrowthNextTurn()
    {
        if (Creature.IsDead || _promoteGrowthState == null)
        {
            return;
        }

        QueenBee? queen = CombatState?.Enemies
            .Select(static enemy => enemy.Monster)
            .OfType<QueenBee>()
            .FirstOrDefault(static candidate => candidate.Creature is { IsAlive: true });
        if (queen == null)
        {
            return;
        }

        if (ReferenceEquals(NextMove, _promoteGrowthState))
        {
            // Already performing the multiattack this turn; consume the token so the
            // second worker cannot also force the same move.
            queen.TryClaimWorkerPromoteGrowthNextTurn();
            return;
        }

        if (ShouldUseDeathEmbrace()
            || ReferenceEquals(NextMove, _deathEmbraceState)
            || !queen.TryClaimWorkerPromoteGrowthNextTurn())
        {
            return;
        }

        SetMoveImmediate(_promoteGrowthState, forceTransition: true);
        NCreature? creatureNode = CombatQueries.CreatureNodeOf(this);
        if (creatureNode != null)
        {
            TaskHelper.RunSafely(creatureNode.RefreshIntents());
        }
    }

    private Creature? FindQueen()
    {
        return CombatState?.Enemies
            .FirstOrDefault(static enemy => enemy.IsAlive && enemy.Monster is QueenBee);
    }

    private async Task GuardQueenMove(IReadOnlyList<Creature> targets)
    {
        await ExecuteAttackSegment("Attack", GuardQueenDamage, HistoryFloorWorkerBee.AttackThrustSfxPath);
        AdvanceCadence();
    }

    private async Task CarryLarvaMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(HistoryFloorWorkerBee.DodgeSfxPath, -2f);
        await CreatureCmd.TriggerAnim(Creature, "Defend", 0.45f);
        await CreatureCmd.GainBlock(Creature, CarryLarvaBlock, ValueProp.Move, null);
        AdvanceCadence();
    }

    private async Task NutrientMixMove(IReadOnlyList<Creature> targets)
    {
        Creature? queen = FindQueen();
        if (queen == null)
        {
            return;
        }

        LocalOggOneShotPlayer.Play(HistoryFloorWorkerBee.AttackThrustSfxPath, -2f);
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.45f);
        int healAmount = Math.Max(
            1,
            (int)(queen.MaxHp * NutrientMixHealPercent));
        await CreatureCmd.Heal(
            queen,
            MultiplayerScalingPatchHelper.ScaleMonsterHealAmount(queen, healAmount));
        AdvanceCadence();
    }

    private async Task PromoteGrowthMove(IReadOnlyList<Creature> targets)
    {
        for (int hit = 0; hit < PromoteGrowthHits; hit++)
        {
            (string trigger, string sfxPath) = PromoteGrowthSegments[hit % PromoteGrowthSegments.Length];
            await ExecuteAttackSegment(trigger, PromoteGrowthDamage, sfxPath);
        }

        AdvanceCadence();
    }

    private async Task DeathEmbraceMove(IReadOnlyList<Creature> targets)
    {
        Creature? queen = FindQueen();
        if (queen == null)
        {
            return;
        }

        QueenBeeWorkerCreatureVisuals? visuals =
            CombatQueries.CreatureNodeOf(this)?.Visuals
                as QueenBeeWorkerCreatureVisuals;
        visuals?.FaceQueen();
        LocalOggOneShotPlayer.Play(HistoryFloorWorkerBee.AttackThrustSfxPath, -2f);
        using (TargetedMonsterAttackHelper.ForceTargets(Creature, [queen]))
        {
            await DamageCmd.Attack(DeathEmbraceDamage)
                .FromMonster(this)
                .WithAttackerAnim("Attack2", SegmentDelaySeconds)
                .WithHitFx("vfx/vfx_attack_slash")
                .Execute(null);
        }

        if (queen is LibraryCreature queenLc)
        {
            await LibraryCreatureCmd.SetCurrentChaoValue(queenLc, DeathEmbraceChao);
        }

        AdvanceCadence();
        visuals?.FacePlayers();
    }

    private void AdvanceCadence()
    {
        _cadenceIndex++;
    }

    private async Task ExecuteAttackSegment(
        string trigger,
        int damage,
        string sfxPath)
    {
        LocalOggOneShotPlayer.Play(sfxPath, -2f);
        await DamageCmd.Attack(damage)
            .FromMonster(this)
            .WithAttackerAnim(trigger, SegmentDelaySeconds)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);
    }

    private IEnumerable<AbstractIntent> EnumerateIntentAssets()
    {
        yield return new SingleAttackIntent(GuardQueenDamage);
        yield return new DefendIntent();
        yield return new HealIntent();
        yield return new MultiAttackIntent(PromoteGrowthDamage, PromoteGrowthHits);
        yield return new QueenBeeDeathEmbraceIntent(DeathEmbraceDamage);
    }
}
