using System.Linq;
using System.Threading.Tasks;
using Godot;
using LibraryOfRuina.content.abnormalities.DeadButterfly;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.features.moontext;
using LibraryOfRuina.framework.audio;
using LibraryOfRuina.framework.combat;
using LibraryOfRuina.framework.intents;
using LibraryOfRuina.framework.monsters;
using LibraryOfRuina.framework.powers;
using LibraryOfRuina.framework.relics;
using LibraryOfRuina.framework.visuals;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;

namespace LibraryOfRuina.content.abnormalities.FuneralOfTheDeadButterflies;

public sealed class FuneralOfTheDeadButterflies : LorMonsterModel
{
    public override float DeathAnimLengthOverride => LayeredBossSpine.DeathLength(this);

    private const int MaxSummonedButterflies = 4;
    private const float AnimationDurationScale = 2.25f;
    private const string Move1Id = "FUNERAL_GUNFIRE";
    private const string Move2Id = "BUTTERFLY_CROSSING";
    private const string Move3Id = "SALVATION_HAND";
    private const string Move4Id = "FUNERAL_SHOT";
    private const int ButterflyCrossingHits = 3; // 蝶群穿行：攻击次数（单次伤害见 Move2Damage），各进阶相同。
    private const int SalvationHandWeak = 2; // 救赎之手：给予目标的虚弱层数，各进阶相同。
    private const int SalvationHandStrength = 1; // 救赎之手：所有存活敌人获得的力量，各进阶相同。

    public const string IdleTexturePath = FuneralOfTheDeadButterfliesAssets.FuneralOfTheDeadButterfliesIdleTexture;
    public const string CoffinPrepareTexturePath = FuneralOfTheDeadButterfliesAssets.CoffinPrepareTexture;
    public const string CoffinAttackTexturePath = FuneralOfTheDeadButterfliesAssets.CoffinAttackTexture;
    public const string FireBlackTexturePath = FuneralOfTheDeadButterfliesAssets.FireBlackTexture;
    public const string FireWhiteTexturePath = FuneralOfTheDeadButterfliesAssets.FireWhiteTexture;
    public const string HitTexturePath = FuneralOfTheDeadButterfliesAssets.FuneralOfTheDeadButterfliesHitTexture;
    public const string WhiteFilterTexturePath = FuneralOfTheDeadButterfliesAssets.FuneralWhiteFilterOverlayTexture;

    public const string AttackBlackSfxPath = FuneralOfTheDeadButterfliesAssets.FuneralAttackBlackSfx;
    public const string AttackWhiteSfxPath = FuneralOfTheDeadButterfliesAssets.FuneralAttackWhiteSfx;
    public const string StrongPrepareSfxPath = FuneralOfTheDeadButterfliesAssets.FuneralStrongPrepareSfx;
    public const string ParrySfxPath = FuneralOfTheDeadButterfliesAssets.FuneralParrySfx;
    public const string StunSfxPath = FuneralOfTheDeadButterfliesAssets.FuneralStunSfx;

    private static readonly string[] NormalBackgroundTextLineKeys =
    [
        "FUNERAL_OF_THE_DEAD_BUTTERFLIES.backgroundText.normal.0",
        "FUNERAL_OF_THE_DEAD_BUTTERFLIES.backgroundText.normal.1",
        "FUNERAL_OF_THE_DEAD_BUTTERFLIES.backgroundText.normal.2",
        "FUNERAL_OF_THE_DEAD_BUTTERFLIES.backgroundText.normal.3",
        "FUNERAL_OF_THE_DEAD_BUTTERFLIES.backgroundText.normal.4",
    ];

    private static readonly string[] FourthTurnBackgroundTextLineKeys =
    [
        "FUNERAL_OF_THE_DEAD_BUTTERFLIES.backgroundText.fourthTurn.0",
        "FUNERAL_OF_THE_DEAD_BUTTERFLIES.backgroundText.fourthTurn.1",
        "FUNERAL_OF_THE_DEAD_BUTTERFLIES.backgroundText.fourthTurn.2",
    ];

    private const float BackgroundTextIntervalSeconds = 5f;
    private static readonly Rect2 BackgroundTextSpawnArea = new(150f, 200f, 900f, 450f);
    private bool _backgroundMoonTextLoopStarted;

    private static readonly string FuneralPageRelicTitleLocKey =
        $"{ModelDb.GetId<FuneralOfTheDeadButterfliesPageRelic>().Entry}.title";

