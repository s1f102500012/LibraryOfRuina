using LibraryLib.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using LibraryLib.Entities.Creatures;
using LibraryOfRuina.content.abnormalities.GalaxyChild;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.framework.audio;
using LibraryOfRuina.framework.combat;
using LibraryOfRuina.framework.intents;
using LibraryOfRuina.framework.monsters;
using LibraryOfRuina.infra.helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;

namespace LibraryOfRuina.content.liberation.Art;

internal enum ArtFloorGalaxyFriendInitialMove
{
    Wait,
    StarlightFall,
    Twinkle
}

public sealed class ArtFloorGalaxyFriend : LorMonsterModel
{
    private const string WaitMoveId = "WAIT";
    private const string StarlightFallMoveId = "STARLIGHT_FALL";
    private const string TwinkleMoveId = "TWINKLE";
    private const string StunnedMoveId = "STUNNED";
    private const string FakeDeathHiddenMoveId = "FAKE_DEATH_HIDDEN";
    private const string FakeDeathReviveMoveId = "FAKE_DEATH_REVIVE";

    public const int FakeDeathHp = 0;
    public const int FakeDeathStunTurns = 2;
    private const int MinimumRecoveryHp = 1;
    private const int WaitIntangible = 2; // 等候：自身获得的无实体层数（施加后跳过下一次衰减），各进阶相同。
    private const int StarlightHits = 5; // 星辉洒落：攻击次数（单次伤害见 StarlightDamage），各进阶相同。
    private const int TwinkleBlock = 9; // 闪烁：攻击后自身获得的格挡，各进阶相同。
    private const decimal RecoveryHpRatio = 0.80m;
    private const float SegmentDelaySeconds = 0.48f;

    private ArtFloorGalaxyFriendInitialMove _initialMove = ArtFloorGalaxyFriendInitialMove.Wait;
    private bool _isFakeDead;
    private int _fakeDeathPlayerTurnStunsRemaining;
    private string _fakeDeathRecoveryMoveId = WaitMoveId;
    private bool _forceTrueDeath;
    private bool _isResolvingAllFriendsDeath;
    private MoveState _waitState = null!;
    private MoveState _starlightState = null!;
    private MoveState _twinkleState = null!;
    private MoveState _fakeDeathHiddenState = null!;

    public bool IsFakeDead => _isFakeDead;

    public override bool ShouldDisappearFromDoom => false;

