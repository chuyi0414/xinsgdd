using Spine.Unity;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Addressables / AssetBundle 材质 Shader 修复器。
/// 用途：WebGL 或远端 AssetBundle 中的 Material 反序列化后可能拿到粉色的 InternalErrorShader；
///      本工具在资源加载成功的统一出口尝试用 Shader.Find 重新绑定同名 Shader，并对 SkeletonGraphic 做 UI Shader 兜底。
/// 注意：Shader.Find 只能找到已经进入 Player 的 Shader；因此还必须配合 ProjectSettings/GraphicsSettings.asset 的 Always Included Shaders。
/// </summary>
public static class AddressablesShaderRepair
{
    /// <summary>
    /// Spine UI Shader 名称。
    /// 初始状态为常量字符串；用于 SkeletonGraphic 专项兜底，避免 UI Spine 材质被普通 Spine/Skeleton 或 InternalErrorShader 卡住。
    /// </summary>
    private const string SpineSkeletonGraphicShaderName = "Spine/SkeletonGraphic";

    /// <summary>
    /// Unity 粉色材质常见的错误 Shader 名称片段。
    /// </summary>
    private const string InternalErrorShaderName = "Hidden/InternalErrorShader";

    /// <summary>
    /// 复用的 Renderer 缓冲区。
    /// 只在 Addressables 加载完成时使用，非 Update 高频路径；缓存数组可避免每次遍历 prefab 时产生额外数组分配。
    /// </summary>
    private static readonly List<Renderer> s_Renderers = new List<Renderer>(32);

    /// <summary>
    /// 复用的 UI Graphic 缓冲区。
    /// </summary>
    private static readonly List<Graphic> s_Graphics = new List<Graphic>(64);

    /// <summary>
    /// 复用的 Spine SkeletonGraphic 缓冲区。
    /// </summary>
    private static readonly List<SkeletonGraphic> s_SkeletonGraphics = new List<SkeletonGraphic>(16);

    /// <summary>
    /// 修复 Addressables 加载出来的资源对象中的材质 Shader。
    /// </summary>
    /// <param name="asset">Addressables 返回的资源对象；可以是 GameObject prefab、Material、Sprite、Texture 等。</param>
    public static void Repair(UnityEngine.Object asset)
    {
        if (asset == null)
        {
            return;
        }

        Material material = asset as Material;
        if (material != null)
        {
            RepairMaterial(material);
            return;
        }

        GameObject gameObject = asset as GameObject;
        if (gameObject == null)
        {
            return;
        }

        RepairGameObject(gameObject);
    }

    /// <summary>
    /// 修复 GameObject prefab 及其子节点上的 Renderer、Graphic、SkeletonGraphic 材质。
    /// </summary>
    /// <param name="gameObject">Addressables 加载出的 GameObject prefab 或实例。</param>
    private static void RepairGameObject(GameObject gameObject)
    {
        gameObject.GetComponentsInChildren(true, s_Renderers);
        for (int i = 0; i < s_Renderers.Count; i++)
        {
            Material[] sharedMaterials = s_Renderers[i].sharedMaterials;
            for (int j = 0; j < sharedMaterials.Length; j++)
            {
                RepairMaterial(sharedMaterials[j]);
            }
        }
        s_Renderers.Clear();

        gameObject.GetComponentsInChildren(true, s_Graphics);
        for (int i = 0; i < s_Graphics.Count; i++)
        {
            RepairMaterial(s_Graphics[i].material);
        }
        s_Graphics.Clear();

        gameObject.GetComponentsInChildren(true, s_SkeletonGraphics);
        for (int i = 0; i < s_SkeletonGraphics.Count; i++)
        {
            RepairSkeletonGraphic(s_SkeletonGraphics[i]);
        }
        s_SkeletonGraphics.Clear();
    }

    /// <summary>
    /// 修复 SkeletonGraphic 自身材质与 SkeletonDataAsset 下 Atlas 材质。
    /// </summary>
    /// <param name="graphic">目标 SkeletonGraphic。</param>
    private static void RepairSkeletonGraphic(SkeletonGraphic graphic)
    {
        if (graphic == null)
        {
            return;
        }

        RepairMaterial(graphic.material, SpineSkeletonGraphicShaderName);

        SkeletonDataAsset skeletonDataAsset = graphic.skeletonDataAsset;
        if (skeletonDataAsset == null || skeletonDataAsset.atlasAssets == null)
        {
            return;
        }

        for (int i = 0; i < skeletonDataAsset.atlasAssets.Length; i++)
        {
            AtlasAssetBase atlasAsset = skeletonDataAsset.atlasAssets[i];
            if (atlasAsset == null)
            {
                continue;
            }

            foreach (Material atlasMaterial in atlasAsset.Materials)
            {
                RepairMaterial(atlasMaterial, SpineSkeletonGraphicShaderName);
            }
        }
    }

    /// <summary>
    /// 修复单个材质 Shader。
    /// 优先按材质当前 Shader 名称重新 Shader.Find；如果已经变成 InternalErrorShader，则使用调用方提供的兜底 Shader 名称。
    /// </summary>
    /// <param name="material">需要修复的材质。</param>
    /// <param name="fallbackShaderName">当前 Shader 已丢失时使用的兜底 Shader 名称；可空。</param>
    private static void RepairMaterial(Material material, string fallbackShaderName = null)
    {
        if (material == null)
        {
            return;
        }

        Shader currentShader = material.shader;
        string shaderName = currentShader != null ? currentShader.name : null;
        bool isErrorShader = string.IsNullOrEmpty(shaderName) || shaderName == InternalErrorShaderName;
        string targetShaderName = isErrorShader ? fallbackShaderName : shaderName;

        if (string.IsNullOrEmpty(targetShaderName))
        {
            return;
        }

        Shader resolvedShader = Shader.Find(targetShaderName);
        if (resolvedShader == null || resolvedShader == currentShader)
        {
            return;
        }

        // 关键：只替换 Shader，不替换 Material 实例，保留贴图、颜色、Stencil、混合等序列化参数。
        material.shader = resolvedShader;
    }
}
