using System;
using System.Linq;
using System.Threading.Tasks;
using LibraryLib.Entities.Creatures;
using LibraryLib.Models;
using LibraryOfRuina.content.liberation.History;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.framework.audio;
using LibraryOfRuina.framework.combat;
using LibraryOfRuina.framework.intents;
using LibraryOfRuina.framework.monsters;
using LibraryOfRuina.framework.visuals;
using LibraryOfRuina.infra.helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;

namespace LibraryOfRuina.content.abnormalities.DespairKnight;

public enum ForgottenKnightSwordSpecial
{
    None,
    PiercingHeart,
    RendingHeart,
    RuiningHeart
}

public sealed class ForgottenKnightSword : LorMonsterModel
{
    public const int RequiredSwordCount = 3;

    public const string FadedTrustMoveId = "FADED_TRUST";
    public const string HollowPrideMoveId = "HOLLOW_PRIDE";
    public const string SwordOfGriefMoveId = "SWORD_OF_GRIEF";
    public const string TearEdgeSwordMoveId = "TEAR_EDGE_SWORD";
    public const string PiercingHeartMoveId = "PIERCING_HEART_SWORD";
    public const string RendingHeartMoveId = "RENDING_HEART_SWORD";
    public const string RuiningHeartMoveId = "RUINING_HEART_SWORD";
    public const string FalseDeathMoveId = "FALSE_DEATH";
    private const string FalseDeathHiddenMoveId = "FALSE_DEATH_HIDDEN";

    public const string Root = DespairKnightAssets.ForgottenKnightSwordMonsterRoot;
    public const string NormalIdleTexturePath = Root + "normal_idle.png";
    public const string NormalBluntTexturePath = Root + "normal_blunt.png";
    public const string NormalPierceTexturePath = Root + "normal_pierce.png";
    public const string NormalSlashTexturePath = Root + "normal_slash.png";
    public const string NormalHitTexturePath = Root + "normal_hit.png";
    public const string NormalParryTexturePath = Root + "normal_parry.png";
    public const string TeardropIdleTexturePath = Root + "teardrop_idle.png";
    public const string TeardropBluntTexturePath = Root + "teardrop_blunt.png";
    public const string TeardropPierceTexturePath = Root + "teardrop_pierce.png";
    public const string TeardropSlashTexturePath = Root + "teardrop_slash.png";
    public const string TeardropHitTexturePath = Root + "teardrop_hit.png";
    public const string TeardropParryTexturePath = Root + "teardrop_parry.png";
    public const string DespairIdleTexturePath = Root + "despair_idle.png";
    public const string DespairAttackTexturePath = Root + "despair_attack.png";
    public const string DespairHitTexturePath = Root + "despair_hit.png";

    public const string SfxRoot = DespairKnightAssets.DespairKnightSfxRoot;
    public const string NormalBluntSfxPath = SfxRoot + "sword_protected_blunt.ogg";
    public const string NormalPierceSfxPath = SfxRoot + "sword_protected_pierce.ogg";
    public const string NormalSlashSfxPath = SfxRoot + "sword_protected_slash.ogg";
    public const string TeardropBluntSfxPath = SfxRoot + "sword_teardrop_blunt.ogg";
    public const string TeardropPierceSfxPath = SfxRoot + "sword_teardrop_pierce.ogg";
    public const string TeardropSlashSfxPath = SfxRoot + "sword_teardrop_slash.ogg";

    public static readonly string[] SfxPaths =
    [
        NormalBluntSfxPath,
        NormalPierceSfxPath,
        NormalSlashSfxPath,
        TeardropBluntSfxPath,
        TeardropPierceSfxPath,
        TeardropSlashSfxPath
    ];

    public static readonly string[] PowerIconPaths =
    [
        DespairKnightAssets.ForgottenKnightSwordTeardropPowerIcon,
        DespairKnightAssets.ForgottenKnightSwordFalseDeathPowerIcon,
        DespairKnightAssets.ForgottenKnightSwordPierceDespairPowerIcon
    ];

    public static readonly string[] AssetPathsStatic =
        ForgottenKnightSwordCreatureVisuals.Profile.AssetPaths
            .Concat(SfxPaths)
            .Concat(PowerIconPaths)
            .ToArray();

