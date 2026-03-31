using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Rendering;
using Raylib_cs;

namespace FrinkyEngine.Core.Components;

/// <summary>
/// Abstract base class for components that can be drawn by the renderer.
/// Geometry and GPU resource ownership live in renderer-managed caches, not on the component itself.
/// </summary>
public abstract class RenderableComponent : Component
{
    /// <summary>
    /// Monotonically increasing version incremented whenever this renderable invalidates its renderer-side resources.
    /// </summary>
    public int RenderVersion { get; private set; }

    /// <summary>
    /// Marks cached renderer-owned resources and bounds as stale.
    /// </summary>
    public virtual void Invalidate()
    {
        RenderVersion++;
        RenderGeometryQueries.Current?.Invalidate(this);
    }

    /// <summary>
    /// Casts a ray against this renderable's mesh in world space.
    /// </summary>
    public RayCollision? GetWorldRayCollision(Ray ray, bool frontFacesOnly = true)
    {
        return GetWorldRayCollision(ray, out _, frontFacesOnly);
    }

    /// <summary>
    /// Casts a ray against this renderable's mesh in world space, also reporting whether mesh data was available.
    /// </summary>
    public RayCollision? GetWorldRayCollision(Ray ray, out bool hasMeshData, bool frontFacesOnly = true)
    {
        var queries = RenderGeometryQueries.Current;
        if (queries == null)
        {
            hasMeshData = false;
            return null;
        }

        return queries.GetWorldRayCollision(this, ray, out hasMeshData, frontFacesOnly);
    }

    /// <summary>
    /// Computes the world-space axis-aligned bounding box of this renderable.
    /// </summary>
    public BoundingBox? GetWorldBoundingBox()
    {
        return RenderGeometryQueries.Current?.GetWorldBoundingBox(this);
    }
}
