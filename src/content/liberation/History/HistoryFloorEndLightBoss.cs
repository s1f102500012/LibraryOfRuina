using LibraryLib.Models;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.features.moontext;
using LibraryOfRuina.framework.audio;
using LibraryOfRuina.framework.combat;
using LibraryOfRuina.framework.intents;
using LibraryOfRuina.framework.monsters;
using LibraryOfRuina.framework.visuals;
using LibraryOfRuina.infra.helpers;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Saves;

namespace LibraryOfRuina.content.liberation.History;

public sealed class HistoryFloorEndLightBoss : LiberationPhaseBossMonster
{
    private const string EndLightMoveId = "END_LIGHT";
    private const string MatchEnhancementMoveId = "MATCH_ENHANCEMENT";
    internal const string BackgroundTextScope = "history_floor_liberation_phase_1";
    private const int Phase = 1;
    private const int MatchEnhancementStrength = 1; // 火柴强化：其他存活敌人获得的力量，各进阶相同。
    private const float BackgroundTextIntervalSeconds = 5f;

    public override int DefaultChaoResistance => 30;

    public override float DeathAnimLengthOverride => LayeredBossSpine.DeathLength(this);


    public const string IdleTexturePath = HistoryFloorAssets.EndLightTexture;
    public const string AttackTexturePath = HistoryFloorAssets.EndLightAttackTexture;
    public const string HitTexturePath = HistoryFloorAssets.EndLightHitTexture;
    public const string CastTexturePath = HistoryFloorAssets.EndLightCastTexture;

    internal const string EndLightAttackSfxPath = HistoryFloorAssets.ScorchedGirlExplosionSfx;

    private static readonly Rect2 BackgroundTextSpawnArea = new(150f, 190f, 980f, 470f);
    private static readonly string[] BackgroundTextLineKeys =
    [
        "HISTORY_FLOOR_END_LIGHT_BOSS.backgroundText.normal.0",
        "HISTORY_FLOOR_END_LIGHT_BOSS.backgroundText.normal.1",
        "HISTORY_FLOOR_END_LIGHT_BOSS.backgroundText.normal.2",
        "HISTORY_FLOOR_END_LIGHT_BOSS.backgroundText.normal.3",
        "HISTORY_FLOOR_END_LIGHT_BOSS.backgroundText.normal.4",
        "HISTORY_FLOOR_END_LIGHT_BOSS.backgroundText.normal.5",
        "HISTORY_FLOOR_END_LIGHT_BOSS.backgroundText.normal.6",
        "HISTORY_FLOOR_END_LIGHT_BOSS.backgroundText.normal.7",
        "HISTORY_FLOOR_END_LIGHT_BOSS.backgroundText.normal.8",
        "HISTORY_FLOOR_END_LIGHT_BOSS.backgroundText.normal.9"
    ];

    private static readonly string[] SfxAssetPaths =
    [
        EndLightAttackSfxPath,
        HistoryFloorAssets.FourthMatchFlameAttackSfx
    ];

    private int _turnIndex;
    private bool _backgroundMoonTextLoopStarted;

    public override int LiberationPhase => Phase;

    protected override bool ReviveTriggerPlaysHitAnimation => true;

    protected override string? ReviveAndEmpowerAnimation => "Cast";

    public override bool ShouldDisappearFromDoom => false;

