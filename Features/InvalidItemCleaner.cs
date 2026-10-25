using System;
using System.Collections.Generic;
using HarmonyLib;

namespace PathOfIdleTweaks.Features;

// 存档非法物品清理（强制启用，无开关）：
//   1) 表里查不到的物品（例如功能关闭后残留的异界精华）—— 载入时会让原生流程崩溃；
//   2) 定义存在但 isEnable == false 的自建物品/配方产物 —— 功能关闭时从存档里删掉。
internal static class InvalidItemCleaner
{
    internal static void Cleanup(string stage)
    {
        var removed = 0;
        try
        {
            var lordData = Game.dataMgr?.nowSeasonData?.lordData;
            removed += CleanFieldList(lordData?.lordBagData?.fieldList, stage + "-背包", null);
            removed += CleanFieldList(lordData?.lordWalletData?.fieldList, stage + "-钱包", null);

            var townData = Game.dataMgr?.nowSeasonData?.townData;
            var houseDic = townData?.houseDic;
            if (houseDic != null && houseDic.ContainsKey(EHouseType.Secret))
                removed += CleanFieldList(houseDic[EHouseType.Secret]?.houseSecretData?.fieldList, stage + "-秘所", null);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"[Cleaner] {stage} 清理失败：{exception.Message}");
        }

        if (removed > 0)
            Plugin.Log.LogWarning($"[Cleaner] {stage}：删除了 {removed} 件表里不存在的非法物品");
    }

    // 强制清理：把「定义里 isEnable == false」的物品从存档中删除。
    internal static void CleanupDisabled(string stage)
    {
        var disabled = new HashSet<int>();
        foreach (var tool in ItemRegister.Tools)
        {
            if (!tool.isEnable)
                disabled.Add(tool.id);
        }
        foreach (var curio in ItemRegister.Curios)
        {
            if (!curio.isEnable)
                disabled.Add(curio.id);
        }
        foreach (var recipe in RecipeRegister.Recipes)
        {
            if (!recipe.isEnable && recipe.productId != 0)
                disabled.Add(recipe.productId);
        }

        if (disabled.Count == 0)
            return;

        var removed = 0;
        try
        {
            var lordData = Game.dataMgr?.nowSeasonData?.lordData;
            removed += CleanFieldList(lordData?.lordBagData?.fieldList, stage + "-背包", disabled);
            removed += CleanFieldList(lordData?.lordWalletData?.fieldList, stage + "-钱包", disabled);

            var townData = Game.dataMgr?.nowSeasonData?.townData;
            var houseDic = townData?.houseDic;
            if (houseDic != null && houseDic.ContainsKey(EHouseType.Secret))
                removed += CleanFieldList(houseDic[EHouseType.Secret]?.houseSecretData?.fieldList, stage + "-秘所", disabled);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"[Cleaner] {stage} 清理禁用物品失败：{exception.Message}");
        }

        if (removed > 0)
            Plugin.Log.LogWarning($"[Cleaner] {stage}：功能已关闭，删除了 {removed} 件自建物品（id: {string.Join(",", disabled)}）");
    }

    private static int CleanFieldList(Il2CppSystem.Collections.Generic.List<ItemFieldData>? fields, string where,
        HashSet<int>? disabledIds)
    {
        if (fields == null)
            return 0;
        var removed = 0;
        // 倒序删除，避免索引错位。
        for (var i = fields.Count - 1; i >= 0; i--)
        {
            var field = fields[i];
            var saveItemData = field?.itemData?.saveItemData;
            if (saveItemData == null)
                continue;

            var shouldRemove = disabledIds != null
                ? disabledIds.Contains(saveItemData.id)
                : !IsValid(saveItemData);
            if (!shouldRemove)
                continue;

            Plugin.Log.LogWarning($"[Cleaner] {where}：删除物品 id={saveItemData.id} type={saveItemData.type} count={saveItemData.count}");
            try
            {
                ItemSys.RemoveItemFromField(field!);
                removed++;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning($"[Cleaner] {where}：删除 id={saveItemData.id} 失败：{exception.Message}");
            }
        }
        return removed;
    }

// 诊断：打印背包 / 钱包 / 秘所 里每格物品的 id 与数量。
    internal static void DumpIds(string stage)
    {
        try
        {
            var lordData = Game.dataMgr?.nowSeasonData?.lordData;
            var townData = Game.dataMgr?.nowSeasonData?.townData;
            var houseDic = townData?.houseDic;
            Plugin.Log.LogInfo($"[Cleaner] {stage} 背包：{Describe(lordData?.lordBagData?.fieldList)}");
            Plugin.Log.LogInfo($"[Cleaner] {stage} 钱包：{Describe(lordData?.lordWalletData?.fieldList)}");
            if (houseDic != null && houseDic.ContainsKey(EHouseType.Secret))
                Plugin.Log.LogInfo($"[Cleaner] {stage} 秘所：{Describe(houseDic[EHouseType.Secret]?.houseSecretData?.fieldList)}");
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"[Cleaner] {stage} 诊断失败：{exception.Message}");
        }
    }

    private static string Describe(Il2CppSystem.Collections.Generic.List<ItemFieldData>? fields)
    {
        if (fields == null)
            return "null";
        var text = new System.Text.StringBuilder();
        text.Append($"[{fields.Count}] ");
        for (var i = 0; i < fields.Count; i++)
        {
            var save = fields[i]?.itemData?.saveItemData;
            if (save == null)
                continue;
            text.Append($"{save.id}x{save.count} ");
        }
        return text.ToString();
    }

    private static bool IsValid(SaveItemData saveItemData)
    {
        switch (saveItemData.type)
        {
            case EItemType.equip:
                return TableData.TEquipDict != null && TableData.TEquipDict.ContainsKey(saveItemData.id);
            case EItemType.rune:
                return TableData.TRuneDict != null && TableData.TRuneDict.ContainsKey(saveItemData.id);
            case EItemType.curio:
                return TableData.TResDict != null && TableData.TResDict.ContainsKey(saveItemData.id);
            case EItemType.tool:
                return TableData.TToolDict != null && TableData.TToolDict.ContainsKey(saveItemData.id);
            default:
                return true;   // 资源类等不校验
        }
    }
}

// 载入存档结束后（LordData.GameInit 内部会依次构建背包与钱包）：
// 同步启用状态 → 删除禁用物品 → 清理非法物品。
// 注意：不能挂在 LordBagData.Init，因为钱包(LordWalletData.Init)可能在其后才构建，
// 那时扫描钱包还会是空的，导致自建物品漏删。
[HarmonyPatch(typeof(LordData), nameof(LordData.GameInit))]
internal static class InvalidItemCleanerPatch
{
    private static void Postfix()
    {
        InvalidItemCleaner.DumpIds("清理前");
        InvalidItemCleaner.CleanupDisabled("载入存档");
        InvalidItemCleaner.Cleanup("载入存档后");
        InvalidItemCleaner.DumpIds("清理后");
    }
}
