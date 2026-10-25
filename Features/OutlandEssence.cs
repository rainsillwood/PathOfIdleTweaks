using HarmonyLib;
using Il2CppSystem.Security.Cryptography;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace PathOfIdleTweaks.Features;

// 异界精华（TTool 7160101）使用效果：
//   3.0 按"最高等级 + 无房屋加成"预算一张表：从普通升到各品质的期望血肉（累计值）
//   3.1 使用时：
//       3.1.1 已是神话(5) → 提示"已是最高品质"并跳过
//       3.1.2 计算当前等级+品质下每次扭曲的血肉 cost
//       3.1.3 扣血肉、写 costBlood（神选者存档累计）
//       3.1.4 rate = 100 * costBlood / 表[下一品质]，执行 hero.ChangeQuality(rate)
//       成功后清空 costBlood（否则下一次会直接 100%）
internal static class OutlandEssence
{
    // ── 自建物品 / 配方的实例定义（类型定义见 ItemRegister / RecipeRegister）──────
    // 实际定义（isEnable 由 configItemRegisterEnabled 决定；mergeArrArr 注册时补模板）
    internal static readonly NewTool Tool = new()
    {
        id = 7160101,
        name = "古神之眼",
        type = 2,   // runeBox 型：走 ItemToolData.UseTool 流程，Prefix 直接接管
        typeParamArr = null,
        index = 1,
        quality = 4,
        des = "可以对当前神选者发动一次扭曲。",
        source = "curio_301",   // 图标源：静态初始化阶段不能读 TableData，注册时再兜底
        rotate = 330,
        isEnable = false
    };

    internal static readonly NewRecipe Recipe = new()
    {
        id = 7160001,
        itemType = 5,
        itemId = 301,
        mergeCount = 10,
        mergePrice = 10000,
        mergeArrArr = null,   // 注册时填入模板 23 的数组（自建 int[,] 会崩）
        des = "{0}概率：{1}%",
        productId = 7160101,
        isEnable = false
    };

    private const int AttrPriceDown = 10241;
    // 冷却改为"同帧去重"：允许连续快速点击，但同一帧内的重复触发只算一次。

    private static int lastUseFrame = -1;
    private static int lastUseItemId = -1;

    // 索引 = 目标品质；值 = 从 1(普通) 升到该品质的累计期望血肉
    private static readonly long[] ExpectedBlood = new long[16];
    private static bool tableReady;
    private static int tableMaxLevel = -1;

    internal static bool IsOurTool(ItemData? itemData) =>
        itemData?.saveItemData != null && itemData.saveItemData.id == Tool.id;

    // ── ② 推送定义：按配置设置 isEnable，并把定义放进 Register 的列表 ──
    // 注意：此时 TableData 还没 init（见 RegisterManager 的执行顺序），所以不读模板/图标，
    //       也不 Clear 列表——清空由 RegisterManager.Init() 负责，其它模块可继续追加。
    internal static void DefineItems()
    {
        var enabled = Plugin.configItemRegisterEnabled.Value;

        Tool.isEnable = enabled;
        if (!ItemRegister.Tools.Exists(t => t.id == Tool.id))
            ItemRegister.Tools.Add(Tool);

        Recipe.productId = Tool.id;
        Recipe.isEnable = enabled;
        if (!RecipeRegister.Recipes.Exists(r => r.id == Recipe.id))
            RecipeRegister.Recipes.Add(Recipe);
    }



    internal static bool IsToolEnabled(int toolId) =>
        ItemRegister.Tools.Exists(t => t.id == toolId && t.isEnable);

