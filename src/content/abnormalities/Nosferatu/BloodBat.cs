using System;
using System.Linq;
using System.Threading.Tasks;
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
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace LibraryOfRuina.content.abnormalities.Nosferatu;

public abstract class NosferatuBloodBatBase : LorMonsterModel
{
    public override float DeathAnimLengthOverride => LayeredBossSpine.DeathLength(this);

    protected const string ThirstMoveId = "THIRST";
    protected const string VampirismMoveId = "VAMPIRISM";
    protected const string DeepFangsMoveId = "DEEP_FANGS";

    private const int ThirstRapidWear = 3; // 干渴：给予所有存活玩家的易损层数，各进阶相同。
    private const int ThirstRapidWearTurns = 1; // 干渴：易损的持续回合数，各进阶相同。
    private const int ThirstBlood = 2; // 干渴：自身获得的鲜血层数，各进阶相同。
    private const int VampirismHits = 1; // 吸血：攻击次数（单次伤害见 VampirismDamage），各进阶相同。
    private const int VampirismBleed = 4; // 吸血：给予受到未被格挡伤害的玩家的流血层数，各进阶相同。
    private const int VampirismBlood = 1; // 吸血：造成未被格挡伤害时自身获得的鲜血层数，各进阶相同。
    private const int DeepFangsHits = 2; // 尖牙深入：攻击次数（单次伤害见 DeepFangsDamage），各进阶相同。
    private const int DeepFangsBleed = 6; // 尖牙深入：给予受到未被格挡伤害的玩家的流血层数，各进阶相同。

    internal const string TextureRoot = Nosferatu.TextureRoot;

    internal static readonly string[] SharedAssetPaths =
        BloodBatCreatureVisuals.Profile.AssetPaths
            .Concat(
            [
                NosferatuAssets.NosferatuSfxRoot + "blood_bat_attack.ogg",
                NosferatuAssets.BloodPowerIcon,
                NosferatuAssets.HydrophobiaPassivePowerIcon
            ])
            .ToArray();

    protected abstract string RightBatSlotName { get; }

    public override LibraryCreatureResistanceData.Resistance?
        DefaultPhysicalResistanceData => new()
        {
            Slash = LibraryResistanceLevel.Normal,
            Pierce = LibraryResistanceLevel.Normal,
            Blunt = LibraryResistanceLevel.Endure
        };

    public override LibraryCreatureResistanceData.Resistance?
        DefaultChaoResistanceData => new()
        {
            Slash = LibraryResistanceLevel.Normal,
            Pierce = LibraryResistanceLevel.Normal,
            Blunt = LibraryResistanceLevel.Endure
        };

