using HarmonyLib;
using System;

namespace PathOfIdleTweaks.Features;

internal static class EquipmentTweaks
{
    // 装备词条表确认 1～9 为合法等级，9 是最高级。
    internal const int MaximumAffixLevel = 9;

    // 使用游戏自身的 LevelUp 流程升级，避免只改 level 字段而遗漏内部状态。
    internal static int MaximizeAffixes(SaveItemData saveItemData)
    {
        if (saveItemData?.affixList == null||
            Plugin.configEquipmentSuffixQuality.Value == false)
            return 0;

        var changed = 0;
        //限制词条等级不超过装备等级的十分之一，避免出现不合理的高词条。
        var acceptLevel = Plugin.configEquipmentSuffixQuality.Value ? MaximumAffixLevel : saveItemData.GetBaseAffixLevelCap();
        for (var i = 0; i < saveItemData.affixList.Count; i++)
        {
            var affix = saveItemData.affixList[i];
            if (affix == null)
                continue;

            var originalLevel = affix.level;

            while (affix.level < acceptLevel)
                affix.LevelUp();

            if (affix.level != originalLevel)
                changed++;
        }

        return changed;
    }
}

// 游戏初始化装备箱随机池后，仅将等级池替换成当前区间的合法最高值。已弃用，改为在 SaveItemData 初始化时直接提升词条等级。
[HarmonyPatch(typeof(ItemToolData), "InitEquipBoxWeight")]
internal static class MaximizeEquipmentBoxLevelPatch
{
    private static void Postfix(ItemToolData __instance)
    {
        var boxLevelData = __instance?.tBoxLevelData;
        if (boxLevelData == null)
            return;

        // 每档宝箱都有独立的等级区间，仅取当前区间的上限，而非全局最高等级。
        var maximumLevel = boxLevelData.maxEquipLevel;
        var maximumLevelOnly = new WeightRandom<int>();
        maximumLevelOnly.AddEntry(maximumLevel, 1);
        __instance.cachedLevelWeight = maximumLevelOnly;

        Plugin.Log.LogDebug(
            $"Equipment box level roll restricted to its legal maximum: {maximumLevel}.");
    }
}

// 新装备生成完成后，把该装备的所有词条等级提升到最高。
[HarmonyPatch(typeof(SaveItemData), nameof(SaveItemData.InitEquip))]
internal static class MaximizeNewEquipmentAffixesPatch
{
    private static void Postfix(SaveItemData __instance)
    {
        if (Plugin.configEquipmentNewSuffixQuality.Value == false) return;

        var changed = EquipmentTweaks.MaximizeAffixes(__instance);

        if (changed > 0)
        {
            Plugin.Log.LogDebug(
                $"Maxed {changed} affix(es) to level {EquipmentTweaks.MaximumAffixLevel} " +
                $"for equipment id={__instance.id}, level={__instance.level}.");
        }
    }
}

// 已有装备被载入运行时数据前同样处理，兼容旧存档中的装备。
[HarmonyPatch(typeof(ItemEquipData), nameof(ItemEquipData.Init))]
internal static class MaximizeLoadedEquipmentAffixesPatch
{
    private static void Prefix(ItemEquipData __instance)
    {
        if (Plugin.configEquipmentExistSuffixQuality.Value == false) return;

        var saveItemData = __instance?.itemData?.saveItemData;
        var changed = EquipmentTweaks.MaximizeAffixes(saveItemData);

        if (changed > 0)
        {
            Plugin.Log.LogDebug(
                $"Maxed {changed} existing affix(es) to level {EquipmentTweaks.MaximumAffixLevel} " +
                $"for equipment id={saveItemData.id}, level={saveItemData.level}.");
        }
    }
}
