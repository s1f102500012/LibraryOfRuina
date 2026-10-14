using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LibraryOfRuina.core.settings;
using LibraryOfRuina.content.acts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace LibraryOfRuina.content.relics.BookShadow;

internal static class BookShadowLibraryActAncientGrant
{
    internal static async Task GrantToHostAsync(AncientEventModel ancient)
    {
        if (ancient.Owner is not { } eventOwner
            || !LibraryRunSettings.IsMonsterExtensionEnabled(eventOwner.RunState)
            || eventOwner.RunState.Act is not LibraryOfRuinaActModel)
        {
            return;
        }

        Player? host = BookShadowHostPlayerResolver.Resolve(eventOwner.RunState);
        if (host == null || eventOwner.NetId != host.NetId)
        {
            return;
        }

        BookShadowRelic? relic = host.Relics.OfType<BookShadowRelic>().FirstOrDefault();
        if (relic == null)
        {
            relic = await RelicCmd.Obtain<BookShadowRelic>(host);
        }

        await relic.GrantVanillaCharacterProtection();
    }
}

[HarmonyPatch(typeof(AncientEventModel), "BeforeEventStarted")]
internal static class BookShadowLibraryActAncientGrantPatch
{
    [HarmonyPostfix]
    private static void Postfix(AncientEventModel __instance, ref Task __result)
    {
        __result = GrantAfterAncientInitializationAsync(__result, __instance);
    }

    private static async Task GrantAfterAncientInitializationAsync(
        Task original,
        AncientEventModel ancient)
    {
        await original;
        await BookShadowLibraryActAncientGrant.GrantToHostAsync(ancient);
    }
}
