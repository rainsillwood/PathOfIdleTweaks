using HarmonyLib;
using System;

namespace PathOfIdleTweaks.Features;

/// 装备感知（Awareness）封印词条位 / 等级上限修改。
///
/// 设计要点：
///   补丁**不在** <see cref="Plugin.Load"/> 里无条件挂载，而是由 <see cref="Install"/> 按配置决定是否注入
///   （false 时一个跳板都不挂）。这一点是刻意的：Harmony 只要看到 [HarmonyPatch] 特性就会生成
///   native->managed 跳板，即使补丁体直接 return，反序列化早期（DMD&lt;CodexEquipData::Init&gt;）仍可能踩到。
///
/// 三种模式的分工：
///   Extra  —— 只改写 GetSealAwarenessCap 的返回值，按 16 阶为基准等比放大，不动任何存档字段。
///   Max    —— 只改写 GetSealAwarenessCap 的返回值，固定为目标上限，不动任何存档字段。
///   Origin —— 不改写可封词条位、也不改当前等级，只抬高「感知等级上限」，让玩家自己升；
///             超出 TSteleStageData 范围时用最高阶的经验阈值兜底。
///
/// 所有补丁体都用 try/catch 包裹：托管异常一旦抛回原生层就是 trampoline 崩溃 + 刷屏。
/// 配置只在启动时读取，修改后需重启游戏。
/// 
internal static class AwarenessTweaker
{
    // 原生等级上限。必须在类型初始化时固定：之后 Extra 模式拿它当等比基准，而 Origin 的
    // SyncGlobalCap 会改写 TConstant，不能现取现用。
    // TConstant 是 int（非可空），所以不能用 ??；这里按「读出来 <= 0 就兜底 16」处理 ——
    // 既是表里的默认值，也避免 Extra 模式下 `cap / (float)VanillaLevel` 变成 Inf/NaN
    // 再被 (int) 转成 int.MinValue。
    internal static int VanillaLevel = TConstant.awarenessMaxLevel > 0 ? TConstant.awarenessMaxLevel : 16;
    internal static int TargetLevel = Plugin.configEquipmentAwarenessMaxLevel.Value;
    internal static bool Enabled = Plugin.configEquipmentAwarenessTweaker.Value;
    internal static string Mode => Plugin.configEquipmentAwarenessType.Value ?? "Origin";
    internal static void SyncGlobalCap()
    {
        var target = TargetLevel;
        if (TConstant.awarenessMaxLevel < target)
            TConstant.awarenessMaxLevel = target;
    }

    // 阶段表最高 id。必须惰性求值：本类型的静态字段初始化可能早于 TableData.init，
    // 那一刻 TSteleStageDict 还是 null，静态字段会把 0 永久缓存下来。
    private static int _steleStageMaxId;

    internal static int SteleStageMaxId
    {
        get
        {
            if (_steleStageMaxId > 0)
                return _steleStageMaxId;
            var dict = TableData.TSteleStageDict;
            if (dict == null)
                return 0;                      // 表还没 init，本次放弃，不缓存
            foreach (var pair in dict)
                if (pair.Key > _steleStageMaxId)
                    _steleStageMaxId = pair.Key;
            return _steleStageMaxId;
        }
    }
}

//修改需求感知等级文案
[HarmonyPatch(typeof(CodexEquipData), nameof(CodexEquipData.GetNeedAwarenessLevelDes))]
internal static class GetNeedAwarenessLevelDesPatch
{
    private static void Postfix(CodexEquipData __instance, ref string __result)
    {
        if (AwarenessTweaker.Enabled)
        {
            __result = "可封印词条数：" + __instance.nowSealAffixCount + "/" + __instance.GetSealAwarenessCap();
        }
    }
}

// 模式为Extra/Max时,扩展可封印词条数量
[HarmonyPatch(typeof(CodexEquipData), nameof(CodexEquipData.GetSealAwarenessCap))]
internal static class GetSealAwarenessCapPatch
{
    private static void Postfix(ref int __result)
    {
        if (AwarenessTweaker.Enabled && AwarenessTweaker.Mode != "Origin")
        {
            if (AwarenessTweaker.Mode == "Extra")
            {
                // 按 16 阶为基准等比放大
                __result = (int)Math.Floor(__result / (float)AwarenessTweaker.VanillaLevel * AwarenessTweaker.TargetLevel);
            }
            else if (AwarenessTweaker.Mode == "Max")
            {
                // 固定为目标上限
                __result = AwarenessTweaker.TargetLevel;
            }
        }
    }
}

//模式为Origin时,扩展感知等级上限
[HarmonyPatch(typeof(CodexEquipData), nameof(CodexEquipData.GetMaxAwarenessLevel))]
internal static class GetMaxAwarenessLevelPatch
{
    private static void Postfix(ref int __result)
    {
        if (AwarenessTweaker.Enabled && AwarenessTweaker.Mode == "Origin")
        {
            __result = AwarenessTweaker.TargetLevel;
            AwarenessTweaker.SyncGlobalCap();
        }
    }
}

//扩展经验表：越界（id > 表内最高 id）时借用最高阶那一行，改成本次等级需要的阈值。
//  公式在最高阶及以后与表完全一致（f(17..20) = 39/41/42/44），所以最高阶那一行「重算即原值」：
//    · id == maxId 时不借行，直接把 __result 重算写回 —— 数值不变，顺带把上一次越界借用留下的残留修回原值；
//    · id > maxId 时才借 maxId 那一行，改成本次的值。借行后的那次重入 id == maxId，走上面那条分支，
//      只写一次就返回，不会自调用。
[HarmonyPatch(typeof(TableData), nameof(TableData.getTSteleStageData))]
internal static class getTSteleStageDataPatch
{
    internal static void Postfix(int id, ref TSteleStage __result)
    {
        try
        {
            if (!AwarenessTweaker.Enabled || AwarenessTweaker.Mode != "Origin")
                return;
            var maxId = AwarenessTweaker.SteleStageMaxId;
            if (maxId <= 0 || id < maxId)
                return;

            if (id == maxId)
            {
                // 自赋值：f(maxId) 就是表值，写回等于没写；但能把残留污染纠正回来。
                if (__result != null)
                    __result.maxCodexAwar = Threshold(id);
                return;
            }

            if (__result != null)
                return;                                  // 表比缓存还大，这一行是真的，别动
            __result = TableData.getTSteleStageData(maxId);
            if (__result == null)
                return;
            __result.maxCodexAwar = Threshold(id);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"[AwarenessTweaker] getTSteleStageData({id}) 兜底失败：{exception.Message}");
        }
    }

    // 17 阶起 maxCodexAwar = floor(38 + 1.5 × (id - 16))，与表数据逐级吻合。
    private static int Threshold(int id) => (int)Math.Floor(38 + 1.5 * (id - 16));
}