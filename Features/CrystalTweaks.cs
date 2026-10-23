using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using HarmonyLib;

namespace PathOfIdleTweaks.Features;

// ── 血肉结晶保底升品 ────────────────────────────────────────────────────────
// 需求：每使用一次血肉结晶，给「这次被使用的那位神选者」记一次使用次数；
//       累计达到阈值（默认 100）后，调用游戏原生的品质变更流程升一品，并清空该角色的计数。
//
// 原生链路（1.1.5）：
//   · 使用血肉结晶 = UseCrystalLayer.OnClickUse（:1956269）
//       → 取 lordData.nowHeroData（当前选中的人物）
//       → HeroData.RerollBaseAttrThree()（:1535788，重掷三项主属性）
//       → LordBagData.UseTool(itemData) 扣掉结晶
//     RerollBaseAttrThree 在整个工程里**只有这一个调用者**，所以用它的 Postfix 计数最稳：
//     每触发一次 = 消耗一颗血肉结晶，且能直接拿到被使用的 HeroData。
//   · 升品 = HeroData.ChangeQuality(upRate)（原生品质变更：重算主属性/成长/品质表数据）。
//     监牢扭曲 PriDistortData.DistortHero（:1933779）里走的就是
//     `HeroData.ChangeQuality(THeroQuality[quality].distortQualityUpRate)`，
//     这里作为保底奖励传 100f（必定成功），并做「品质必须+1」的收敛校验。
//
// 存储说明（重要）：
//   SaveHeroData 里没有任何空闲字段可复用 —— costBlood 被原生扭曲累加（:1933927/:1934332），
//   changeSanityCount 被理智逻辑占用（:2223368/:2224328），其余字段都有明确语义。
//   因此使用次数保存在插件自己的持久化文件里，按 saveHeroData.uniqueId 区分角色：
//     BepInEx/config/PathOfIdleTweaks/CrystalUseCounts.txt
internal static class CrystalTweaks
{
    // 品质变更后通知界面刷新的原生事件 id（与旧版监牢扭曲补丁一致）。
    private const int HeroChangedEventId = 130;

    private static int maxHeroQualityId = -1;

    internal static void OnCrystalUsed(HeroData hero)
    {
        if (!Plugin.configCrystalPityEnabled.Value)
            return;

        var saveHeroData = hero?.saveHeroData;
        if (saveHeroData == null)
            return;

        var uniqueId = saveHeroData.uniqueId;
        var threshold = Math.Max(1, Plugin.configCrystalPityThreshold.Value);
        var count = CrystalUseCounter.Get(uniqueId) + 1;

        if (count < threshold)
        {
            CrystalUseCounter.Set(uniqueId, count);
            Plugin.Log.LogDebug($"[Crystal] 神选者 {uniqueId} 使用次数 {count}/{threshold}");
            return;
        }

        // 已达阈值：冒险中不动物品数据，保留计数等下一次触发。
        if (hero!.IsAdventureBusy())
        {
            CrystalUseCounter.Set(uniqueId, count);
            Plugin.Log.LogInfo($"[Crystal] 神选者 {uniqueId} 已达 {count}/{threshold}，但正在冒险，等下次再升品。");
            return;
        }

        var before = saveHeroData.quality;
        var maxQuality = GetMaxHeroQualityId();
        if (before >= maxQuality)
        {
            CrystalUseCounter.Set(uniqueId, 0);
            Plugin.Log.LogInfo($"[Crystal] 神选者 {uniqueId} 已是最高品质 {before}，清空使用次数。");
            return;
        }

        // 原生品质变更：ChangeQuality(100f) 必定成功，并同步重算属性。
        // 循环兜底：万一某次调用没有真的提升品质，再试（最多 32 次）。
        var guard = 0;
        while (saveHeroData.quality <= before && guard++ < 32)
            hero.ChangeQuality(100f);

        // 品质变了要通知界面重新读数据（原生扭曲走完也是发这个事件）。
        try
        {
            Game.eventMgr?.sendEvent((EEvent)HeroChangedEventId, hero);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogDebug($"[Crystal] 发送品质变更事件失败：{exception.Message}");
        }

        if (saveHeroData.quality > before)
        {
            CrystalUseCounter.Set(uniqueId, 0);
            Plugin.Log.LogInfo($"[Crystal] 神选者 {uniqueId} 保底升品成功：品质 {before} → {saveHeroData.quality}，使用次数已清零。");
        }
        else
        {
            CrystalUseCounter.Set(uniqueId, count);
            Plugin.Log.LogWarning($"[Crystal] 神选者 {uniqueId} 保底升品失败（品质仍为 {saveHeroData.quality}），保留使用次数。");
        }
    }

    // 英雄品质表的最高 id（THeroQuality 的最大 key）。
    internal static int GetMaxHeroQualityId()
    {
        if (maxHeroQualityId > 0)
            return maxHeroQualityId;

        var max = 0;
        foreach (var pair in THeroQuality.create())
            if (pair.Key > max)
                max = pair.Key;

        maxHeroQualityId = max;
        return max;
    }
}

// 使用次数持久化：uniqueId=count 每行一条，放在 BepInEx/config/PathOfIdleTweaks/ 下。
internal static class CrystalUseCounter
{
    private static Dictionary<int, int>? counts;

    private static string StorageDirectory => Path.Combine(Paths.ConfigPath, "PathOfIdleTweaks");

    private static string StorageFile => Path.Combine(StorageDirectory, "CrystalUseCounts.txt");

    internal static int Get(int uniqueId)
    {
        EnsureLoaded();
        return counts!.TryGetValue(uniqueId, out var value) ? value : 0;
    }

    internal static void Set(int uniqueId, int value)
    {
        EnsureLoaded();
        if (value <= 0)
            counts!.Remove(uniqueId);
        else
            counts![uniqueId] = value;
        Save();
    }

    private static void EnsureLoaded()
    {
        if (counts != null)
            return;

        counts = new Dictionary<int, int>();
        try
        {
            if (!File.Exists(StorageFile))
                return;

            foreach (var line in File.ReadAllLines(StorageFile))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                    continue;

                var parts = trimmed.Split('=');
                if (parts.Length != 2)
                    continue;

                if (int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var uniqueId) &&
                    int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                {
                    counts[uniqueId] = count;
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"[Crystal] 读取使用次数失败：{exception.Message}");
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(StorageDirectory);
            var lines = new List<string> { "# 血肉结晶使用次数：uniqueId=count（由 PathOfIdleTweaks 维护）" };
            foreach (var pair in counts!)
                lines.Add($"{pair.Key}={pair.Value}");
            File.WriteAllLines(StorageFile, lines);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"[Crystal] 保存使用次数失败：{exception.Message}");
        }
    }
}

// 计数钩子：HeroData.RerollBaseAttrThree 只由「使用血肉结晶」触发（UseCrystalLayer.OnClickUse）。
[HarmonyPatch(typeof(HeroData), nameof(HeroData.RerollBaseAttrThree))]
internal static class TrackCrystalUsePatch
{
    private static void Postfix(HeroData __instance)
    {
        CrystalTweaks.OnCrystalUsed(__instance);
    }
}
