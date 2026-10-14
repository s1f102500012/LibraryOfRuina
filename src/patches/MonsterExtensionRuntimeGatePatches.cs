using System.Linq;
using System.Reflection;
using HarmonyLib;
using LibraryOfRuina.content.relics.BookShadow;
using LibraryOfRuina.core;
using LibraryOfRuina.core.settings;
using LibraryOfRuina.infra.lifecycle;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LibraryOfRuina.patches;






internal static class MonsterExtensionRuntimeGate
{
    private static readonly Assembly ModAssembly = typeof(LibraryOfRuinaInitializer).Assembly;

    public static bool IsInjectedByThisMod(AbstractModel model) =>
        model.GetType().Assembly == ModAssembly;
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.PullNextEncounter))]
[HarmonyPriority(Priority.Last)]
internal static class MonsterExtensionPullNextEncounterGatePatch
{
    [HarmonyPostfix]
    public static void Postfix(ActModel __instance, RoomType roomType, ref EncounterModel __result)
    {
        if (LibraryRunSettings.MonsterExtensionEnabled)
        {
            if (BookShadowEncounterReplacement.TryReplaceActiveGuestEncounter(
                    __instance,
                    roomType,
                    __result,
                    out EncounterModel bookShadowReplacement))
            {
                __result = bookShadowReplacement;
            }
            else if (LibraryEncounterWeighting.TrySelectRuntimeModReplacement(
                    __instance,
                    roomType,
                    __result,
                    out EncounterModel replacement))
            {
                __result = replacement;
            }

            __result = LibraryEncounterWeighting.ApplyEncounterCycleRule(
                __instance,
                roomType,
                __result);
            return;
        }

        if (!MonsterExtensionRuntimeGate.IsInjectedByThisMod(__result))
        {
            return;
        }

        // 本局的开关在局内不会再变，走到这里的是关闭内容的局里仍排着本模组遭遇的情况：开启时建的旧局
        // 按关闭补建了本局设置，或本模组的幕原生遭遇池里就有本模组遭遇。两端读同一份本局设置，对称执行。
        LibraryEncounterWeighting.RestoreVanillaEncounters(CurrentRun.State);
        if (LibraryEncounterWeighting.TryGetScheduledVanillaEncounter(
                __instance,
                roomType,
                out EncounterModel fallback))
        {
            __result = fallback;
        }
    }
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.PullNextEvent))]
internal static class MonsterExtensionPullNextEventGatePatch
{
    [HarmonyPostfix]
    public static void Postfix(ActModel __instance, RunState runState, ref EventModel __result)
    {
        if (LibraryRunSettings.IsMonsterExtensionEnabled(runState)
            || !MonsterExtensionRuntimeGate.IsInjectedByThisMod(__result))
        {
            return;
        }

        EventModel? fallback = __instance.AllEvents
            .Concat(ModelDb.AllSharedEvents)
            .FirstOrDefault(eventModel =>
                !MonsterExtensionRuntimeGate.IsInjectedByThisMod(eventModel)
                && eventModel.IsAllowed(runState)
                && !runState.VisitedEventIds.Contains(eventModel.Id));

        fallback ??= __instance.AllEvents
            .Concat(ModelDb.AllSharedEvents)
            .FirstOrDefault(eventModel =>
                !MonsterExtensionRuntimeGate.IsInjectedByThisMod(eventModel)
                && eventModel.IsAllowed(runState));

        if (fallback != null)
        {
            runState.AddVisitedEvent(fallback);
            __result = fallback;
        }
    }
}

[HarmonyPatch(typeof(RelicPoolModel), nameof(RelicPoolModel.GetUnlockedRelics))]
internal static class MonsterExtensionEventRelicPoolGatePatch
{
    [HarmonyPostfix]
    public static void Postfix(RelicPoolModel __instance, ref IEnumerable<RelicModel> __result)
    {
        if (LibraryRunSettings.MonsterExtensionEnabled || __instance is not EventRelicPool)
        {
            return;
        }

        __result = __result.Where(relic => !MonsterExtensionRuntimeGate.IsInjectedByThisMod(relic));
    }
}

// 内容始终注册以支持客机自动跟随；关闭内容的局从可抽取卡池中排除本模组卡牌。
[HarmonyPatch(typeof(CardPoolModel), nameof(CardPoolModel.GetUnlockedCards))]
internal static class MonsterExtensionCardPoolGatePatch
{
    [HarmonyPostfix]
    private static void Postfix(ref IEnumerable<CardModel> __result)
    {
        if (!LibraryRunSettings.MonsterExtensionEnabled)
        {
            __result = __result.Where(card => !MonsterExtensionRuntimeGate.IsInjectedByThisMod(card));
        }
    }
}
