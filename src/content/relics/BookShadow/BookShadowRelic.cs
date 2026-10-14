using System.Threading.Tasks;
using LibraryOfRuina.content.acts;
using LibraryOfRuina.core.settings;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace LibraryOfRuina.content.relics.BookShadow;

/// <summary>
/// A host relic granted in the first entered Library Act's Ancient room which controls weak
/// or strong normal-encounter reception replacement. Its active state is saved and all
/// right-click changes run through the synchronized LibraryRelic action.
/// </summary>
public sealed class BookShadowRelic : LibraryRelicModel
{
    public const int GuestReplacementChancePercent = 33;

    // 原版角色保护：第一幕进入图书馆地图时赠送的随机药水数量。
    public const int ProtectionPotionCount = 2;

    // 原版角色保护：第一幕进入图书馆地图时永久增加的药水栏位数量。
    public const int ProtectionPotionSlotCount = 2;

    // 原版角色保护仅在第一幕生效，幕索引从零开始。
    private const int ProtectionActIndex = 0;

    protected override string IconBaseName => "book_shadow_relic";

    // The relic is catalogued with event relics, but IsAllowed prevents it
    // from entering any random grab bag. The Library-Act Ancient grant owns
    // its distribution.
    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool HasRightClick => true;

    public override bool IsAllowed(IRunState runState)
    {
        _ = runState;
        return false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DynamicVar("Chance", GuestReplacementChancePercent),
        new DynamicVar("ProtectionPotions", ProtectionPotionCount),
        new DynamicVar("ProtectionSlots", ProtectionPotionSlotCount)
    ];

    [SavedProperty]
    public bool IsActive { get; private set; }

    [SavedProperty]
    public bool ProtectionProcessed { get; private set; }

    public override Task AfterObtained()
    {
        RefreshStatus();
        return Task.CompletedTask;
    }

    internal async Task GrantVanillaCharacterProtection()
    {
        IRunState runState = Owner.RunState;
        if (!LibraryRunSettings.IsMonsterExtensionEnabled(runState)
            || ProtectionProcessed
            || runState.CurrentActIndex != ProtectionActIndex
            || runState.Act is not LibraryOfRuinaActModel)
        {
            return;
        }

        ProtectionProcessed = true;
        if (!LibraryRunSettings.IsVanillaCharacterProtectionEnabled(runState))
        {
            return;
        }

        foreach (Player player in runState.Players)
        {
            if (player.Character is not (Ironclad or Silent or Regent or Necrobinder or Defect))
            {
                continue;
            }

            int slotIndex = 0;
            await PlayerCmd.GainMaxPotionCount(ProtectionPotionSlotCount, player);
            IEnumerable<PotionModel> potions = PotionFactory.CreateRandomPotionsOutOfCombat(
                player,
                ProtectionPotionCount,
                runState.Rng.CombatPotionGeneration);
            foreach (PotionModel potion in potions)
            {
                await PotionCmd.TryToProcure(potion.ToMutable(), player, slotIndex);
                slotIndex++;
            }
        }
    }

    public override Task AfterRoomEntered(AbstractRoom room)
    {
        _ = room;
        RefreshStatus();
        return Task.CompletedTask;
    }

    public override bool CanHandleRightClickLocal(LibraryRightClickContext context) =>
        CanToggle(context.Player);

    public override bool CanExecuteRightClick(LibraryRightClickExecutionContext context) =>
        CanToggle(context.Player);

    public override Task OnRightClick(LibraryRightClickExecutionContext context)
    {
        if (!CanToggle(context.Player))
        {
            return Task.CompletedTask;
        }

        IsActive = !IsActive;
        RefreshStatus();
        Flash();
        return Task.CompletedTask;
    }

    private bool CanToggle(Player player) =>
        Owner == player && !HasBeenRemovedFromState;

    private void RefreshStatus()
    {
        Status = IsActive ? RelicStatus.Active : RelicStatus.Disabled;
    }
}
