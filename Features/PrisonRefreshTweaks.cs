using System;
using HarmonyLib;
using System.Collections.Generic;

namespace PathOfIdleTweaks.Features;

internal static class PrisonRefreshQualityContext
{
    // 囚犯生成发生在同一线程内，用线程局部状态避免影响游戏中其他人物的创建。
    [ThreadStatic]
    internal static int MaximumQuality;
    [ThreadStatic]
    internal static SaveHouseData SaveHouseData = null!;
    [ThreadStatic]
    internal static int RefreshCountQuality;
    [ThreadStatic]
    internal static int RefreshCountJob;
}

// 在监牢创建或刷新囚犯列表期间，读取监牢当前允许出现的最高人物品级。
[HarmonyPatch(typeof(HousePrisonData), nameof(HousePrisonData.CreateHeroList))]
internal static class TrackPrisonHeroCreationPatch
{
    private static void Prefix(HousePrisonData __instance)
    {
        var HouseData = __instance?.houseData;

        if (HouseData == null) return;

        PrisonRefreshQualityContext.SaveHouseData = HouseData.saveHouseData;

        var maximumQuality = HouseData?.houseAttrData?.GetAttrValue((EHouseAttrType)10250, null) ?? 0f;
        PrisonRefreshQualityContext.MaximumQuality = Math.Max(1, (int)maximumQuality);
    }

    private static Exception Finalizer(Exception __exception)
    {
        // 无论原方法是否正常结束，都要清理上下文，避免影响其他来源的人物。
        PrisonRefreshQualityContext.MaximumQuality = 0;
        PrisonRefreshQualityContext.SaveHouseData = null!;
        return __exception;
    }
}

// 在 SaveHeroData 初始化属性、天赋和初始装备之前写入最高品级。
// 这样刷出的囚犯从创建开始就是完整的最高品级人物，而不是事后只改品级字段。
[HarmonyPatch(typeof(SaveHeroData), nameof(SaveHeroData.Create))]
internal static class MaximizeRefreshedPrisonHeroQualityPatch
{
    private static void Prefix(ref int quality,ref int jobId)
    {
        if (PrisonRefreshQualityContext.MaximumQuality <= 0 || 
            PrisonRefreshQualityContext.SaveHouseData == null ||
            Plugin.configPrisonMaxQuality.Value == false
            ) return;

        PrisonRefreshQualityContext.RefreshCountQuality++;
        PrisonRefreshQualityContext.RefreshCountJob++;

        List<int> jobList = new List<int>();
        var prisonJobTipDic = PrisonRefreshQualityContext.SaveHouseData.prisonJobTipDic;

        // 获取监牢中所有愿望职业的列表
        if (prisonJobTipDic != null)
        {
            foreach (var wishedJob in prisonJobTipDic)
            {
                if (wishedJob.Value) jobList.Add(wishedJob.Key);
            }
        }

        bool isMaxQuality = quality >= PrisonRefreshQualityContext.MaximumQuality;
        bool isMaxJob = false;

        //如果当前囚犯的品级已经是最大品级，则重置刷新计数器，并检查职业是否在愿望职业列表中。
        if (isMaxQuality)
        {
            PrisonRefreshQualityContext.RefreshCountQuality = 0;
            isMaxJob = jobList.Contains(jobId);
        }
        //如果当前囚犯的品级不是最大品级，则在刷新计数器达到阈值时，将其提升到最大品级，并重置刷新计数器。
        else
        {
            if (PrisonRefreshQualityContext.RefreshCountQuality >= Plugin.configPrisonMaxQualityCount.Value && Plugin.configPrisonMaxQualityCount.Value > 0)
            {
                quality = PrisonRefreshQualityContext.MaximumQuality;
                PrisonRefreshQualityContext.RefreshCountQuality = 0;
            }
        }
        //如果当前囚犯的职业已经是愿望职业，则重置刷新计数器。
        if (isMaxJob)
        {
            PrisonRefreshQualityContext.RefreshCountJob = 0;
        }
        //如果当前囚犯的职业不是愿望职业，则在刷新计数器达到阈值时，将其随机提升为愿望职业，并重置刷新计数器。
        else
        {
            if (jobList.Count > 0 && PrisonRefreshQualityContext.RefreshCountJob >= Plugin.configPrisonMaxJobCount.Value && Plugin.configPrisonMaxJobCount.Value > 0)
            {
                jobId = jobList[UnityEngine.Random.Range(0, jobList.Count)];
                quality = PrisonRefreshQualityContext.MaximumQuality;
                PrisonRefreshQualityContext.RefreshCountQuality = 0;
                PrisonRefreshQualityContext.RefreshCountJob = 0;
            }
        }
    }
}
