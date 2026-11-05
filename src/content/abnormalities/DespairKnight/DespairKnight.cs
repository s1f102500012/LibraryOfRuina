using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LibraryLib.Entities.Creatures;
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
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;

namespace LibraryOfRuina.content.abnormalities.DespairKnight;

public sealed class DespairKnight : LorMonsterModel
{
    public override float DeathAnimLengthOverride => LayeredBossSpine.DeathLength(this);

    public const string MoveId = "SHELTERING_UNKNOWN";
    public const string GrantTeardropMoveId = "GRANT_TEARDROP";
    public const int TeardropFalseDeathHpLossPercent = 10;

    public const string Root = DespairKnightAssets.DespairKnightMonsterRoot;
    public const string IdleTexturePath = Root + "idle.png";
    public const string StabbedOneTexturePath = Root + "stabbed_1.png";
    public const string StabbedTwoTexturePath = Root + "stabbed_2.png";
    public const string StabbedThreeTexturePath = Root + "stabbed_3.png";
    public const string DespairTexturePath = Root + "despair.png";

    public const string SfxRoot = DespairKnightAssets.DespairKnightSfxRoot;
    public const string EnterDespairSfxPath = SfxRoot + "enter_despair.ogg";
    public const string CryingLoopSfxPath = SfxRoot + "crying_loop.ogg";
    public const string StabbedSfxPath = SfxRoot + "stabbed.ogg";
    public const string BrokenHeartSfxPath = SfxRoot + "broken_heart.ogg";
    public const string TeardropGrantSfxPath = SfxRoot + "grant_teardrop.ogg";

    public static readonly string[] SfxPaths =
    [
        EnterDespairSfxPath,
        CryingLoopSfxPath,
        StabbedSfxPath,
        BrokenHeartSfxPath,
        TeardropGrantSfxPath
    ];

    public static readonly string[] PowerIconPaths =
    [
        DespairKnightAssets.SorrowPowerIcon,
        DespairKnightAssets.DespairPowerIcon,
        DespairKnightAssets.ProtectionPowerIcon,
        DespairKnightAssets.BrokenHeartPowerIcon
    ];

    public static readonly string[] AssetPathsStatic =
        DespairKnightCreatureVisuals
            .Profile.AssetPaths
            .Concat(SfxPaths)
            .Concat(PowerIconPaths)
            .Concat(DespairKnightPierceDespairOverlayController.AssetPaths)
            .ToArray();

    private static readonly string[] NormalBackgroundTextLineKeys =
    [
        "DESPAIR_KNIGHT.backgroundText.normal.0",
        "DESPAIR_KNIGHT.backgroundText.normal.1",
        "DESPAIR_KNIGHT.backgroundText.normal.2",
        "DESPAIR_KNIGHT.backgroundText.normal.3",
        "DESPAIR_KNIGHT.backgroundText.normal.4",
    ];

    private static readonly string[] DespairBackgroundTextLineKeys =
    [
        "DESPAIR_KNIGHT.backgroundText.despair.0",
        "DESPAIR_KNIGHT.backgroundText.despair.1",
        "DESPAIR_KNIGHT.backgroundText.despair.2",
        "DESPAIR_KNIGHT.backgroundText.despair.3",
    ];

    private static readonly string[] BrokenHeartBackgroundTextLineKeys =
    [
        "DESPAIR_KNIGHT.backgroundText.brokenHeart.0",
        "DESPAIR_KNIGHT.backgroundText.brokenHeart.1",
        "DESPAIR_KNIGHT.backgroundText.brokenHeart.2",
        "DESPAIR_KNIGHT.backgroundText.brokenHeart.3",
    ];

    private const float BackgroundTextIntervalSeconds = 5f;
    private static readonly Rect2 BackgroundTextSpawnArea = new(150f, 200f, 900f, 450f);

    private MoveState _moveState = null!;
    private MoveState _grantTeardropState = null!;
    private bool _despairPending;
    private bool _isInDespair;
    private bool _brokenHeartPending;
    private int _stabbedSwordCount;
    private int _stabbedTexturePlayerTurnsRemaining;
    private int _shelterMovesUntilTeardrop = 2;
    private LocalOggLoopPlayer.LoopHandle? _cryingLoop;
    private bool _backgroundMoonTextLoopStarted;
    private bool _isBrokenHeart;

