using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Godot;
using HarmonyLib;
using LibraryOfRuina.core.settings;
using LibraryOfRuina.framework.assets;
using LibraryOfRuina.infra.helpers;
using LibraryOfRuina.infra.patching;
using LibraryOfRuina.interop;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace LibraryOfRuina.features.secondascension;

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeMultiplayerAsHost))]
internal static class LibrarySecondAscensionCharacterSelectHostPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance) =>
        LibrarySecondAscensionUi.EnsureSecondPanel(__instance, MultiplayerUiMode.Host);
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeMultiplayerAsClient))]
internal static class LibrarySecondAscensionCharacterSelectClientPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance) =>
        LibrarySecondAscensionUi.EnsureSecondPanel(__instance, MultiplayerUiMode.Client);
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeSingleplayer))]
internal static class LibrarySecondAscensionCharacterSelectSingleplayerPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance) =>
        LibrarySecondAscensionUi.EnsureSecondPanel(__instance, MultiplayerUiMode.Singleplayer);
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.InitializeMultiplayerAsHost))]
internal static class LibrarySecondAscensionCustomRunHostPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance) =>
        LibrarySecondAscensionUi.EnsureSecondPanel(__instance, MultiplayerUiMode.Host);
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.InitializeMultiplayerAsClient))]
internal static class LibrarySecondAscensionCustomRunClientPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance) =>
        LibrarySecondAscensionUi.EnsureSecondPanel(__instance, MultiplayerUiMode.Client);
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.InitializeSingleplayer))]
internal static class LibrarySecondAscensionCustomRunSingleplayerPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance) =>
        LibrarySecondAscensionUi.EnsureSecondPanel(__instance, MultiplayerUiMode.Singleplayer);
}

[HarmonyPatch(typeof(NAscensionPanel), nameof(NAscensionPanel.SetAscensionLevel))]
internal static class LibrarySecondAscensionPanelLevelPatch
{
    [HarmonyPostfix]
    private static void Postfix(NAscensionPanel __instance)
    {
        if (LibrarySecondAscensionUi.IsSecondPanel(__instance))
        {
            LibrarySecondAscensionUi.RefreshSecondPanelDeferred(__instance);
        }
    }
}

[HarmonyPatch(typeof(NAscensionPanel), nameof(NAscensionPanel.SetMaxAscension))]
internal static class LibrarySecondAscensionPanelMaxPatch
{
    [HarmonyPostfix]
    private static void Postfix(NAscensionPanel __instance)
    {
        if (LibrarySecondAscensionUi.IsSecondPanel(__instance))
        {
            __instance.Visible = LibrarySecondAscensionUi.IsPanelEnabled(__instance);
            if (__instance.Visible)
            {
                LibrarySecondAscensionUi.RefreshSecondPanelDeferred(__instance);
            }
        }
    }
}

[HarmonyPatch(typeof(NAscensionPanel), "RefreshAscensionText")]
internal static class LibrarySecondAscensionPanelTextPatch
{
    [HarmonyPostfix]
    private static void Postfix(NAscensionPanel __instance)
    {
        if (LibrarySecondAscensionUi.IsSecondPanel(__instance))
        {
            LibrarySecondAscensionUi.RefreshSecondPanel(__instance);
        }
    }
}

[HarmonyPatch(typeof(NTopBar), nameof(NTopBar.Initialize))]
internal static class LibrarySecondAscensionTopBarPatch
{
    [HarmonyPostfix]
    private static void Postfix(NTopBar __instance, IRunState runState) =>
        LibrarySecondAscensionUi.AddTopBarSecondAscensionIcon(__instance, runState);
}

[HarmonyPatch(typeof(NTopBar), "UpdateNavigation")]
internal static class LibrarySecondAscensionTopBarNavigationSafetyPatch
{

