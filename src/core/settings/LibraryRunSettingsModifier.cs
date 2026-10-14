using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace LibraryOfRuina.core.settings;

/// <summary>
/// 本局的玩法与战斗动画设置。第二进阶实际等级由独立载体保存；局内按房主的载体读取。
/// <para>
/// 值由开局的一端决定：单人是本机，联机是房主。房主开局时按本地设置创建，随原版开局消息
/// <c>LobbyBeginRunMessage.modifiers</c> 发给各端；之后随 <c>SerializableRun.Modifiers</c> 存档，
/// 联机读档时客户端拿到的是房主规范化过的存档（<c>ClientLoadJoinResponseMessage</c>），所以各端一致。
/// 没有这份载体的旧局在读档时按当时的本地设置补建（房主一端），见 <see cref="LibraryRunSettings"/>。
/// </para>
/// <para>
/// 属性名进入 SavedProperty 表并决定 net-id 布局，改名或删除都会与之前的版本无法联机，
/// 也会让存档里的值读不回来（原版按属性名回填，缺失时保留下面的缺省值）。
/// </para>
/// </summary>
public sealed class LibraryRunSettingsModifier : ModifierModel
{
    [SavedProperty]
    public bool LibraryOfRuina_RunMonsterExtensionEnabled { get; set; } = true;

    // 与设置界面同一刻度：1 无视、2 弱、3 正常。存映射后的整数，不存滑条的原始小数。
    [SavedProperty]
    public int LibraryOfRuina_RunResistanceMode { get; set; } = LibraryOfRuinaSettings.NormalResistanceSetting;

    // 旧存档没有版本标记；房主规范化存档时一次性补齐新增设置，之后各端读取同一份值。
    [SavedProperty]
    public int LibraryOfRuina_RunSettingsVersion { get; set; }

    [SavedProperty]
    public bool LibraryOfRuina_RunVanillaCharacterProtectionEnabled { get; set; } = true;

    [SavedProperty]
    public bool LibraryOfRuina_RunExperimentalContentEnabled { get; set; }

    [SavedProperty]
    public bool LibraryOfRuina_RunSecondAscensionEnabled { get; set; }

    [SavedProperty]
    public bool LibraryOfRuina_RunAnimationEffectsEnabled { get; set; } = true;

    public bool MonsterExtensionEnabled => LibraryOfRuina_RunMonsterExtensionEnabled;

    public LibraryResistanceMode ResistanceMode =>
        LibraryOfRuinaSettings.ToLibraryResistanceMode(LibraryOfRuina_RunResistanceMode);

    // 界面上不显示（顶栏、Neow、每日挑战读档界面都过滤了这个类型）；文案只在别的模组或调试工具列出本局
    // 修改器时出现。
    public override LocString Title => new("modifiers", "LIBRARY_RUN_SETTINGS.title");

    public override LocString Description => new("modifiers", "LIBRARY_RUN_SETTINGS.description");

    internal static LibraryRunSettingsModifier CreateFromLocalSettings()
    {
        var carrier = (LibraryRunSettingsModifier)ModelDb.Modifier<LibraryRunSettingsModifier>().ToMutable();
        carrier.LibraryOfRuina_RunMonsterExtensionEnabled = LibraryOfRuinaSettings.MonsterExtensionEnabled;
        carrier.LibraryOfRuina_RunResistanceMode = LibraryOfRuinaSettings.ResistanceSettingLevel;
        carrier.CaptureAdditionalLocalSettings();
        return carrier;
    }

    internal void CaptureAdditionalLocalSettings()
    {
        LibraryOfRuina_RunSettingsVersion = 1;
        LibraryOfRuina_RunVanillaCharacterProtectionEnabled = LibraryOfRuinaSettings.VanillaCharacterProtectionEnabled;
        LibraryOfRuina_RunExperimentalContentEnabled = LibraryOfRuinaSettings.ExperimentalContentEnabled;
        LibraryOfRuina_RunSecondAscensionEnabled = LibraryOfRuinaSettings.SecondAscensionEnabled;
        LibraryOfRuina_RunAnimationEffectsEnabled = LibraryOfRuinaSettings.AnimationEffectsEnabled;
    }

    // 原版在 InitializeNewRun / InitializeSavedRun 里对 RunState.Modifiers 逐个调用，两端对称；
    // 读档不会经过 AfterRunCreated，所以两处都要设。基础库的抗性倍率只读这个静态值。
    protected override void AfterRunCreated(RunState runState) => LibraryRunSettings.ApplyToRun(this);

    protected override void AfterRunLoaded(RunState runState) => LibraryRunSettings.ApplyToRun(this);
}