    internal static void Use(ItemData? toolItemData, HeroData? hero)
    {
        if (!IsToolEnabled(Tool.id))
            return;

        var itemId = toolItemData?.saveItemData?.id ?? 0;
        var frame = UnityEngine.Time.frameCount;
        if (frame == lastUseFrame && lastUseItemId == itemId)
            return;
        lastUseFrame = frame;
        lastUseItemId = itemId;

        var save = hero?.saveHeroData;
        if (hero == null || save == null ||  (toolItemData?.saveItemData?.count ?? 0) <= 0)
            return;

        var townData = Game.dataMgr?.nowSeasonData?.townData;
        var prison = GetPrisonHouse();
        if (townData == null)
        {
            Plugin.Log.LogWarning("[OutlandEssence] 找不到城镇数据");
            return;
        }

        // 3.1.1 品质上限
        var current = save.quality;
        var next = current + 1;
        if (current >= 5)
        {
            Tip("已是最高品质");
            Plugin.Log.LogInfo("[OutlandEssence] 当前已是神话(5)，不执行");
            return;
        }

        // 3.1.2 每次扭曲的血肉 cost（当前等级 + 当前品质）
        var cost = GetDistortPrice(prison, hero);
        if (cost < 0)
            cost = 0;
        var bloodNow = townData.GetRes(EResType.blood);
        if (bloodNow < cost)
        {
            Tip($"血肉不足（需要 {cost}，现有 {bloodNow}）");
            Plugin.Log.LogWarning($"[OutlandEssence] 血肉不足：需要 {cost}，现有 {bloodNow}，不执行逻辑");
            return;
        }

        // 3.1.3 扣血肉 + 累计 costBlood
        if (cost > 0)
            townData.AddRes(EResType.blood, -cost);
        save.costBlood += cost;

        // 3.1.4 rate 与升品
        EnsureTable();
        // rate = 100 × (costBlood − Expected[now]) / (Expected[next] − Expected[now])
        var nowExpect = ExpectedBlood[Mathf.Clamp(current, 0, ExpectedBlood.Length - 1)];
        var nextExpect = ExpectedBlood[Mathf.Clamp(next, 0, ExpectedBlood.Length - 1)];
        var span = nextExpect - nowExpect;
        var rate = span <= 0
            ? 100f
            : (float)(100.0 * (save.costBlood - nowExpect) / span);
        Plugin.Log.LogDebug($"[OutlandEssence] 进度：costBlood={save.costBlood} 区间=[{nowExpect},{nextExpect}] 跨度={span}");
        // 下限 = 无房屋加成时原生当前品质的升品概率（THeroQuality[current].distortQualityUpRate）
        float minRate = 0f;
        try
        {
            var qualityRow = TableData.getTHeroQualityData(current);
            if (qualityRow != null)
                minRate = Mathf.Clamp(qualityRow.distortQualityUpRate, 0f, 100f);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogDebug($"[OutlandEssence] 读取原生成功率失败：{exception.Message}");
        }
        if (rate < minRate)
        {
            rate = minRate;
            Plugin.Log.LogInfo($"[OutlandEssence] rate 取原生下限 {minRate:0.##}%");
        }

        if (rate > 100f) rate = 100f;
        if (rate < 0f) rate = 0f;

        var before = save.quality;
        var levelBefore = save.level;
        hero.ChangeQuality(rate);

        Plugin.Log.LogInfo($"[OutlandEssence] cost={cost} costBlood={save.costBlood} 区间=[{nowExpect},{nextExpect}] rate={rate:0.##}% " +
                           $"品质 {before} → {save.quality}（等级 {levelBefore} → {save.level}）");

        // costBlood 永不清零、也不扣除：它表示「从普通一路投入的总量」。
        if (save.quality < before)
        {
            // ChangeQuality 失败会降级（原生行为）：把等级补回来，累计保留以便下次更容易成功
            if (save.level < levelBefore)
                save.level = levelBefore;
            Plugin.Log.LogWarning($"[OutlandEssence] 升品失败并降级：{before} → {save.quality}");
        }

        ConsumeOne(toolItemData);
        NativeSeasonData.isSave = true;
        try
        {
            // 刷新英雄面板（品质/属性变化）
            Game.eventMgr?.sendEvent(EEvent.updateHeroField, hero);
            Game.eventMgr?.sendEvent(EEvent.lordHeroChange, hero);
        }
        catch (Exception exception) { Plugin.Log.LogDebug($"[OutlandEssence] 刷新英雄界面失败：{exception.Message}"); }
    }