    [HarmonyFinalizer]
    private static Exception? Finalizer(NTopBar __instance, Exception? __exception)
    {
        if (__exception is not InvalidOperationException { Message: "Sequence contains no elements" })
        {
            return __exception;
        }

        if (!LibrarySecondAscensionUi.HideEmptyModifiersContainer(__instance))
        {
            return __exception;
        }

        LorLog.WarnFirst(
            "SecondAscension.TopBarNavigation",
            4,
            "[LibrarySecondAscension] suppressed empty top-bar modifier navigation update.");

        return null;
    }
}

[HarmonyPatch(typeof(NTopBarPortraitTip), nameof(NTopBarPortraitTip.Initialize))]
internal static class LibrarySecondAscensionPortraitTipPatch
{
    [HarmonyPostfix]
    private static void Postfix(NTopBarPortraitTip __instance, IRunState runState) =>
        LibrarySecondAscensionUi.AppendPortraitHoverTip(__instance, runState);
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.AscensionChanged))]
internal static class LibrarySecondAscensionCharacterSelectAscensionChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance) =>
        LibrarySecondAscensionUi.SyncClientPanelFromLobby(__instance);
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.ModifiersChanged))]
[LibraryPatch(Reason = "标准模式下原版 ModifiersChanged 固定抛 NotImplementedException，二层飞升借大厅修饰符同步时必然触发；接口实现非虚无 Hook，仅在大厅修饰符为空或全为本模组载体时跳过。")]
internal static class LibrarySecondAscensionCharacterSelectModifiersChangedPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NCharacterSelectScreen __instance)
    {
        if (!LibrarySecondAscensionUi.LobbyHasOnlySecondAscensionCarrier(__instance))
        {
            return true;
        }

        LibrarySecondAscensionUi.SyncClientPanelFromLobby(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter))]
internal static class LibrarySecondAscensionCharacterSelectCharacterPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance, NCharacterSelectButton charSelectButton) =>
        LibrarySecondAscensionUi.RefreshCharacterSelectPanelVisibility(__instance, !charSelectButton.IsLocked);
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.AscensionChanged))]
internal static class LibrarySecondAscensionCustomRunAscensionChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance) =>
        LibrarySecondAscensionUi.SyncClientPanelFromLobby(__instance);
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.ModifiersChanged))]
internal static class LibrarySecondAscensionCustomRunModifiersChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance) =>
        LibrarySecondAscensionUi.SyncClientPanelFromLobby(__instance);
}

[HarmonyPatch(typeof(NCustomRunModifiersList), nameof(NCustomRunModifiersList.GetModifiersTickedOn))]
internal static class LibrarySecondAscensionCustomRunModifierListPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref List<ModifierModel> __result)
    {
        __result = LibrarySecondAscensionState.WithCarrier(__result, LibrarySecondAscensionState.PreferredLevel).ToList();
    }
}

internal static class LibrarySecondAscensionUi
{
    private const string PanelName = "LibrarySecondAscensionPanel";
    private const string TopBarIconName = "LibrarySecondAscensionTopBarIcon";
    private const string AscensionPanelScenePath = "res://scenes/screens/ascension_panel.tscn";

    private static readonly StringName FontOutlineTheme = "font_outline_color";
    private static readonly StringName LabelFontTheme = "font";
    private static readonly StringName Hue = new("h");
    private static readonly StringName Value = new("v");
    private static readonly Color GoldOutline = new(LibrarySecondAscensionConfig.GoldOutline);


