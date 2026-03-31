using FrinkyEngine.Core.Animation.IK;
using FrinkyEngine.Core.Components;
using Raylib_cs;

namespace FrinkyEngine.Core.Rendering;

internal interface IRenderGeometryQueries
{
    void Invalidate(RenderableComponent renderable);
    BoundingBox? GetWorldBoundingBox(RenderableComponent renderable);
    RayCollision? GetWorldRayCollision(RenderableComponent renderable, Ray ray, out bool hasMeshData, bool frontFacesOnly);
    bool TryGetSharedModel(MeshRendererComponent meshRenderer, out Model model);
    bool TryGetAnimationModel(MeshRendererComponent meshRenderer, out Model model, out SkinPaletteHandle skinPaletteHandle);
    BoneHierarchy? GetBoneHierarchy(MeshRendererComponent meshRenderer);
    void InvalidateAssets(IEnumerable<string> relativePaths);
    void Clear();
}

/// <summary>
/// Global access point for renderer-owned geometry queries.
/// Components and editor systems use this to query bounds/raycast data without owning live render models.
/// </summary>
public static class RenderGeometryQueries
{
    private static IRenderGeometryQueries? _current;

    internal static IRenderGeometryQueries? Current => _current;

    internal static void Install(IRenderGeometryQueries queries)
    {
        _current = queries;
    }

    internal static void Uninstall(IRenderGeometryQueries queries)
    {
        if (ReferenceEquals(_current, queries))
            _current = null;
    }
}
