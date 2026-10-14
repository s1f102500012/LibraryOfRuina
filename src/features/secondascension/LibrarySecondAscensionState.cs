using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.core.settings;
using LibraryOfRuina.infra.lifecycle;
using LibraryOfRuina.interop;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;

namespace LibraryOfRuina.features.secondascension;

internal static class LibrarySecondAscensionState
{
    private sealed class PendingModifiersBox
    {
        public IReadOnlyList<ModifierModel>? Value { get; set; }
    }

    private static readonly ConditionalWeakTable<StartRunLobby, PendingModifiersBox> PendingRunModifiers = new();
    private static int? _pendingSingleplayerStandardLevel;

    public static bool IsSelectionEnabled =>
        LibraryRunSettings.ExperimentalContentEnabled && LibraryRunSettings.SecondAscensionEnabled;

    public static int Clamp(int level) => Math.Clamp(level, 0, LibrarySecondAscensionConfig.MaxLevel);

    public static int PreferredLevel
    {
        get => IsSelectionEnabled ? Clamp(LibraryOfRuinaSettings.PreferredSecondAscensionLevel) : 0;
        set => LibraryOfRuinaSettings.PreferredSecondAscensionLevel = Clamp(value);
    }

    public static int GetRunLevel(IRunState? runState)
    {
        if (runState == null)
        {
            return 0;
        }

        return GetLevelFromModifiers(runState.Modifiers);
    }

    public static int GetLevelFromModifiers(IReadOnlyList<ModifierModel> modifiers) =>
        Clamp(modifiers.OfType<LibrarySecondAscensionModifier>().FirstOrDefault()?.Level ?? 0);

    public static int GetCurrentRunLevel() => GetRunLevel(CurrentRun.State);

    public static bool HasLevel(LibrarySecondAscensionLevel level, IRunState? runState = null)
    {
        int runLevel = runState == null ? GetCurrentRunLevel() : GetRunLevel(runState);
        return runLevel >= (int)level;
    }

    public static bool HasCounterIntentLevel(IRunState? runState = null) =>
        HasLevel(LibrarySecondAscensionLevel.UrbanLegend, runState);

    public static bool HasCounterIntentUpgradeI(IRunState? runState = null) =>
        HasLevel(LibrarySecondAscensionLevel.ReverberationEnsemble, runState);

    public static bool HasCounterIntentUpgradeII(IRunState? runState = null) =>
        HasLevel(LibrarySecondAscensionLevel.DistortedReverberationEnsemble, runState);

    // 传闻（1级）：按玩家人数取敌人混乱抗性上限倍率，未达等级时不提高。
    public static decimal GetChaoCapMultiplier(int level, int playerCount)
    {
        if (Clamp(level) < (int)LibrarySecondAscensionLevel.Rumor)
        {
            return 1m;
        }

        return playerCount switch
        {
            <= 1 => LibrarySecondAscensionConfig.RumorChaoCapMultiplierOnePlayer,
            2 => LibrarySecondAscensionConfig.RumorChaoCapMultiplierTwoPlayers,
            3 => LibrarySecondAscensionConfig.RumorChaoCapMultiplierThreePlayers,
            _ => LibrarySecondAscensionConfig.RumorChaoCapMultiplierFourPlayers
        };
    }

    // 都市传说（3级）：按玩家人数取敌人生命上限倍率，未达等级时不提高。
    public static decimal GetEnemyMaxHpMultiplier(int level, int playerCount)
    {
        if (Clamp(level) < (int)LibrarySecondAscensionLevel.UrbanLegend)
        {
            return 1m;
        }

        return playerCount switch
        {
            <= 1 => LibrarySecondAscensionConfig.UrbanLegendMaxHpMultiplierOnePlayer,
            2 => LibrarySecondAscensionConfig.UrbanLegendMaxHpMultiplierTwoPlayers,
            3 => LibrarySecondAscensionConfig.UrbanLegendMaxHpMultiplierThreePlayers,
            _ => LibrarySecondAscensionConfig.UrbanLegendMaxHpMultiplierFourPlayers
        };
    }

