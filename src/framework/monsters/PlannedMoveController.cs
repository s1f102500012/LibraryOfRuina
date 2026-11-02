using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace LibraryOfRuina.framework.monsters;

/// <summary>
/// 多意图计划：怪物把一回合要做的几个招式记在自己的槽位属性里，由一个“复合”行动一次展示、一次执行。
/// 本类只负责槽位与意图之间的机械部分：建复合/隐藏行动、按槽位重建意图、揭示与隐藏、写入与清空槽位、逐槽执行。
/// 槽位本身（怪物上的属性）、什么时候规划、用哪个随机数、规划后要不要立刻揭示，都由怪物自己决定。
/// <list type="bullet">
/// <item>意图数组交给 <see cref="MoveState"/> 后一直是同一个实例：原版 <c>MoveState.Intents</c> 保存的就是这个引用，
/// <c>NCreature.RefreshIntents</c> 读的也是它。<see cref="RefreshIntents"/> 只能原地改写数组元素，不能换数组。</item>
/// <item>行动状态每次 <c>GenerateMoveStateMachine</c> 都会重建（原版 <c>MonsterModel.GetIntents</c> 也会在规范实例上调用它），
/// 同一个行动 ID 再建一次会顶替旧的数组。实例要跟着怪物实例走：怪物在 <c>DeepCloneFields</c> 里丢掉它、用到时重建，
/// 否则克隆出来的怪物会改写规范实例的意图。</item>
/// <item><see cref="Reveal"/>、<see cref="Hide"/> 用强制切换：复合行动通常要求“执行过一次才能离开”，普通切换会被拒绝。
/// 这是战斗状态而不是表现：每个客户端都在同一个钩子里调用，原版 <c>SetMoveImmediate</c> 只在有节点时顺带刷新意图显示。</item>
/// <item>多个复合行动可以共用同一组槽位，各自只展示前若干个（语言层的高/低血量、影子形态、三种形态），
/// 用不同的行动 ID 各建一次，揭示时传入要切换的那个。</item>
/// <item>怪物可以在控制器之外直接写槽位（单独改某一个槽位、调试入口、槽位数之外的旧存档槽位），
/// 写完调用 <see cref="RefreshIntents"/> 即可；控制器不缓存槽位的值。</item>
/// </list>
/// </summary>
internal sealed class PlannedMoveController<TMove>
    where TMove : struct, Enum
{
    private readonly MonsterModel _owner;
    private readonly int _slotCount;
    private readonly Func<int, TMove> _readSlot;
    private readonly Action<int, TMove> _writeSlot;
    private readonly Func<int, TMove, AbstractIntent> _createIntent;
    private readonly TMove _emptyMove;
    private readonly Dictionary<string, AbstractIntent[]> _intentViews = new(StringComparer.Ordinal);

    /// <param name="owner">切换行动时调用它的 <c>SetMoveImmediate</c>。</param>
    /// <param name="slotCount">槽位数；也是单个复合行动默认展示的意图数。</param>
    /// <param name="readSlot">读一个槽位。把非法值映射成某个默认招式的怪物，执行时永远不会遇到空槽位。</param>
    /// <param name="writeSlot">写一个槽位。</param>
    /// <param name="createIntent">按槽位和招式建意图；要按槽位取目标或伤害的怪物用第一个参数。</param>
    /// <param name="emptyMove">空槽位的值：清空与写入剩余槽位时写它，逐槽执行读到它就停。</param>
    internal PlannedMoveController(
        MonsterModel owner,
        int slotCount,
        Func<int, TMove> readSlot,
        Action<int, TMove> writeSlot,
        Func<int, TMove, AbstractIntent> createIntent,
        TMove emptyMove)
    {
        _owner = owner;
        _slotCount = slotCount;
        _readSlot = readSlot;
        _writeSlot = writeSlot;
        _createIntent = createIntent;
        _emptyMove = emptyMove;
    }

    /// <summary>最近一次建的复合行动；只有一个复合行动的怪物用它揭示。</summary>
    internal MoveState? CompositeState { get; private set; }

    internal MoveState? HiddenState { get; private set; }

    internal TMove GetMove(int slot) => _readSlot(slot);

    internal bool IsEmpty(TMove move) =>
        EqualityComparer<TMove>.Default.Equals(move, _emptyMove);

    /// <summary>
    /// 建复合行动：分配意图数组、按当前槽位填好，再交给行动状态。
    /// <paramref name="intentCount"/> 缺省时等于槽位数。
    /// </summary>
    internal MoveState CreateCompositeState(
        string id,
        Func<IReadOnlyList<Creature>, Task> perform,
        bool mustPerformOnce = false,
        int? intentCount = null)
    {
        var intents = new AbstractIntent[intentCount ?? _slotCount];
        _intentViews[id] = intents;
        RefreshView(intents);
        CompositeState = new MoveState(id, perform, intents)
        {
            MustPerformOnceBeforeTransitioning = mustPerformOnce,
        };
        return CompositeState;
    }

    /// <summary>不做任何事、只显示隐藏意图的行动；它的后续状态由怪物设置。</summary>
    internal MoveState CreateHiddenState(string id, bool mustPerformOnce = false)
    {
        HiddenState = new MoveState(
            id,
            static _ => Task.CompletedTask,
            new HiddenIntent())
        {
            MustPerformOnceBeforeTransitioning = mustPerformOnce,
        };
        return HiddenState;
    }

    /// <summary>按当前槽位重建所有复合行动的意图。还没建过行动时什么也不做。</summary>
    internal void RefreshIntents()
    {
        foreach (AbstractIntent[] intents in _intentViews.Values)
        {
            RefreshView(intents);
        }
    }

    /// <summary>
    /// 前 <paramref name="count"/> 个槽位按顺序写 <paramref name="moveAt"/>，其余写空槽位。只写槽位、不重建意图：
    /// 意图可能还依赖怪物接着要写的其他状态（例如预掷的伤害），由怪物写完后调用 <see cref="RefreshIntents"/>。
    /// </summary>
    internal void WriteSlots(int count, Func<int, TMove> moveAt)
    {
        for (int slot = 0; slot < _slotCount; slot++)
        {
            _writeSlot(slot, slot < count ? moveAt(slot) : _emptyMove);
        }
    }

    /// <summary>所有槽位写空槽位。同 <see cref="WriteSlots"/>，不重建意图。</summary>
    internal void ClearSlots()
    {
        for (int slot = 0; slot < _slotCount; slot++)
        {
            _writeSlot(slot, _emptyMove);
        }
    }

    /// <summary>强制切到复合行动（缺省为 <see cref="CompositeState"/>）。行动还没建时返回 false。</summary>
    internal bool Reveal(MoveState? state = null)
    {
        state ??= CompositeState;
        if (state == null)
        {
            return false;
        }

        _owner.SetMoveImmediate(state, forceTransition: true);
        return true;
    }

    /// <summary>强制切到隐藏行动；还没建时什么也不做。</summary>
    internal void Hide()
    {
        if (HiddenState != null)
        {
            _owner.SetMoveImmediate(HiddenState, forceTransition: true);
        }
    }

    /// <summary>
    /// 从第一个槽位起逐个执行。每个槽位执行前检查 <paramref name="canContinue"/> 与执行者仍在战斗中，槽位读到空值就停；
    /// 每个招式执行完检查 <paramref name="stopAfterMove"/>，为真就停。缺省时槽位在执行时逐个读取，
    /// 招式执行中改动了后面的槽位（清空、左移）会影响后面执行什么；<paramref name="readAllFirst"/> 为真时
    /// 开始前一次读完前 <paramref name="slotLimit"/> 个槽位，按这份快照执行（钴蓝伤痕）。
    /// </summary>
    internal async Task PerformPlan(
        Func<bool> canContinue,
        Func<int, TMove, Task> performMove,
        int? slotLimit = null,
        Func<bool>? stopAfterMove = null,
        bool readAllFirst = false)
    {
        int limit = slotLimit ?? _slotCount;
        TMove[]? snapshot = null;
        if (readAllFirst)
        {
            snapshot = new TMove[limit];
            for (int slot = 0; slot < limit; slot++)
            {
                snapshot[slot] = _readSlot(slot);
            }
        }

        for (int slot = 0; slot < limit && canContinue() && IsOwnerInCombat(); slot++)
        {
            TMove move = snapshot != null ? snapshot[slot] : _readSlot(slot);
            if (IsEmpty(move))
            {
                break;
            }

            await performMove(slot, move);
            if (stopAfterMove?.Invoke() == true)
            {
                break;
            }
        }
    }

    // 招式可能让执行者离场（击杀搭档触发转阶段清场会把它移出战斗），离场后剩余槽位不再对它结算。
    private bool IsOwnerInCombat() =>
        _owner.Creature.CombatState?.ContainsCreature(_owner.Creature) == true;

    private void RefreshView(AbstractIntent[] intents)
    {
        for (int slot = 0; slot < intents.Length; slot++)
        {
            intents[slot] = _createIntent(slot, _readSlot(slot));
        }
    }
}
