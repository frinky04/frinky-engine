using FrinkyEngine.Core.ECS;
using Raylib_cs;

namespace FrinkyEngine.Core.Components;

/// <summary>
/// A procedural cylinder primitive with configurable radius, height, and tessellation.
/// </summary>
[ComponentCategory("Rendering/Primitives")]
[ComponentDisplayName("Cylinder")]
public class CylinderPrimitive : PrimitiveComponent
{
    private float _radius = 0.5f;
    private float _height = 2.0f;
    private int _slices = 16;

    /// <summary>
    /// Cylinder radius (defaults to 0.5).
    /// </summary>
    public float Radius
    {
        get => _radius;
        set { if (_radius != value) { _radius = value; MarkMeshDirty(); } }
    }

    /// <summary>
    /// Cylinder height along the Y axis (defaults to 2).
    /// </summary>
    public float Height
    {
        get => _height;
        set { if (_height != value) { _height = value; MarkMeshDirty(); } }
    }

    /// <summary>
    /// Number of circumferential slices for tessellation (defaults to 16).
    /// </summary>
    public int Slices
    {
        get => _slices;
        set { if (_slices != value) { _slices = value; MarkMeshDirty(); } }
    }

    /// <inheritdoc />
    protected internal override Mesh CreateMesh() => Raylib.GenMeshCylinder(_radius, _height, _slices);
}
