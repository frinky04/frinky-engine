using FrinkyEngine.Core.ECS;
using Raylib_cs;

namespace FrinkyEngine.Core.Components;

/// <summary>
/// A procedural sphere primitive with configurable radius and tessellation.
/// </summary>
[ComponentCategory("Rendering/Primitives")]
[ComponentDisplayName("Sphere")]
public class SpherePrimitive : PrimitiveComponent
{
    private float _radius = 0.5f;
    private int _rings = 16;
    private int _slices = 16;

    /// <summary>
    /// Sphere radius (defaults to 0.5).
    /// </summary>
    public float Radius
    {
        get => _radius;
        set { if (_radius != value) { _radius = value; MarkMeshDirty(); } }
    }

    /// <summary>
    /// Number of horizontal rings for tessellation (defaults to 16).
    /// </summary>
    public int Rings
    {
        get => _rings;
        set { if (_rings != value) { _rings = value; MarkMeshDirty(); } }
    }

    /// <summary>
    /// Number of vertical slices for tessellation (defaults to 16).
    /// </summary>
    public int Slices
    {
        get => _slices;
        set { if (_slices != value) { _slices = value; MarkMeshDirty(); } }
    }

    /// <inheritdoc />
    protected internal override Mesh CreateMesh() => Raylib.GenMeshSphere(_radius, _rings, _slices);
}
