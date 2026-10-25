using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace PathOfIdleTweaks.Features;
internal sealed class NewRecipe
{
    public int id;
    public int itemType;
    public int itemId;
    public int mergeCount;
    public int mergePrice;
    public Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? mergeArrArr;
    public string des = "";
    public int productId;
    public bool isEnable = false;
}

// 秘所配方：新行共享模板 id=23 的 mergeArrArr；Prefix 临时替换产物 id、Postfix 还原。
internal static class RecipeRegister
{
    internal const int TemplateId = 23;
    internal static readonly List<NewRecipe> Recipes = new();
    private static readonly List<(IntPtr Address, int Original)> Swapped = new();

    // ① 注册表初始化（TableData.init 之后）：只清空列表，条目由 OutlandEssence 在载入存档时推送。
    internal static void Init()
    {
        Recipes.Clear();
    }

    // ④ 同步：isEnable == true 的配方写入 TSecretMergeDict，isEnable == false 的从表里移除。
    //    每次都全量对比，不做 ready 短路，这样游戏内改配置后重载存档能立刻生效。
    internal static void RegisterAll()
    {
        var dict = TableData.TSecretMergeDict;
        if (dict == null || !dict.ContainsKey(TemplateId))
            return;
        try
        {
            foreach (var recipe in Recipes)
            {
                if (!recipe.isEnable)
                {
                    if (dict.ContainsKey(recipe.id))
                    {
                        dict.Remove(recipe.id);
                        Plugin.Log.LogInfo($"[RecipeRegister] 配方 {recipe.id} 已禁用，从表中移除");
                    }
                    continue;
                }
                recipe.mergeArrArr ??= dict[TemplateId].mergeArrArr;   // 模板数组：注册时补齐
                if (recipe.mergeArrArr == null)
                {
                    Plugin.Log.LogWarning($"[RecipeRegister] 配方 {recipe.id} 缺少模板数组，跳过");
                    continue;
                }
                if (dict.ContainsKey(recipe.id))
                    continue;
                var row = new TSecretMerge
                {
                    id = recipe.id,
                    itemType = recipe.itemType,
                    itemId = recipe.itemId,
                    mergeCount = recipe.mergeCount,
                    mergePrice = recipe.mergePrice,
                    mergeArrArr = recipe.mergeArrArr,   // 共享模板
                    des = recipe.des,
                    _des = "{0}概率：{1}%"
                };
                dict.Add(recipe.id, row);
                Plugin.Log.LogInfo($"[RecipeRegister] 新增配方 {recipe.id}：材料 {recipe.itemId} × {recipe.mergeCount}，共享模板 {TemplateId}");
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"[RecipeRegister] 注册失败：{exception.Message}");
        }
    }


    internal static bool isMixinRecipe(SecMergeData merge) =>
        merge?.tSecretMergeData != null && Recipes.Exists(r => r.id == merge.tSecretMergeData.id);

    // 按放入的材料 id 查找配方（秘所界面里靠材料反查）
    internal static NewRecipe? FindByMaterial(int materialId) => Recipes.Find(r => r.itemId == materialId);

    // 按配方行 id 查找（Prefix 里要拿对应的产物 id）
    internal static NewRecipe? FindById(int recipeId) => Recipes.Find(r => r.id == recipeId);

    // 把 int[,] 里每行第 0 列（产物 id）临时换成我们的，并记录原值。
    internal static void SwapToProduct(Il2CppObjectBase? arrObj, int productId)
    {
        Swapped.Clear();
        if (arrObj == null)
            return;
        try
        {
            var sys = arrObj.TryCast<Il2CppSystem.Array>();
            if (sys == null)
            {
                Plugin.Log.LogWarning("[RecipeRegister] mergeArrArr 不是 System.Array");
                return;
            }
            var rows = sys.GetLength(0);
            var cols = sys.GetLength(1);
            Plugin.Log.LogDebug($"[RecipeRegister] mergeArrArr dims=[{rows},{cols}] length={sys.Length}");

            var data = arrObj.Pointer + 0x20;   // 多维数组元素内联存放
            for (var i = 0; i < rows; i++)
            {
                var address = data + i * cols * 4;
                var original = Marshal.ReadInt32(address);
                Marshal.WriteInt32(address, productId);
                Swapped.Add((address, original));
            }
            Plugin.Log.LogDebug($"[RecipeRegister] 临时替换产物 id：{Swapped.Count} 行 → {productId}");
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"[RecipeRegister] 替换失败：{exception.Message}");
        }
    }

    internal static void RestoreProducts()
    {
        try
        {
            foreach (var item in Swapped)
                Marshal.WriteInt32(item.Address, item.Original);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogDebug($"[RecipeRegister] 还原失败：{exception.Message}");
        }
        finally
        {
            Swapped.Clear();
        }
    }
}

[HarmonyPatch(typeof(SecMergeData), nameof(SecMergeData.UpdateMergeData))]
internal static class RecipeSelectPatch
{
    private static void Postfix(SecMergeData __instance)
    {
        var fields = __instance.houseSecretData?.fieldList;
        if (fields == null || fields.Count == 0)
            return;
        var materialId = fields[0]?.itemData?.saveItemData?.id ?? 0;
        var recipe = RecipeRegister.FindByMaterial(materialId);
        if (recipe == null)
            return;
        var dict = TableData.TSecretMergeDict;
        if (dict != null && dict.ContainsKey(recipe.id))
            __instance.tSecretMergeData = dict[recipe.id];
    }
}

[HarmonyPatch(typeof(SecMergeData), nameof(SecMergeData.CheckItemError))]
internal static class RecipeCheckPatch
{
    private static void Postfix(SecMergeData __instance, ref int __result)
    {
        if (__result != 0)
            return;
        var fields = __instance.houseSecretData?.fieldList;
        if (fields == null || fields.Count == 0)
            return;
        if (RecipeRegister.FindByMaterial(fields[0]?.itemData?.saveItemData?.id ?? 0) != null)
            __result = 0;
    }
}

// 文案：临时换 id → 原生生成文案 → 还原。
[HarmonyPatch(typeof(SecMergeData), nameof(SecMergeData.GetRateDesc))]
internal static class RecipeRateDescPatch
{
    private static void Prefix(SecMergeData __instance)
    {
        if (!RecipeRegister.isMixinRecipe(__instance))
            return;
        var recipe = RecipeRegister.FindById(__instance.tSecretMergeData.id);
        if (recipe == null)
            return;
        RecipeRegister.SwapToProduct(__instance.tSecretMergeData.mergeArrArr, recipe.productId);
    }

    private static void Postfix() => RecipeRegister.RestoreProducts();
}

// 产出：临时换 id → 原生扣材料并按池产出 → 还原。
[HarmonyPatch(typeof(SecMergeData), nameof(SecMergeData.StartMergeItem))]
internal static class RecipeStartMergePatch
{
    private static void Prefix(SecMergeData __instance)
    {
        if (!RecipeRegister.isMixinRecipe(__instance))
            return;
        var recipe = RecipeRegister.FindById(__instance.tSecretMergeData.id);
        if (recipe == null)
            return;
        RecipeRegister.SwapToProduct(__instance.tSecretMergeData.mergeArrArr, recipe.productId);
    }

    private static void Postfix() => RecipeRegister.RestoreProducts();
}
