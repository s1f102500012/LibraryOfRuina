using System;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Godot;
using HarmonyLib;
using LibraryLib.Multiplayer;
using LibraryOfRuina.content.specialguests;
using LibraryOfRuina.core.compat;
using LibraryOfRuina.core.networking;
using LibraryOfRuina.core.settings;
using LibraryOfRuina.features.intentgraph;
using LibraryOfRuina.features.temporarymaps;
using LibraryOfRuina.framework.audio;
using LibraryOfRuina.framework.combat;
using LibraryOfRuina.infra.helpers;
using LibraryOfRuina.infra.hooks;
using LibraryOfRuina.infra.patching;
using LibraryOfRuina.interop;
using LibraryOfRuina.patches;
using LibraryOfRuina.ui;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace LibraryOfRuina.core;

[ModInitializer(nameof(Initialize))]
public static class LibraryOfRuinaInitializer
{
    private const string LogPrefix = "[LibraryOfRuina] ";

    /// <summary>
    /// 一个初始化步骤。必需步骤失败时不再执行后续步骤（尤其不再安装玩法补丁），输出汇总后把异常
    /// 抛回游戏，由 ModManager 照常记为该模组初始化失败；可选步骤（表现层）失败只记录，继续初始化。
    /// 已注册的部分内容不会回滚，这一点与拆分前相同。
    /// </summary>
    private readonly record struct InitStep(string Name, bool Required, Action Run);

    public static void Initialize()
    {
        var report = new InitReport();

        // 联机类型与框架必须先于任何内容注册；失败时整个模组不注入内容。
        // 这一组一直是捕获后返回，不向游戏报告初始化失败，保持原样。
        if (!report.RunAll(
            [
                new("NetTypes", true, static () => LibraryManagedNetTypes.RegisterAssembly(Assembly.GetExecutingAssembly())),
                new("Network", true, LibraryNetwork.Initialize),
                new("HealthBarForecast", true, LibraryHealthBarForecastFeature.Initialize),
                new("MadokaLongbowCompat", true, MadokaLongbowSavedStateCompat.Initialize),
            ]))
        {
            report.LogSummary("gameplay initialization was skipped");
            return;
        }

        var harmony = new Harmony(LibraryPatcher.HarmonyId);
        if (!report.RunAll(
            [
                new("Settings", true, static () => ExtSettingsRegistry.Register("LibraryOfRuina", new LibraryOfRuinaSettings())),
                new("SfxMixer", false, LibrarySfxMixer.Initialize),
            ]))
        {
            report.LogSummary("gameplay initialization was skipped");
            report.RethrowRequiredFailure();
        }

        // 始终注册内容能力，客机才能在本地关闭内容时自动采用房主的本局设置。
        LibraryOfRuinaSettings.EnableRuntimeSideEffects();

        LibraryPatcher.Result? patchResult = null;
        bool completed = report.RunAll(
        [
            new("VanillaPrivate", false, VanillaPrivate.Report),
            new("IntentGraphDisplayConfig", false, IntentGraphDisplayConfigRepository.Initialize),
            // Intent Graph 的程序集在它自己的初始化里加载，等所有模组初始化完再挂钩子。
            new("IntentGraphWorkshopBridge", false, static () => Callable.From(IntentGraphWorkshopModBridge.EnsurePatched).CallDeferred()),
            new("TemporaryMaps", true, TemporaryMapController.Initialize),
            new("MainMenuBgm", false, MainMenuBgmController.Initialize),
            new("NonCombatRunBgm", false, NonCombatRunBgmController.Initialize),
            new("AbnormalityEliteBgm", false, AbnormalityEliteBgmController.Initialize),
            new("ReverberationEnsembleBgm", false, ReverberationEnsembleBgmController.Initialize),
            new("AllyTurnProviders", true, RegisterAllyTurnProviders),
            new("SavedPropertyTypes", true, SavedPropertiesTypeCacheCompat.InjectModSavedPropertyTypes),
            new("SpecialGuests", true, SpecialGuestAutoRegistrar.Initialize),
            new("CardPools", true, RegisterRuntimeCardPools),
            new("LibraryExtensionPoints", true, LibraryExtensionPoints.Register),
            new("HookListener", true, LibraryOfRuinaHookListener.Subscribe),
            new("GameplayPatches", true, () => patchResult = LibraryPatcher.ApplyAll(harmony)),
            new("Cursor", false, LibraryCursorPatch.ApplyToCurrentGame),
        ]);

        if (patchResult != null)
        {
            report.AddPatchResult(patchResult);
        }

        report.LogSummary(completed ? null : "a required step failed; later steps were skipped");
        report.RethrowRequiredFailure();
    }

