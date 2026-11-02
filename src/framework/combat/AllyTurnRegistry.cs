using System;
using System.Linq;
using System.Threading.Tasks;
using LibraryLib.Entities.Creatures;
using LibraryOfRuina.content.abnormalities.LittleRedMercenary;
using LibraryOfRuina.framework.monsters;
using LibraryOfRuina.infra.lifecycle;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace LibraryOfRuina.framework.combat;

public enum AllyType
{
    Neutral,
    Friendly,
    Hostile
}

public enum AllyPersistence
{
    Player,
    Encounter
}

public interface IAllyTurnProvider
{
    string AllyId { get; }
    
    AllyType AllyType { get; }
    
    AllyPersistence AllyPersistence { get; }

    bool IsActiveEncounter(CombatStateLike combatState);

    /// <summary>
    /// 返回的生物必须是 <see cref="LorMonsterModel"/>：盟友保留格挡、不吃玩家方敌方范围卡的能力，
    /// 是由它的覆写按本注册表判断实现的，其他怪物类型的盟友不会得到这两条规则。
    /// 泛型参数的约束只限制 <c>TMonster</c>，不约束这里的返回值；现有实现都已核对，新增实现要自己遵守，
    /// 注册表查到不符合的盟友时每个 provider 记一次警告。
    /// </summary>
    Creature? FindAlly(CombatStateLike combatState);

    bool HasFullAllyTurn => true;

    int TurnOrder => 0;

    bool ShouldClearBlockBeforePlayerTurn => true;
    
    bool CanTransferBlock { get; }

    bool ShouldKeepBlockFromTransfer => true;

    void OnCombatReset(Creature? creature);

    bool IsAllyMonster(MonsterModel monster) => false;

    AllyType ResolveAllyType(Creature ally) => AllyType;
}

public interface IAllyTurnProvider<TMonster> : IAllyTurnProvider
    where TMonster : LorMonsterModel
{
    bool IAllyTurnProvider.IsAllyMonster(MonsterModel monster) => monster is TMonster;
}

internal sealed class AllyTurnState
{
    public int ActedRound;

    public required IAllyTurnProvider Provider;
}

public static class AllyTurnRegistry
{
    private static readonly List<IAllyTurnProvider> Providers = [];

    private static readonly Dictionary<Creature, AllyTurnState> States = [];

    public static void RegisterProvider(IAllyTurnProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (Providers.Contains(provider))
        {
            return;
        }

        Providers.Add(provider);
    }

    public static void UnRegisterProvider(IAllyTurnProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Providers.Remove(provider);
    }

    private static IAllyTurnProvider? FindProviderFor(Creature? creature)
    {
        if (creature?.CombatState is null)
        {
            return null;
        }

        foreach (var provider in Providers)
        {
            if (!provider.IsActiveEncounter(creature.CombatState))
            {
                continue;
            }

            var ally = provider.FindAlly(creature.CombatState);
            if (ally == creature)
            {
                WarnIfNotLorMonster(provider, ally);
                return provider;
            }
        }

        return null;
    }

    private static readonly HashSet<IAllyTurnProvider> WarnedNonLorProviders = [];

    private static void WarnIfNotLorMonster(IAllyTurnProvider provider, Creature ally)
    {
        if (ally.Monster is LorMonsterModel || !WarnedNonLorProviders.Add(provider))
        {
            return;
        }

        Log.Warn("[LibraryOfRuina.Ally] Provider " + provider.AllyId + " returned "
            + (ally.Monster?.GetType().FullName ?? "a non-monster creature")
            + ", which is not a LorMonsterModel; it will not keep block as an ally or ignore enemy-range powers from players.");
    }

    internal static bool IsAllyCreature(Creature? creature)
    {
        return GetAllyType(creature) is AllyType.Neutral or AllyType.Friendly;
    }

    internal static AllyType? GetAllyType(Creature? creature)
    {
        IAllyTurnProvider? provider = FindProviderFor(creature);
        return provider == null || creature == null
            ? null
            : provider.ResolveAllyType(creature);
    }

