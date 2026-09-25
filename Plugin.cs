using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;

namespace PathOfIdleTweaks;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("PathOfIdle.exe")]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;
    internal static ConfigEntry<bool> configPrisonMaxQuality;
    internal static ConfigEntry<bool> configEquipmentSuffixQuality;
    internal static ConfigEntry<bool> configEquipmentNewSuffixQuality;
    internal static ConfigEntry<bool> configEquipmentExistSuffixQuality;
    internal static ConfigEntry<bool> configEquipmentSuffixMaxQuality;
    internal static ConfigEntry<int> configPrisonMaxQualityCount;
    internal static ConfigEntry<int> configPrisonMaxJobCount;

    public override void Load()
    {
        Log = base.Log;

        // 自动扫描并注册当前程序集中的全部 Harmony 补丁。
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, MyPluginInfo.PLUGIN_GUID);

        var customFile = new ConfigFile(Path.Combine(Paths.ConfigPath, "custom_config.cfg"), true);

        configPrisonMaxQuality = Config.Bind("监牢.保底设置", "开启功能:", true, "此功能仅能将品质提升到当前监牢允许的最高品质");
        configPrisonMaxQualityCount = Config.Bind("监牢.保底设置", "小保底次数:", 10, "到达此次数后保证刷新出最高品质囚犯,设置为1表示每个囚犯都是最高品质,设置为0表示关闭功能");
        configPrisonMaxJobCount = Config.Bind("监牢.保底设置", "大保底次数:", 50, "到达此次数后保证刷新出最高品质符合职业的囚犯,设置为1表示每个囚犯都是符合职业,设置为0表示不对职业作要求");
        configEquipmentSuffixQuality = Config.Bind("装备.词条增强", "开启功能:", true, "此功能将装备词条品质提升到当前等级允许的最高");
        configEquipmentNewSuffixQuality = Config.Bind("装备.词条增强", "修改新装备:", true, "关闭后将不再修改新装备");
        configEquipmentExistSuffixQuality = Config.Bind("装备.词条增强", "修改已有装备:", true, "关闭后将不再修改已有装备");
        configEquipmentSuffixMaxQuality = Config.Bind("装备.词条增强", "突破上限:", false, "此功能将无视装备等级将词条等级提升到9级");

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

        Log.LogInfo("Path of Idle Tweaks loaded.");
    }
}