    private static void PatchSettingsUi(Harmony harmony)
    {
        harmony.CreateClassProcessor(typeof(InjectSettingsScreenModConfigPatch)).Patch();
        harmony.CreateClassProcessor(typeof(SettingsScreenModConfigVisibilityPatch)).Patch();
        harmony.CreateClassProcessor(typeof(MainMenuShowPendingSettingsErrorsPatch)).Patch();
        harmony.CreateClassProcessor(typeof(NGameQuitExtSettingsSavePatch)).Patch();
    }

    private static void RegisterRuntimeCardPools()
    {
        foreach (var type in LibraryAssemblyTypes.All)
        {
            var attr = type.GetCustomAttribute<CardPoolAttribute>();
            if (attr != null)
            {
                ModHelper.AddModelToPool(attr.PoolType, type);
            }
        }
    }

    private static void RegisterAllyTurnProviders()
    {
        var providerType = typeof(IAllyTurnProvider);
        var types = LibraryAssemblyTypes.All
            .Where(p => providerType.IsAssignableFrom(p)
                        && p is { IsAbstract: false, IsInterface: false });
        foreach (var type in types)
        {
            var provider = (IAllyTurnProvider)Activator.CreateInstance(type)!;
            AllyTurnRegistry.RegisterProvider(provider);
        }
    }

    private sealed class InitReport
    {
        private readonly List<string> _succeeded = [];
        private readonly List<string> _failures = [];
        private ExceptionDispatchInfo? _requiredFailure;

        /// <summary>依次执行步骤；遇到失败的必需步骤时停止并返回 false。</summary>
        public bool RunAll(IEnumerable<InitStep> steps)
        {
            foreach (InitStep step in steps)
            {
                try
                {
                    step.Run();
                    _succeeded.Add(step.Name);
                }
                catch (Exception exception)
                {
                    _failures.Add(step.Name + (step.Required ? " (required)" : ""));
                    Log.Error(LogPrefix + "Initialization step " + step.Name + " failed: " + exception);
                    if (step.Required)
                    {
                        _requiredFailure = ExceptionDispatchInfo.Capture(exception);
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 单个必需补丁类失败只记入汇总，不中断初始化（例如 Android 的 Mono 运行时无法为某些方法生成
        /// 替换体），与拆分前逐类 try/catch 的行为一致。可选补丁未安装只在汇总里列出。
        /// </summary>
        public void AddPatchResult(LibraryPatcher.Result result)
        {
            if (result.FailedRequired.Count > 0)
            {
                _failures.Add(result.FailedRequired.Count + " patch class(es) skipped: "
                              + string.Join(", ", result.FailedRequired));
            }

            if (result.SkippedOptional.Count > 0 || result.NotApplied.Count > 0)
            {
                Log.Info(LogPrefix + "Patch classes not applied (optional or Prepare=false): "
                         + string.Join(", ", result.SkippedOptional.Concat(result.NotApplied)));
            }
        }

        /// <summary>必需步骤失败时按原堆栈重新抛出，让游戏的模组加载器记录初始化失败。</summary>
        public void RethrowRequiredFailure() => _requiredFailure?.Throw();

        /// <summary>整个初始化只输出这一行结果，失败项与说明都在里面。</summary>
        public void LogSummary(string? note)
        {
            if (_failures.Count == 0 && note == null)
            {
                Log.Info("LibraryOfRuina loaded successfully (" + _succeeded.Count + " initialization steps).");
                return;
            }

            string summary = LogPrefix + "Initialization finished: " + _succeeded.Count + " step(s) ok"
                             + (_failures.Count == 0 ? "" : "; failed: " + string.Join("; ", _failures))
                             + (note == null ? "" : "; " + note)
                             + ".";
            if (_failures.Count == 0)
            {
                Log.Info(summary);
            }
            else
            {
                Log.Error(summary);
            }
        }
    }
}