    // 都市恶疾（4级）：按玩家人数取敌人每回合恢复的混乱抗性占上限比例，未达等级时不恢复。
    public static decimal GetChaoRecoveryPercent(int level, int playerCount)
    {
        if (Clamp(level) < (int)LibrarySecondAscensionLevel.UrbanPlague)
        {
            return 0m;
        }

        return playerCount switch
        {
            <= 1 => LibrarySecondAscensionConfig.UrbanPlagueChaoRecoveryPercentOnePlayer,
            2 => LibrarySecondAscensionConfig.UrbanPlagueChaoRecoveryPercentTwoPlayers,
            3 => LibrarySecondAscensionConfig.UrbanPlagueChaoRecoveryPercentThreePlayers,
            _ => LibrarySecondAscensionConfig.UrbanPlagueChaoRecoveryPercentFourPlayers
        };
    }

    // 残响乐团（8级）：开始游戏时失去的生命上限，按比例向下取整且不少于最低值。
    public static int GetStartingMaxHpLoss(int maxHp) =>
        Math.Max(
            LibrarySecondAscensionConfig.ReverberationEnsembleMinMaxHpLoss,
            (int)Math.Floor(maxHp * LibrarySecondAscensionConfig.ReverberationEnsembleMaxHpLossPercent));

    public static bool SkipsLiberationPhaseRecovery(IRunState? runState) =>
        HasLevel(LibrarySecondAscensionLevel.UrbanMyth, runState);

    public static IReadOnlyList<ModifierModel> WithPreferredCarrier(IReadOnlyList<ModifierModel> modifiers)
    {
        int existingLevel = GetLevelFromModifiers(modifiers);
        return WithCarrier(modifiers, existingLevel > 0 ? existingLevel : PreferredLevel);
    }

    public static IReadOnlyList<ModifierModel> WithCarrier(IReadOnlyList<ModifierModel> modifiers, int level)
    {
        if (!IsSelectionEnabled)
        {
            return WithoutCarrier(modifiers);
        }

        List<ModifierModel> result = modifiers
            .Where(modifier => modifier is not LibrarySecondAscensionModifier)
            .ToList();

        level = Clamp(level);
        if (level > 0)
        {
            result.Add(LibrarySecondAscensionModifier.Create(level));
        }

        return result;
    }

    public static IReadOnlyList<ModifierModel> WithoutCarrier(IReadOnlyList<ModifierModel> modifiers) =>
        modifiers.Where(modifier => modifier is not LibrarySecondAscensionModifier).ToList();

    public static void CaptureStandardBeginRunModifiers(StartRunLobby lobby, ref IReadOnlyList<ModifierModel> modifiers)
    {
        // 客户端收到的是房主的修改器列表，第二进阶开没开由房主决定，不看本机开关。
        if (lobby.NetService.Type != NetGameType.Client && !IsSelectionEnabled)
        {
            _pendingSingleplayerStandardLevel = null;
            modifiers = WithoutCarrier(modifiers);
            return;
        }

        if (lobby.GameMode != GameMode.Standard)
        {
            return;
        }

        if (lobby.NetService.Type == NetGameType.Singleplayer)
        {
            int level = GetLevelFromModifiers(modifiers);
            if (level <= 0)
            {
                level = PreferredLevel;
            }

            _pendingSingleplayerStandardLevel = level > 0 ? level : null;
            Log.Info("[LibrarySecondAscension] Captured standard singleplayer level="
                + (_pendingSingleplayerStandardLevel ?? 0)
                + ".");
        }
        else if (modifiers.Count > 0)
        {
            SetPendingRunModifiers(lobby, modifiers);
            Log.Info("[LibrarySecondAscension] Captured standard multiplayer modifiers: count="
                + modifiers.Count
                + ", level="
                + GetLevelFromModifiers(modifiers)
                + ".");
        }

        if (modifiers.Count > 0 && modifiers.All(modifier => modifier is LibrarySecondAscensionModifier))
        {
            modifiers = Array.Empty<ModifierModel>();
        }
    }

