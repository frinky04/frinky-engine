using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.ECS;
using Raylib_cs;

namespace FrinkyEngine.Core.Components;

/// <summary>
/// Renders a 3D model loaded from a file (e.g. .obj, .gltf, .glb).
/// The component stores asset references and material overrides only; renderer-owned caches manage live model resources.
/// </summary>
[ComponentCategory("Rendering")]
public class MeshRendererComponent : RenderableComponent
{
    private AssetReference _modelPath = new("");

    /// <summary>
    /// Monotonically increasing counter incremented each time the model binding is invalidated.
    /// Sibling systems use this to detect when renderer-owned resources need to be reacquired.
    /// </summary>
    internal int ModelVersion { get; private set; }

    /// <summary>
    /// Asset-relative path to the model file.
    /// </summary>
    [AssetFilter(AssetType.Model)]
    public AssetReference ModelPath
    {
        get => _modelPath;
        set
        {
            if (_modelPath.Path == value.Path)
                return;

            _modelPath = value;
            SyncMaterialSlotsWithModel();
            Invalidate();
        }
    }

    /// <summary>
    /// Per-material override configurations for this model.
    /// Missing slots fall back to default material settings at render time.
    /// </summary>
    [InspectorFixedListSize]
    public List<Material> MaterialSlots { get; set; } = new();

    /// <summary>
    /// Forces renderer-owned mesh/material bindings to be refreshed.
    /// </summary>
    public void RefreshMaterials()
    {
        SyncMaterialSlotsWithModel();
        Invalidate();
    }

    /// <summary>
    /// Resizes <see cref="MaterialSlots"/> to match the current model material count while preserving existing overrides.
    /// </summary>
    public void SyncMaterialSlotsWithModel()
    {
        if (_modelPath.IsEmpty)
        {
            MaterialSlots.Clear();
            return;
        }

        Model model = AssetManager.Instance.LoadModel(_modelPath.Path);
        int targetCount = Math.Max(0, model.MaterialCount);
        if (targetCount == MaterialSlots.Count)
            return;

        if (targetCount == 0)
        {
            MaterialSlots.Clear();
            return;
        }

        if (MaterialSlots.Count > targetCount)
        {
            MaterialSlots.RemoveRange(targetCount, MaterialSlots.Count - targetCount);
        }
        else
        {
            while (MaterialSlots.Count < targetCount)
                MaterialSlots.Add(new Material());
        }
    }

    /// <inheritdoc />
    public override void Invalidate()
    {
        ModelVersion++;
        base.Invalidate();
    }
}
