using System;
using System.Linq;
using ActLikeIt2;
using HarmonyLib;
using LibraryOfRuina.core.settings;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Bestiary;
using MegaCrit.Sts2.Core.Saves;

namespace LibraryOfRuina.content.acts;

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.InitIds))]
internal static class LibraryActRegistrationPatch
{
    private static bool _registered;

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (_registered)
        {
            return;
        }

        RegisterGrouped(ModelDb.Act<Malkuth>(), 1, "LibraryOfRuina.FirstPair");
        RegisterGrouped(ModelDb.Act<Yesod>(), 1, "LibraryOfRuina.FirstPair");
        RegisterGrouped(ModelDb.Act<Hod>(), 1, "LibraryOfRuina.FirstPair");
        RegisterGrouped(ModelDb.Act<NetZech>(), 2, "LibraryOfRuina.SecondPair");
        RegisterGrouped(ModelDb.Act<Gebura>(), 2, "LibraryOfRuina.SecondPair");
        RegisterGrouped(ModelDb.Act<Tiphereth>(), 2, "LibraryOfRuina.SecondPair");
        RegisterGrouped(ModelDb.Act<Chesed>(), 3, "LibraryOfRuina.ThirdPair");
        RegisterGrouped(ModelDb.Act<Binah>(), 3, "LibraryOfRuina.ThirdPair");
        RegisterGrouped(ModelDb.Act<Hokma>(), 3, "LibraryOfRuina.ThirdPair");
        ActRegistry.Register(new ActRegistration
        {
            CanonicalAct = ModelDb.Act<ReverberationEnsembleAct>(),
            ActNumber = ReverberationEnsembleAct.ActNumber,
            IsAvailable = LibraryRunSettings.IsMonsterExtensionEnabled,
            OptionDescription = new LocString("acts", "REVERBERATION_ENSEMBLE_ACT.description"),
            CustomCreateMap = static (_, _) => new ReverberationEnsembleActMap()
        });
        _registered = true;

        Log.Info(
            "[LibraryActs] Registered Malkuth, Yesod, Hod, NetZech, Gebura, Tiphereth, "
            + "Chesed, Binah, Hokma and ReverberationEnsembleAct with ActLikeIt2.");
    }

    private static void RegisterGrouped(
        ActModel act,
        int actNumber,
        string groupId)
    {
        string groupLocPrefix =
            $"LIBRARY_OF_RUINA_ACT_{actNumber}_GROUP";
        ActRegistry.Register(new ActRegistration
        {
            CanonicalAct = act,
            ActNumber = actNumber,
            IsAvailable = LibraryRunSettings.IsMonsterExtensionEnabled,
            SelectionGroupId = groupId,
            SelectionGroupTitle = new LocString(
                "acts",
                groupLocPrefix + ".title"),
            SelectionGroupDescription = new LocString(
                "acts",
                groupLocPrefix + ".description"),
            OptionDescription = ((LibraryOfRuinaActModel)act).ExpectedBoss.Title
        });
    }
}

internal static class LibraryActCatalog
{
    private static readonly Type[] ActTypes =
    [
        typeof(Malkuth),
        typeof(Yesod),
        typeof(Hod),
        typeof(NetZech),
        typeof(Gebura),
        typeof(Tiphereth),
        typeof(Chesed),
        typeof(Binah),
        typeof(Hokma)
    ];

    /// <summary>
    /// Android Mono 可能在 ModelDb.Init 逐个实例化模组模型期间提前运行 UnlockState 静态构造，
    /// 经 AllEncounters 进入这里；此时尚未写入 ModelDb 的幕直接跳过，避免静态构造永久失败。
    /// </summary>
    public static IEnumerable<LibraryOfRuinaActModel> All()
    {
        foreach (Type actType in ActTypes)
        {
            LibraryOfRuinaActModel? act =
                ModelDb.GetByIdOrNull<LibraryOfRuinaActModel>(ModelDb.GetId(actType));
            if (act != null)
            {
                yield return act;
            }
        }
    }
}

/// <summary>
/// ActLikeIt2 presents Library acts in selection groups. Once one member has legitimately
/// entered run history, expose the complete group in the Bestiary.
/// </summary>
[HarmonyPatch(typeof(NBestiary), "CreateEntries")]
internal static class LibraryActBestiaryDiscoveryPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        var progress = SaveManager.Instance.Progress;
        bool changed = false;
        changed |= SynchronizeGroup(
            progress,
            ModelDb.Act<Malkuth>().Id,
            ModelDb.Act<Yesod>().Id,
            ModelDb.Act<Hod>().Id);
        changed |= SynchronizeGroup(progress, ModelDb.Act<NetZech>().Id,
            ModelDb.Act<Gebura>().Id, ModelDb.Act<Tiphereth>().Id);
        changed |= SynchronizeGroup(progress, ModelDb.Act<Chesed>().Id,
            ModelDb.Act<Binah>().Id, ModelDb.Act<Hokma>().Id);
        if (!changed)
        {
            return;
        }

        SaveManager.Instance.SaveProgressFile();
        Log.Info("[LibraryActs] Synchronized paired Act discovery for the Bestiary.");
    }

    private static bool SynchronizePair<TFirst, TSecond>(ProgressState progress)
        where TFirst : ActModel
        where TSecond : ActModel
    {
        return SynchronizeGroup(
            progress,
            ModelDb.Act<TFirst>().Id,
            ModelDb.Act<TSecond>().Id);
    }

    internal static bool SynchronizeGroup(
        ProgressState progress,
        params ModelId[] actIds)
    {
        if (!actIds.Any(progress.DiscoveredActs.Contains))
        {
            return false;
        }

        bool changed = false;
        foreach (ModelId actId in actIds)
        {
            if (!progress.DiscoveredActs.Contains(actId))
            {
                changed |= progress.MarkActAsSeen(actId);
            }
        }

        return changed;
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllEncounters), MethodType.Getter)]
internal static class LibraryActEncounterCatalogPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref IEnumerable<EncounterModel> __result)
    {
        __result = __result
            .Concat(LibraryActCatalog.All().SelectMany(static act => act.AllEncounters))
            .DistinctBy(static encounter => encounter.Id);
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Monsters), MethodType.Getter)]
internal static class LibraryActMonsterCatalogPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref IEnumerable<MonsterModel> __result)
    {
        __result = __result
            .Concat(LibraryActCatalog.All().SelectMany(static act => act.AllMonsters))
            .DistinctBy(static monster => monster.Id);
    }
}
