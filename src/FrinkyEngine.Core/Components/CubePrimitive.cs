using FrinkyEngine.Core.ECS;
using Raylib_cs;

namespace FrinkyEngine.Core.Components;

/// <summary>
/// A procedural box primitive with configurable width, height, and depth.
/// </summary>
[ComponentCategory("Rendering/Primitives")]
[ComponentDisplayName("Cube")]
public class CubePrimitive : PrimitiveComponent
{
    private float _width = 1.0f;
    private float _height = 1.0f;
    private float _depth = 1.0f;

    /// <summary>
    /// Size along the X axis (defaults to 1).
    /// </summary>
    public float Width
    {
        get => _width;
        set { if (_width != value) { _width = value; MarkMeshDirty(); } }
    }

    /// <summary>
    /// Size along the Y axis (defaults to 1).
    /// </summary>
    public float Height
    {
        get => _height;
        set { if (_height != value) { _height = value; MarkMeshDirty(); } }
    }

    /// <summary>
    /// Size along the Z axis (defaults to 1).
    /// </summary>
    public float Depth
    {
        get => _depth;
        set { if (_depth != value) { _depth = value; MarkMeshDirty(); } }
    }

    /// <inheritdoc />
    protected internal override Mesh CreateMesh() => Raylib.GenMeshCube(_width, _height, _depth);
}