    public static void EnsureSecondPanel(Control screen, MultiplayerUiMode mode)
    {
        if (TestMode.IsOn)
        {
            return;
        }

        NAscensionPanel? existing = FindSecondPanel(screen);
        if (mode != MultiplayerUiMode.Client && !LibrarySecondAscensionState.IsSelectionEnabled)
        {
            if (existing != null)
            {
                existing.Visible = false;
            }

            return;
        }

        if (existing != null)
        {
            existing.Visible = true;
            SyncClientPanelFromLobby(screen);
            RefreshSecondPanelDeferred(existing);
            return;
        }

        NAscensionPanel? original = FindOriginalPanel(screen);
        if (original == null)
        {
            return;
        }

        NAscensionPanel panel = PreloadManager.Cache.GetScene(AscensionPanelScenePath).Instantiate<NAscensionPanel>();
        panel.Name = PanelName;
        original.GetParent().AddChildSafely(panel);
        CopyPanelLayout(original, panel, GetPanelOffset(screen));
        panel.ZIndex = original.ZIndex;
        panel.Initialize(mode);
        ConfigureSecondPanel(panel, mode, GetLobbyLevel(screen));
        panel.Connect(NAscensionPanel.SignalName.AscensionLevelChanged, Callable.From(() => OnSecondPanelLevelChanged(screen, panel)));
        if (mode is MultiplayerUiMode.Host or MultiplayerUiMode.Singleplayer)
        {
            OnSecondPanelLevelChanged(screen, panel);
        }
        else
        {
            RefreshSecondPanel(panel);
        }

        RefreshSecondPanelDeferred(panel);
        panel.Visible = mode != MultiplayerUiMode.Client || GetLobbyLevel(screen) > 0;
    }

    public static bool IsSecondPanel(NAscensionPanel panel) => panel.Name == PanelName;

    public static void RefreshSecondPanel(NAscensionPanel panel)
    {
        ApplyGoldStyle(panel);
        RichTextLabel? info = VanillaPrivate.AscensionPanelInfo.Get(panel) as RichTextLabel;
        info ??= panel.GetNodeOrNull<RichTextLabel>("HBoxContainer/AscensionDescription/Description");
        info ??= panel.FindChildren("*", string.Empty, recursive: true, owned: false)
            .OfType<RichTextLabel>()
            .FirstOrDefault();

        string title = LibrarySecondAscensionState.GetTitle(panel.Ascension).GetFormattedText();
        if (info != null)
        {
            string description = LibrarySecondAscensionState.GetDescription(panel.Ascension).GetFormattedText();
            SetRichTextAutoSize(info, "[b][gold]" + title + "[/gold][/b]\n" + description);
        }

        if (LibraryOfRuinaSettings.DebugMode)
        {
            Log.Info($"[LibrarySecondAscension] Refreshed selection panel: level={panel.Ascension}, title={title}, infoFound={info != null}");
        }
    }

