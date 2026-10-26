using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using PathOfIdleTweaks.Features;
using HarmonyLib;

namespace PathOfIdleTweaks;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("PathOfIdle.exe")]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log = null!;
    internal static ConfigEntry<bool> configPrisonMaxQuality = null!;
    internal static ConfigEntry<bool> configEquipmentSuffixQuality = null!;
    internal static ConfigEntry<bool> configEquipmentNewSuffixQuality = null!;
    internal static ConfigEntry<bool> configEquipmentExistSuffixQuality = null!;
    internal static ConfigEntry<int> configEquipmentSuffixMaxQuality = null!;
    internal static ConfigEntry<int> configPrisonMaxQualityCount = null!;
    internal static ConfigEntry<int> configPrisonMaxJobCount = null!;
    internal static ConfigEntry<bool> configItemRegisterEnabled = null!;
    internal static ConfigEntry<float> configOutlandExpectMultiplier = null!;

    public override void Load()
    {
        Log = base.Log;

        //Config.Bind("监牢", "", false, "");
        configPrisonMaxQuality = Config.Bind("监牢.保底", "开启功能:", false,
            "此功能仅能将品质提升到当前监牢允许的最高品质");
        configPrisonMaxQualityCount = Config.Bind("监牢.保底", "小保底次数:", 10,
            "到达此次数后保证刷新出最高品质囚犯,设置为1表示每个囚犯都是最高品质,设置为0表示关闭功能");
        configPrisonMaxJobCount = Config.Bind("监牢.保底", "大保底次数:", 50,
            "到达此次数后保证刷新出最高品质符合职业的囚犯,设置为1表示每个囚犯都是符合职业,设置为0表示不对职业作要求");
        if (configPrisonMaxQualityCount.Value < 0)
        {
            configPrisonMaxQualityCount.Value = 10;
            Log.LogWarning("小保底次数不能小于0，已自动修正为10。");
        }
        if (configPrisonMaxJobCount.Value < 0)
        {
            configPrisonMaxJobCount.Value = 50;
            Log.LogWarning("大保底次数不能小于0，已自动修正为50。");
        }

        //Config.Bind("装备", "", false, "");
        configEquipmentSuffixQuality = Config.Bind("装备.词条增强", "开启功能:", false,
            "此功能将装备词条品质提升到当前等级允许的最高,使用编辑器请关闭此功能");
        configEquipmentNewSuffixQuality = Config.Bind("装备.词条增强", "修改新装备:", false,
            "关闭后将不再修改新装备");
        configEquipmentExistSuffixQuality = Config.Bind("装备.词条增强", "修改已有装备:", false,
            "关闭后将不再修改已有装备,重启后生效");
        configEquipmentSuffixMaxQuality = Config.Bind("装备.词条增强", "固定等级:", 0,
            "此功能将根据无视装备等级将词条等级强行设定为指定等级,0则关闭此功能,最高为9");
        if (configEquipmentSuffixMaxQuality.Value < 0)
        {
            configEquipmentSuffixMaxQuality.Value = 0;
            Log.LogWarning("固定等级不能小于0，已自动修正为0。");
        }
        if (configEquipmentSuffixMaxQuality.Value > 9)
        {
            configEquipmentSuffixMaxQuality.Value = 9;
            Log.LogWarning("固定等级不能大于9，已自动修正为9。");
        }

        //Config.Bind("功能", "", false, "");
        configItemRegisterEnabled = Config.Bind("功能.古神之眼", "开启功能:", false,
            "注册自定义物品/配方（古神之眼 7160101）。关闭时,秘所配方不注册，存档里残留的该物品会在载入存档时被删除。此选项只在游戏启动时读取，修改后必须重启游戏才生效。");
        configOutlandExpectMultiplier = Config.Bind("功能.古神之眼", "期望血肉倍率:", 3.0f,
            "对期望血肉表整体乘一个倍率：小于 3.0 = 更快升品（花费更少），大于 3.0 = 更慢");

        // ① 初始化各 Register 的列表（最优先）
        RegisterManager.Init();
        // ② 各模块按配置设置 isEnable 并把定义推进列表（此时 TableData 还没 init，不碰表）
        RegisterManager.DefineModules();

        // 所有配置绑定完成后再注册补丁，避免补丁回调里读到 null 配置。
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, MyPluginInfo.PLUGIN_GUID);
        Log.LogInfo("Path of Idle Tweaks loaded.");
    }
}
