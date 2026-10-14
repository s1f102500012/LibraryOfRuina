using System;
using System.Linq;
using LibraryOfRuina.infra.lifecycle;
using LibraryOfRuina.interop;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace LibraryOfRuina.core.settings;

/// <summary>
/// 影响玩法的设置按局固定：局内读 <see cref="LibraryRunSettingsModifier"/>，局外（主菜单、图鉴）读本地设置。
/// <para>
/// 载体从哪里来（补丁见 <c>LibraryRunSettingsPatches</c>）：
/// </para>
/// <list type="bullet">
/// <item>新局：房主（单人即本机）在 <c>StartRunLobby.BeginRunForAllPlayers</c> 按本地设置追加载体，原版把这份列表
/// 写进发给客户端的 <c>LobbyBeginRunMessage</c>，再交给本机的 <c>BeginRunLocally</c>。各端在 <c>BeginRunLocally</c>
/// 把载体取出暂存（标准模式的选角界面遇到非空列表会报错并丢弃全部修改器），由同一种子的
/// <c>RunState.CreateForNewRun</c> 接回。不经过大厅直接建局的路径（验证套件、调试开局）用本地设置。</item>
/// <item>读档：存档里有载体就用存档的。没有（v0.21.2 及更早的存档）时在 <c>RunState.FromSerializable</c> 后按
/// 此刻的本地设置补建。联机读档时房主先用 <c>CanonicalizeSave</c> 规范化存档（其中调用 FromSerializable），
/// 补建的载体因此写进发给客户端的存档，客户端读到的是房主的值；之后的存档里都有它，只补建这一次。</item>
/// </list>
/// 所有进程始终注册内容能力，是否启用由本局载体决定；初始化失败时的能力不一致由
/// <see cref="LibraryRunInjectionGuard"/> 诊断并退出开局或读档。
/// </summary>
internal static class LibraryRunSettings
{
    private const string LogPrefix = "[LibraryOfRuina.RunSettings] ";

    // BeginRunLocally 取出、CreateForNewRun 接走之间的载体。两者之间隔着选角界面的转场（await），
    // 按种子配对，开局失败留下的旧值会被下一次开局覆盖或在离开本局时清掉。
    private static PendingCarrier? _pendingLobbyCarrier;

    private sealed record PendingCarrier(string Seed, LibraryRunSettingsModifier Carrier);

    /// <summary>当前局是否启用废墟图书馆内容；没有局时为本地设置。</summary>
    internal static bool MonsterExtensionEnabled =>
        IsMonsterExtensionEnabled(CurrentRun.State);

    internal static bool IsVanillaCharacterProtectionEnabled(IRunState runState) =>
        Find(runState)?.LibraryOfRuina_RunVanillaCharacterProtectionEnabled
        ?? LibraryOfRuinaSettings.VanillaCharacterProtectionEnabled;

    internal static bool ExperimentalContentEnabled =>
        Find(CurrentRun.State)?.LibraryOfRuina_RunExperimentalContentEnabled
        ?? LibraryOfRuinaSettings.ExperimentalContentEnabled;

    internal static bool SecondAscensionEnabled =>
        Find(CurrentRun.State)?.LibraryOfRuina_RunSecondAscensionEnabled
        ?? LibraryOfRuinaSettings.SecondAscensionEnabled;

    internal static bool AnimationEffectsEnabled =>
        Find(CurrentRun.State)?.LibraryOfRuina_RunAnimationEffectsEnabled
        ?? LibraryOfRuinaSettings.AnimationEffectsEnabled;

    internal static bool IsMonsterExtensionEnabled(IRunState? runState) =>
        Find(runState)?.MonsterExtensionEnabled ?? LibraryOfRuinaSettings.MonsterExtensionEnabled;

    /// <summary>
    /// 读档时 RunState 还没建出来的读取点用这个。存档里没有载体时返回本地设置，与随后补建的值相同，
    /// 所以与补建补丁谁先执行无关。
    /// </summary>
    internal static bool IsMonsterExtensionEnabled(SerializableRun save) =>
        Find(save)?.MonsterExtensionEnabled ?? LibraryOfRuinaSettings.MonsterExtensionEnabled;

    internal static LibraryRunSettingsModifier? Find(IRunState? runState) =>
        runState?.Modifiers.OfType<LibraryRunSettingsModifier>().FirstOrDefault();

    internal static LibraryRunSettingsModifier? Find(SerializableRun save)
    {
        ModelId carrierId = ModelDb.Modifier<LibraryRunSettingsModifier>().Id;
        SerializableModifier? entry = save.Modifiers?.FirstOrDefault(modifier => modifier.Id == carrierId);
        return entry == null ? null : ModifierModel.FromSerializable(entry) as LibraryRunSettingsModifier;
    }

    internal static List<ModifierModel> WithHostCarrier(IEnumerable<ModifierModel> modifiers) =>
        modifiers
            .Where(static modifier => modifier is not LibraryRunSettingsModifier)
            .Append(LibraryRunSettingsModifier.CreateFromLocalSettings())
            .ToList();

