using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.ECS;
using Raylib_cs;

namespace FrinkyEngine.Core.Components;

/// <summary>
/// Triangle-mesh collider sourced from a model asset or a sibling <see cref="MeshRendererComponent"/>.
/// Supports static and kinematic physics participants only.
/// </summary>
[ComponentCategory("Physics/Colliders")]
[InspectorMessageIf(nameof(ShowMissingMeshSourceWarning), "Mesh collider has no resolved model source.", Severity = InspectorMessageSeverity.Warning, Order = 0)]
[InspectorMessageIf(nameof(ShowNoMeshDataWarning), "Resolved model source has no triangle mesh data.", Severity = InspectorMessageSeverity.Warning, Order = 1)]
[InspectorMessageIf(nameof(ShowSkinnedMeshWarning), "Skinned or bone-driven models are not supported by MeshColliderComponent.", Severity = InspectorMessageSeverity.Warning, Order = 2)]
[InspectorMessageIf(nameof(ShowDynamicRigidbodyWarning), "MeshColliderComponent does not support Dynamic rigidbodies.", Severity = InspectorMessageSeverity.Warning, Order = 3)]
public sealed class MeshColliderComponent : ColliderComponent
{
    private AssetReference _meshPath = new("");
    private bool _useMeshRendererWhenEmpty = true;

    /// <summary>
    /// Optional override model asset used as the collider source.
    /// When empty, the collider can fall back to a sibling <see cref="MeshRendererComponent"/>.
    /// </summary>
    [AssetFilter(AssetType.Model)]
    public AssetReference MeshPath
    {
        get => _meshPath;
        set
        {
            if (_meshPath.Path == value.Path)
                return;

            _meshPath = value;
            MarkColliderDirty();
        }
    }

    /// <summary>
    /// When <c>true</c> and <see cref="MeshPath"/> is empty, use the sibling <see cref="MeshRendererComponent"/>'s model as the collider source.
    /// </summary>
    public bool UseMeshRendererWhenEmpty
    {
        get => _useMeshRendererWhenEmpty;
        set
        {
            if (_useMeshRendererWhenEmpty == value)
                return;

            _useMeshRendererWhenEmpty = value;
            MarkColliderDirty();
        }
    }

    private bool ShowMissingMeshSourceWarning()
    {
        return Enabled && !TryResolveSourceModel(out _, out _);
    }

    private bool ShowNoMeshDataWarning()
    {
        return Enabled &&
               TryResolveSourceModel(out var model, out _)
               && !ModelUsesSkinning(model)
               && !ModelHasMeshData(model);
    }

    private bool ShowSkinnedMeshWarning()
    {
        return Enabled &&
               TryResolveSourceModel(out var model, out _)
               && ModelUsesSkinning(model);
    }

    private bool ShowDynamicRigidbodyWarning()
    {
        return Enabled && Entity.GetComponent<RigidbodyComponent>() is { Enabled: true, MotionType: BodyMotionType.Dynamic };
    }

    private bool TryResolveSourceModel(out Model model, out bool usesMeshRendererFallback)
    {
        model = default;
        usesMeshRendererFallback = false;

        if (!TryResolveSourcePath(out var sourcePath, out usesMeshRendererFallback))
            return false;

        model = AssetManager.Instance.LoadModel(sourcePath);
        return true;
    }

    private bool TryResolveSourcePath(out string sourcePath, out bool usesMeshRendererFallback)
    {
        sourcePath = string.Empty;
        usesMeshRendererFallback = false;

        if (!MeshPath.IsEmpty)
            return TryResolveAssetPath(MeshPath.Path, out sourcePath);

        if (!UseMeshRendererWhenEmpty)
            return false;

        if (Entity.GetComponent<MeshRendererComponent>() is not { } meshRenderer || meshRenderer.ModelPath.IsEmpty)
            return false;

        usesMeshRendererFallback = true;
        return TryResolveAssetPath(meshRenderer.ModelPath.Path, out sourcePath);
    }

    private static bool TryResolveAssetPath(string path, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = (AssetDatabase.Instance.ResolveAssetPath(path) ?? path).Replace('\\', '/');
        if (!File.Exists(AssetManager.Instance.ResolvePath(normalized)))
            return false;

        resolvedPath = normalized;
        return true;
    }

    private static unsafe bool ModelHasMeshData(Model model)
    {
        if (model.MeshCount <= 0)
            return false;

        for (int i = 0; i < model.MeshCount; i++)
        {
            var mesh = model.Meshes[i];
            if (mesh.VertexCount > 0 && mesh.TriangleCount > 0)
                return true;
        }

        return false;
    }

    private static unsafe bool ModelUsesSkinning(Model model)
    {
        if (model.BoneCount > 0)
            return true;

        for (int i = 0; i < model.MeshCount; i++)
        {
            if (model.Meshes[i].BoneCount > 0)
                return true;
        }

        return false;
    }
}
