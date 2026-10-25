using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppObjectBase = Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
using UnityEngine;

namespace PathOfIdleTweaks.Features;

internal sealed class NewTool
{
    public int id;
    public string name = "";
    public int type;
    public Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? typeParamArr;
    public int index;
    public int quality;
    public string des = "";
    public string source = "";
    public int rotate;
    public bool isEnable = false;
}

internal sealed class NewCurio
{
    public int id;
    public string name = "";
    public int type;
    public int index;
    public int quality;
    public string des = "";
    public string explain = "";
    public string source = "";
    public int rotate;
    public bool isEnable = false;
}
// 物品注册：把自定义 TTool / TCurio 行写进 TableData，并把图标按「色相偏转」生成后注入 ResMgr。
internal static class ItemRegister
{
    internal static readonly List<NewTool> Tools = new();
    internal static readonly List<NewCurio> Curios = new();
    private static readonly Dictionary<string, Sprite> Sprites = new();

    // ① 注册表初始化（TableData.init 之后）：只清空列表，条目由 OutlandEssence 在载入存档时推送。
    internal static void Init()
    {
        Tools.Clear();
        Curios.Clear();
    }

    // ④ 注册：把列表里 isEnable == true 的物品写入 TableData.TToolDict / TCurioDict。
    //    注：TTool/TCurio 行始终写入（避免旧存档里有该 id 时 LordWalletData.Init 崩溃），
    //        isEnable 只影响图标生成与「是否在载入存档时被清理」。
    internal static void RegisterAll()
    {
        if (TableData.TToolDict == null || TableData.TCurioDict == null)
            return;
        try
        {
            foreach (var tool in Tools)
            {
                if (TableData.TToolDict.ContainsKey(tool.id))
                    continue;
                var row = new TTool
                {
                    id = tool.id,
                    name = tool.name,
                    type = tool.type,
                    typeParamArr = tool.typeParamArr,
                    index = tool.index,
                    quality = tool.quality,
                    icon = "tool_" + tool.id,   // 指向我们自己生成的偏转图标（由 ResMgr.GetSprite Postfix 注入）
                    des = tool.des
                };
                TableData.TToolDict.Add(tool.id, row);
                try { row._name = tool.name; row._des = tool.des; }
                catch (Exception e2) { Plugin.Log.LogWarning("[ItemRegister] 设置 _name/_des 失败：" + e2.Message); }
                Plugin.Log.LogInfo($"[ItemRegister] TTool {tool.id}（{tool.name}, type={tool.type}, quality={tool.quality}）" +
                                   (tool.isEnable ? "" : "［已禁用，将在载入存档时从存档中移除］"));
            }

            foreach (var curio in Curios)
            {
                if (TableData.TCurioDict.ContainsKey(curio.id))
                    continue;
                var row = new TCurio
                {
                    id = curio.id,
                    name = curio.name,
                    type = curio.type,
                    index = curio.index,
                    quality = curio.quality,
                    icon = "curio_" + curio.id,
                    des = curio.des,
                    explain = curio.explain
                };
                TableData.TCurioDict.Add(curio.id, row);
                Plugin.Log.LogInfo($"[ItemRegister] TCurio {curio.id}（{curio.name}）");
            }

            // 1.3 注册图标（仅启用的物品才生成；源图标为空时兜底 curio_701）
            foreach (var tool in Tools)
            {
                if (!tool.isEnable)
                    continue;
                var sourceName = string.IsNullOrEmpty(tool.source) ? "curio_701" : tool.source;
                ColorConvert(sourceName, tool.rotate, "tool_" + tool.id);
            }
            foreach (var curio in Curios)
            {
                if (!curio.isEnable)
                    continue;
                var sourceName = string.IsNullOrEmpty(curio.source) ? ("curio_" + curio.id) : curio.source;
                ColorConvert(sourceName, curio.rotate, "curio_" + curio.id);
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"[ItemRegister] 注册失败：{exception.Message}");
        }
    }

    internal static Sprite? GetSprite(string name)
    {
        return Sprites.TryGetValue(name, out var sprite) ? sprite : null;
    }