    private int Move1Damage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 9, 6);

    private int Move2Damage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 7, 5);

    private int Move4Damage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 12, 9);

    public override int MinInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 145, 140);

    public override int MaxInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 150, 143);

    public override int DefaultChaoResistance => 40;

    public override LibraryCreatureResistanceData.Resistance? DefaultPhysicalResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Endure,
        Pierce = LibraryResistanceLevel.Endure,
        Blunt = LibraryResistanceLevel.Endure
    };

    public override LibraryCreatureResistanceData.Resistance? DefaultChaoResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Resist,
        Pierce = LibraryResistanceLevel.Resist,
        Blunt = LibraryResistanceLevel.Resist
    };

    public override IEnumerable<string> AssetPaths
    {
        get
        {
            var paths = new List<string>(
                FuneralOfTheDeadButterfliesCreatureVisuals
                    .Profile.AssetPaths
                    .Concat(base.AssetPaths.Skip(1)))
            {
                WhiteFilterTexturePath,
                AttackBlackSfxPath,
                AttackWhiteSfxPath,
                StrongPrepareSfxPath,
                ParrySfxPath,
                StunSfxPath
            };

            return paths.Distinct();
        }
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        _backgroundMoonTextLoopStarted = false;
        EncounterBgmController.RegisterMonster(Creature);
        await PowerCmdCompat.Apply<LibraryOfRuinaSalvationHandPower>(Creature, 1m, Creature, null, silent: true);
    }

    public override Task BeforeCombatStart()
    {
        if (Creature.IsDead)
        {
            return Task.CompletedTask;
        }

        if (!_backgroundMoonTextLoopStarted)
        {
            _backgroundMoonTextLoopStarted = true;
            StartBackgroundMoonTextLoopForCurrentTurn();
        }

        return Task.CompletedTask;
    }

    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, IReadOnlyList<Creature> participants, CombatStateLike combatState)
    {
        if (side == CombatSide.Enemy && !Creature.IsDead)
        {
            StartBackgroundMoonTextLoopForCurrentTurn();
        }

        return base.BeforeSideTurnStart(choiceContext, side, participants, combatState);
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

        if (creature.CombatState?.RunState.CurrentRoom is not CombatRoom room)
        {
            return Task.CompletedTask;
        }

        foreach (var player in room.CombatState.Players)
        {
            if (AbnormalityPageRewardHelper.ShouldAddPageReward<FuneralOfTheDeadButterfliesPageRelic>(
                room,
                player,
                FuneralPageRelicTitleLocKey))
            {
                room.AddExtraReward(player, new RelicReward(ModelDb.Relic<FuneralOfTheDeadButterfliesPageRelic>().ToMutable(), player));
            }
        }

        return Task.CompletedTask;
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var move1 = new MoveState(
            Move1Id,
            Move1,
            new SingleAttackIntent(Move1Damage),
            new SummonIntent());

        var move2 = new MoveState(
            Move2Id,
            Move2,
            new MultiAttackIntent(Move2Damage, ButterflyCrossingHits),
            new SummonIntent());

        var move3 = new MoveState(
            Move3Id,
            Move3,
            new DebuffIntent(),
            new DetailedBuffIntent<StrengthPower>(SalvationHandStrength, DetailedBuffTargetScope.AllEnemies),
            new SummonIntent());

        var move4 = new MoveState(
            Move4Id,
            Move4,
            new SingleAttackIntent(Move4Damage),
            new DebuffIntent(strong: true));

        var summonChooser = new ConditionalBranchState("FUNERAL_SUMMON_CHOOSER");
        var afterMove1 = new ConditionalBranchState("FUNERAL_AFTER_MOVE1");
        var afterMove2 = new ConditionalBranchState("FUNERAL_AFTER_MOVE2");
        var afterMove3 = new ConditionalBranchState("FUNERAL_AFTER_MOVE3");
        summonChooser.AddState(move1, CanSummonButterfly);
        summonChooser.AddState(move4, () => true);
        afterMove1.AddState(move2, CanSummonButterfly);
        afterMove1.AddState(move4, () => true);
        afterMove2.AddState(move3, CanSummonButterfly);
        afterMove2.AddState(move4, () => true);
        afterMove3.AddState(move1, CanSummonButterfly);
        afterMove3.AddState(move4, () => true);

        move1.FollowUpState = afterMove1;
        move2.FollowUpState = afterMove2;
        move3.FollowUpState = afterMove3;
        move4.FollowUpState = summonChooser;

        return new MonsterMoveStateMachine(
            [move1, move2, move3, move4, summonChooser, afterMove1, afterMove2, afterMove3],
            summonChooser);
    }

    private async Task Move1(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(AttackBlackSfxPath, -1.5f);
        await DamageCmd.Attack(Move1Damage)
            .FromMonster(this)
            .WithAttackerAnim("AttackBlack", ScaleDuration(0.35f))
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);

        await SummonButterfly(DeadButterflyInitialMove.AngryRelease);
    }

    private async Task Move2(IReadOnlyList<Creature> targets)
    {
        for (int i = 0; i < ButterflyCrossingHits; i++)
        {
            if (Creature.IsDead) return;
            LocalOggOneShotPlayer.Play(AttackBlackSfxPath, -1.5f);
            await DamageCmd.Attack(Move2Damage)
                .FromMonster(this)
                .WithAttackerAnim("AttackBlack", ScaleDuration(0.22f))
                .WithHitFx("vfx/vfx_attack_slash")
                .Execute(null);
            await Cmd.CustomScaledWait(ScaleDuration(0.04f), ScaleDuration(0.08f));
        }

        await SummonButterfly(DeadButterflyInitialMove.SpiritRelease);
    }

    private async Task Move3(IReadOnlyList<Creature> targets)
    {
        await CreatureCmd.TriggerAnim(Creature, "Cast", ScaleDuration(0.1f));

        if (targets.Count > 0)
        {
            await PowerCmdCompat.Apply<WeakPower>(targets, SalvationHandWeak, Creature, null);
        }

        if (Creature.CombatState == null)
        {
            return;
        }

        IReadOnlyList<Creature> enemies = Creature.CombatState.LivingEnemies().ToArray();
        if (enemies.Count > 0)
        {
            await PowerCmdCompat.Apply<StrengthPower>(enemies, SalvationHandStrength, Creature, null);
        }

        await SummonButterfly(DeadButterflyInitialMove.PainfulRelease);
    }

    private async Task Move4(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(StrongPrepareSfxPath, -1.5f);
        FuneralWhiteFilterOverlayController.PlayOverlay();
        await CreatureCmd.TriggerAnim(Creature, "Cast", ScaleDuration(0.15f));
        LocalOggOneShotPlayer.Play(AttackWhiteSfxPath, -1.5f);
        await DamageCmd.Attack(Move4Damage)
            .FromMonster(this)
            .WithAttackerAnim("AttackWhite", ScaleDuration(0.45f))
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);
    }

    private bool CanSummonButterfly()
    {
        return AliveSummonedButterflyCount() < MaxSummonedButterflies
            && TryGetSummonSlot(out _);
    }

    private int AliveSummonedButterflyCount()
    {
        if (Creature.CombatState == null)
        {
            return 0;
        }

        return Creature.CombatState.Enemies.Count(enemy => enemy.IsAlive && enemy.Monster is DeadButterfly.DeadButterfly);
    }

    private bool TryGetSummonSlot(out string slot)
    {
        slot = string.Empty;
        if (Creature.CombatState == null)
        {
            return false;
        }

        slot = Creature.CombatState.Encounter?.GetNextSlot(Creature.CombatState) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(slot))
        {
            return false;
        }

        return true;
    }

    private async Task<bool> SummonButterfly(DeadButterflyInitialMove initialMove)
    {
        if (Creature.CombatState == null
            || AliveSummonedButterflyCount() >= MaxSummonedButterflies
            || !TryGetSummonSlot(out string slot))
        {
            return false;
        }

        var butterfly = (DeadButterfly.DeadButterfly)ModelDb.Monster<DeadButterfly.DeadButterfly>().ToMutable();
        butterfly.ConfigureMoveSequence(CreateButterflySequence(initialMove));
        Creature summoned = await CreatureCmd.Add(butterfly, Creature.CombatState, CombatSide.Enemy, slot);
        Creature.CombatState.SortEnemiesBySlotName();
        await PowerCmdCompat.Apply<MinionPower>(summoned, 1m, Creature, null, silent: true);
        return true;
    }

    private static DeadButterflyInitialMove[] CreateButterflySequence(DeadButterflyInitialMove initialMove) => initialMove switch
    {
        DeadButterflyInitialMove.SpiritRelease =>
        [
            DeadButterflyInitialMove.SpiritRelease,
            DeadButterflyInitialMove.PainfulRelease,
            DeadButterflyInitialMove.PeacefulRepose,
            DeadButterflyInitialMove.AngryRelease
        ],
        DeadButterflyInitialMove.PainfulRelease =>
        [
            DeadButterflyInitialMove.PainfulRelease,
            DeadButterflyInitialMove.PeacefulRepose,
            DeadButterflyInitialMove.AngryRelease,
            DeadButterflyInitialMove.SpiritRelease
        ],
        DeadButterflyInitialMove.PeacefulRepose =>
        [
            DeadButterflyInitialMove.PeacefulRepose,
            DeadButterflyInitialMove.AngryRelease,
            DeadButterflyInitialMove.SpiritRelease,
            DeadButterflyInitialMove.PainfulRelease
        ],
        _ =>
        [
            DeadButterflyInitialMove.AngryRelease,
            DeadButterflyInitialMove.SpiritRelease,
            DeadButterflyInitialMove.PainfulRelease,
            DeadButterflyInitialMove.PeacefulRepose
        ]
    };

    private void StartBackgroundMoonTextLoopForCurrentTurn()
    {
        IReadOnlyList<string> lineKeys = IsFourthTurn() ? FourthTurnBackgroundTextLineKeys : NormalBackgroundTextLineKeys;
        MonsterMoonTextLoop.Start(lineKeys, BackgroundTextIntervalSeconds, BackgroundTextSpawnArea);
    }

    private bool IsFourthTurn()
    {
        int round = CombatState?.RoundNumber ?? 1;
        return round > 0 && round % 4 == 0;
    }

    private static float ScaleDuration(float seconds) => seconds * AnimationDurationScale;
}
