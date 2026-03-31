using FrinkyEngine.Core.ECS;
using Raylib_cs;

namespace FrinkyEngine.Core.Components;

/// <summary>
/// A procedural flat plane primitive with configurable size and subdivision.
/// </summary>
[ComponentCategory("Rendering/Primitives")]
[ComponentDisplayName("Plane")]
public class PlanePrimitive : PrimitiveComponent
{
    private float _width = 10.0f;
    private float _depth = 10.0f;
    private int _resolutionX = 1;
    private int _resolutionZ = 1;

    /// <summary>
    /// Size along the X axis (defaults to 10).
    /// </summary>
    public float Width
    {
        get => _width;
        set { if (_width != value) { _width = value; MarkMeshDirty(); } }
    }

    /// <summary>
    /// Size along the Z axis (defaults to 10).
    /// </summary>
    public float Depth
    {
        get => _depth;
        set { if (_depth != value) { _depth = value; MarkMeshDirty(); } }
    }

    /// <summary>
    /// Number of subdivisions along the X axis (defaults to 1).
    /// </summary>
    public int ResolutionX
    {
        get => _resolutionX;
        set { if (_resolutionX != value) { _resolutionX = value; MarkMeshDirty(); } }
    }

    /// <summary>
    /// Number of subdivisions along the Z axis (defaults to 1).
    /// </summary>
    public int ResolutionZ
    {
        get => _resolutionZ;
        set { if (_resolutionZ != value) { _resolutionZ = value; MarkMeshDirty(); } }
    }

    /// <inheritdoc />
    protected internal override Mesh CreateMesh() => Raylib.GenMeshPlane(_width, _depth, _resolutionX, _resolutionZ);
}