    public const int FalseDeathHp = 0;
    public const int FalseDeathTurns = 2;
    private const int FalseDeathPlayerTurns = FalseDeathTurns;
    private const int MinimumRecoveryHp = 1;
    private const int HollowPrideDazed = 2;
    private const int HollowPrideVulnerable = 2;
    private const int HollowPrideVulnerableTurns = 1;
    private const int SwordOfGriefBlock = 16;
    private const int SwordOfGriefStrength = 2;
    private const int TearEdgeFrail = 1;
    private const int PiercingHeartWeak = 2;
    private const int RendingHeartDazed = 1;
    private const int RuiningHeartStrength = 2;
    private const int PiercingHeartBossDamage = 20;
    private const int RendingHeartBossDamage = 30;
    private const int RuiningHeartBossDamage = 50;
    private const float SegmentDelaySeconds = 0.50f;
    private const float HitPauseFastSeconds = 0.05f;
    private const float HitPauseStandardSeconds = 0.12f;

    private int _swordIndex;
    private bool _isFakeDead;
    private int _fakeDeathPlayerTurnsRemaining;
    private string _fakeDeathRecoveryMoveId = FadedTrustMoveId;
    private ForgottenKnightSwordSpecial _forcedSpecial = ForgottenKnightSwordSpecial.None;
    private bool _hasStabbedKnight;
    private bool _hasReceivedTeardrop;

    private MoveState _fadedTrustState = null!;
    private MoveState _hollowPrideState = null!;
    private MoveState _swordOfGriefState = null!;
    private MoveState _tearEdgeSwordState = null!;
    private MoveState _piercingHeartState = null!;
    private MoveState _rendingHeartState = null!;
    private MoveState _ruiningHeartState = null!;
    private MoveState _falseDeathState = null!;

    public bool IsFakeDead => _isFakeDead;

    public override bool ShouldDisappearFromDoom => false;

    public bool HasStabbedKnight => _hasStabbedKnight;

    public int SwordIndex => _swordIndex;

    public bool HasTeardrop => _hasReceivedTeardrop && Creature.HasPower<ForgottenKnightSwordTeardropPower>();

    public bool IsKnightInDespair =>
        DespairKnight.GetKnights(Creature.CombatState).Any(static knight => knight.IsInDespair);