    public static IReadOnlyList<ModifierModel> ResolveSingleplayerRunModifiers(IReadOnlyList<ModifierModel> modifiers)
    {
        if (!IsSelectionEnabled)
        {
            _pendingSingleplayerStandardLevel = null;
            return WithoutCarrier(modifiers);
        }

        if (_pendingSingleplayerStandardLevel is not { } level)
        {
            return modifiers;
        }

        _pendingSingleplayerStandardLevel = null;
        Log.Info("[LibrarySecondAscension] Resolved standard singleplayer run level=" + level + ".");
        return WithCarrier(modifiers, level);
    }

    public static void EnsureStandardSingleplayerRunModifier(RunState runState)
    {
        if (!IsSelectionEnabled)
        {
            _pendingSingleplayerStandardLevel = null;
            return;
        }

        if (!RunManagerCompat.IsStandardGameMode(runState))
        {
            return;
        }

        if (runState.Players.Count != 1)
        {
            return;
        }

        int existingLevel = GetLevelFromModifiers(runState.Modifiers);
        if (existingLevel > 0)
        {
            _pendingSingleplayerStandardLevel = null;
            Log.Info("[LibrarySecondAscension] RunState contains second ascension level=" + existingLevel + ".");
            return;
        }

        if (_pendingSingleplayerStandardLevel is not { } level)
        {
            return;
        }

        _pendingSingleplayerStandardLevel = null;
        IReadOnlyList<ModifierModel> modifiers = WithCarrier(runState.Modifiers, level);
        if (!VanillaPrivate.RunStateModifiers.IsAvailable)
        {
            Log.Warn("[LibrarySecondAscension] Failed to apply standard singleplayer RunState level: Modifiers backing field missing.");
            return;
        }

        VanillaPrivate.RunStateModifiers.Set(runState, modifiers);
        Log.Info("[LibrarySecondAscension] Applied standard singleplayer RunState level=" + level + ".");
    }

    public static LocString GetTitle(int level) =>
        new("gameplay_ui", "SECOND_ASCENSION.LEVEL_" + Clamp(level).ToString("D2") + ".title");

    public static LocString GetDescription(int level) =>
        new("gameplay_ui", "SECOND_ASCENSION.LEVEL_" + Clamp(level).ToString("D2") + ".description");

    public static IEnumerable<string> GetUnlockedTitleLines(int level)
    {
        for (int i = 1; i <= Clamp(level); i++)
        {
            yield return GetTitle(i).GetFormattedText();
        }
    }

    public static IReadOnlyList<ModifierModel> ResolveMultiplayerRunModifiers(StartRunLobby lobby, IReadOnlyList<ModifierModel> modifiers)
    {
        // 同上：客户端照用房主的载体。
        if (lobby.NetService.Type != NetGameType.Client && !IsSelectionEnabled)
        {
            return WithoutCarrier(modifiers);
        }

        if (lobby.GameMode != GameMode.Standard)
        {
            return modifiers;
        }

        IReadOnlyList<ModifierModel>? pending = TakePendingRunModifiers(lobby);
        if (pending is { Count: > 0 })
        {
            Log.Info("[LibrarySecondAscension] Resolved standard multiplayer pending level="
                + GetLevelFromModifiers(pending)
                + ".");
            return pending;
        }

        if (lobby.Modifiers.Count > 0)
        {
            Log.Info("[LibrarySecondAscension] Resolved standard multiplayer lobby level="
                + GetLevelFromModifiers(lobby.Modifiers)
                + ".");
            return CloneModifiers(lobby.Modifiers);
        }

        return modifiers;
    }

    public static IReadOnlyList<ModifierModel> CloneModifiers(IReadOnlyList<ModifierModel> modifiers) =>
        modifiers.Select(modifier => ModifierModel.FromSerializable(modifier.ToSerializable())).ToList();

    public static void SetPendingRunModifiers(StartRunLobby lobby, IReadOnlyList<ModifierModel> modifiers)
    {
        PendingRunModifiers.GetOrCreateValue(lobby).Value = CloneModifiers(modifiers);
    }

    private static IReadOnlyList<ModifierModel>? TakePendingRunModifiers(StartRunLobby lobby)
    {
        if (!PendingRunModifiers.TryGetValue(lobby, out PendingModifiersBox? box))
        {
            return null;
        }

        IReadOnlyList<ModifierModel>? modifiers = box.Value;
        box.Value = null;
        return modifiers;
    }
}