    public static void RefreshSecondPanelDeferred(NAscensionPanel panel)
    {
        RefreshSecondPanel(panel);
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(panel) && IsSecondPanel(panel))
            {
                RefreshSecondPanel(panel);
            }
        }).CallDeferred();
    }

    public static void AddTopBarSecondAscensionIcon(NTopBar topBar, IRunState runState)
    {
        int level = LibrarySecondAscensionState.GetRunLevel(runState);
        if (level <= 0 || VanillaPrivate.TopBarAscensionIcon.Get(topBar) is not Control original)
        {
            RefreshModifiersContainer(topBar, runState);
            return;
        }

        if (topBar.GetNodeOrNull<Control>(TopBarIconName) != null)
        {
            RefreshModifiersContainer(topBar, runState);
            return;
        }

        Control icon = (Control)original.Duplicate((int)Node.DuplicateFlags.UseInstantiation);
        icon.Name = TopBarIconName;
        original.GetParent().AddChildSafely(icon);
        icon.Visible = true;
        icon.Position = original.Position + new Vector2(32f, 0f);

        if (icon.Material is ShaderMaterial shader)
        {
            icon.Material = (Material)shader.Duplicate();
        }

        ApplyGoldStyleToIcon(icon, "%AscensionLabel", level);
        RefreshModifiersContainer(topBar, runState);
    }

    public static void AppendPortraitHoverTip(NTopBarPortraitTip portraitTip, IRunState runState)
    {
        int level = LibrarySecondAscensionState.GetRunLevel(runState);
        if (level <= 0)
        {
            return;
        }

        IHoverTip hoverTip = BuildPortraitHoverTip(runState, level);
        VanillaPrivate.TopBarPortraitTipHoverTip.Set(portraitTip, hoverTip);
        VanillaPrivate.TopBarPortraitTipShowTip.Set(portraitTip, true);
        portraitTip.FocusMode = Control.FocusModeEnum.All;
    }

    public static void SyncClientPanelFromLobby(Control screen)
    {
        NAscensionPanel? panel = FindSecondPanel(screen);
        if (panel == null)
        {
            return;
        }

        StartRunLobby? lobby = GetLobby(screen);
        if (lobby == null)
        {
            return;
        }

        int level = lobby.NetService.Type == NetGameType.Singleplayer && lobby.GameMode == GameMode.Standard
            ? LibrarySecondAscensionState.PreferredLevel
            : GetLobbyLevel(screen);
        if (lobby.NetService.Type == NetGameType.Client)
        {
            panel.Visible = level > 0;
        }

        if (level >= 0 && panel.Ascension != level)
        {
            panel.SetAscensionLevel(level);
            RefreshSecondPanelDeferred(panel);
        }
    }

    public static bool LobbyHasOnlySecondAscensionCarrier(Control screen)
    {
        StartRunLobby? lobby = GetLobby(screen);
        return lobby != null
               && (lobby.Modifiers.Count == 0 || lobby.Modifiers.All(modifier => modifier is LibrarySecondAscensionModifier));
    }

    public static void RefreshCharacterSelectPanelVisibility(NCharacterSelectScreen screen, bool characterUnlocked)
    {
        NAscensionPanel? panel = FindSecondPanel(screen);
        if (panel == null)
        {
            return;
        }

        bool visible = characterUnlocked && IsPanelEnabled(panel);
        panel.Visible = visible;
        if (visible && !screen.Lobby.NetService.Type.IsMultiplayer())
        {
            panel.AnimIn();
        }
    }

    private static NAscensionPanel? FindOriginalPanel(Control screen)
    {
        NAscensionPanel? uniquePanel = screen.GetNodeOrNull<NAscensionPanel>("%AscensionPanel");
        if (uniquePanel != null && !IsSecondPanel(uniquePanel))
        {
            return uniquePanel;
        }

        return screen.FindChildren("AscensionPanel", string.Empty, recursive: true, owned: false)
            .OfType<NAscensionPanel>()
            .FirstOrDefault(panel => !IsSecondPanel(panel));
    }

    private static NAscensionPanel? FindSecondPanel(Control screen) =>
        screen.FindChildren(PanelName, string.Empty, recursive: true, owned: false)
            .OfType<NAscensionPanel>()
            .FirstOrDefault();

    private static void CopyPanelLayout(Control original, Control panel, Vector2 offset)
    {
        panel.LayoutMode = original.LayoutMode;
        panel.AnchorsPreset = original.AnchorsPreset;
        panel.AnchorLeft = original.AnchorLeft;
        panel.AnchorTop = original.AnchorTop;
        panel.AnchorRight = original.AnchorRight;
        panel.AnchorBottom = original.AnchorBottom;
        panel.OffsetLeft = original.OffsetLeft + offset.X;
        panel.OffsetTop = original.OffsetTop + offset.Y;
        panel.OffsetRight = original.OffsetRight + offset.X;
        panel.OffsetBottom = original.OffsetBottom + offset.Y;
        panel.GrowHorizontal = original.GrowHorizontal;
        panel.GrowVertical = original.GrowVertical;
        panel.Scale = original.Scale;
    }

    private static void ConfigureSecondPanel(NAscensionPanel panel, MultiplayerUiMode mode, int lobbyLevel)
    {
        bool interactive = mode is MultiplayerUiMode.Host or MultiplayerUiMode.Singleplayer;
        int level = interactive ? LibrarySecondAscensionState.PreferredLevel : Math.Max(0, lobbyLevel);
        VanillaPrivate.AscensionPanelMaxAscension.Set(panel, LibrarySecondAscensionConfig.MaxLevel);
        VanillaPrivate.AscensionPanelArrowsVisible.Set(panel, interactive);
        RemovePanelHotkeys(panel);
        panel.SetMaxAscension(LibrarySecondAscensionConfig.MaxLevel);
        panel.SetAscensionLevel(level);
        panel.Visible = interactive || level > 0;
        ApplyGoldStyle(panel);
    }

    internal static bool IsPanelEnabled(NAscensionPanel panel)
    {
        // 客机面板按房主同步的等级显示，创建能力不受本地实验性内容开关影响。
        for (Node? node = panel.GetParent(); node != null; node = node.GetParent())
        {
            if (node is Control screen && GetLobby(screen) is { } lobby)
            {
                return lobby.NetService.Type == NetGameType.Client
                    ? LibrarySecondAscensionState.GetLevelFromModifiers(lobby.Modifiers) > 0
                    : LibrarySecondAscensionState.IsSelectionEnabled;
            }
        }

        return LibrarySecondAscensionState.IsSelectionEnabled;
    }

    private static void OnSecondPanelLevelChanged(Control screen, NAscensionPanel panel)
    {
        if (GetLobby(screen)?.NetService.Type == NetGameType.Client)
        {
            return;
        }

        LibrarySecondAscensionState.PreferredLevel = panel.Ascension;
        if (GetLobby(screen) is StartRunLobby lobby
            && (lobby.NetService.Type == NetGameType.Host
                || (lobby.NetService.Type == NetGameType.Singleplayer && lobby.GameMode == GameMode.Custom)))
        {
            IReadOnlyList<ModifierModel> next = LibrarySecondAscensionState.WithCarrier(lobby.Modifiers, panel.Ascension);
            if (!EquivalentModifiers(lobby.Modifiers, next))
            {
                lobby.SetModifiers(next.ToList());
            }
        }

        RefreshSecondPanel(panel);
        RefreshSecondPanelDeferred(panel);
    }

    private static bool EquivalentModifiers(IReadOnlyList<ModifierModel> current, IReadOnlyList<ModifierModel> next)
    {
        if (current.Count != next.Count)
        {
            return false;
        }

        for (int i = 0; i < current.Count; i++)
        {
            if (!current[i].IsEquivalent(next[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static StartRunLobby? GetLobby(Control screen) =>
        screen switch
        {
            NCharacterSelectScreen characterSelect => characterSelect.Lobby,
            NCustomRunScreen customRun => customRun.Lobby,
            _ => null
        };

    private static int GetLobbyLevel(Control screen)
    {
        try
        {
            return LibrarySecondAscensionState.GetLevelFromModifiers(GetLobby(screen)?.Modifiers ?? Array.Empty<ModifierModel>());
        }
        catch
        {
            return LibrarySecondAscensionState.PreferredLevel;
        }
    }

    private static Vector2 GetPanelOffset(Control screen) =>
        screen is NCustomRunScreen ? new Vector2(0f, 118f) : new Vector2(0f, -118f);

    private static void ApplyGoldStyle(NAscensionPanel panel)
    {
        ShaderMaterial? shader = VanillaPrivate.AscensionPanelIconHsv.Get(panel) as ShaderMaterial;
        shader ??= panel.GetNodeOrNull<Control>("%AscensionIcon")?.Material as ShaderMaterial;

        if (shader != null)
        {
            shader.SetShaderParameter(Hue, LibrarySecondAscensionConfig.GoldHue);
            shader.SetShaderParameter(Value, LibrarySecondAscensionConfig.GoldValue);
        }

        Label? label = VanillaPrivate.AscensionPanelAscensionLevel.Get(panel) as Label;
        label ??= panel.GetNodeOrNull<Label>("HBoxContainer/AscensionIconContainer/AscensionIcon/AscensionLevel");

        if (label != null)
        {
            label.AddThemeColorOverride(FontOutlineTheme, GoldOutline);
        }
    }

    private static void RemovePanelHotkeys(NAscensionPanel panel)
    {
        if (NHotkeyManager.Instance == null)
        {
            return;
        }

        if (VanillaPrivate.AscensionPanelDecrementAscension.Method?.CreateDelegate(typeof(Action), panel) is Action decrement)
        {
            NHotkeyManager.Instance.RemoveHotkeyPressedBinding(MegaInput.viewDeckAndTabLeft, decrement);
        }

        if (VanillaPrivate.AscensionPanelIncrementAscension.Method?.CreateDelegate(typeof(Action), panel) is Action increment)
        {
            NHotkeyManager.Instance.RemoveHotkeyPressedBinding(MegaInput.viewExhaustPileAndTabRight, increment);
        }
    }

    private static void ApplyGoldStyleToIcon(Control icon, string labelPath, int level)
    {
        if (icon.Material is ShaderMaterial shader)
        {
            shader.SetShaderParameter(Hue, LibrarySecondAscensionConfig.GoldHue);
            shader.SetShaderParameter(Value, LibrarySecondAscensionConfig.GoldValue);
        }

        Label? label = icon.GetNodeOrNull<Label>(labelPath);
        label ??= icon.FindChildren("*", string.Empty, recursive: true, owned: false).OfType<Label>().FirstOrDefault();
        if (label != null)
        {
            label.AddThemeColorOverride(FontOutlineTheme, GoldOutline);
            EnsureLabelFontOverride(label);
            SetLabelTextAutoSize(label, level.ToString());
        }
    }

    private static void EnsureLabelFontOverride(Label label)
    {
        if (label.HasThemeFontOverride(LabelFontTheme))
        {
            return;
        }

        label.AddThemeFontOverride(LabelFontTheme, ResourceLoader.Load<Font>(SharedAssets.KreonBoldGlyphSpaceOneResource));
    }

    private static void SetLabelTextAutoSize(Label label, string text)
    {
        MethodInfo? method = label.GetType().GetMethod("SetTextAutoSize", new[] { typeof(string) });
        if (method != null)
        {
            method.Invoke(label, new object[] { text });
            return;
        }

        label.Text = text;
    }

    private static void SetRichTextAutoSize(RichTextLabel label, string text)
    {
        MethodInfo? method = label.GetType().GetMethod("SetTextAutoSize", new[] { typeof(string) });
        if (method != null)
        {
            method.Invoke(label, new object[] { text });
            return;
        }

        label.Text = text;
    }

    private static void RefreshModifiersContainer(NTopBar topBar, IRunState runState)
    {
        if (VanillaPrivate.TopBarModifiersContainer.Get(topBar) is Control modifiersContainer)
        {
            bool hasVisibleModifierNode = modifiersContainer.GetChildren()
                .OfType<Control>()
                .Any(static child => GodotObject.IsInstanceValid(child));
            modifiersContainer.Visible = hasVisibleModifierNode
                && runState.Modifiers.Any(modifier => modifier is not LibrarySecondAscensionModifier);
        }
    }

    public static bool HideEmptyModifiersContainer(NTopBar topBar)
    {
        if (VanillaPrivate.TopBarModifiersContainer.Get(topBar) is not Control modifiersContainer)
        {
            return false;
        }

        bool hasModifierNode = modifiersContainer.GetChildren()
            .OfType<Control>()
            .Any(static child => GodotObject.IsInstanceValid(child));
        if (hasModifierNode)
        {
            return false;
        }

        modifiersContainer.Visible = false;
        return true;
    }

    private static IHoverTip BuildPortraitHoverTip(IRunState runState, int level)
    {
        StringBuilder description = new();
        var localPlayer = LocalContext.GetMe(runState);
        if (runState.AscensionLevel > 0
            && localPlayer != null
            && AscensionHelper.GetHoverTip(localPlayer.Character, runState.AscensionLevel, false) is { } ascensionHoverTip)
        {
            description.Append(ascensionHoverTip.Description);
            description.Append("\n\n");
        }

        description.Append(new LocString("gameplay_ui", "SECOND_ASCENSION.PORTRAIT_DESCRIPTION_PREFIX").GetFormattedText());
        foreach (string line in LibrarySecondAscensionState.GetUnlockedTitleLines(level))
        {
            description.Append('\n');
            description.Append(line);
        }

        return new HoverTip(new LocString("gameplay_ui", "SECOND_ASCENSION.PORTRAIT_TITLE"), description.ToString());
    }
}
