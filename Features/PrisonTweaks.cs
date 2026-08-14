using HarmonyLib;

namespace PathOfIdleTweaks.Features;

// 监牢扭曲界面和实际品质提升判定都会读取这个概率。
[HarmonyPatch(typeof(PriDistortData), nameof(PriDistortData.GetDistortQualityUpRate))]
internal static class GuaranteePrisonDistortQualityUpgradePatch
{
    private static void Postfix(ref float __result)
    {
        // 保留游戏原有的品质上限、资源消耗和理智判定，只将成功率固定为 100%。
        __result = 100f;
    }
}