    // ── 3.0 期望血肉表（最高等级、无房屋加成）───────────────────────────────
    private static void EnsureTable()
    {
        if (tableReady)
            return;
        tableReady = true;
        try
        {
            var levels = new List<int>();
            foreach (var pair in THeroLevel.create())
                levels.Add(pair.Key);
            levels.Sort();
            if (levels.Count == 0)
                return;
            tableMaxLevel = levels[levels.Count - 1];

            var levelRow = TableData.getTHeroLevelData(tableMaxLevel);
            if (levelRow == null)
                return;
            var basePrice = levelRow.distortPrice;

            double cumulative = 0;
            for (var q = 1; q <= 5; q++)
            {
                if (q > 1)
                {
                    // 从 q-1 升到 q 的期望 = price(q-1) / p(q-1)
                    var prev = TableData.getTHeroQualityData(q - 1);
                    if (prev == null)
                        break;
                    var price = Math.Round(basePrice * prev.distortPriceRate);
                    var p = Math.Clamp(prev.distortQualityUpRate, 0f, 100f) / 100f;
                    if (p <= 0.0)
                    {
                        ExpectedBlood[q] = long.MaxValue;
                        cumulative = double.PositiveInfinity;
                        continue;
                    }
                    cumulative += price / p;
                }
                ExpectedBlood[q] = cumulative >= long.MaxValue ? long.MaxValue : (long)Math.Round(cumulative);
            }

            // ── 校准倍率：让"普通→神话"的总期望与原生一致 ──
            // 我们的逻辑：成功率随投入线性从 p0 涨到 100%，成功即停；
            // 该模型下单档期望花费 ≈ sqrt(π·c·E/2)（c=单次费用，E=表值）。
            // 令 Σ sqrt(π·c_q·k·E_q/2) = Σ E_q  →  k = (ΣE / Σ sqrt(π·c_q·E_q/2))²
            var rawTotal = 0.0;
            var modelTotal = 0.0;
            for (var q = 2; q <= 5; q++)
            {
                if (ExpectedBlood[q] <= 0 || ExpectedBlood[q] == long.MaxValue)
                    continue;
                rawTotal += ExpectedBlood[q];
                var prev = TableData.getTHeroQualityData(q - 1);
                var stepCost = prev != null ? Math.Round(basePrice * prev.distortPriceRate) : 1.0;
                if (stepCost <= 0) stepCost = 1.0;
                modelTotal += Math.Sqrt(Math.PI * stepCost * ExpectedBlood[q] / 2.0);
            }
            // 固定倍率：直接取配置值（默认 3.0），不再自动校准。
            var modelFactor = modelTotal > 0 ? Math.Pow(rawTotal / modelTotal, 2.0) : 1.0;
            var multiplier = Mathf.Max(0.01f, Plugin.configOutlandExpectMultiplier.Value);

            var rawParts = new List<string>();
            for (var q = 1; q <= 5; q++)
                rawParts.Add($"{q}={ExpectedBlood[q]}");

            for (var q = 2; q <= 5; q++)
            {
                if (ExpectedBlood[q] <= 0 || ExpectedBlood[q] == long.MaxValue)
                    continue;
                ExpectedBlood[q] = Math.Max(1, RoundSignificant((long)Math.Round(ExpectedBlood[q] * multiplier), 3));
            }
            // 第 1 档保持 0（从普通起步）
            ExpectedBlood[1] = 0;

            var scaledParts = new List<string>();
            for (var q = 1; q <= 5; q++)
                scaledParts.Add($"{q}={ExpectedBlood[q]}");
            Plugin.Log.LogInfo($"[OutlandEssence] 期望血肉表原始（L{tableMaxLevel}、无加成）：{string.Join(" ", rawParts)}");
            Plugin.Log.LogInfo($"[OutlandEssence] 倍率={multiplier:0.###}（模型参考值 {modelFactor:0.###}，仅供对照）");
            Plugin.Log.LogInfo($"[OutlandEssence] 期望血肉表（3 位有效数字）：{string.Join(" ", scaledParts)}");
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"[OutlandEssence] 计算期望表失败：{exception.Message}");
        }
    }

    // ── 当前等级/品质下的一次扭曲价格 ──
    private static int GetDistortPrice(HousePrisonData? prison, HeroData? hero)
    {
        try
        {
            if (prison != null && hero != null)
            {
                var distort = PriDistortData.Create(prison);
                if (distort != null)
                {
                    var level = hero.tHeroLevelData;
                    var quality = hero.tHeroQualityData;
                    if (level != null && quality != null)
                    {
                        var basePrice = Math.Round(level.distortPrice * quality.distortPriceRate);
                        var priceDown = GetAttr(prison, AttrPriceDown);
                        return (int)Math.Round((100f - priceDown) * basePrice / 100f);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.LogDebug($"[OutlandEssence] 计算价格失败：{exception.Message}");
        }
        return 0;
    }


    // 顶部提示：Game.uiMgr.ShowTip(...)
    internal static void Tip(string text)
    {
        try { Game.uiMgr?.ShowTip(text); }
        catch (Exception exception) { Plugin.Log.LogDebug("[OutlandEssence] ShowTip 失败：" + exception.Message); }
    }


    // 保留指定位数的有效数字（例如 3 位：17660 → 17700，105960 → 106000）
    private static long RoundSignificant(long value, int digits)
    {
        if (value <= 0)
            return 0;
        var magnitude = (long)Math.Pow(10, Math.Floor(Math.Log10(value)) - digits + 1);
        if (magnitude <= 1)
            return value;
        return (long)Math.Round((double)value / magnitude) * magnitude;
    }

    private static HousePrisonData? GetPrisonHouse()
    {
        var town = Game.dataMgr?.nowSeasonData?.townData;
        if (town?.houseDic == null || !town.houseDic.ContainsKey(EHouseType.Prison))
            return null;
        return town.houseDic[EHouseType.Prison]?.housePrisonData;
    }

    private static float GetAttr(HousePrisonData? prison, int attrId)
    {
        try
        {
            return prison?.houseData?.houseAttrData?.GetAttrValue((EHouseAttrType)attrId, null) ?? 0f;
        }
        catch
        {
            return 0f;
        }
    }

    // 从背包/钱包的格子里扣 1 个；数量归零则移除该格子。
    private static void ConsumeOne(ItemData? itemData)
    {
        try
        {
            var lordData = Game.dataMgr?.nowSeasonData?.lordData;
            var field = FindField(lordData?.lordBagData?.fieldList)
                        ?? FindField(lordData?.lordWalletData?.fieldList);
            var save = field?.itemData?.saveItemData ?? itemData?.saveItemData;
            if (save == null)
                return;
            // 同时改传入实例，避免界面读的是另一个包装
            if (itemData?.saveItemData != null && !ReferenceEquals(itemData.saveItemData, save) && itemData.saveItemData.count > 0)
                itemData.saveItemData.count -= 1;
            if (save.count > 1)
            {
                save.count -= 1;
                try { Game.eventMgr?.sendEvent(EEvent.itemFieldUpdata, field); }
                catch (Exception exception) { Plugin.Log.LogDebug($"[OutlandEssence] 刷新物品栏失败：{exception.Message}"); }
                return;
            }
            save.count = 0;
            if (field != null)
            {
                ItemSys.RemoveItemFromField(field);
                Plugin.Log.LogDebug("[OutlandEssence] 道具用尽，已从格子移除");
                try { Game.eventMgr?.sendEvent(EEvent.itemFieldUpdata, field); }
                catch (Exception exception) { Plugin.Log.LogDebug($"[OutlandEssence] 刷新物品栏失败：{exception.Message}"); }
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"[OutlandEssence] 扣除道具失败：{exception.Message}");
        }
    }


    // 在格子列表里找到本物品（id 匹配且数量 > 0）的第一个格子。
    private static ItemFieldData? FindField(Il2CppSystem.Collections.Generic.List<ItemFieldData>? fields)
    {
        if (fields == null)
            return null;
        for (var i = 0; i < fields.Count; i++)
        {
            var save = fields[i]?.itemData?.saveItemData;
            if (save != null && save.id == Tool.id && save.count > 0)
                return fields[i];
        }
        return null;
    }
}


// 直接从背包使用（不打开任何使用界面）：拦截 UseTool，返回空奖励列表。
[HarmonyPatch(typeof(ItemToolData), nameof(ItemToolData.UseTool))]
internal static class OutlandEssenceUseToolPatch
{
    private static bool Prefix(ItemToolData __instance, ref Il2CppSystem.Collections.Generic.List<ItemData>? __result)
    {
        var toolItemData = __instance?.itemData;
        Plugin.Log.LogInfo($"[OutlandEssence] UseTool 入口 id={toolItemData?.saveItemData?.id ?? -1}");
        if (!OutlandEssence.IsOurTool(toolItemData))
            return true;
        var hero = Game.dataMgr?.nowSeasonData?.lordData?.nowHeroData;
        OutlandEssence.Use(toolItemData, hero);
        __result = new Il2CppSystem.Collections.Generic.List<ItemData>();
        return false;
    }
}


// type=2（runeBox）时，数量 > 100 会通过 UIMgr.openLayer(TLayer.UseRuneLayer, …) 打开
// 「批量使用」界面，确认后走 LordBagData.UseToolCount → 原生符石袋效果。
// 在开层入口拦截：识别到本物品就直接执行效果，不打开该界面。
[HarmonyPatch(typeof(UIMgr), nameof(UIMgr.openLayer))]
internal static class OutlandEssenceBlockRuneLayerPatch
{
    private static bool Prefix(string layerTag, BaseData data)
    {
        if (layerTag != TLayer.UseRuneLayer)
            return true;
        var itemData = data?.TryCast<ItemData>();
        if (!OutlandEssence.IsOurTool(itemData))
            return true;
        var hero = Game.dataMgr?.nowSeasonData?.lordData?.nowHeroData;
        OutlandEssence.Use(itemData, hero);
        return false;   // 不打开批量使用界面
    }
}

// 兜底：万一界面被其它路径打开，确认时也走我们的逻辑，不执行原生符石袋。
[HarmonyPatch(typeof(LordBagData), nameof(LordBagData.UseToolCount))]
internal static class OutlandEssenceUseCountPatch
{
    private static bool Prefix(ItemData itemData, int count, ref Il2CppSystem.Collections.Generic.List<ItemData>? __result)
    {
        if (!OutlandEssence.IsOurTool(itemData))
            return true;
        var hero = Game.dataMgr?.nowSeasonData?.lordData?.nowHeroData;
        OutlandEssence.Use(itemData, hero);
        __result = new Il2CppSystem.Collections.Generic.List<ItemData>();
        return false;
    }
}

// 右键长按连发：原生长按在 TryUseTool 被接管（UseToolCount 返回空列表）后只会触发 1~2 次，
// 所以连发由我们自己驱动；单击/首次使用仍走原生链路（UseToolCount 内执行我们的效果）。
internal static class OutlandHoldState
{
    internal const float HoldDelay = 0.45f;    // 按住多久后开始连发
    internal const float HoldInterval = 0.15f; // 连发间隔

    internal static int HoverCellId = -1;
    internal static float NextUseTime;
}

[HarmonyPatch(typeof(ToolCell), nameof(ToolCell.OnPointerEnter))]
internal static class OutlandEssenceHoverEnterPatch
{
    private static void Postfix(ToolCell __instance)
    {
        if (OutlandEssence.IsOurTool(__instance?.itemData))
            OutlandHoldState.HoverCellId = __instance!.GetInstanceID();
    }
}

[HarmonyPatch(typeof(ToolCell), nameof(ToolCell.OnPointerExit))]
internal static class OutlandEssenceHoverExitPatch
{
    private static void Postfix(ToolCell __instance)
    {
        if (OutlandEssence.IsOurTool(__instance?.itemData))
            OutlandHoldState.HoverCellId = -1;
    }
}

[HarmonyPatch(typeof(ToolCell), nameof(ToolCell.Update))]
internal static class OutlandEssenceHoldPatch
{
    private static void Postfix(ToolCell __instance)
    {
        var itemData = __instance?.itemData;
        if (!OutlandEssence.IsOurTool(itemData))
            return;
        if (OutlandHoldState.HoverCellId != __instance!.GetInstanceID())
            return;

        // 右键未按住：清空计时，下次按下重新计时
        if (!UnityEngine.Input.GetMouseButton(1))
        {
            OutlandHoldState.NextUseTime = 0f;
            return;
        }

        var now = UnityEngine.Time.realtimeSinceStartup;
        if (OutlandHoldState.NextUseTime <= 0f)
        {
            OutlandHoldState.NextUseTime = now + OutlandHoldState.HoldDelay;
            return;
        }
        if (now < OutlandHoldState.NextUseTime)
            return;

        OutlandHoldState.NextUseTime = now + OutlandHoldState.HoldInterval;
        var hero = Game.dataMgr?.nowSeasonData?.lordData?.nowHeroData;
        OutlandEssence.Use(itemData, hero);
    }
}
