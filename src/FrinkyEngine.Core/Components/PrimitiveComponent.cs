using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Rendering;
using Raylib_cs;

namespace FrinkyEngine.Core.Components;

/// <summary>
/// Abstract base class for procedurally generated mesh primitives (cubes, spheres, etc.).
/// Primitive geometry is generated into renderer-owned caches instead of being stored on the component.
/// </summary>
public abstract class PrimitiveComponent : RenderableComponent
{
    private Material _material = new();

    /// <summary>
    /// Monotonically increasing geometry version incremented whenever primitive shape data changes.
    /// </summary>
    internal int GeometryVersion { get; private set; }

    /// <summary>
    /// Material configuration for this primitive.
    /// </summary>
    [InspectorOnChanged(nameof(MarkMeshDirty))]
    public Material Material
    {
        get => _material;
        set
        {
            _material = value ?? new Material();
            Invalidate();
        }
    }

    /// <summary>
    /// Creates the procedural mesh for this primitive. Called by renderer-side resource caches.
    /// </summary>
    protected internal abstract Mesh CreateMesh();

    /// <summary>
    /// Flags the primitive geometry as stale.
    /// </summary>
    protected void MarkMeshDirty()
    {
        Invalidate();
    }

    /// <inheritdoc />
    public override void Invalidate()
    {
        GeometryVersion++;
        base.Invalidate();
    }

    internal string GetPrimitiveResourceKey()
    {
        var hash = new HashCode();
        hash.Add(GetType().FullName);

        switch (this)
        {
            case CubePrimitive cube:
                hash.Add(BitConverter.SingleToInt32Bits(cube.Width));
                hash.Add(BitConverter.SingleToInt32Bits(cube.Height));
                hash.Add(BitConverter.SingleToInt32Bits(cube.Depth));
                break;
            case SpherePrimitive sphere:
                hash.Add(BitConverter.SingleToInt32Bits(sphere.Radius));
                hash.Add(sphere.Rings);
                hash.Add(sphere.Slices);
                break;
            case PlanePrimitive plane:
                hash.Add(BitConverter.SingleToInt32Bits(plane.Width));
                hash.Add(BitConverter.SingleToInt32Bits(plane.Depth));
                hash.Add(plane.ResolutionX);
                hash.Add(plane.ResolutionZ);
                break;
            case CylinderPrimitive cylinder:
                hash.Add(BitConverter.SingleToInt32Bits(cylinder.Radius));
                hash.Add(BitConverter.SingleToInt32Bits(cylinder.Height));
                hash.Add(cylinder.Slices);
                break;
        }

        return $"{GetType().FullName}:{hash.ToHashCode():X8}";
    }
}
