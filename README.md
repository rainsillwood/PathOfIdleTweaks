# PathOfIdleTweaks

《Path of Idle》的非官方 BepInEx IL2CPP Mod，提供装备强化、监牢囚犯品质调整和原生物品管理调试界面。

> [!WARNING]
> 使用 Mod 前请备份存档。本项目会修改游戏运行时数据，其中部分修改可能随游戏存档永久保存。游戏更新后，补丁也可能失效或产生兼容性问题。

## 当前功能

### 装备调整

- 新生成装备的所有词条等级提升至游戏当前合法上限 `9`。
- 加载已有装备时，同样把装备词条等级提升至 `9`。
- 装备宝箱只生成该宝箱等级区间内合法的最高装备等级。
- 不强制修改装备品级。

### 监牢调整

- 监牢创建或刷新囚犯时，直接按照监牢当前允许的最高人物品级生成囚犯。
- 人物属性、天赋和初始装备会按照该品级走游戏原生创建流程。
- 旧版“扭曲囚犯时 100% 提升品质”代码已经停用。

### 原生物品管理界面

- 按 `F6` 打开或关闭游戏内置的 `DebugLayer`。
- 可以使用其中的装备、套装、宝箱、符文、材料和奇物等物品发放控件。

> [!CAUTION]
> `DebugLayer` 是游戏开发调试界面，不是专门为本 Mod 制作的安全菜单。不要点击与物品管理无关的地图、进度、关卡等调试按钮，否则可能造成存档进度异常甚至游戏报错。

## 运行环境

- Windows x64
- Unity `2022.3.59f1`
- BepInEx `6.0.0-be.697` IL2CPP
- .NET 6

目前按《Path of Idle》`1.0.1` 的程序集结构开发。其他版本未验证。

## 安装

1. 为游戏安装 BepInEx 6 IL2CPP 版本。
2. 启动一次游戏，让 BepInEx 生成 IL2CPP interop 程序集，然后退出游戏。
3. 将 `PathOfIdleTweaks.dll` 复制到：

   ```text
   <游戏目录>\BepInEx\plugins\PathOfIdleTweaks.dll
   ```

4. 重新启动游戏，在 BepInEx 日志中确认出现：

   ```text
   Path of Idle Tweaks loaded.
   ```

## 从源码编译

项目默认从以下目录引用游戏生成的 interop 程序集：

```text
D:\app\Steam\steamapps\common\PathOfIdle
```

如果游戏安装在其他位置，可在构建时指定 `PathOfIdleGameDir`：

```powershell
dotnet build -c Release -p:PathOfIdleGameDir="D:\你的游戏目录\PathOfIdle"
```

构建结果位于：

```text
bin\Release\net6.0\PathOfIdleTweaks.dll
```

## 项目结构

```text
Features/
  EquipmentTweaks.cs       装备词条与装备箱等级调整
  PrisonRefreshTweaks.cs   监牢刷新囚犯品质调整
  ItemManagementUi.cs      F6 原生调试界面入口
Plugin.cs                   BepInEx 插件入口
```

## 免责声明

本项目与《Path of Idle》开发商无关，仅供个人研究和 Mod 开发学习使用。游戏名称、代码及相关资源的权利归其各自权利人所有。