    public override int MinInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 500, 490);

    public override int MaxInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 500, 490);

    public static int ResolveEndLightDamage() =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, HistoryFloorEgoNumbers.EndLightHighAscensionDamage, HistoryFloorEgoNumbers.EndLightBaseDamage);

    private int EndLightDamage => ResolveEndLightDamage();

    public override IEnumerable<string> AssetPaths =>
        HistoryFloorEndLightCreatureVisuals.Profile.AssetPaths
            .Concat(base.AssetPaths.Skip(1))
            .Concat(SfxAssetPaths)
            .Distinct();

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        _turnIndex = 0;
        _backgroundMoonTextLoopStarted = false;
        SaveManager.Instance.MarkCardAsSeen(ModelDb.Card<EndLightEgoCard>());

        if (CombatState?.Encounter is HistoryFloorLiberationEncounter encounter)
        {
            await encounter.EnsureControllerPowers(CombatState);
            encounter.MarkBossPhaseStarted(Phase);
        }

        EncounterBgmController.RegisterMonster(Creature);
        await PowerCmdCompat.Apply<HistoryFloorCorrosionPower>(Creature, 1m, Creature, null, silent: true);
        await PowerCmdCompat.Apply<HistoryFloorRekindledSparkPower>(Creature, 1m, Creature, null, silent: true);
    }

    public override Task BeforeCombatStart()
    {
        if (!_backgroundMoonTextLoopStarted && Creature.IsAlive)
        {
            _backgroundMoonTextLoopStarted = true;
            MoonTextService.StartRandomLoop(
                Creature,
                BackgroundTextScope,
                BackgroundTextLineKeys.Select(L10NMonsterLookup).ToArray(),
                BackgroundTextIntervalSeconds,
                BackgroundTextSpawnArea);
        }

        return Task.CompletedTask;
    }

    public override void BeforeRemovedFromRoom()
    {
        StopBackgroundMoonTextLoop();
        base.BeforeRemovedFromRoom();
    }

    public override Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature creature,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (creature != Creature
            || Creature.CombatState?.Encounter is not HistoryFloorLiberationEncounter encounter)
        {
            return Task.CompletedTask;
        }

        return encounter.OnPhaseBossDeath(this, wasRemovalPrevented, deathAnimLength);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        MoveState reviveAndEmpower = CreateReviveAndEmpowerState();

        var endLight = new MoveState(
            EndLightMoveId,
            EndLightMove,
            new PlayCardAttackIntent<EndLightEgoCard>(
                "END_LIGHT_EGO_CARD",
                () => EndLightDamage,
                static (card, damages) => { card.UpgradePreview(); card.SetPreviewDamage(damages[0]); }),
                new DebuffIntent());

        var matchEnhancement = new MoveState(
            MatchEnhancementMoveId,
            MatchEnhancementMove,
            new DetailedBuffIntent<StrengthPower>(MatchEnhancementStrength, DetailedBuffTargetScope.OtherEnemies));

        var chooser = new ConditionalBranchState("CHOOSER");
        chooser.AddState(endLight, () => (_turnIndex + 1) % 4 == 0);
        chooser.AddState(matchEnhancement, () => true);

        endLight.FollowUpState = chooser;
        matchEnhancement.FollowUpState = chooser;
        reviveAndEmpower.FollowUpState = chooser;

        return new MonsterMoveStateMachine(
            new MonsterState[] { reviveAndEmpower, endLight, matchEnhancement, chooser },
            chooser);
    }

    private async Task EndLightMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(EndLightAttackSfxPath, -2f);

        await DamageCmd.Attack(EndLightDamage)
            .FromMonster(this)
            .WithAttackerAnim("Attack", AbnormalityAnimHelper.DefaultAttackSegmentDelaySeconds)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);

        await PowerCmdCompat.Apply<LibraryBurnPower>(
            targets.Where(static target => target.IsAlive),
            HistoryFloorEgoNumbers.EndLightBurnAmount,
            Creature,
            null);

        AdvanceTurn();
    }

    private async Task MatchEnhancementMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(HistoryFloorAssets.FourthMatchFlameAttackSfx, -4f);

        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.6f);

        IReadOnlyList<Creature> others = CombatState.LivingEnemies()
            .Where(enemy => enemy != Creature)
            .ToArray();
        foreach (Creature enemy in others)
        {
            await PowerCmdCompat.Apply<StrengthPower>(enemy, MatchEnhancementStrength, Creature, null);
        }

        AdvanceTurn();
    }

    protected override Task CompleteLiberationPhaseTransition() =>
        Creature.CombatState?.Encounter is HistoryFloorLiberationEncounter encounter
            ? encounter.CompletePhaseTransition(this)
            : Task.CompletedTask;

    private void AdvanceTurn()
    {
        _turnIndex++;
    }

    internal void StopBackgroundMoonTextLoop() =>
        MonsterMoonTextLoop.Stop(this, BackgroundTextScope);
}
