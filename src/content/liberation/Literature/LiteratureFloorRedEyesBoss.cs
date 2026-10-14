using LibraryLib.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LibraryOfRuina.content.abnormalities.SpiderBud;
using LibraryOfRuina.content.liberation.History;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.features.moontext;
using LibraryOfRuina.framework.audio;
using LibraryOfRuina.framework.combat;
using LibraryOfRuina.framework.intents;
using LibraryOfRuina.framework.monsters;
using LibraryOfRuina.framework.powers;
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
using MegaCrit.Sts2.Core.ValueProps;

namespace LibraryOfRuina.content.liberation.Literature;

public sealed class LiteratureFloorRedEyesBoss :
    LiberationPhaseBossMonster
{
    public const int Phase = 2;
    public const string FlickeringEyesMoveId = "FLICKERING_EYES";
    public const string UnknownMoveId = "UNKNOWN";
    public const string ScreechMoveId = "SCREECH";

    public const int FlickeringEyesBlock = 8; // 闪烁的眼瞳：每只存活小蜘蛛获得的格挡，各进阶相同。
    public const int ScreechHits = 3; // 尖啸：攻击次数（单次伤害见 ScreechDamage），各进阶相同。
    public const int CocoonStacksApplied = 1; // 尖啸：意图显示的茧缚层数，攻击后每名存活玩家获得或刷新一次茧缚，各进阶相同。

    public const string IdleTexturePath = LiteratureFloorAssets.RedEyesMonsterRoot + "red_eyes_idle.png";
    public const string StrikeTexturePath = LiteratureFloorAssets.RedEyesMonsterRoot + "red_eyes_strike.png";
    public const string S1TexturePath = LiteratureFloorAssets.RedEyesMonsterRoot + "red_eyes_s1.png";
    public const string SpecialTexturePath = LiteratureFloorAssets.RedEyesMonsterRoot + "red_eyes_special.png";
    public const string SlashTexturePath = LiteratureFloorAssets.RedEyesMonsterRoot + "red_eyes_slash.png";
    public const string HitTexturePath = LiteratureFloorAssets.RedEyesMonsterRoot + "red_eyes_hit.png";

    public const string ScreechSfxPath =
        LiteratureFloorAssets.RedEyesSfxRoot + "red_eyes_screech.ogg";
    public const string FlickeringEyesSfxPath =
        LiteratureFloorAssets.RedEyesSfxRoot + "red_eyes_flickering_eyes.ogg";
    public const string VigilanceSfxPath =
        LiteratureFloorAssets.RedEyesSfxRoot + "red_eyes_vigilance.ogg";
    public const string HitSfxPath = LiteratureFloorAssets.RedEyesSfxRoot + "red_eyes_hit.ogg";
    public const string HuntStartSfxPath =
        LiteratureFloorAssets.RedEyesSfxRoot + "red_eyes_hunt_start.ogg";

    private const string BackgroundTextScope =
        "literature_floor_red_eyes_phase_2";
    private const float BackgroundTextIntervalSeconds = 5f;
    private static readonly Rect2 BackgroundTextSpawnArea =
        new(150f, 200f, 900f, 450f);
    private static readonly string[] NormalBackgroundTextLineKeys =
    [
        "SPIDER_BUD.backgroundText.normal.0",
        "SPIDER_BUD.backgroundText.normal.1",
        "SPIDER_BUD.backgroundText.normal.2",
        "SPIDER_BUD.backgroundText.normal.3",
        "SPIDER_BUD.backgroundText.normal.4"
    ];
    private static readonly string[] HuntBackgroundTextLineKeys =
    [
        "SPIDER_BUD.backgroundText.hunt.0",
        "SPIDER_BUD.backgroundText.hunt.1",
        "SPIDER_BUD.backgroundText.hunt.2",
        "SPIDER_BUD.backgroundText.hunt.3",
        "SPIDER_BUD.backgroundText.hunt.4"
    ];

    private MoveState? _flickeringEyesState;
    private MoveState? _unknownState;
    private MoveState? _screechState;
    private bool _backgroundTextStarted;
    private bool _backgroundTextHuntMode;

    public bool HuntPending { get; private set; }

    public override int LiberationPhase => Phase;

    /// <summary>死亡动画时长；转阶段的假死返回 0，见 <see cref="LayeredBossSpine.DeathLength"/>。</summary>
    public override float DeathAnimLengthOverride => LayeredBossSpine.DeathLength(this);

    private int ScreechDamage => AscensionHelper.GetValueIfAscension(
        AscensionLevel.DeadlyEnemies,
        5,
        3);

    private int FlickeringEyesStrength =>
        AscensionHelper.GetValueIfAscension(
            AscensionLevel.DeadlyEnemies,
            2,
            1);

    internal static (int Min, int Max) DebugHpRange(bool toughEnemies) =>
        toughEnemies ? (133, 140) : (120, 127);

    internal static int DebugScreechDamage(bool deadlyEnemies) =>
        deadlyEnemies ? 7 : 5;

    internal static int DebugFlickeringEyesStrength(bool deadlyEnemies) =>
        deadlyEnemies ? 2 : 1;

    internal int DebugHuntingActionsRemaining =>
        Creature.GetPower<LiteratureFloorRedEyesHuntingWindowPower>()
            ?.Amount ?? 0;

    internal bool DebugHasLivingEnhancedSpiders =>
        HasLivingEnhancedSpiders();

    public override int MinInitialHp =>
        AscensionHelper.GetValueIfAscension(
            AscensionLevel.ToughEnemies,
            103,
            90);

    public override int MaxInitialHp =>
        AscensionHelper.GetValueIfAscension(
            AscensionLevel.ToughEnemies,
            110,
            94);

    public override int DefaultChaoResistance => 60;

    public override bool ShouldDisappearFromDoom => false;

    public override LibraryCreatureResistanceData.Resistance?
        DefaultPhysicalResistanceData => UniformNormalResistance();

    public override LibraryCreatureResistanceData.Resistance?
        DefaultChaoResistanceData => UniformNormalResistance();

    public override IEnumerable<string> AssetPaths
    {
        get
        {
            var paths = new List<string>
            {
                LiteratureFloorRedEyesCreatureVisuals.ScenePath,
                ScreechSfxPath,
                FlickeringEyesSfxPath,
                VigilanceSfxPath,
                HitSfxPath,
                HuntStartSfxPath,
                LiteratureFloorAssets.LibraryPassiveGreenIcon,
                LiteratureFloorAssets.HistoryFloorCorrosionPowerIcon,
                LiteratureFloorAssets.SpiderBudUntargetablePowerIcon,
                LiteratureFloorAssets.CocoonBindPowerIcon
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
        _backgroundTextStarted = false;
        _backgroundTextHuntMode = false;

        if (Creature.CombatState?.Encounter
            is LiteratureFloorLiberationEncounter encounter)
        {
            await encounter.EnsureControllerPowers(Creature.CombatState);
        }

        LiteratureFloorLiberationBackgroundController.SetPhaseBackground(
            Phase);
        EncounterBgmController.RegisterMonster(Creature);

        await PowerCmdCompat.Apply<HistoryFloorCorrosionPower>(
            Creature,
            1m,
            Creature,
            null,
            silent: true);
        await PowerCmdCompat.Apply<
            LiteratureFloorRedEyesStartHuntingPassivePower>(
            Creature,
            1m,
            Creature,
            null,
            silent: true);
        await PowerCmdCompat.Apply<
            LiteratureFloorRedEyesVigilancePassivePower>(
            Creature,
            1m,
            Creature,
            null,
            silent: true);
        await PowerCmdCompat.Ensure<UntargetablePower>(Creature);

        StartBackgroundText(huntMode: false);
    }

    public override Task BeforeCombatStart()
    {
        if (!Creature.IsDead)
        {
            StartBackgroundText(huntMode: false);
        }

        return Task.CompletedTask;
    }

    public override void BeforeRemovedFromRoom()
    {
        StopBackgroundText();
        base.BeforeRemovedFromRoom();
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

        StopBackgroundText();
        if (Creature.CombatState?.Encounter
            is LiteratureFloorLiberationEncounter encounter)
        {
            await encounter.OnPhaseBossDeath(
                this,
                wasRemovalPrevented,
                deathAnimLength);
        }
    }

    public override Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target == Creature && result.TotalDamage > 0)
        {
            LocalOggOneShotPlayer.Play(HitSfxPath, -2f);
        }

        return Task.CompletedTask;
    }

    public async Task OnEnhancedSmallSpiderDeath()
    {
        if (Creature.IsDead || _screechState == null)
        {
            return;
        }

        HuntPending = true;
        if (SpiderBudHuntTiming.HasPlayerResponseWindow(Creature))
        {
            await StartHuntFromSmallSpiderDeath();
        }
    }

    public override async Task AfterSideTurnStart(
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        await base.AfterSideTurnStart(side, participants, combatState);
        // 意图和狩猎窗口一起延后，避免玩家无法操作时提前消耗狩猎次数。
        if (TurnParticipants.IsRoundPlayerTurn(side) && HuntPending)
        {
            await StartHuntFromSmallSpiderDeath();
        }
    }

    private async Task StartHuntFromSmallSpiderDeath()
    {
        if (Creature.IsDead || _screechState == null)
        {
            return;
        }

        HuntPending = false;
        LocalOggOneShotPlayer.Play(HuntStartSfxPath, -1.5f);
        UntargetablePower? untargetable =
            Creature.GetPower<UntargetablePower>();
        if (untargetable != null)
        {
            await PowerCmd.Remove(untargetable);
        }

        if (HasLivingEnhancedSpiders())
        {
            await PowerCmdCompat.SetAmount<
                LiteratureFloorRedEyesHuntingWindowPower>(
                Creature,
                LiteratureFloorRedEyesStartHuntingPassivePower
                    .ActionWindow,
                Creature,
                null);
        }
        else
        {
            LiteratureFloorRedEyesHuntingWindowPower? window =
                Creature.GetPower<
                    LiteratureFloorRedEyesHuntingWindowPower>();
            if (window != null)
            {
                await PowerCmd.Remove(window);
            }
        }

        StartBackgroundText(huntMode: true);
        ShowImmediateHuntText();
        SetMoveImmediate(_screechState, forceTransition: true);
        if (CombatQueries.CreatureNodeOf(this) is { } node)
        {
            await node.RefreshIntents();
        }
    }

    internal void PlayVigilanceSfx()
    {
        LocalOggOneShotPlayer.Play(VigilanceSfxPath, -1.5f);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        _flickeringEyesState = new MoveState(
            FlickeringEyesMoveId,
            FlickeringEyesMove,
            new CombinedDefendBuffIntent(
                FlickeringEyesBlock,
                "LITERATURE_FLOOR_RED_EYES_FLICKERING_EYES.description",
                IntentBadge.Strength(FlickeringEyesStrength)));
        _unknownState = new MoveState(
            UnknownMoveId,
            UnknownMove,
            new UnknownIntent());
        _screechState = new MoveState(
            ScreechMoveId,
            ScreechMove,
            new MultiAttackIntent(ScreechDamage, ScreechHits),
            new BadgedDebuffIntent(IntentBadge.FromPower<LiteratureFloorCocoonBindPower>(), CocoonStacksApplied));
        MoveState reviveAndEmpower = CreateReviveAndEmpowerState();

        var afterScreech = new ConditionalBranchState("AFTER_SCREECH");
        afterScreech.AddState(_screechState, ShouldUseScreech);
        afterScreech.AddState(_flickeringEyesState, () => true);

        _flickeringEyesState.FollowUpState = _unknownState;
        _unknownState.FollowUpState = _flickeringEyesState;
        _screechState.FollowUpState = afterScreech;
        reviveAndEmpower.FollowUpState = _flickeringEyesState;

        return new MonsterMoveStateMachine(
            [
                _flickeringEyesState,
                _unknownState,
                _screechState,
                reviveAndEmpower,
                afterScreech
            ],
            _flickeringEyesState);
    }

    private async Task FlickeringEyesMove(
        IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(FlickeringEyesSfxPath, -1.5f);
        await CreatureCmd.TriggerAnim(
            Creature,
            "FlickeringEyes",
            LiteratureFloorRedEyesAnimationContract
                .FlickeringEyesDurationSeconds);

        Creature[] spiders = LivingEnhancedSpiders().ToArray();
        foreach (Creature spider in spiders)
        {
            await CreatureCmd.GainBlock(
                spider,
                FlickeringEyesBlock,
                ValueProp.Move,
                null);
            await PowerCmdCompat.Apply<StrengthPower>(
                spider,
                FlickeringEyesStrength,
                Creature,
                null);
        }
    }

    private async Task UnknownMove(IReadOnlyList<Creature> targets)
    {
        await CreatureCmd.TriggerAnim(
            Creature,
            "Unknown",
            LiteratureFloorRedEyesAnimationContract
                .UnknownDurationSeconds);
    }

    private async Task ScreechMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(HuntStartSfxPath, -2f);
        await CreatureCmd.TriggerAnim(Creature, "Screech", 0f);
        int hitIndex = 0;
        float previousHitTime = 0f;
        await DamageCmd.Attack(ScreechDamage)
            .WithHitCount(ScreechHits)
            .FromMonster(this)
            .WithNoAttackerAnim()
            .BeforeDamage(async () =>
            {
                if (hitIndex >= LiteratureFloorRedEyesAnimationContract
                        .ScreechHitFrameTimesSeconds.Count)
                {
                    return;
                }

                float hitTime = LiteratureFloorRedEyesAnimationContract
                    .ScreechHitFrameTimesSeconds[hitIndex];
                await Cmd.Wait(hitTime - previousHitTime);
                previousHitTime = hitTime;
                hitIndex++;

                if (hitIndex == 1)
                {
                    LocalOggOneShotPlayer.Play(ScreechSfxPath, -1f);
                }

                if (Creature.CombatState is { } combatState)
                {
                    VfxCmd.PlayOnSide(
                        CombatSide.Player,
                        "vfx/vfx_attack_blunt",
                        combatState);
                }
            })
            .Execute(null);

        foreach (Creature player in Creature.CombatState?.LivingPlayerCreatures()
                     .ToArray() ?? [])
        {
            await LiteratureFloorCocoonBindPower.ApplyOrRefresh(
                player,
                Creature);
        }

        await Cmd.Wait(Math.Max(
            0f,
            LiteratureFloorRedEyesAnimationContract
                .ScreechDurationSeconds - previousHitTime));
        await ConsumeHuntingAction();
    }

    private async Task ConsumeHuntingAction()
    {
        if (!HasLivingEnhancedSpiders())
        {
            LiteratureFloorRedEyesHuntingWindowPower? permanentWindow =
                Creature.GetPower<
                    LiteratureFloorRedEyesHuntingWindowPower>();
            if (permanentWindow != null)
            {
                await PowerCmd.Remove(permanentWindow);
            }

            StartBackgroundText(huntMode: true);
            return;
        }

        LiteratureFloorRedEyesHuntingWindowPower? window =
            Creature.GetPower<LiteratureFloorRedEyesHuntingWindowPower>();
        if (window == null)
        {
            await RestoreUntargetableState();
            return;
        }

        int remaining = await PowerCmdCompat.ModifyAmount(
            window,
            -1m,
            Creature,
            null,
            silent: true);
        if (remaining <= 0)
        {
            await RestoreUntargetableState();
        }
    }

    private async Task RestoreUntargetableState()
    {
        await PowerCmdCompat.Ensure<UntargetablePower>(Creature);
        StartBackgroundText(huntMode: false);
    }

    protected override Task CompleteLiberationPhaseTransition() =>
        Creature.CombatState?.Encounter
            is LiteratureFloorLiberationEncounter encounter
            ? encounter.CompletePhaseTransition(this)
            : Task.CompletedTask;

    private bool ShouldUseScreech() =>
        !HasLivingEnhancedSpiders()
        || Creature.GetPower<
                LiteratureFloorRedEyesHuntingWindowPower>()
            is { Amount: > 0 };

    private bool HasLivingEnhancedSpiders() =>
        LivingEnhancedSpiders().Any();

    private IEnumerable<Creature> LivingEnhancedSpiders() =>
        Creature.CombatState?.Enemies.Where(static enemy =>
            enemy.IsAlive
            && enemy.Monster is LiteratureFloorEnhancedSmallSpider)
        ?? [];

    private void StartBackgroundText(bool huntMode)
    {
        if (_backgroundTextStarted
            && _backgroundTextHuntMode == huntMode)
        {
            return;
        }

        StopBackgroundText();
        _backgroundTextStarted = true;
        _backgroundTextHuntMode = huntMode;
        string[] keys = huntMode
            ? HuntBackgroundTextLineKeys
            : NormalBackgroundTextLineKeys;
        MoonTextService.StartRandomLoop(
            Creature,
            BackgroundTextScope,
            keys.Select(L10NMonsterLookup).ToArray(),
            BackgroundTextIntervalSeconds,
            BackgroundTextSpawnArea);
    }

    private void ShowImmediateHuntText()
    {
        MoonTextService.StartSequence(
        [
            new MoonTextSequenceEntry(
                L10NMonsterLookup(
                    HuntBackgroundTextLineKeys[0]),
                0f)
        ]);
    }

    private void StopBackgroundText()
    {
        if (!_backgroundTextStarted)
        {
            return;
        }

        MoonTextService.StopRandomLoop(Creature, BackgroundTextScope);
        _backgroundTextStarted = false;
    }

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

    private static LibraryCreatureResistanceData.Resistance
        UniformNormalResistance() =>
        new()
        {
            Slash = LibraryResistanceLevel.Normal,
            Pierce = LibraryResistanceLevel.Normal,
            Blunt = LibraryResistanceLevel.Normal
        };
}
