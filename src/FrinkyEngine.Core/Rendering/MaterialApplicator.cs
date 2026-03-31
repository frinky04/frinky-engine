using FrinkyEngine.Core.Assets;
using Raylib_cs;

namespace FrinkyEngine.Core.Rendering;

/// <summary>
/// Applies engine material settings to Raylib model materials.
/// </summary>
public static class MaterialApplicator
{
    /// <summary>
    /// Applies engine material settings to a Raylib material value.
    /// </summary>
    /// <param name="material">Target material value.</param>
    /// <param name="materialConfig">Material configuration to apply.</param>
    public static unsafe void ApplyToMaterial(ref Raylib_cs.Material material, Components.Material materialConfig)
    {
        ApplyToMaterial(
            ref material,
            materialConfig.MaterialType,
            materialConfig.TexturePath.Path,
            materialConfig.TriplanarScale,
            materialConfig.TriplanarBlendSharpness,
            materialConfig.TriplanarUseWorldSpace,
            materialConfig.Tint);
    }

    /// <summary>
    /// Applies material settings from a <see cref="Components.Material"/> to the specified model material slot.
    /// </summary>
    /// <param name="model">Target model.</param>
    /// <param name="materialIndex">Material index in the model.</param>
    /// <param name="material">Material configuration to apply.</param>
    public static unsafe void ApplyToModel(Model model, int materialIndex, Components.Material material)
    {
        ApplyToModel(
            model,
            materialIndex,
            material.MaterialType,
            material.TexturePath.Path,
            material.TriplanarScale,
            material.TriplanarBlendSharpness,
            material.TriplanarUseWorldSpace,
            material.Tint);
    }

    /// <summary>
    /// Applies material settings to the specified model material slot.
    /// </summary>
    /// <param name="model">Target model.</param>
    /// <param name="materialIndex">Material index in the model.</param>
    /// <param name="materialType">Material mapping mode.</param>
    /// <param name="texturePath">Asset-relative albedo texture path.</param>
    /// <param name="triplanarScale">Triplanar projection scale.</param>
    /// <param name="triplanarBlendSharpness">Triplanar axis blend sharpness.</param>
    /// <param name="triplanarUseWorldSpace">Whether triplanar uses world-space coordinates.</param>
    /// <param name="tint">Color multiplier applied to the albedo map.</param>
    internal static unsafe void ApplyToModel(
        Model model,
        int materialIndex,
        MaterialType materialType,
        string texturePath,
        float triplanarScale,
        float triplanarBlendSharpness,
        bool triplanarUseWorldSpace,
        Color tint)
    {
        if (materialIndex < 0 || materialIndex >= model.MaterialCount)
            return;

        var albedo = ResolveAlbedoTexture(materialType, texturePath);
        bool triplanarEnabled = materialType == MaterialType.TriplanarTexture;
        var triplanarParams = AssetManager.Instance.GetTriplanarParamsTexture(
            triplanarEnabled,
            triplanarScale,
            triplanarBlendSharpness,
            triplanarUseWorldSpace);

        model.Materials[materialIndex].Maps[(int)MaterialMapIndex.Albedo].Texture = albedo;
        model.Materials[materialIndex].Maps[(int)MaterialMapIndex.Albedo].Color = tint;
        model.Materials[materialIndex].Maps[(int)MaterialMapIndex.Brdf].Texture = triplanarParams;
    }

    /// <summary>
    /// Applies material settings to a Raylib material value without mutating shared model state.
    /// </summary>
    internal static unsafe void ApplyToMaterial(
        ref Raylib_cs.Material material,
        MaterialType materialType,
        string texturePath,
        float triplanarScale,
        float triplanarBlendSharpness,
        bool triplanarUseWorldSpace,
        Color tint)
    {
        var albedo = ResolveAlbedoTexture(materialType, texturePath);
        bool triplanarEnabled = materialType == MaterialType.TriplanarTexture;
        var triplanarParams = AssetManager.Instance.GetTriplanarParamsTexture(
            triplanarEnabled,
            triplanarScale,
            triplanarBlendSharpness,
            triplanarUseWorldSpace);

        material.Maps[(int)MaterialMapIndex.Albedo].Texture = albedo;
        material.Maps[(int)MaterialMapIndex.Albedo].Color = tint;
        material.Maps[(int)MaterialMapIndex.Brdf].Texture = triplanarParams;
    }

    private static Texture2D ResolveAlbedoTexture(MaterialType materialType, string texturePath)
    {
        if ((materialType == MaterialType.Textured || materialType == MaterialType.TriplanarTexture)
            && !string.IsNullOrEmpty(texturePath))
        {
            return AssetManager.Instance.LoadTexture(texturePath);
        }

        return new Texture2D
        {
            Id = Rlgl.GetTextureIdDefault(),
            Width = 1,
            Height = 1,
            Mipmaps = 1,
            Format = PixelFormat.UncompressedR8G8B8A8
        };
    }
}