    internal static bool IsFriendlyAlly(Creature? creature) =>
        GetAllyType(creature) == AllyType.Friendly;

    internal static bool IsHostileAlly(Creature? creature) =>
        GetAllyType(creature) == AllyType.Hostile;

    internal static bool IsPlayerAlignedForTargeting(Creature? creature) =>
        creature is { Side: CombatSide.Player } || IsFriendlyAlly(creature);

    internal static bool IsPlayerEnemy(Creature? creature) =>
        creature is { Side: CombatSide.Enemy } && !IsFriendlyAlly(creature);

    internal static bool CanTransferBlockWith(Creature? creature)
    {
        IAllyTurnProvider? provider = FindProviderFor(creature);
        return provider != null
            && creature != null
            && provider.ResolveAllyType(creature) != AllyType.Hostile
            && provider.CanTransferBlock;
    }

    internal static bool ShouldUseAllyTurn(Creature? creature)
    {
        IAllyTurnProvider? provider = FindProviderFor(creature);
        return provider != null
            && creature != null
            && provider.ResolveAllyType(creature) != AllyType.Hostile
            && provider.HasFullAllyTurn;
    }

    internal static IReadOnlyList<Creature> FilterPlayerEnemyTargets(
        IEnumerable<Creature>? creatures)
    {
        if (creatures is null)
        {
            return [];
        }

        IReadOnlyList<Creature> targets =
            creatures as IReadOnlyList<Creature> ?? creatures.ToArray();
        List<Creature>? filtered = null;
        for (int index = 0; index < targets.Count; index++)
        {
            Creature target = targets[index];
            if (IsFriendlyAlly(target))
            {
                if (filtered == null)
                {
                    filtered = new List<Creature>(targets.Count);
                    for (int previous = 0; previous < index; previous++)
                    {
                        filtered.Add(targets[previous]);
                    }
                }
            }
            else
            {
                filtered?.Add(target);
            }
        }

        return filtered ?? targets;
    }

    internal static bool ShouldSkipMultiplayerScaling(Creature? creature)
    {
        MonsterModel? monster = creature?.Monster;
        if (monster == null)
        {
            return false;
        }

        IAllyTurnProvider? activeProvider = FindProviderFor(creature);
        return activeProvider != null
            ? activeProvider.ResolveAllyType(creature!) != AllyType.Hostile
            : Providers.Any(provider => provider.IsAllyMonster(monster));
    }

    internal static bool HasActedThisRound(Creature? creature)
    {
        if (!IsAllyCreature(creature) && creature?.CombatState == null)
        {
            return false;
        }

        if (!States.TryGetValue(creature!, out var states))
        {
            return false;
        }

        return states.ActedRound == creature?.CombatState?.RoundNumber;
    }

    // 在盟友回合行动后才转为敌对（暴怒）的盟友按敌人处理：玩家回合开始时 ClearBlockBeforePlayerTurnStart 也跳过
    // 敌对单位，这里若仍阻止清除，格挡会多留一个玩家回合。
    internal static bool ShouldPreventVanillaBlockClearing(Creature? creature)
    {
        return HasActedThisRound(creature)
               && FindProviderFor(creature)?.ResolveAllyType(creature!) != AllyType.Hostile;
    }

    // 本次盟友回合是否结束了战斗。原版切边不再检查战斗是否结束；只有战斗是被盟友回合结束的，
    // AllySkipEnemySideSwitchWhenCombatEndsPatch 才跳过这次切边，其他方式结束的战斗保持原版流程。
    // 只在盟友回合正常跑完时置位：抛异常时原版不会走到切边，置位只会残留到之后的战斗。
    // 下一次盟友回合开始、玩家回合开始时都会清掉。
    private static bool _combatEndedByAllyTurn;

    internal static bool ConsumeCombatEndedByAllyTurn()
    {
        bool ended = _combatEndedByAllyTurn;
        _combatEndedByAllyTurn = false;
        return ended;
    }

    internal static void ForgetCombatEndedByAllyTurn() => _combatEndedByAllyTurn = false;

