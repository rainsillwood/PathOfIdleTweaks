using System;
using HarmonyLib;

namespace PathOfIdleTweaks.Features;

internal static class PrisonRefreshQualityContext
{
    // 囚犯生成发生在同一线程内，用线程局部状态避免影响游戏中其他人物的创建。
    [ThreadStatic]
    internal static int MaximumQuality;
}

// 在监牢创建或刷新囚犯列表期间，读取监牢当前允许出现的最高人物品级。
[HarmonyPatch(typeof(HousePrisonData), nameof(HousePrisonData.CreateHeroList))]
internal static class TrackPrisonHeroCreationPatch
{
    private static void Prefix(HousePrisonData __instance)
    {
        var maximumQuality = __instance?.houseData?.houseAttrData?
            .GetAttrValue((EHouseAttrType)10250, null) ?? 0f;

        PrisonRefreshQualityContext.MaximumQuality = Math.Max(1, (int)maximumQuality);
    }

    private static Exception Finalizer(Exception __exception)
    {
        // 无论原方法是否正常结束，都要清理上下文，避免影响其他来源的人物。
        PrisonRefreshQualityContext.MaximumQuality = 0;
        return __exception;
    }
}

// 在 SaveHeroData 初始化属性、天赋和初始装备之前写入最高品级。
// 这样刷出的囚犯从创建开始就是完整的最高品级人物，而不是事后只改品级字段。
[HarmonyPatch(typeof(SaveHeroData), nameof(SaveHeroData.Create))]
internal static class MaximizeRefreshedPrisonHeroQualityPatch
{
    private static void Prefix(ref int quality)
    {
        if (PrisonRefreshQualityContext.MaximumQuality > 0)
            quality = PrisonRefreshQualityContext.MaximumQuality;
    }
}