    // 1.0 色相偏转：取 ResMgr 里的源 Sprite，色相 +degree 度，生成新 Sprite 并登记到 name。
    internal static Sprite? ColorConvert(string iconName, int degree, string name)
    {
        if (Sprites.TryGetValue(name, out var cached))
            return cached;
        try
        {
            var resMgr = Game.resMgr;
            if (resMgr == null)
                return null;
            var source = resMgr.GetSprite(iconName);
            if (source?.texture == null)
            {
                Plugin.Log.LogWarning($"[ItemRegister] 找不到源图标 {iconName}");
                return null;
            }

            var texture = source.texture;
            // sprite 常来自图集：先整张读入，再按 textureRect 精确取子区域（避免缩放 Blit 造成的拉伸/错位）
            var atlasWidth = texture.width;
            var atlasHeight = texture.height;
            var atlasRt = RenderTexture.GetTemporary(atlasWidth, atlasHeight, 0);
            var previous = RenderTexture.active;
            Texture2D atlasCopy;
            try
            {
                Graphics.Blit(texture, atlasRt);
                RenderTexture.active = atlasRt;
                atlasCopy = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false);
                atlasCopy.ReadPixels(new Rect(0, 0, atlasWidth, atlasHeight), 0, 0, false);
                atlasCopy.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(atlasRt);
            }

            // 图集坐标：textureRect 是"图集里的紧贴图案区域"，rect 是"原始未打包尺寸"
            // 正确做法：从图集取 textureRect 的图案，再贴回 rect 尺寸的画布（按 textureRectOffset 留白）
            var packed = source.textureRect;
            var trimOffset = source.textureRectOffset;
            var canvasWidth = Mathf.Max(1, Mathf.RoundToInt(source.rect.width));
            var canvasHeight = Mathf.Max(1, Mathf.RoundToInt(source.rect.height));
            var packWidth = Mathf.Max(1, Mathf.RoundToInt(packed.width));
            var packHeight = Mathf.Max(1, Mathf.RoundToInt(packed.height));
            var packX = Mathf.Clamp(Mathf.RoundToInt(packed.x), 0, atlasWidth - packWidth);
            var packY = Mathf.Clamp(Mathf.RoundToInt(packed.y), 0, atlasHeight - packHeight);

            var copy = new Texture2D(canvasWidth, canvasHeight, TextureFormat.RGBA32, false);
            var blank = new Color[canvasWidth * canvasHeight];
            for (var i = 0; i < blank.Length; i++)
                blank[i] = new Color(0f, 0f, 0f, 0f);
            copy.SetPixels(blank);

            var dstX = Mathf.Clamp(Mathf.RoundToInt(trimOffset.x), 0, Mathf.Max(0, canvasWidth - packWidth));
            var dstY = Mathf.Clamp(Mathf.RoundToInt(trimOffset.y), 0, Mathf.Max(0, canvasHeight - packHeight));
            copy.SetPixels(dstX, dstY, packWidth, packHeight,
                atlasCopy.GetPixels(packX, packY, packWidth, packHeight));
            copy.Apply();

            var shift = degree / 360f;
            var pixels = copy.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                var color = pixels[i];
                if (color.a <= 0.01f)
                    continue;
                Color.RGBToHSV(color, out var h, out var s, out var v);
                h += shift;
                if (h >= 1f) h -= 1f;
                if (h < 0f) h += 1f;
                var rotated = Color.HSVToRGB(h, s, v);
                rotated.a = color.a;
                pixels[i] = rotated;
            }
            copy.SetPixels(pixels);
            copy.Apply();

            // 保持原 sprite 的轴心比例（相对 rect）
            var pivot = new Vector2(
                source.rect.width  > 0f ? source.pivot.x / source.rect.width  : 0.5f,
                source.rect.height > 0f ? source.pivot.y / source.rect.height : 0.5f);
            var sprite = Sprite.Create(copy, new Rect(0, 0, canvasWidth, canvasHeight), pivot,
                source.pixelsPerUnit > 0f ? source.pixelsPerUnit : 100f);
            sprite.name = name;
            Sprites[name] = sprite;
            Plugin.Log.LogInfo($"[ItemRegister] 图标 {name} ← {iconName}（画布 {canvasWidth}×{canvasHeight}，图案 {packWidth}×{packHeight}，色相+{degree}°）");
            return sprite;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"[ItemRegister] 生成图标 {name} 失败：{exception.Message}");
            return null;
        }
    }
}

// 图标注入：界面按名字取 Sprite，找不到我们的名字时返回生成的偏转图。
[HarmonyPatch(typeof(ResMgr), nameof(ResMgr.GetSprite))]
internal static class ItemRegisterSpritePatch
{
    private static void Postfix(string name, ref Sprite? __result)
    {
        if (__result == null && name != null)
            __result = ItemRegister.GetSprite(name);
    }
}