    public override IEnumerable<string> AssetPaths =>
        SharedAssetPaths
            .Concat(EnumerateIntentAssets().SelectMany(static intent =>
                intent.AssetPaths))
            .Distinct();

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        EncounterBgmController.RegisterMonster(Creature);
        await ApplyHydrophobiaPassive();
        await PowerCmdCompat.Apply<MinionPower>(
            Creature,
            1,
            Creature,
            null,
            silent: true);
    }

    protected abstract Task ApplyHydrophobiaPassive();

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var thirst = new MoveState(
            ThirstMoveId,
            ThirstMove,
            new BadgedDebuffIntent(
                [
                    IntentBadge.RapidWear(ThirstRapidWear, ThirstRapidWearTurns),
                    IntentBadge.FromPower<NosferatuBloodPower>(ThirstBlood)
                ],
                amount: ThirstRapidWear,
                "BLOOD_BAT_THIRST.description"));
        var vampirism = new MoveState(
            VampirismMoveId,
            VampirismMove,
            new CombinedAttackDebuffIntent(
                () => VampirismDamage,
                () => VampirismHits,
                "BLOOD_BAT_VAMPIRISM.description",
                IntentBadge.Bleed(VampirismBleed),
                IntentBadge.FromPower<NosferatuBloodPower>(VampirismBlood)));
        var deepFangs = new MoveState(
            DeepFangsMoveId,
            DeepFangsMove,
            new CombinedAttackDebuffIntent(
                () => DeepFangsDamage,
                () => DeepFangsHits,
                "BLOOD_BAT_DEEP_FANGS.description",
                IntentBadge.Bleed(DeepFangsBleed)));

        var router = new DelegatingMonsterRouterState(
            "BLOOD_BAT_ROUTER",
            (_, _) => ResolvePlannedMoveId());
        thirst.FollowUpState = router;
        vampirism.FollowUpState = router;
        deepFangs.FollowUpState = router;

        return new MonsterMoveStateMachine(
            [thirst, vampirism, deepFangs, router],
            router);
    }

    internal string ResolvePlannedMoveId()
    {
        string[] cycle = [ThirstMoveId, VampirismMoveId, DeepFangsMoveId];
        int round = Math.Max(0, Creature.CombatState?.RoundNumber ?? 0);
        int offset = Creature.SlotName == RightBatSlotName ? 1 : 0;
        return cycle[(round + offset) % cycle.Length];
    }

    private int VampirismDamage =>
        AscensionHelper.GetValueIfAscension(
            AscensionLevel.DeadlyEnemies,
            12,
            9);

    private int DeepFangsDamage =>
        AscensionHelper.GetValueIfAscension(
            AscensionLevel.DeadlyEnemies,
            6,
            5);

    private async Task ThirstMove(IReadOnlyList<Creature> targets)
    {
        await AbnormalityAnimHelper.TriggerCast(Creature);
        foreach (Creature player in LivingPlayers())
        {
            await LibraryPowerCmd.Apply<LibraryVulnerablePower>(
                    player,
                    ThirstRapidWear,
                    turns: ThirstRapidWearTurns,
                    Creature,
                    null);
        }

        await NosferatuBloodPower.Change(Creature, ThirstBlood, Creature);
    }

    private async Task VampirismMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(
            NosferatuAssets.NosferatuSfxRoot + "blood_bat_attack.ogg",
            -2f);
        GroupAttackOutcome attack = await ExecuteAttackDetailed(
            VampirismDamage,
            hits: VampirismHits,
            "Attack");
        foreach (Creature target in attack.UnblockedTargets.Distinct())
        {
            await PowerCmdCompat.ApplyDebuff<LibraryBleedingPower>(
                target,
                VampirismBleed,
                Creature,
                null);
        }

        if (attack.AnyUnblocked)
        {
            await NosferatuBloodPower.Change(Creature, VampirismBlood, Creature);
        }
    }

    private async Task DeepFangsMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(
            NosferatuAssets.NosferatuSfxRoot + "blood_bat_attack.ogg",
            -2f);
        GroupAttackOutcome attack = await ExecuteAttackDetailed(
            DeepFangsDamage,
            hits: DeepFangsHits,
            "Attack");
        foreach (Creature target in attack.UnblockedTargets.Distinct())
        {
            await PowerCmdCompat.ApplyDebuff<LibraryBleedingPower>(
                target,
                DeepFangsBleed,
                Creature,
                null);
        }
    }

    private async Task<GroupAttackOutcome> ExecuteAttackDetailed(
        int damage,
        int hits,
        string anim)
    {
        AttackCommand attack = await DamageCmd.Attack(damage)
            .FromMonster(this)
            .WithHitCount(hits)
            .OnlyPlayAnimOnce()
            .WithAttackerAnim(
                anim,
                AbnormalityAnimHelper.DefaultAttackSegmentDelaySeconds)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);
        return GroupAttackOutcome.From(
            Creature.CombatState?.LivingPlayerCreatures()
                .ToArray()
            ?? [],
            AttackCommandCompat.Results(attack).ToArray());
    }

    private IEnumerable<Creature> LivingPlayers() =>
        Creature.CombatState?.LivingPlayerCreatures()
            .ToArray()
        ?? [];

    private IEnumerable<AbstractIntent> EnumerateIntentAssets()
    {
        MonsterMoveStateMachine stateMachine = GenerateMoveStateMachine();
        foreach (MonsterState state in stateMachine.States.Values)
        {
            if (state is not MoveState moveState)
            {
                continue;
            }

            foreach (AbstractIntent intent in moveState.Intents)
            {
                yield return intent;
            }
        }
    }

    private sealed record GroupAttackOutcome(
        IReadOnlyList<Creature> Targets,
        IReadOnlyList<DamageResult> Results)
    {
        public bool AnyUnblocked =>
            Results.Any(static result => result.UnblockedDamage > 0);

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

public sealed class BloodBat : NosferatuBloodBatBase
{
    public static readonly string[] AssetPathsStatic = SharedAssetPaths;

    protected override string RightBatSlotName =>
        NosferatuElite.RightBatSlot;

    public override int MinInitialHp =>
        AscensionHelper.GetValueIfAscension(
            AscensionLevel.ToughEnemies,
            110,
            85);

    public override int MaxInitialHp =>
        AscensionHelper.GetValueIfAscension(
            AscensionLevel.ToughEnemies,
            115,
            90);

    public override int DefaultChaoResistance => 70;

    protected override Task ApplyHydrophobiaPassive() =>
        PowerCmdCompat.Apply<NosferatuHydrophobiaPassivePower>(
            Creature,
            1m,
            Creature,
            null,
            silent: true);
}