    private static int FadedTrustDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 5, 4);

    private static int SwordOfGriefDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 7, 6);

    private static int PiercingHeartDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 6, 5);

    private static int RendingHeartDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 5, 4);

    private static int RuiningHeartDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 6, 5);

    public override int MinInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 100, 95);

    public override int MaxInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 103, 98);
    
    public override int DefaultChaoResistance => 80;

    public override float DeathAnimLengthOverride => LayeredBossSpine.DeathLength(this);

    public override LibraryCreatureResistanceData.Resistance DefaultPhysicalResistanceData => NormalResistance();

    public override LibraryCreatureResistanceData.Resistance DefaultChaoResistanceData => NormalResistance();

    public override IEnumerable<string> AssetPaths =>
        AssetPathsStatic
            .Concat(EnumerateIntentAssets().SelectMany(static intent => intent.AssetPaths))
            .Distinct();

    public void ConfigureSwordIndex(int index)
    {
        AssertMutable();
        _swordIndex = Math.Clamp(index, 0, RequiredSwordCount - 1);
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        ResetFalseDeathState();
        _forcedSpecial = ForgottenKnightSwordSpecial.None;
        _hasStabbedKnight = false;
        _hasReceivedTeardrop = false;
        EncounterBgmController.RegisterMonster(Creature);

        await PowerCmdCompat.Apply<MinionPower>(Creature, 1m, Creature, null, silent: true);
        await PowerCmdCompat.Apply<ForgottenKnightSwordFalseDeathPower>(Creature, 1m, Creature, null, silent: true);
        await PowerCmdCompat.Apply<ForgottenKnightSwordPierceDespairPower>(Creature, 1m, Creature, null, silent: true);
    }

    public string ResolveIdleTexturePath()
    {
        if (IsKnightInDespair)
        {
            return DespairIdleTexturePath;
        }

        return HasTeardrop ? TeardropIdleTexturePath : NormalIdleTexturePath;
    }

    public bool CanReceiveTeardrop() => Creature.IsAlive && !_isFakeDead && !HasTeardrop;

    public async Task ApplyTeardrop()
    {
        if (!CanReceiveTeardrop())
        {
            return;
        }

        LocalOggOneShotPlayer.Play(DespairKnight.TeardropGrantSfxPath, -2f);
        await PowerCmdCompat.Apply<ForgottenKnightSwordTeardropPower>(Creature, 1m, Creature, null);
        if (HasTeardrop)
        {
            SetMoveImmediate(GetTeardropInitialState(), forceTransition: true);
        }
    }

    public void MarkTeardropApplied()
    {
        _hasReceivedTeardrop = true;
    }

    public void MarkTeardropRemoved()
    {
        _hasReceivedTeardrop = false;
    }

    private bool HasLivingKnight =>
        DespairKnight.GetKnights(Creature.CombatState).Any(static knight => knight.Creature.IsAlive);

    public bool CanEnterFalseDeath(Creature creature)
    {
        return creature == Creature && !_isFakeDead && HasLivingKnight;
    }

    public async Task EnterFalseDeath()
    {
        if (_isFakeDead)
        {
            return;
        }

        foreach (DespairKnight knight in DespairKnight.GetKnights(Creature.CombatState))
        {
            await knight.LoseHpFromTeardropSwordFalseDeath();
            if (knight.Creature.IsAlive)
            {
                knight.QueueDespair();
            }
        }

        // 假死扣血可能直接击杀骑士；骑士死亡时已让本剑真正死亡，不再进入假死。
        if (!HasLivingKnight)
        {
            return;
        }

        _fakeDeathRecoveryMoveId = ResolveRecoveryMoveId();
        if (Creature.GetPower<ForgottenKnightSwordTeardropPower>() is { } teardrop)
        {
            await PowerCmd.Remove(teardrop);
            _fakeDeathRecoveryMoveId = GetNormalInitialState().Id;
        }

        _fakeDeathPlayerTurnsRemaining = FalseDeathPlayerTurns;
        _isFakeDead = true;
        await FakeDeathDebuffHelper.ClearDebuffs(Creature);
        await ForceFakeDeathChaoZero();
        ForceFalseDeathHiddenIntent();
    }

    public async Task TickFalseDeathOnPlayerTurnStart()
    {
        if (!_isFakeDead)
        {
            return;
        }

        await ForceFakeDeathChaoZero();

        if (_fakeDeathPlayerTurnsRemaining > 1)
        {
            _fakeDeathPlayerTurnsRemaining--;
            ForceFalseDeathHiddenIntent();
            return;
        }

        if (_fakeDeathPlayerTurnsRemaining == 1)
        {
            _fakeDeathPlayerTurnsRemaining = 0;
            if (_isFakeDead) ForceFalseDeathReviveIntent();
            return;
        }

        ForceFalseDeathHiddenIntent();
    }

    public async Task PrepareDespairSpecial(int index, PlayerChoiceContext choiceContext)
    {
        if (_isFakeDead)
        {
            await ForceFakeDeathChaoZero();
            return;
        }

        if (Creature.IsDead)
        {
            return;
        }

        int hpHeal = Math.Max(1, (int)Math.Ceiling(Creature.MaxHp * 0.20m));
        await CreatureCmd.Heal(Creature, hpHeal);
        if (Creature is LibraryCreature lc)
        {
            int chaoHeal = Math.Max(1, (int)Math.Ceiling(lc.MaxChaoValue * 0.20m));
            await LibraryCreatureCmd.HealChaoValue(lc, chaoHeal);
            lc.HealthBar?.RefreshValues();
        }

        _forcedSpecial = index switch
        {
            0 => ForgottenKnightSwordSpecial.PiercingHeart,
            1 => ForgottenKnightSwordSpecial.RendingHeart,
            _ => ForgottenKnightSwordSpecial.RuiningHeart
        };
        _hasStabbedKnight = false;
        SetMoveImmediate(GetSpecialState(_forcedSpecial), forceTransition: true);
    }

    public async Task ApplyTeardropResistances(PlayerChoiceContext choiceContext)
    {
        await ApplyResistanceSet(choiceContext, TeardropResistance());
    }

    public async Task RestoreNormalResistances(PlayerChoiceContext choiceContext)
    {
        await ApplyResistanceSet(choiceContext, NormalResistance());
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        _fadedTrustState = new MoveState(
            FadedTrustMoveId,
            FadedTrustMove,
            new MultiAttackIntent(FadedTrustDamage, 3));

        _hollowPrideState = new MoveState(
            HollowPrideMoveId,
            HollowPrideMove,
            new BadgedDebuffIntent(IntentBadge.RapidWear(HollowPrideVulnerable, HollowPrideVulnerableTurns), HollowPrideVulnerable),
            new DetailedStatusCardIntent<Dazed>(HollowPrideDazed, PileType.Discard));

        _swordOfGriefState = new MoveState(
            SwordOfGriefMoveId,
            SwordOfGriefMove,
            new SingleAttackIntent(SwordOfGriefDamage),
            new DefendIntent(),
            new DetailedBuffIntent<StrengthPower>(SwordOfGriefStrength));

        _tearEdgeSwordState = new MoveState(
            TearEdgeSwordMoveId,
            TearEdgeSwordMove,
            new DebuffIntent(),
            new DetailedSplitStatusCardIntent<Dazed>(
                [(1, PileType.Draw), (1, PileType.Discard)],
                descriptionKey: "FORGOTTEN_KNIGHT_SWORD.tear_edge_sword.description",
                descriptionVars: new Dictionary<string, decimal>
                {
                    ["Frail"] = TearEdgeFrail
                }));

        _piercingHeartState = new MoveState(
            PiercingHeartMoveId,
            PiercingHeartMove,
            new BadgedAttackIntent(
                PiercingHeartDamage,
                "FORGOTTEN_KNIGHT_SWORD.special.no_damage_stab.description",
                IntentBadge.Weak(PiercingHeartWeak)),
            new DebuffIntent());

        _rendingHeartState = new MoveState(
            RendingHeartMoveId,
            RendingHeartMove,
            new MultiAttackIntent(RendingHeartDamage, 2),
            new DetailedStatusCardIntent<Dazed>(RendingHeartDazed, PileType.Draw));

        _ruiningHeartState = new MoveState(
            RuiningHeartMoveId,
            RuiningHeartMove,
            new BadgedAttackIntent(
                RuiningHeartDamage,
                3,
                "FORGOTTEN_KNIGHT_SWORD.special.no_damage_stab.description",
                IntentBadge.Strength(RuiningHeartStrength)),
            new BuffIntent());

        _falseDeathState = new MoveState(
            FalseDeathHiddenMoveId,
            static _ => Task.CompletedTask,
            new HiddenIntent())
        {
            MustPerformOnceBeforeTransitioning = true
        };

        _swordOfGriefState.MustPerformOnceBeforeTransitioning = true;
        _tearEdgeSwordState.MustPerformOnceBeforeTransitioning = true;
        _piercingHeartState.MustPerformOnceBeforeTransitioning = true;
        _rendingHeartState.MustPerformOnceBeforeTransitioning = true;
        _ruiningHeartState.MustPerformOnceBeforeTransitioning = true;

        _fadedTrustState.FollowUpState = _hollowPrideState;
        _hollowPrideState.FollowUpState = _fadedTrustState;
        _swordOfGriefState.FollowUpState = _tearEdgeSwordState;
        _tearEdgeSwordState.FollowUpState = _swordOfGriefState;
        _piercingHeartState.FollowUpState = GetTeardropInitialState();
        _rendingHeartState.FollowUpState = GetTeardropInitialState();
        _ruiningHeartState.FollowUpState = GetTeardropInitialState();
        _falseDeathState.FollowUpState = _falseDeathState;

        return new MonsterMoveStateMachine(
            [
                _fadedTrustState,
                _hollowPrideState,
                _swordOfGriefState,
                _tearEdgeSwordState,
                _piercingHeartState,
                _rendingHeartState,
                _ruiningHeartState,
                _falseDeathState
            ],
            GetNormalInitialState());
    }

    public override async Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        CombatStateLike combatState)
    {
        if (TurnParticipants.IsRoundPlayerTurn(side))
        {
            await TickFalseDeathOnPlayerTurnStart();
        }

        await base.BeforeSideTurnStart(choiceContext, side, participants, combatState);
    }

    private async Task FadedTrustMove(IReadOnlyList<Creature> targets)
    {
        await ExecuteAttack(FadedTrustDamage, 3, LibraryDamageType.Blunt, "NormalBlunt", NormalBluntSfxPath);
    }

    private async Task HollowPrideMove(IReadOnlyList<Creature> targets)
    {
        await CreatureCmd.TriggerAnim(Creature, "NormalParry", 0.42f);
        foreach (Creature target in targets.Where(static target => target.IsAlive))
        {
            await CardPileCmdCompat.AddToCombatAndPreview<Dazed>(target, PileType.Discard, HollowPrideDazed, addedByPlayer: false);
            await LibraryPowerCmd.Apply<LibraryVulnerablePower>(
                target,
                HollowPrideVulnerable,
                HollowPrideVulnerableTurns,
                Creature,
                null);
        }
    }

    private async Task SwordOfGriefMove(IReadOnlyList<Creature> targets)
    {
        await ExecuteAttack(SwordOfGriefDamage, 1, LibraryDamageType.Pierce, "TeardropPierce", TeardropPierceSfxPath);
        await CreatureCmd.GainBlock(Creature, SwordOfGriefBlock, ValueProp.Move, null);
        await PowerCmdCompat.Apply<StrengthPower>(Creature, SwordOfGriefStrength, Creature, null);
    }

    private async Task TearEdgeSwordMove(IReadOnlyList<Creature> targets)
    {
        await CreatureCmd.TriggerAnim(Creature, "TeardropParry", 0.42f);
        foreach (Creature target in targets.Where(static target => target.IsAlive))
        {
            await PowerCmdCompat.ApplyDebuff<FrailPower>(target, TearEdgeFrail, Creature, null);
            await CardPileCmdCompat.AddToCombatAndPreview<Dazed>(target, PileType.Draw, 1, addedByPlayer: false);
            await CardPileCmdCompat.AddToCombatAndPreview<Dazed>(target, PileType.Discard, 1, addedByPlayer: false);
        }
    }

    private Task PiercingHeartMove(IReadOnlyList<Creature> targets) =>
        ExecuteSpecialMove(
            PiercingHeartDamage,
            1,
            LibraryDamageType.Pierce,
            PiercingHeartBossDamage,
            async hitTargets =>
            {
                if (hitTargets.Count > 0)
                {
                    await PowerCmdCompat.ApplyDebuff<WeakPower>(hitTargets, PiercingHeartWeak, Creature, null);
                }
            });

    private Task RendingHeartMove(IReadOnlyList<Creature> targets) =>
        ExecuteSpecialMove(
            RendingHeartDamage,
            2,
            LibraryDamageType.Slash,
            RendingHeartBossDamage,
            async hitTargets =>
            {
                foreach (Creature target in hitTargets)
                {
                    await CardPileCmdCompat.AddToCombatAndPreview<Dazed>(
                        target,
                        PileType.Draw,
                        RendingHeartDazed,
                        addedByPlayer: false);
                }
            });

    private Task RuiningHeartMove(IReadOnlyList<Creature> targets) =>
        ExecuteSpecialMove(
            RuiningHeartDamage,
            3,
            LibraryDamageType.Blunt,
            RuiningHeartBossDamage,
            _ => PowerCmdCompat.Apply<StrengthPower>(Creature, RuiningHeartStrength, Creature, null));

    private async Task ExecuteSpecialMove(
        int damage,
        int hits,
        LibraryDamageType damageType,
        int bossDamage,
        Func<IReadOnlyList<Creature>, Task> afterEffect)
    {
        IReadOnlyList<DamageResult> results = await ExecuteAttack(
            damage,
            hits,
            damageType,
            "DespairAttack",
            ResolveTeardropSfx(damageType));

        IReadOnlyList<Creature> hitTargets = results
            .Where(static result => result.Receiver.IsPlayer && result.UnblockedDamage > 0)
            .Select(static result => result.Receiver)
            .Distinct()
            .ToArray();

        if (hitTargets.Count == 0)
        {
            await StabKnight(bossDamage);
        }

        await afterEffect(hitTargets);
        _forcedSpecial = ForgottenKnightSwordSpecial.None;
    }

    private async Task<IReadOnlyList<DamageResult>> ExecuteAttack(
        int damage,
        int hits,
        LibraryDamageType damageType,
        string animId,
        string sfxPath)
    {
        LocalOggOneShotPlayer.Play(sfxPath, -2f);
        AttackCommand attack = await DamageCmd.Attack(damage)
            .FromMonster(this)
            .WithHitCount(hits)
            .WithAttackerAnim(animId, SegmentDelaySeconds)
            .WithWaitBeforeHit(HitPauseFastSeconds, HitPauseStandardSeconds)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(null);

        return AttackCommandCompat.Results(attack);
    }

    private async Task StabKnight(int bossDamage)
    {
        if (_hasStabbedKnight)
        {
            return;
        }

        DespairKnight? knight = DespairKnight.GetKnights(Creature.CombatState)
            .FirstOrDefault(static knight => knight.Creature.IsAlive);
        if (knight == null)
        {
            return;
        }

        _hasStabbedKnight = true;
        int stabbedSwordCount = knight.RegisterSwordStab(_swordIndex);
        await DespairKnightPierceDespairOverlayController.PlayAsync(stabbedSwordCount);
        await CreatureCmd.SetCurrentHp(
            knight.Creature,
            Math.Max(0m, knight.Creature.CurrentHp - bossDamage));
    }

    public async Task RecoverFromFalseDeath()
    {
        if (!_isFakeDead)
        {
            return;
        }

        ResetFalseDeathState();
        await CreatureCmd.SetCurrentHp(Creature, Math.Max(MinimumRecoveryHp, Creature.MaxHp / 2));
        if (Creature is LibraryCreature lc)
        {
            if (lc.IsChaoed)
            {
                lc.RestoreChaoOnNextOwnerTurn = false;
                lc.RestorePreStunResistance();
            }

            await LibraryCreatureCmd.SetCurrentChaoValue(lc, lc.MaxChaoValue);
            lc.HealthBar?.RefreshValues();
        }
    }

    public async Task DieWithKnight()
    {
        ResetFalseDeathState();
        await CreatureCmd.Kill(Creature);
    }

    private async Task ForceFakeDeathChaoZero()
    {
        if (Creature is not LibraryCreature lc || !lc.HasChaoResistance)
        {
            return;
        }

        if (lc.CurrentChaoValue != 0)
        {
            await LibraryCreatureCmd.SetCurrentChaoValue(lc, 0m);
        }

        lc.HealthBar?.RefreshValues();
    }

    private void ForceFalseDeathHiddenIntent()
    {
        SetMoveImmediate(_falseDeathState, forceTransition: true);
    }

    private void ForceFalseDeathReviveIntent()
    {
        MoveState recoveryState = ResolveRecoveryState();
        SetMoveImmediate(CreateFalseDeathReviveMove(recoveryState), forceTransition: true);
    }

    private MoveState CreateFalseDeathReviveMove(MoveState recoveryState)
    {
        // 假死期间混乱值保持为零，复活动作必须能直接替换混乱状态。
        return new LibraryPhaseTransitionMoveState(
            FalseDeathMoveId,
            ReviveAndPassFalseDeathTurn,
            new HealIntent(),
            new BuffIntent())
        {
            FollowUpState = ResolveFollowUpState(recoveryState),
            MustPerformOnceBeforeTransitioning = true
        };
    }

    private Task ReviveAndPassFalseDeathTurn(IReadOnlyList<Creature> _) => RecoverFromFalseDeath();

    private MoveState ResolveRecoveryState() => _fakeDeathRecoveryMoveId switch
    {
        HollowPrideMoveId => _hollowPrideState,
        SwordOfGriefMoveId => _swordOfGriefState,
        TearEdgeSwordMoveId => _tearEdgeSwordState,
        PiercingHeartMoveId => _piercingHeartState,
        RendingHeartMoveId => _rendingHeartState,
        RuiningHeartMoveId => _ruiningHeartState,
        _ => _fadedTrustState
    };

    private MoveState ResolveFollowUpState(MoveState state)
    {
        string? nextStateId = state.FollowUpState?.Id ?? state.FollowUpStateId;
        return nextStateId switch
        {
            HollowPrideMoveId => _hollowPrideState,
            SwordOfGriefMoveId => _swordOfGriefState,
            TearEdgeSwordMoveId => _tearEdgeSwordState,
            PiercingHeartMoveId => _piercingHeartState,
            RendingHeartMoveId => _rendingHeartState,
            RuiningHeartMoveId => _ruiningHeartState,
            _ => _fadedTrustState
        };
    }

    private string ResolveRecoveryMoveId()
    {
        string? nextMoveId = Creature.Monster?.NextMove.Id;
        return string.IsNullOrWhiteSpace(nextMoveId)
            || nextMoveId is "STUNNED" or FalseDeathMoveId or FalseDeathHiddenMoveId
            ? FadedTrustMoveId
            : nextMoveId;
    }

    private void ResetFalseDeathState()
    {
        _isFakeDead = false;
        _fakeDeathPlayerTurnsRemaining = 0;
        _fakeDeathRecoveryMoveId = FadedTrustMoveId;
    }

    private MoveState GetSpecialState(ForgottenKnightSwordSpecial special) => special switch
    {
        ForgottenKnightSwordSpecial.PiercingHeart => _piercingHeartState,
        ForgottenKnightSwordSpecial.RendingHeart => _rendingHeartState,
        ForgottenKnightSwordSpecial.RuiningHeart => _ruiningHeartState,
        _ => _fadedTrustState
    };

    private MoveState GetNormalInitialState() =>
        _swordIndex == 1 ? _hollowPrideState : _fadedTrustState;

    private MoveState GetTeardropInitialState() =>
        _swordIndex == 1 ? _tearEdgeSwordState : _swordOfGriefState;

    private async Task ApplyResistanceSet(
        PlayerChoiceContext choiceContext,
        LibraryCreatureResistanceData.Resistance resistance)
    {
        if (Creature is not LibraryCreature lc)
        {
            return;
        }

        await LibraryCreatureCmd.SetPhysicalResistance(choiceContext, lc, Creature, LibraryDamageType.Blunt, resistance.Blunt);
        await LibraryCreatureCmd.SetPhysicalResistance(choiceContext, lc, Creature, LibraryDamageType.Slash, resistance.Slash);
        await LibraryCreatureCmd.SetPhysicalResistance(choiceContext, lc, Creature, LibraryDamageType.Pierce, resistance.Pierce);
        await LibraryCreatureCmd.SetChaoResistance(choiceContext, lc, Creature, LibraryDamageType.Blunt, resistance.Blunt);
        await LibraryCreatureCmd.SetChaoResistance(choiceContext, lc, Creature, LibraryDamageType.Slash, resistance.Slash);
        await LibraryCreatureCmd.SetChaoResistance(choiceContext, lc, Creature, LibraryDamageType.Pierce, resistance.Pierce);
        lc.HealthBar?.RefreshValues();
    }

    private static LibraryCreatureResistanceData.Resistance NormalResistance() => new()
    {
        Slash = LibraryResistanceLevel.Normal,
        Pierce = LibraryResistanceLevel.Normal,
        Blunt = LibraryResistanceLevel.Normal
    };

    private static LibraryCreatureResistanceData.Resistance TeardropResistance() => new()
    {
        Slash = LibraryResistanceLevel.Resist,
        Pierce = LibraryResistanceLevel.Fatal,
        Blunt = LibraryResistanceLevel.Resist
    };

    private static string ResolveTeardropSfx(LibraryDamageType type) => type switch
    {
        LibraryDamageType.Blunt => TeardropBluntSfxPath,
        LibraryDamageType.Slash => TeardropSlashSfxPath,
        _ => TeardropPierceSfxPath
    };

    public static bool IsCreatureFakeDead(Creature creature) =>
        creature.Monster is ForgottenKnightSword { IsFakeDead: true };

    private IEnumerable<AbstractIntent> EnumerateIntentAssets()
    {
        yield return new MultiAttackIntent(FadedTrustDamage, 3);
        yield return new DetailedStatusCardIntent<Dazed>(HollowPrideDazed, PileType.Discard);
        yield return new BadgedDebuffIntent(IntentBadge.RapidWear(HollowPrideVulnerable, HollowPrideVulnerableTurns), HollowPrideVulnerable);
        yield return new SingleAttackIntent(SwordOfGriefDamage);
        yield return new DefendIntent();
        yield return new DetailedBuffIntent<StrengthPower>(SwordOfGriefStrength);
        yield return new DetailedSplitStatusCardIntent<Dazed>(
            [(1, PileType.Draw), (1, PileType.Discard)],
            descriptionKey: "FORGOTTEN_KNIGHT_SWORD.tear_edge_sword.description",
            descriptionVars: new Dictionary<string, decimal>
            {
                ["Frail"] = TearEdgeFrail
            });
        yield return new BadgedAttackIntent(PiercingHeartDamage, "FORGOTTEN_KNIGHT_SWORD.special.no_damage_stab.description", IntentBadge.Weak(PiercingHeartWeak));
        yield return new BadgedAttackIntent(RendingHeartDamage, 2, "FORGOTTEN_KNIGHT_SWORD.special.no_damage_stab.description", IntentBadge.StatusCard<Dazed>(RendingHeartDazed));
        yield return new BadgedAttackIntent(RuiningHeartDamage, 3, "FORGOTTEN_KNIGHT_SWORD.special.no_damage_stab.description", IntentBadge.Strength(RuiningHeartStrength));
    }
}
