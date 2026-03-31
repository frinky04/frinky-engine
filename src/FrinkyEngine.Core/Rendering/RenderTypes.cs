using System.Numerics;
using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.UI;
using Raylib_cs;

namespace FrinkyEngine.Core.Rendering;

/// <summary>
/// Opaque handle to a cached mesh resource.
/// </summary>
public readonly record struct RenderMeshHandle(int Id)
{
    public bool IsValid => Id > 0;
}

/// <summary>
/// Opaque handle to a cached material binding resource.
/// </summary>
public readonly record struct RenderMaterialHandle(int Id)
{
    public bool IsValid => Id > 0;
}

/// <summary>
/// Opaque handle to an internal render target.
/// </summary>
public readonly record struct RenderTargetHandle(int Id)
{
    public bool IsValid => Id > 0;
}

/// <summary>
/// Opaque handle to backend-owned skinning state for a render object.
/// </summary>
public readonly record struct SkinPaletteHandle(int Id)
{
    public bool IsValid => Id > 0;
}

/// <summary>
/// Shader variant needed by the backend for a draw.
/// </summary>
public enum RenderShaderVariant
{
    Lit = 0,
    Depth = 1,
    SelectionMask = 2
}

/// <summary>
/// Backend-neutral light data extracted from the scene.
/// </summary>
public readonly record struct RenderLight(
    Entity Entity,
    LightType LightType,
    Vector3 Position,
    Vector3 Direction,
    Vector3 Color,
    float Intensity,
    float Range,
    bool EditorOnly);

/// <summary>
/// Backend-neutral render object data extracted from the scene.
/// </summary>
public readonly record struct RenderObject(
    Entity Entity,
    RenderableComponent Renderable,
    RenderMeshHandle MeshHandle,
    RenderMaterialHandle MaterialHandle,
    Matrix4x4 WorldMatrix,
    BoundingBox WorldBounds,
    bool UsesSkinning,
    SkinPaletteHandle SkinPaletteHandle,
    bool SupportsInstancing);

/// <summary>
/// Extracted frame data passed from the renderer frontend to the backend.
/// </summary>
public sealed class RenderFrame
{
    public RenderFrame(
        ulong frameToken,
        IReadOnlyList<RenderObject> objects,
        IReadOnlyList<RenderLight> lights,
        int activeRenderObjectCount,
        int skinnedObjectCount)
    {
        FrameToken = frameToken;
        Objects = objects;
        Lights = lights;
        ActiveRenderObjectCount = activeRenderObjectCount;
        SkinnedObjectCount = skinnedObjectCount;
    }

    public ulong FrameToken { get; }
    public IReadOnlyList<RenderObject> Objects { get; }
    public IReadOnlyList<RenderLight> Lights { get; }
    public int ActiveRenderObjectCount { get; }
    public int SkinnedObjectCount { get; }
}

/// <summary>
/// Culled visible-set result for a frame and view.
/// </summary>
public sealed class RenderVisibleSet
{
    public required IReadOnlyList<RenderObject> VisibleObjects { get; init; }
    public required IReadOnlyList<RenderObject> SelectedObjects { get; init; }
    public required int VisibleObjectCount { get; init; }
    public required int CulledObjectCount { get; init; }
}

/// <summary>
/// Final-pass callback context supplied by the renderer while the resolved target is bound.
/// </summary>
public readonly record struct RenderFinalOverlayContext(int Width, int Height);

/// <summary>
/// A single render request describing how to build and compose a scene view.
/// </summary>
public sealed class RenderViewRequest
{
    public required Core.Scene.Scene Scene { get; init; }
    public required Camera3D Camera { get; init; }
    public CameraComponent? CameraComponent { get; init; }
    public required int DisplayWidth { get; init; }
    public required int DisplayHeight { get; init; }
    public int RenderWidth { get; init; }
    public int RenderHeight { get; init; }
    public bool IsEditorMode { get; init; } = true;
    public bool EnablePostProcessing { get; init; } = true;
    public bool EnableSelectionOutline { get; init; }
    public IReadOnlyList<Entity>? SelectedEntities { get; init; }
    public Action? DrawSceneOverlay3D { get; init; }
    public Action<RenderFinalOverlayContext>? ComposeFinalOverlay { get; init; }

    public int EffectiveRenderWidth => RenderWidth > 0 ? RenderWidth : DisplayWidth;
    public int EffectiveRenderHeight => RenderHeight > 0 ? RenderHeight : DisplayHeight;
}

/// <summary>
/// Renderer diagnostics captured for the most recent view render.
/// </summary>
public readonly record struct RenderDiagnostics(
    int ActiveRenderObjects,
    int VisibleRenderObjects,
    int CulledRenderObjects,
    int DrawCalls,
    int SkinnedObjects,
    int InstancedBatchCount,
    int InstancedInstanceCount,
    int PostProcessPasses,
    long RenderTargetMemoryBytes);

/// <summary>
/// Forward+ light-grid diagnostics captured by the backend.
/// </summary>
public readonly record struct LightGridData(
    int SceneLights,
    int VisibleLights,
    int Skylights,
    int DirectionalLights,
    int PointLights,
    int AssignedLights,
    int ClippedLights,
    int DroppedTileLinks,
    int TileCountX,
    int TileCountY,
    int TileSize,
    int MaxLights,
    int MaxLightsPerTile,
    float AverageLightsPerTile,
    int PeakLightsPerTile);

/// <summary>
/// Final renderer output for a view render.
/// </summary>
public readonly record struct RenderViewResult(
    RenderTargetHandle FinalTarget,
    UiImageHandle FinalImage,
    int Width,
    int Height,
    RenderDiagnostics Diagnostics);
