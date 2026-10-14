using System.Linq;
using System.Threading.Tasks;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.framework.audio;
using LibraryOfRuina.framework.monsters;
using LibraryOfRuina.framework.relics;
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
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace LibraryOfRuina.content.abnormalities.DeadButterfly;

internal enum DeadButterflyInitialMove
{
    AngryRelease = 1,
    SpiritRelease = 2,
    PainfulRelease = 3,
    PeacefulRepose = 4
}

public sealed class DeadButterfly : LorMonsterModel
{
    // Spine 身体的死亡动画由 SpineSpriteDeathAnimPatch 补发；设了时长原版才会等动画播完再溶解
    public override float DeathAnimLengthOverride => AnimationEffects.DeathLength(this, DeadButterflyCreatureVisuals.DeathSeconds);

    public override int DefaultChaoResistance => 10;

    public override LibraryCreatureResistanceData.Resistance? DefaultPhysicalResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Endure,
        Pierce = LibraryResistanceLevel.Normal,
        Blunt = LibraryResistanceLevel.Normal
    };

    public override LibraryCreatureResistanceData.Resistance? DefaultChaoResistanceData => new()
    {
        Slash = LibraryResistanceLevel.Resist,
        Pierce = LibraryResistanceLevel.Resist,
        Blunt = LibraryResistanceLevel.Resist
    };

    private const float AnimationDurationScale = 2.25f;
    public const string AngryReleaseMoveId = "ANGRY_RELEASE";
    public const string SpiritReleaseMoveId = "SPIRIT_RELEASE";
    public const string PainfulReleaseMoveId = "PAINFUL_RELEASE";
    public const string PeacefulReposeMoveId = "PEACEFUL_REPOSE";

    private const int AngryReleaseWeak = 1; // 愤怒释放：攻击后给予目标的虚弱层数，各进阶相同。
    private const int SpiritReleaseBlock = 9; // 灵魂释放：攻击后自身获得的格挡，各进阶相同。
    private const int PainfulReleaseBlock = 6; // 痛苦释放：自身获得的格挡，各进阶相同。
    private const int PainfulReleaseFrail = 1; // 痛苦释放：给予目标的脆弱层数，各进阶相同。
    private const int PeacefulReposeHits = 2; // 安息：攻击次数（单次伤害见 HeavyDamage），各进阶相同。

    public const string IdleTexturePath = DeadButterflyAssets.DeadButterflyIdleTexture;
    public const string AttackTexturePath = DeadButterflyAssets.DeadButterflyAttackTexture;
    public const string HitTexturePath = DeadButterflyAssets.DeadButterflyHitTexture;
    public const string AttackSfxPath = DeadButterflyAssets.DeadButterflyAttackSfx;
    public const string DodgeSfxPath = DeadButterflyAssets.DeadButterflyDodgeSfx;

    private static readonly string BookRelicTitleLocKey =
        $"{ModelDb.GetId<DeadButterfliesBookRelic>().Entry}.title";

    private List<DeadButterflyInitialMove> _sequence =
    [
        DeadButterflyInitialMove.AngryRelease,
        DeadButterflyInitialMove.SpiritRelease,
        DeadButterflyInitialMove.PainfulRelease,
        DeadButterflyInitialMove.PeacefulRepose
    ];

    private MoveState _move1 = null!;
    private MoveState _move2 = null!;
    private MoveState _move3 = null!;
    private MoveState _move4 = null!;

    public override int MinInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 14, 13);

    public override int MaxInitialHp =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 15, 14);

    private int LightDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 3, 2);

    private int SpiritDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 2, 1);

    private int HeavyDamage =>
        AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 4, 3);

    public override IEnumerable<string> AssetPaths
    {
        get
        {
            var paths = new List<string>(
                DeadButterflyCreatureVisuals.Profile.AssetPaths)
            {
                AttackSfxPath,
                DodgeSfxPath
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
        EncounterBgmController.RegisterMonster(Creature);
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

        if (creature.CombatState?.RunState.CurrentRoom is not CombatRoom room
            || room.Encounter is not DeadButterflyWeak and not DeadButterflyStrong)
        {
            return;
        }

        foreach (var player in room.CombatState.Players)
        {
            if (player.GetRelic<DeadButterfliesBookRelic>() != null
                || AbnormalityPageRewardHelper.HasRelicReward(room, player, BookRelicTitleLocKey))
            {
                continue;
            }

            room.AddExtraReward(player, new RelicReward(ModelDb.Relic<DeadButterfliesBookRelic>().ToMutable(), player));
        }
    }

    internal void ConfigureMoveSequence(params DeadButterflyInitialMove[] sequence)
    {
        AssertMutable();
        if (sequence.Length > 0)
        {
            _sequence.Clear();
            _sequence.AddRange(sequence);
        }
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        _move1 = new MoveState(
            AngryReleaseMoveId,
            AngryReleaseMove,
            new SingleAttackIntent(LightDamage),
            new DebuffIntent());

        _move2 = new MoveState(
            SpiritReleaseMoveId,
            SpiritReleaseMove,
            new SingleAttackIntent(SpiritDamage),
            new DefendIntent());

        _move3 = new MoveState(
            PainfulReleaseMoveId,
            PainfulReleaseMove,
            new DefendIntent(),
            new DebuffIntent());

        _move4 = new MoveState(
            PeacefulReposeMoveId,
            PeacefulReposeMove,
            new MultiAttackIntent(HeavyDamage, PeacefulReposeHits));

        LinkConfiguredMoveSequence();
        MonsterState initialState = StateFor(_sequence[0]);

        return new MonsterMoveStateMachine([_move1, _move2, _move3, _move4], initialState);
    }

    private MoveState StateFor(DeadButterflyInitialMove move) => move switch
    {
        DeadButterflyInitialMove.SpiritRelease => _move2,
        DeadButterflyInitialMove.PainfulRelease => _move3,
        DeadButterflyInitialMove.PeacefulRepose => _move4,
        _ => _move1
    };

    private void LinkConfiguredMoveSequence()
    {
        if (_sequence.Count == 0)
        {
            _sequence.Add(DeadButterflyInitialMove.AngryRelease);
        }

        for (int i = 0; i < _sequence.Count; i++)
        {
            MoveState current = StateFor(_sequence[i]);
            MoveState next = StateFor(_sequence[(i + 1) % _sequence.Count]);
            current.FollowUpState = next;
        }
    }

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _sequence = new List<DeadButterflyInitialMove>(_sequence);
    }

    private async Task AngryReleaseMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(AttackSfxPath, -2f);
        await ExecuteAttackSegment(LightDamage);

        if (targets.Count > 0)
        {
            await PowerCmdCompat.Apply<WeakPower>(targets, AngryReleaseWeak, Creature, null);
        }
    }

    private async Task SpiritReleaseMove(IReadOnlyList<Creature> targets)
    {
        LocalOggOneShotPlayer.Play(AttackSfxPath, -2f);
        await ExecuteAttackSegment(SpiritDamage);
        await CreatureCmd.GainBlock(Creature, SpiritReleaseBlock, ValueProp.Move, null);
    }

    private async Task PainfulReleaseMove(IReadOnlyList<Creature> targets)
    {
        await CreatureCmd.TriggerAnim(Creature, "Cast", ScaleDuration(0.05f));
        await CreatureCmd.GainBlock(Creature, PainfulReleaseBlock, ValueProp.Move, null);

        if (targets.Count > 0)
        {
            await PowerCmdCompat.Apply<FrailPower>(targets, PainfulReleaseFrail, Creature, null);
        }
    }

    private async Task PeacefulReposeMove(IReadOnlyList<Creature> targets)
    {
        for (int i = 0; i < PeacefulReposeHits; i++)
        {
            if (Creature.IsDead) return;
            LocalOggOneShotPlayer.Play(AttackSfxPath, -2f);
            await ExecuteAttackSegment(HeavyDamage, followUp: i > 0);
            await Cmd.CustomScaledWait(ScaleDuration(0.04f), ScaleDuration(0.08f));
        }
    }

    private IEnumerable<AbstractIntent> EnumerateIntentAssets()
    {
        yield return new SingleAttackIntent(LightDamage);
        yield return new SingleAttackIntent(SpiritDamage);
        yield return new DefendIntent();
        yield return new DebuffIntent();
        yield return new MultiAttackIntent(HeavyDamage, PeacefulReposeHits);
    }

    private static float ScaleDuration(float seconds) => seconds * AnimationDurationScale;

    // 多段攻击只播一次 Spine 攻击动画：第一段等到扑到最远的命中帧，后续各段短等待，都落在同一次动画里（见 DeadButterflyCreatureVisuals）
    private Task ExecuteAttackSegment(int damage, bool followUp = false)
    {
        return AbnormalityAnimHelper.ExecuteAttackSegment(
            this,
            damage,
            delaySeconds: followUp ? DeadButterflyCreatureVisuals.FollowUpHitSeconds : DeadButterflyCreatureVisuals.AttackImpactSeconds);
    }
}
