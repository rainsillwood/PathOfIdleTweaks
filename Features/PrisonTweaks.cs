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

// 游戏在理智变为 0 或 100 时会跳过品质判定；内部随机比较也使用严格小于号。
// 因此在扭曲成功结束后再次核对结果，确保囚犯的品质确实提升一级。
[HarmonyPatch(typeof(PriDistortData), nameof(PriDistortData.DistortHero))]
internal static class EnsurePrisonDistortQualityActuallyUpgradedPatch
{
    private static void Prefix(PriDistortData __instance, out int __state)
    {
        __state = __instance?.housePrisonData?.houseData?.selectHeroData?
            .saveHeroData?.quality ?? -1;
    }

    private static void Postfix(PriDistortData __instance, int __result, int __state)
    {
        // 返回值 0 才表示本次扭曲已正常支付资源并执行；其他返回值不应改动人物。
        if (__result != 0 || __state < 0)
            return;

        var heroData = __instance?.housePrisonData?.houseData?.selectHeroData;
        var saveHeroData = heroData?.saveHeroData;
        if (heroData == null || saveHeroData == null)
            return;

        var expectedQuality = __state + 1;

        // 使用游戏原生品质变更流程，以同步重算人物属性和品质表数据。
        // 正常只需一次；循环同时兜底原判定已经失败并导致品质降低的情况。
        for (var attempt = 0; saveHeroData.quality < expectedQuality && attempt < 32; attempt++)
            heroData.ChangeQuality(100f);

        if (saveHeroData.quality < expectedQuality)
        {
            Plugin.Log.LogWarning(
                $"Prison distortion quality correction failed: " +
                $"before={__state}, after={saveHeroData.quality}.");
        }
    }
}