    public override int MinInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 197, 124);

    public override int MaxInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 200, 126);

    public override int DefaultChaoResistance => 120;

    public override LibraryCreatureResistanceData.Resistance? DefaultPhysicalResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Endure,
        Pierce = LibraryResistanceLevel.Normal,
        Blunt = LibraryResistanceLevel.Resist
    };

    public override LibraryCreatureResistanceData.Resistance? DefaultChaoResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Vulnerable,
        Pierce = LibraryResistanceLevel.Fatal,
        Blunt = LibraryResistanceLevel.Normal
    };

    private int StarlightDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 7, 5);

    private int TwinkleDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 26, 24);

    public override IEnumerable<string> AssetPaths =>
        new[]
        {
            GalaxyFriend.IdleTexturePath,
            GalaxyFriend.AttackTexturePath,
            GalaxyFriend.HitTexturePath,
            GalaxyFriend.ParryTexturePath,
            GalaxyFriend.AttackSfxPath,
            GalaxyFriend.HealSfxPath,
            GalaxyFriend.ParrySfxPath
        }
        .Concat(EnumerateIntentAssets().SelectMany(static intent => intent.AssetPaths))
        .Distinct();

    internal void ConfigureInitialMove(ArtFloorGalaxyFriendInitialMove initialMove)
    {
        AssertMutable();
        _initialMove = initialMove;
    }

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        ResetFakeDeathState();
        _forceTrueDeath = false;
        SetIntentContainerVisible(true);
        EncounterBgmController.RegisterMonster(Creature);

        await PowerCmdCompat.Apply<MinionPower>(Creature, 1m, Creature, null, silent: true);
        await PowerCmdCompat.Apply<ArtFloorGalaxyDoNotLeaveMePower>(Creature, 1m, Creature, null, silent: true);
    }

    public override Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature creature,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (creature != Creature || wasRemovalPrevented || _isFakeDead)
        {
            return Task.CompletedTask;
        }

        return AreAllOtherFriendsDeadOrFakeDead(Creature.CombatState, this)
            ? TriggerAllFriendsTrueDeath(Creature.CombatState)
            : NotifyLittleGalaxyAfterFriendDeath(Creature.CombatState);
    }

    public bool CanEnterFakeDeath(Creature creature)
    {
        return creature == Creature
            && !_forceTrueDeath
            && !_isFakeDead
            && !_isResolvingAllFriendsDeath
            && !CombatManager.Instance.IsOverOrEnding
            && !AreAllOtherFriendsDeadOrFakeDead(creature.CombatState, this)
            && creature.CombatState?.Encounter is ArtFloorLiberationEncounter { CurrentPhase: 3 };
    }

    public async Task EnterFakeDeath()
    {
        if (_isFakeDead || _forceTrueDeath)
        {
            return;
        }

        _fakeDeathRecoveryMoveId = ResolveRecoveryMoveId();
        _fakeDeathPlayerTurnStunsRemaining = Math.Max(0, FakeDeathStunTurns);
        _isFakeDead = true;

        await FakeDeathDebuffHelper.ClearDebuffs(Creature);
        if (ShouldSkipFakeDeathMoveUpdate())
        {
            return;
        }

        ForceFakeDeathChaoZero();
        ForceFakeDeathHiddenIntent();
    }

    public async Task TickFakeDeathOnPlayerTurnStart()
    {
        if (ShouldTriggerAllFriendsTrueDeath(Creature.CombatState))
        {
            await TriggerAllFriendsTrueDeath(Creature.CombatState);
            return;
        }

        if (!_isFakeDead)
        {
            return;
        }

        ForceFakeDeathChaoZero();
        if (_fakeDeathPlayerTurnStunsRemaining > 1)
        {
            _fakeDeathPlayerTurnStunsRemaining--;
            ForceFakeDeathHiddenIntent();
            return;
        }

        if (_fakeDeathPlayerTurnStunsRemaining == 1)
        {
            _fakeDeathPlayerTurnStunsRemaining = 0;
            ForceFakeDeathReviveIntent();
            return;
        }

        ForceFakeDeathHiddenIntent();
    }

    public static bool IsCreatureFakeDead(Creature creature)
    {
        return creature.Monster is ArtFloorGalaxyFriend { _isFakeDead: true };
    }

    public static bool ShouldTriggerAllFriendsTrueDeath(CombatStateLike? combatState)
    {
        IReadOnlyList<ArtFloorGalaxyFriend> friends = GetFriends(combatState);
        return friends.Count >= 2
            && friends.All(static friend => friend.IsDeadOrFakeDeadForAllFriendsDeath());
    }

    public static bool ShouldHoldCombatOpenForFakeDeath(CombatStateLike? combatState)
    {
        IReadOnlyList<ArtFloorGalaxyFriend> friends = GetFriends(combatState);
        return friends.Count >= 2
            && friends.Any(static friend => friend._isFakeDead)
            && friends.Any(static friend => friend.Creature.IsAlive && !friend._isFakeDead);
    }

    public static bool AreAllOtherFriendsDeadOrFakeDead(
        CombatStateLike? combatState,
        ArtFloorGalaxyFriend owner)
    {
        return GetFriends(combatState)
            .Where(friend => !ReferenceEquals(friend, owner))
            .All(static friend => friend.IsDeadOrFakeDeadForAllFriendsDeath());
    }

    public static async Task TriggerAllFriendsTrueDeath(CombatStateLike? combatState)
    {
        IReadOnlyList<ArtFloorGalaxyFriend> friends = GetFriends(combatState);
        if (friends.Count < 2
            || friends.Any(static friend => !friend.IsDeadOrFakeDeadForAllFriendsDeath())
            || friends.Any(static friend => friend._isResolvingAllFriendsDeath))
        {
            return;
        }

        ArtFloorLittleGalaxyBoss? boss = combatState?.Enemies
            .Select(static creature => creature.Monster)
            .OfType<ArtFloorLittleGalaxyBoss>()
            .FirstOrDefault();

        ArtFloorGalaxyFriend[] fakeDeadFriends = friends
            .Where(static friend => friend._isFakeDead)
            .ToArray();

        Log.Warn("[ArtFloorGalaxyFriend] clearing Galaxy Friends after both reached death or fake death.");
        foreach (ArtFloorGalaxyFriend friend in friends)
        {
            friend._forceTrueDeath = true;
            friend._isResolvingAllFriendsDeath = true;
            friend.SetIntentContainerVisible(false);
            friend.ResetFakeDeathState(clearAllFriendsDeathState: false);
        }

        if (fakeDeadFriends.Length > 0)
        {
            await CreatureCmd.Kill(
                fakeDeadFriends.Select(static friend => friend.Creature).ToArray(),
                force: true);
        }

        if (boss != null)
        {
            await boss.QueueAllFriendsDeadEgo();
        }
    }

    public static IReadOnlyList<ArtFloorGalaxyFriend> GetFriends(CombatStateLike? combatState)
    {
        return combatState?.Enemies
            .Select(static creature => creature.Monster as ArtFloorGalaxyFriend)
            .Where(static friend => friend != null)
            .Cast<ArtFloorGalaxyFriend>()
            .ToArray() ?? [];
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        _waitState = new MoveState(
            WaitMoveId,
            WaitMove,
            new DetailedBuffIntent<IntangiblePower>(WaitIntangible));

        _starlightState = new MoveState(
            StarlightFallMoveId,
            StarlightFallMove,
            new MultiAttackIntent(StarlightDamage, StarlightHits));

        _twinkleState = new MoveState(
            TwinkleMoveId,
            TwinkleMove,
            new CombinedAttackDefendIntent(() => TwinkleDamage, blockAmount: TwinkleBlock));

        _fakeDeathHiddenState = new MoveState(
            FakeDeathHiddenMoveId,
            static _ => Task.CompletedTask,
            new HiddenIntent())
        {
            MustPerformOnceBeforeTransitioning = true
        };

        _waitState.FollowUpState = _starlightState;
        _starlightState.FollowUpState = _twinkleState;
        _twinkleState.FollowUpState = _waitState;
        _fakeDeathHiddenState.FollowUpState = _fakeDeathHiddenState;

        MonsterState initialState = _initialMove switch
        {
            ArtFloorGalaxyFriendInitialMove.StarlightFall => _starlightState,
            ArtFloorGalaxyFriendInitialMove.Twinkle => _twinkleState,
            _ => _waitState
        };

        return new MonsterMoveStateMachine(
            [_waitState, _starlightState, _twinkleState, _fakeDeathHiddenState],
            initialState);
    }

    private async Task WaitMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(GalaxyFriend.ParrySfxPath, -2f);
        await CreatureCmd.TriggerAnim(Creature, "Parry", SegmentDelaySeconds);
        IntangiblePower? intangible = await PowerCmdCompat.Apply<IntangiblePower>(Creature, WaitIntangible, Creature, null);
        if (intangible != null)
        {
            intangible.SkipNextDurationTick = true;
        }
    }

    private async Task StarlightFallMove(IReadOnlyList<Creature> targets)
    {
        for (int i = 0; i < StarlightHits; i++)
        {
            if (Creature.IsDead)
            {
                return;
            }

            LocalOggOneShotPlayer.Play(GalaxyFriend.AttackSfxPath, -2f);
            await AbnormalityAnimHelper.ExecuteAttackSegment(
                this,
                StarlightDamage,
                animId: "Attack",
                // 星光坠落只换一次图，第一段等到换图后的命中，后续各段落在人物鼓起的时刻（见 GalaxyFriendCreatureVisuals）
                delaySeconds: i == 0
                    ? GalaxyFriendCreatureVisuals.AttackImpactSeconds
                    : GalaxyFriendCreatureVisuals.FollowUpHitSeconds);
        }
    }

    private async Task TwinkleMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(GalaxyFriend.AttackSfxPath, -2f);
        await AbnormalityAnimHelper.ExecuteAttackSegment(
            this,
            TwinkleDamage,
            animId: "Attack",
            delaySeconds: GalaxyFriendCreatureVisuals.AttackImpactSeconds);
        await CreatureCmd.GainBlock(Creature, TwinkleBlock, ValueProp.Move, null);
    }

    private async Task RecoverFromFakeDeath()
    {
        if (!_isFakeDead)
        {
            return;
        }

        int targetHp = Math.Max(MinimumRecoveryHp, (int)Math.Ceiling(Creature.MaxHp * RecoveryHpRatio));
        ResetFakeDeathState();
        await CreatureCmd.SetCurrentHp(Creature, targetHp);
        await RestoreChaoAfterFakeDeath();
        SetIntentContainerVisible(true);
    }

    private void ForceFakeDeathChaoZero()
    {
        if (Creature is not LibraryCreature lc || !lc.HasChaoResistance)
        {
            return;
        }

        if (lc.CurrentChaoValue != 0)
        {
            lc.SetCurrentChaoValueInternal(0m);
        }

        RefreshChaoBar(lc);
    }

    private void ForceFakeDeathHiddenIntent()
    {
        TrySetFakeDeathMove(_fakeDeathHiddenState);
        SetIntentContainerVisible(false);
    }

    private void ForceFakeDeathReviveIntent()
    {
        MoveState recoveryState = ResolveRecoveryState();
        TrySetFakeDeathMove(CreateFakeDeathReviveMove(recoveryState));
        SetIntentContainerVisible(false);
    }

    private void TrySetFakeDeathMove(MoveState state)
    {
        if (ShouldSkipFakeDeathMoveUpdate())
        {
            return;
        }

        try
        {
            Creature.Monster?.SetMoveImmediate(state, forceTransition: true);
        }
        catch (NullReferenceException exception) when (ShouldSkipFakeDeathMoveUpdate())
        {
            Log.Warn("[ArtFloorGalaxyFriend] skipped fake-death move update during teardown: " + exception.Message);
        }
    }

    private bool ShouldSkipFakeDeathMoveUpdate()
    {
        return _forceTrueDeath
            || _isResolvingAllFriendsDeath
            || CombatManager.Instance?.IsOverOrEnding == true
            || Creature.IsDead
            || Creature.CombatState == null
            || !ReferenceEquals(Creature.Monster, this);
    }

    private async Task RestoreChaoAfterFakeDeath()
    {
        if (Creature is not LibraryCreature lc || !lc.HasChaoResistance)
        {
            return;
        }

        if (lc.IsChaoed)
        {
            lc.RestoreChaoOnNextOwnerTurn = false;
            lc.RestorePreStunResistance();
        }

        if (lc.CurrentChaoValue < lc.MaxChaoValue)
        {
            await LibraryCreatureCmd.SetCurrentChaoValue(lc, lc.MaxChaoValue);
        }

        RefreshChaoBar(lc);
    }

    private MoveState CreateFakeDeathReviveMove(MoveState recoveryState)
    {
        return new LibraryPhaseTransitionMoveState(
            FakeDeathReviveMoveId,
            ReviveAndPassFakeDeathTurn,
            new HealIntent(),
            new BuffIntent())
        {
            FollowUpState = ResolveFollowUpState(recoveryState),
            MustPerformOnceBeforeTransitioning = true
        };
    }

    private Task ReviveAndPassFakeDeathTurn(IReadOnlyList<Creature> _) => RecoverFromFakeDeath();

    private string ResolveRecoveryMoveId()
    {
        string? nextMoveId = Creature.Monster?.NextMove?.Id;
        return string.IsNullOrWhiteSpace(nextMoveId)
            || nextMoveId is StunnedMoveId or FakeDeathHiddenMoveId or FakeDeathReviveMoveId
            ? WaitMoveId
            : nextMoveId;
    }

    private MoveState ResolveRecoveryState() => _fakeDeathRecoveryMoveId switch
    {
        StarlightFallMoveId => _starlightState,
        TwinkleMoveId => _twinkleState,
        _ => _waitState
    };

    private MoveState ResolveFollowUpState(MoveState state)
    {
        string? nextStateId = state.FollowUpState?.Id ?? state.FollowUpStateId;
        return nextStateId switch
        {
            StarlightFallMoveId => _starlightState,
            TwinkleMoveId => _twinkleState,
            _ => _waitState
        };
    }

    private void ResetFakeDeathState(bool clearAllFriendsDeathState = true)
    {
        _isFakeDead = false;
        _fakeDeathPlayerTurnStunsRemaining = 0;
        _fakeDeathRecoveryMoveId = WaitMoveId;
        if (clearAllFriendsDeathState)
        {
            _isResolvingAllFriendsDeath = false;
        }
    }

    private bool IsDeadOrFakeDeadForAllFriendsDeath()
    {
        return _isFakeDead || _isResolvingAllFriendsDeath || Creature.IsDead;
    }

    private static Task NotifyLittleGalaxyAfterFriendDeath(CombatStateLike? combatState)
    {
        ArtFloorLittleGalaxyBoss? boss = combatState?.Enemies
            .Select(static creature => creature.Monster)
            .OfType<ArtFloorLittleGalaxyBoss>()
            .FirstOrDefault();
        if (boss == null
            || combatState?.Enemies.Any(static enemy =>
                enemy.Monster is ArtFloorGalaxyFriend friend && (enemy.IsAlive || friend._isFakeDead)) == true)
        {
            return Task.CompletedTask;
        }

        return boss.QueueAllFriendsDeadEgo();
    }

    private void SetIntentContainerVisible(bool visible)
    {
        if (CombatQueries.CreatureNodeOf(this) is { } creatureNode)
        {
            creatureNode.IntentContainer.Visible = visible;
        }
    }

    private static void RefreshChaoBar(LibraryCreature creature)
    {
        creature.HealthBar?.RefreshValues();
    }

    private IEnumerable<AbstractIntent> EnumerateIntentAssets()
    {
        MonsterMoveStateMachine stateMachine = GenerateMoveStateMachine();
        foreach (MonsterState state in stateMachine.States.Values)
        {
            if (!state.IsMove || state is not MoveState moveState)
            {
                continue;
            }

            foreach (AbstractIntent intent in moveState.Intents)
            {
                yield return intent;
            }
        }
    }
}
