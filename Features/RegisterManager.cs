using HarmonyLib;

namespace PathOfIdleTweaks.Features;

// 注册总管：把所有「自建物品 / 配方模块」的注册动作收口到一处，统一执行顺序。
//
//   ① Init            —— 插件加载时最优先：清空各 Register 的列表
//   ② DefineModules   —— 插件加载时：各模块按配置设置 isEnable，并把定义推进 Register 的列表
//                        （无论开关都会推送；TableData 尚未 init，所以此时不碰表）
//   ③ TableData.init  —— 游戏原生：表数据就绪
//   ④ RegisterAll     —— LordData.GameInit 的 Prefix：补齐模板/图标来源，再真正写表
//                        （只注册 isEnable == true 的配方；物品行始终写入，避免旧存档崩溃）
//   ⑤ InvalidItemCleaner —— LordData.GameInit 的 Postfix：删除 isEnable == false 的物品
//
// 以后新增模块只需两步：在 Init 里加它的 Register.Init()，在 DefineModules 里调它的 DefineItems()。
internal static class RegisterManager
{
    // ① 初始化（最优先）
    internal static void Init()
    {
        ItemRegister.Init();
        RecipeRegister.Init();
    }

    // ② 各模块推送定义（含 isEnable 设置）
    internal static void DefineModules()
    {
        OutlandEssence.DefineItems();
        // 以后：其它模块的 DefineItems() 也在这里调用
    }

    // ④ 真正写入 TableData（isEnable 在插件启动时由 DefineItems 读配置决定，本局内固定）
    internal static void RegisterAll()
    {
        ItemRegister.RegisterAll();
        RecipeRegister.RegisterAll();
    }
}

// ④ 注册时机：载入存档开始时（钱包/背包在 LordData.GameInit 内部创建，此时表里必须已有 TTool 行）。
[HarmonyPatch(typeof(LordData), nameof(LordData.GameInit))]
internal static class RegisterManagerLoadPatch
{
    private static void Prefix() => RegisterManager.RegisterAll();
}