    /// <summary>
    /// 从开局消息的修改器列表里取出载体暂存，返回不含载体的列表交给原版界面。列表里没有载体时不暂存：
    /// 房主没有注入内容（客户端随后由 LibraryRunInjectionGuard 退出），新局退回本地设置。
    /// </summary>
    internal static List<ModifierModel> TakeLobbyCarrier(string seed, List<ModifierModel> modifiers)
    {
        LibraryRunSettingsModifier? carrier = modifiers.OfType<LibraryRunSettingsModifier>().LastOrDefault();
        _pendingLobbyCarrier = carrier == null ? null : new PendingCarrier(seed, carrier);
        return carrier == null
            ? modifiers
            : modifiers.Where(static modifier => modifier is not LibraryRunSettingsModifier).ToList();
    }

    internal static void AttachToNewRun(RunState runState, string seed)
    {
        PendingCarrier? pending = _pendingLobbyCarrier;
        _pendingLobbyCarrier = null;
        if (Find(runState) != null)
        {
            return;
        }

        bool fromLobby = pending != null && string.Equals(pending.Seed, seed, StringComparison.Ordinal);
        LibraryRunSettingsModifier carrier = fromLobby
            ? pending!.Carrier
            : LibraryRunSettingsModifier.CreateFromLocalSettings();
        Attach(runState, carrier, fromLobby ? "new run, lobby" : "new run, local settings");
    }

    internal static void BackfillLoadedRun(RunState runState)
    {
        if (Find(runState) == null)
        {
            Attach(runState, LibraryRunSettingsModifier.CreateFromLocalSettings(), "loaded run without carrier, backfilled from local settings");
        }
        else if (Find(runState) is { LibraryOfRuina_RunSettingsVersion: 0 } legacy)
        {
            legacy.CaptureAdditionalLocalSettings();
            // 第二进阶等级已经独立存档；旧局保留实际等级，不受当前房主偏好覆盖。
            if (features.secondascension.LibrarySecondAscensionState.GetRunLevel(runState) > 0)
            {
                legacy.LibraryOfRuina_RunExperimentalContentEnabled = true;
                legacy.LibraryOfRuina_RunSecondAscensionEnabled = true;
            }
        }
    }

    internal static void ApplyToRun(LibraryRunSettingsModifier carrier)
    {
        LibraryResistanceModeState.Current = carrier.ResistanceMode;
    }

    /// <summary>离开本局时调用：抗性回到本地设置，丢掉没被接走的开局载体。</summary>
    internal static void OnRunCleaningUp()
    {
        _pendingLobbyCarrier = null;
        LibraryResistanceModeState.Current =
            LibraryOfRuinaSettings.ToLibraryResistanceMode(LibraryOfRuinaSettings.ResistanceSettingLevel);
    }

    /// <param name="setModifiers">写回 <c>RunState.Modifiers</c>；验证套件传入会失败的版本来覆盖挂载失败的分支。</param>
    internal static void Attach(
        RunState runState,
        LibraryRunSettingsModifier carrier,
        string source,
        Func<RunState, IReadOnlyList<ModifierModel>, bool>? setModifiers = null)
    {
        setModifiers ??= static (state, modifiers) => VanillaPrivate.RunStateModifiers.Set(state, modifiers);
        if (!setModifiers(runState, runState.Modifiers.Append(carrier).ToList()))
        {
            // 挂不上时各读取点退回本地设置。联机两端的本地设置可能不同，退回就会分叉，而开局消息里有载体，
            // 注入状态检查也认不出这种失败，所以联机时直接失败：建局与读档构造都在原版界面的 try 里调用，
            // catch 会断开大厅、回主菜单并显示这条信息。单人没有另一端，退回本地设置即可。
            if (runState.Players.Count > 1)
            {
                throw new InvalidOperationException(
                    "LibraryOfRuina: could not attach the run settings carrier to RunState.Modifiers "
                    + "(vanilla backing field unavailable); a multiplayer run cannot start without it.");
            }

            Log.Error(LogPrefix + "RunState.Modifiers backing field is unavailable; run settings fall back to local settings.");
            return;
        }

        Log.Info(LogPrefix + "Run settings (" + source + "): monsterExtension="
                 + carrier.MonsterExtensionEnabled
                 + ", resistance="
                 + carrier.LibraryOfRuina_RunResistanceMode
                 + ", vanillaProtection=" + carrier.LibraryOfRuina_RunVanillaCharacterProtectionEnabled
                 + ", experimental=" + carrier.LibraryOfRuina_RunExperimentalContentEnabled
                 + ", secondAscension=" + carrier.LibraryOfRuina_RunSecondAscensionEnabled
                 + ", animations=" + carrier.LibraryOfRuina_RunAnimationEffectsEnabled
                 + ".");
    }
}
