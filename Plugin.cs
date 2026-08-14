using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace PathOfIdleTweaks;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("PathOfIdle.exe")]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;

    public override void Load()
    {
        Log = base.Log;

        // 自动扫描并注册当前程序集中的全部 Harmony 补丁。
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, MyPluginInfo.PLUGIN_GUID);
        Log.LogInfo("Path of Idle Tweaks loaded.");
    }
}
