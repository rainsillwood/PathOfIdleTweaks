using HarmonyLib;
using UnityEngine;

namespace PathOfIdleTweaks.Features;

// 第一阶段直接复用游戏自带的原生 DebugLayer。
// 该窗口已经包含装备、套装、宝箱、符文、材料和奇物等物品发放控件。
[HarmonyPatch(typeof(Root), "Update")]
internal static class ToggleNativeItemManagementUiPatch
{
    private const KeyCode ToggleKey = KeyCode.F8;

    private static void Postfix()
    {
        if (!Input.GetKeyDown(ToggleKey))
            return;

        var uiMgr = Game.uiMgr;
        if (uiMgr == null)
            return;

        if (uiMgr.CheckActiveLayer(TLayer.DebugLayer))
        {
            uiMgr.closeLayerByTag(TLayer.DebugLayer);
            Plugin.Log.LogDebug("Native item management UI closed.");
        }
        else
        {
            // 使用游戏原本为调试窗口设置的最高层级，避免被普通界面遮挡。
            uiMgr.openLayer(TLayer.DebugLayer, 100, null);
            Plugin.Log.LogDebug("Native item management UI opened.");
        }
    }
}
