using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Rendering;
using Raylib_cs;

namespace FrinkyEngine.Core.Components;

/// <summary>
/// Configures the material for a mesh surface.
/// Used by <see cref="MeshRendererComponent"/> (multiple slots) and <see cref="PrimitiveComponent"/> (single material).
/// </summary>
public class Material
{
    /// <summary>
    /// Color multiplier applied to this material's surface (defaults to white / fully opaque).
    /// </summary>
    public Color Tint { get; set; } = new(255, 255, 255, 255);

    /// <summary>
    /// Which material mapping mode this material uses (defaults to <see cref="Rendering.MaterialType.SolidColor"/>).
    /// </summary>
    [InspectorLabel("Type")]
    public MaterialType MaterialType { get; set; } = MaterialType.SolidColor;

    /// <summary>
    /// Asset-relative path to the texture file, used when <see cref="MaterialType"/> is
    /// <see cref="Rendering.MaterialType.Textured"/> or <see cref="Rendering.MaterialType.TriplanarTexture"/>.
    /// </summary>
    [InspectorLabel("Texture")]
    [InspectorVisibleIf(nameof(UsesTexturedMaterial))]
    [AssetFilter(AssetType.Texture)]
    public AssetReference TexturePath { get; set; } = new("");

    /// <summary>
    /// Texture coordinate scale used when <see cref="MaterialType"/> is <see cref="Rendering.MaterialType.TriplanarTexture"/>.
    /// </summary>
    [InspectorVisibleIf(nameof(UsesTriplanarMaterial))]
    [InspectorRange(0.01f, 512f, 0.05f)]
    public float TriplanarScale { get; set; } = 1f;

    /// <summary>
    /// Blend sharpness used when <see cref="MaterialType"/> is <see cref="Rendering.MaterialType.TriplanarTexture"/>.
    /// Higher values produce harder transitions between projection axes.
    /// </summary>
    [InspectorLabel("Blend Sharpness")]
    [InspectorVisibleIf(nameof(UsesTriplanarMaterial))]
    [InspectorRange(0.01f, 64f, 0.05f)]
    public float TriplanarBlendSharpness { get; set; } = 4f;

    /// <summary>
    /// Whether triplanar projection uses world space (<c>true</c>) or object space (<c>false</c>).
    /// Used when <see cref="MaterialType"/> is <see cref="Rendering.MaterialType.TriplanarTexture"/>.
    /// </summary>
    [InspectorLabel("Use World Space")]
    [InspectorVisibleIf(nameof(UsesTriplanarMaterial))]
    public bool TriplanarUseWorldSpace { get; set; } = true;

    /// <summary>
    /// Returns a hash representing the current material configuration, useful for dirty detection.
    /// </summary>
    internal int GetConfigurationHash()
        => HashCode.Combine(MaterialType, TexturePath.Path, TriplanarScale, TriplanarBlendSharpness, TriplanarUseWorldSpace, Tint);

    /// <summary>
    /// Creates a shallow data clone of this material configuration.
    /// </summary>
    internal Material Clone()
    {
        return new Material
        {
            Tint = Tint,
            MaterialType = MaterialType,
            TexturePath = TexturePath,
            TriplanarScale = TriplanarScale,
            TriplanarBlendSharpness = TriplanarBlendSharpness,
            TriplanarUseWorldSpace = TriplanarUseWorldSpace
        };
    }

    private bool UsesTexturedMaterial()
    {
        return MaterialType is MaterialType.Textured or MaterialType.TriplanarTexture;
    }

    private bool UsesTriplanarMaterial()
    {
        return MaterialType == MaterialType.TriplanarTexture;
    }
}
