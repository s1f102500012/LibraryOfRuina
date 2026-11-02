using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using LibraryOfRuina.content.abnormalities.GalaxyChild;
using LibraryOfRuina.framework.combat;
using LibraryOfRuina.infra.helpers;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LibraryOfRuina.patches;

// The safety net only covers this mod's own monsters. It used to wrap every Hook task,
// TaskHelper.RunSafely, GameAction/ActionExecutor and the turn pipeline, swallowing any mod's
// exception in combat and "recovering" state on whichever client threw — which turned one-sided
// failures into permanent multiplayer desyncs (design philosophy §2, §4).

[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterRoomWithoutExitingCurrentRoom))]
internal static class CombatSafetyNestedCombatReplayPatch
{
    private static void Prefix(AbstractRoom room)
    {
        CombatSafetyNet.EnsureReplayInitializedBeforeNestedCombat(room);
    }
}

[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.PerformMove))]
internal static class CombatSafetyMonsterPerformMovePatch
{
    private static void Postfix(MonsterModel __instance, ref Task __result)
    {
        if (!ModOwnership.IsOwn(__instance))
        {
            return;
        }

        __result = CombatSafetyNet.WrapTask(
            __result,
            new CombatSafetyContext(
                "MonsterModel.PerformMove",
                __instance.Creature.CombatState,
                __instance.Creature,
                __instance));
    }
}

/// <summary>
/// Vanilla AttackCommand stops hitting once its attacker dies, but the rest of the monster's move
/// keeps running on a dead creature. This mod's monsters can also die to their own counters
/// mid-move, or be removed from the running combat by a liberation phase cleanup that their own hit
/// triggered; in both cases the remainder of the move is aborted and finalized by the PerformMove
/// wrapper above.
/// </summary>
[HarmonyPatch(typeof(AttackCommand), nameof(AttackCommand.Execute))]
internal static class CombatSafetyAttackCommandExecutePatch
{
    private static void Postfix(AttackCommand __instance, ref Task<AttackCommand> __result)
    {
        if (!ModOwnership.IsOwnMonster(__instance.Attacker))
        {
            return;
        }

        __result = CombatSafetyNet.WrapMonsterAttackTask(__result, __instance);
    }
}

// Hook.AfterDamageGiven / AfterAttack keep their full dispatch even when this mod's attacker died
// mid-attack: the listeners belong to every model in combat (players' powers, relics, other mods),
// and vanilla dispatches them for dead vanilla attackers as well.

/// <summary>
/// Parting Tears victory runs once the outermost kill batch has finished. Vanilla routes damage deaths
/// and single kills through Kill(IReadOnlyCollection), which completes every creature's death pipeline
/// before returning. Batches can nest (a death hook draws a card that auto-plays an attack killing the
/// other friend), so the depth is tracked per combat and victory is only evaluated at depth zero,
/// when no friend's death is still in progress. Continuing the awaited task keeps it inside the
/// synchronized action on every client.
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Kill), typeof(IReadOnlyCollection<Creature>), typeof(bool))]
internal static class GalaxyChildPartingTearsKillBatchPatch
{
    private static readonly ConditionalWeakTable<CombatStateLike, StrongBox<int>> Depths = new();

    private static void Prefix(IReadOnlyCollection<Creature> creatures, out CombatStateLike? __state)
    {
        // Captured before the batch runs: a removed creature no longer knows its combat state.
        __state = creatures.Select(static creature => creature.CombatState)
            .FirstOrDefault(static state => state?.Encounter is GalaxyChildWeak);
        if (__state != null)
        {
            Depths.GetOrCreateValue(__state).Value++;
        }
    }

    private static void Postfix(CombatStateLike? __state, ref Task __result)
    {
        if (__state == null)
        {
            return;
        }

        __result = TriggerAfter(__result, __state);
    }

    private static async Task TriggerAfter(Task killBatch, CombatStateLike combatState)
    {
        StrongBox<int> depth = Depths.GetOrCreateValue(combatState);
        try
        {
            await killBatch;
        }
        finally
        {
            depth.Value--;
        }

        if (depth.Value == 0 && GalaxyFriend.ShouldTriggerPartingTears(combatState))
        {
            await GalaxyFriend.TriggerPartingTearsVictory(combatState);
        }
    }
}