    private static readonly string DespairKnightPageRelicTitleLocKey =
        $"{ModelDb.GetId<DespairKnightPageRelic>().Entry}.title";

    public bool IsInDespair => _isInDespair;

    public bool IsBrokenHeartPending => _brokenHeartPending;

    public override int MinInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 396, 344);

    public override int MaxInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 400, 350);

    public override int DefaultChaoResistance => 200;

    public override LibraryCreatureResistanceData.Resistance? DefaultPhysicalResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Resist,
        Pierce = LibraryResistanceLevel.Resist,
        Blunt = LibraryResistanceLevel.Resist
    };

    public override LibraryCreatureResistanceData.Resistance? DefaultChaoResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Resist,
        Pierce = LibraryResistanceLevel.Resist,
        Blunt = LibraryResistanceLevel.Resist
    };

    public override IEnumerable<string> AssetPaths =>
        AssetPathsStatic
            .Concat(EnumerateIntentAssets().SelectMany(static intent => intent.AssetPaths))
            .Distinct();

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        _despairPending = false;
        _isInDespair = false;
        _brokenHeartPending = false;
        _stabbedSwordCount = 0;
        _stabbedTexturePlayerTurnsRemaining = 0;
        _shelterMovesUntilTeardrop = 2;
        _backgroundMoonTextLoopStarted = false;
        _isBrokenHeart = false;
        StopCryingLoop();
        EncounterBgmController.RegisterMonster(Creature);
        Creature? sword = Creature.CombatState?.Enemies.FirstOrDefault(static enemy => enemy.Monster is ForgottenKnightSword);
        await PowerCmdCompat.Apply<DespairKnightSorrowPower>(Creature, 1m, Creature, null, silent: true);
        await PowerCmdCompat.Apply<DespairKnightDespairPower>(Creature, 1m, Creature, null, silent: true);
        await PowerCmdCompat.Apply<DespairKnightProtectionPower>(Creature, 1m, Creature, null, silent: true);
        await PowerCmdCompat.Apply<DespairKnightBrokenHeartPower>(Creature, 1m, Creature, null, silent: true);
        await PowerCmdCompat.Ensure<UntargetablePower>(Creature);
        if (sword != null)
        {
            await PowerCmdCompat.Apply<ForgottenKnightSwordTeardropPower>(sword, 1m, Creature, null, true);
        }
    }

    public override void BeforeRemovedFromRoom()
    {
        base.BeforeRemovedFromRoom();
        StopCryingLoop();
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        StopCryingLoop();
        return base.AfterCombatEnd(room);
    }

    public override Task BeforeCombatStart()
    {
        if (!Creature.IsDead && !_backgroundMoonTextLoopStarted)
        {
            _backgroundMoonTextLoopStarted = true;
            StartBackgroundMoonTextLoop(NormalBackgroundTextLineKeys);
        }

        return Task.CompletedTask;
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

        AddDespairKnightPageRewards();
        StopCryingLoop();
        await KillSwordsWithKnight();
    }

    /// <summary>
    /// 骑士死亡后遗忘之剑失去假死依托，包括正处于假死中的剑在内全部真正死亡，避免战斗无法结束。
    /// </summary>
    private async Task KillSwordsWithKnight()
    {
        if (GetKnights(Creature.CombatState).Any(static knight => knight.Creature.IsAlive))
        {
            return;
        }

        foreach (ForgottenKnightSword sword in GetSwords(Creature.CombatState))
        {
            await sword.DieWithKnight();
        }
    }

    public void QueueDespair()
    {
        if (!_isInDespair)
        {
            _despairPending = true;
        }
    }

    public Task LoseHpFromTeardropSwordFalseDeath()
    {
        if (Creature.IsDead)
        {
            return Task.CompletedTask;
        }

        int hpLoss = Math.Max(1, (int)Math.Ceiling(Creature.MaxHp * TeardropFalseDeathHpLossPercent / 100m));
        return CreatureCmd.SetCurrentHp(Creature, Math.Max(0m, Creature.CurrentHp - hpLoss));
    }

    public int RegisterSwordStab(int swordIndex)
    {
        _stabbedSwordCount = Math.Clamp(_stabbedSwordCount + 1, 0, ForgottenKnightSword.RequiredSwordCount);
        _stabbedTexturePlayerTurnsRemaining = 1;
        LocalOggOneShotPlayer.Play(StabbedSfxPath, -2f);

        if (!_brokenHeartPending && !_isBrokenHeart && _stabbedSwordCount > 0)
        {
            _brokenHeartPending = true;
            LocalOggOneShotPlayer.Play(BrokenHeartSfxPath, -2f);
        }

        return _stabbedSwordCount;
    }

    public async Task TriggerDespair(PlayerChoiceContext choiceContext, CombatStateLike combatState)
    {
        if (Creature.IsDead || _isInDespair)
        {
            return;
        }

        _despairPending = false;
        _isInDespair = true;
        _stabbedSwordCount = 0;
        _stabbedTexturePlayerTurnsRemaining = 0;
        LocalOggOneShotPlayer.Play(EnterDespairSfxPath, -2f);
        StopCryingLoop();
        _cryingLoop = LocalOggLoopPlayer.StartLoop(CryingLoopSfxPath, -5f);
        StartBackgroundMoonTextLoop(DespairBackgroundTextLineKeys);

        ForgottenKnightSword[] swords = GetSwords(combatState).ToArray();
        var deadSwords = swords.Where(static sword => sword.IsFakeDead).ToArray();
        foreach (ForgottenKnightSword deadSword in deadSwords)
        {
            await deadSword.RecoverFromFalseDeath();
        }
        for (int i = 0; i < swords.Length; i++)
        {
            await swords[i].PrepareDespairSpecial(i, choiceContext);
        }
    }

    public async Task TriggerBrokenHeart(PlayerChoiceContext choiceContext)
    {
        if (Creature.IsDead || !_brokenHeartPending)
        {
            return;
        }

        _brokenHeartPending = false;
        ClearDespairState();
        _isBrokenHeart = true;
        StartBackgroundMoonTextLoop(BrokenHeartBackgroundTextLineKeys);

        Creature.GetPower<UntargetablePower>()?.RemoveInternal();
        if (Creature is LibraryCreature lc)
        {
            await LibraryCreatureCmd.SetCurrentChaoValue(lc, 0m);
            if (!lc.IsChaoed)
            {
                await LibraryCreatureCmd.Stun(lc);
            }
            lc.HealthBar?.RefreshValues();
        }
    }

    private void ClearDespairState()
    {
        _isInDespair = false;
        _stabbedSwordCount = 0;
        _stabbedTexturePlayerTurnsRemaining = 0;
        StopCryingLoop();
    }

    public string ResolveIdleTexturePath()
    {
        if (_isInDespair)
        {
            return DespairTexturePath;
        }

        if (_stabbedTexturePlayerTurnsRemaining > 0)
        {
            return _stabbedSwordCount switch
            {
                >= 3 => StabbedThreeTexturePath,
                2 => StabbedTwoTexturePath,
                1 => StabbedOneTexturePath,
                _ => IdleTexturePath
            };
        }

        return IdleTexturePath;
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        _moveState = new MoveState(
            MoveId,
            ShelteringUnknownMove,
            new UnknownIntent(),
            new DetailedBuffIntent<PlatingPower>(
                4,
                DetailedBuffTargetScope.OtherEnemies,
                descriptionKey: "DESPAIR_KNIGHT.sheltering_unknown.plating.description"));
        _grantTeardropState = new MoveState(
            GrantTeardropMoveId,
            GrantTeardropMove,
            new DetailedBuffIntent<ForgottenKnightSwordTeardropPower>(
                1,
                DetailedBuffTargetScope.RandomEnemy,
                descriptionKey: "DESPAIR_KNIGHT.grant_teardrop.description"))
        {
            MustPerformOnceBeforeTransitioning = true
        };
        _moveState.FollowUpState = _moveState;
        _grantTeardropState.FollowUpState = _moveState;
        return new MonsterMoveStateMachine([_moveState, _grantTeardropState], _moveState);
    }

    public override async Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        CombatStateLike combatState)
    {
        if (TurnParticipants.IsRoundPlayerTurn(side))
        {
            if (_brokenHeartPending)
            {
                await TriggerBrokenHeart(choiceContext);
            }
            else if (_despairPending)
            {
                await TriggerDespair(choiceContext, combatState);
            }
            else
            {
                ClearDespairState();
            }
        }

        await base.BeforeSideTurnStart(choiceContext, side, participants, combatState);
    }

    private async Task ShelteringUnknownMove(IReadOnlyList<Creature> targets)
    {
        StartBackgroundMoonTextLoopForCurrentState();
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.45f);
        IReadOnlyList<Creature> otherEnemies = Creature.CombatState?.Enemies
            .Where(enemy => enemy.IsAlive && enemy != Creature && !ForgottenKnightSword.IsCreatureFakeDead(enemy))
            .ToArray() ?? [];
        if (otherEnemies.Count > 0)
        {
            await PowerCmdCompat.Apply<PlatingPower>(otherEnemies, 4, Creature, null);
        }

        _shelterMovesUntilTeardrop--;
        if (_shelterMovesUntilTeardrop <= 0)
        {
            _shelterMovesUntilTeardrop = 2;
            SetMoveImmediate(_grantTeardropState, forceTransition: true);
        }
    }

    private async Task GrantTeardropMove(IReadOnlyList<Creature> targets)
    {
        StartBackgroundMoonTextLoopForCurrentState();
        await CreatureCmd.TriggerAnim(Creature, "Cast", 0.45f);
        IReadOnlyList<ForgottenKnightSword> swords = GetSwords(Creature.CombatState)
            .Where(static sword => sword.CanReceiveTeardrop())
            .ToArray();
        ForgottenKnightSword? sword = swords.Count > 0
            ? RunRng.MonsterAi.NextItem(swords)
            : null;
        if (sword != null)
        {
            await sword.ApplyTeardrop();
        }
    }

    private void StopCryingLoop()
    {
        _cryingLoop?.Stop();
        _cryingLoop = null;
    }

    private void StartBackgroundMoonTextLoopForCurrentState()
    {
        if (_isBrokenHeart)
        {
            StartBackgroundMoonTextLoop(BrokenHeartBackgroundTextLineKeys);
        }
        else if (_isInDespair)
        {
            StartBackgroundMoonTextLoop(DespairBackgroundTextLineKeys);
        }
        else
        {
            StartBackgroundMoonTextLoop(NormalBackgroundTextLineKeys);
        }
    }

    private static void StartBackgroundMoonTextLoop(IReadOnlyList<string> lineKeys)
    {
        if (lineKeys.Count == 0)
        {
            return;
        }

        MonsterMoonTextLoop.Start(lineKeys, BackgroundTextIntervalSeconds, BackgroundTextSpawnArea);
    }

    private void AddDespairKnightPageRewards()
    {
        if (Creature.CombatState?.RunState.CurrentRoom is not CombatRoom room
            || !room.Encounter.MonstersWithSlots.Any(pair => pair.Item1 is DespairKnight))
        {
            return;
        }

        foreach (Player player in room.CombatState.Players)
        {
            if (!AbnormalityPageRewardHelper.ShouldAddPageReward<DespairKnightPageRelic>(
                room,
                player,
                DespairKnightPageRelicTitleLocKey))
            {
                continue;
            }

            room.AddExtraReward(player, new RelicReward(
                ModelDb.Relic<DespairKnightPageRelic>().ToMutable(),
                player));
        }
    }

    public static IEnumerable<DespairKnight> GetKnights(CombatStateLike? combatState) =>
        combatState?.Enemies
            .Select(static enemy => enemy.Monster as DespairKnight)
            .Where(static knight => knight != null)
            .Cast<DespairKnight>()
            .ToArray() ?? [];

    public static IEnumerable<ForgottenKnightSword> GetSwords(CombatStateLike? combatState) =>
        combatState?.Enemies
            .Select(static enemy => enemy.Monster as ForgottenKnightSword)
            .Where(static sword => sword != null)
            .Cast<ForgottenKnightSword>()
            .ToArray() ?? [];

    private IEnumerable<AbstractIntent> EnumerateIntentAssets()
    {
        yield return new UnknownIntent();
        yield return new DetailedBuffIntent<PlatingPower>(
            4,
            DetailedBuffTargetScope.OtherEnemies,
            descriptionKey: "DESPAIR_KNIGHT.sheltering_unknown.plating.description");
        yield return new DetailedBuffIntent<ForgottenKnightSwordTeardropPower>(
            1,
            DetailedBuffTargetScope.RandomEnemy,
            descriptionKey: "DESPAIR_KNIGHT.grant_teardrop.description");
    }
}