    internal static async Task ExecuteAllyTurn(CombatManager combatManager)
    {
        _combatEndedByAllyTurn = false;
        var combatState = CurrentCombat.Of(combatManager);
        
        if (combatState == null) return;
        if (!combatManager.IsInProgress) return;

        await ExecuteAllyTurnCore(combatManager, combatState);
        _combatEndedByAllyTurn = !combatManager.IsInProgress;
    }

    private static async Task ExecuteAllyTurnCore(CombatManager combatManager, CombatState combatState)
    {
        if (combatState.CurrentSide != CombatSide.Player) return;
        if (WillAnyPlayerTakeExtraTurn(combatState)) return;

        foreach (var provider in Providers.OrderBy(provider => provider.TurnOrder))
        {
            if (!provider.HasFullAllyTurn) continue;
            if (!provider.IsActiveEncounter(combatState)) continue;
            
            var ally = provider.FindAlly(combatState);
            if (ally?.CombatState == null || ally.IsDead) continue;
            if (provider.ResolveAllyType(ally) == AllyType.Hostile) continue;
            if (HasActedThisRound(ally)) continue;

            if (ally is LibraryCreature { CurrentChaoValue: <= 0, MaxChaoValue: > 0 } libraryAlly
                && !libraryAlly.IsChaoed)
            {
                await LibraryCreatureCmd.Stun(libraryAlly);
            }

            if (ally.Monster?.NextMove.Id == "UNSET_MOVE")
            {
                ally.PrepareForNextTurn(combatState.Enemies);
            }

            var creatureNode = CombatQueries.CreatureNodeOf(ally);
            if (creatureNode != null)
            {
                await creatureNode.PerformIntent();
            }

            await ally.Monster!.PerformMove();

            MarkAsActed(ally, provider);

            // 行动可能让盟友离场（击杀搭档触发转阶段清场），离场后不再为它准备下一回合，同原版敌方回合的 ContainsCreature 检查。
            if (ally.IsAlive
                && combatManager.IsInProgress
                && combatState.ContainsCreature(ally))
            {
                ally.PrepareForNextTurn(combatState.Enemies);
            }

            // ReSharper disable once UseConfigureAwaitFalse
            await combatManager.WaitForUnpause();
            await combatManager.CheckWinCondition();
        }
    }

    private static bool WillAnyPlayerTakeExtraTurn(ICombatState combatState)
    {
        return combatState.Players.Any(player => Hook.ShouldTakeExtraTurn(combatState, player));
    }

    private static void MarkAsActed(Creature? ally, IAllyTurnProvider provider)
    {
        if (ally?.CombatState is null) return;

        if (!States.TryGetValue(ally, out var state))
        {
            state = new AllyTurnState { Provider = provider };
            States[ally] = state;
        }

        state.ActedRound = ally.CombatState.RoundNumber;

    }
    
    internal static void ClearBlockBeforePlayerTurnStart(ICombatState combatState)
    {
        foreach (var provider in Providers)
        {
            if (!provider.ShouldClearBlockBeforePlayerTurn) continue;
            if (!provider.IsActiveEncounter(combatState)) continue;

            var ally = provider.FindAlly(combatState);
            if (ally is null || ally.IsDead || ally.Block <= 0m)
            {
                continue;
            }
            if (provider.ResolveAllyType(ally) == AllyType.Hostile)
            {
                continue;
            }

            int blockToLoss = ally.Block;
            if (provider.ShouldKeepBlockFromTransfer)
            {
                TransferredBlockPower.SuspendTrackingWhile(ally,
                    () => ally.LoseBlockInternal(blockToLoss));
            }
            else
            {
                ally.LoseBlockInternal(blockToLoss);
            }
        }
    }

    internal static void ReSetAll(ICombatState combatState)
    {
        foreach (var provider in Providers)
        {
            if (!provider.IsActiveEncounter(combatState))
            {
                continue;
            }
            var ally = provider.FindAlly(combatState);
            provider.OnCombatReset(ally);
        }
        
        States.Clear();
    }
}

