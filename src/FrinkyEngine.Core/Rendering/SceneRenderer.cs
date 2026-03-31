using System.Numerics;
using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.ECS;
using Raylib_cs;

namespace FrinkyEngine.Core.Rendering;

/// <summary>
/// Public renderer facade that extracts frame data, computes visibility, and delegates submission to the active backend.
/// </summary>
public sealed class SceneRenderer : IDisposable
{
    private readonly RenderResourceCache _resources;
    private readonly RenderExtraction _extraction;
    private readonly RenderVisibility _visibility = new();
    private readonly IRenderBackend _backend;

    public SceneRenderer()
    {
        _resources = new RenderResourceCache();
        _extraction = new RenderExtraction(_resources);
        _backend = new RaylibRenderBackend(_resources);
    }

    /// <summary>
    /// Number of draw calls issued in the most recent render pass.
    /// </summary>
    public int LastFrameDrawCallCount => _backend.LastFrameDrawCallCount;

    /// <summary>
    /// Number of skinned meshes rendered in the most recent render pass.
    /// </summary>
    public int LastFrameSkinnedMeshCount => _backend.LastFrameSkinnedMeshCount;

    /// <summary>
    /// Structured diagnostics from the most recent view render.
    /// </summary>
    public RenderDiagnostics LastViewDiagnostics { get; private set; }

    private RenderViewResult? _lastViewResult;

    /// <summary>
    /// Diagnostic statistics from the most recent automatic instancing frame.
    /// </summary>
    public readonly record struct AutoInstancingFrameStats(
        bool Enabled,
        int BatchCount,
        int InstancedBatchCount,
        int InstancedInstanceCount,
        int FallbackDrawCount,
        int InstancedMeshDrawCalls);

    /// <summary>
    /// Diagnostic statistics from the most recent Forward+ frame.
    /// </summary>
    public readonly record struct ForwardPlusFrameStats(
        bool Valid,
        int SceneLights,
        int VisibleLights,
        int Skylights,
        int DirectionalLights,
        int PointLights,
        int AssignedLights,
        int ClippedLights,
        int DroppedTileLinks,
        int TileSize,
        int TilesX,
        int TilesY,
        int MaxLights,
        int MaxLightsPerTile,
        float AverageLightsPerTile,
        int PeakLightsPerTile);

    public void Dispose()
    {
        _backend.Dispose();
        _resources.Dispose();
    }

    /// <summary>
    /// Loads the renderer shader set.
    /// </summary>
    public void LoadShader(string vsPath, string fsPath) => _backend.LoadShader(vsPath, fsPath);

    /// <summary>
    /// Releases renderer shader resources.
    /// </summary>
    public void UnloadShader() => _backend.UnloadShader();

    /// <summary>
    /// Applies Forward+ lighting settings.
    /// </summary>
    public void ConfigureForwardPlus(ForwardPlusSettings settings) => _backend.ConfigureForwardPlus(settings);

    /// <summary>
    /// Invalidates cached render resources for the supplied asset-relative paths.
    /// </summary>
    public void InvalidateAssets(IEnumerable<string> relativePaths) => _resources.InvalidateAssets(relativePaths);

    /// <summary>
    /// Renders a scene view using the backend-neutral request model.
    /// </summary>
    public RenderViewResult RenderView(RenderViewRequest request)
    {
        var frame = _extraction.Extract(request.Scene, request.IsEditorMode);
        var visibleSet = _visibility.Cull(frame, request);
        var result = _backend.RenderView(frame, visibleSet, request);

        // TODO: Move post-processing, outline composition, and final UI composite into the renderer graph.
        if (request.ComposeFinalOverlay != null && _backend.TryGetRenderTexture(result.FinalTarget, out var target))
        {
            Raylib.BeginTextureMode(target);
            request.ComposeFinalOverlay(new RenderFinalOverlayContext(target.Texture.Width, target.Texture.Height));
            Raylib.EndTextureMode();
        }

        _lastViewResult = result;
        LastViewDiagnostics = result.Diagnostics;
        return result;
    }

    /// <summary>
    /// Compatibility wrapper for existing callers that still supply an external render target.
    /// </summary>
    public void Render(
        Core.Scene.Scene scene,
        Camera3D camera,
        RenderTexture2D? renderTarget = null,
        Action? postSceneRender = null,
        bool isEditorMode = true)
    {
        int renderWidth = renderTarget?.Texture.Width ?? Raylib.GetScreenWidth();
        int renderHeight = renderTarget?.Texture.Height ?? Raylib.GetScreenHeight();
        var request = new RenderViewRequest
        {
            Scene = scene,
            Camera = camera,
            CameraComponent = scene.MainCamera,
            DisplayWidth = renderWidth,
            DisplayHeight = renderHeight,
            RenderWidth = renderWidth,
            RenderHeight = renderHeight,
            IsEditorMode = isEditorMode,
            DrawSceneOverlay3D = postSceneRender
        };

        var result = RenderView(request);
        if (renderTarget.HasValue)
        {
            _backend.ResolveToRenderTexture(result.FinalTarget, renderTarget.Value);
            return;
        }

        if (_backend.TryGetTexture(result.FinalTarget, out var texture))
        {
            var src = new Rectangle(0, 0, texture.Width, -texture.Height);
            var dst = new Rectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
            Raylib.DrawTexturePro(texture, src, dst, Vector2.Zero, 0f, Color.White);
        }
    }

    /// <summary>
    /// Renders a depth pre-pass for the currently visible set.
    /// </summary>
    public void RenderDepthPrePass(
        Core.Scene.Scene scene,
        Camera3D camera,
        RenderTexture2D depthTarget,
        Shader depthShader,
        bool isEditorMode = true)
    {
        var request = new RenderViewRequest
        {
            Scene = scene,
            Camera = camera,
            CameraComponent = scene.MainCamera,
            DisplayWidth = depthTarget.Texture.Width,
            DisplayHeight = depthTarget.Texture.Height,
            RenderWidth = depthTarget.Texture.Width,
            RenderHeight = depthTarget.Texture.Height,
            IsEditorMode = isEditorMode
        };

        var frame = _extraction.Extract(scene, isEditorMode);
        var visibleSet = _visibility.Cull(frame, request);
        _backend.RenderDepthPrePass(visibleSet, camera, depthTarget, depthShader);
    }

    /// <summary>
    /// Renders a selection mask for the currently visible selected entities.
    /// </summary>
    public void RenderSelectionMask(
        Core.Scene.Scene scene,
        Camera3D camera,
        IReadOnlyList<Entity> selectedEntities,
        RenderTexture2D renderTarget,
        bool isEditorMode = true)
    {
        var request = new RenderViewRequest
        {
            Scene = scene,
            Camera = camera,
            CameraComponent = scene.MainCamera,
            DisplayWidth = renderTarget.Texture.Width,
            DisplayHeight = renderTarget.Texture.Height,
            RenderWidth = renderTarget.Texture.Width,
            RenderHeight = renderTarget.Texture.Height,
            IsEditorMode = isEditorMode,
            SelectedEntities = selectedEntities
        };

        var frame = _extraction.Extract(scene, isEditorMode);
        var visibleSet = _visibility.Cull(frame, request);
        _backend.RenderSelectionMask(visibleSet, camera, renderTarget);
    }

    /// <summary>
    /// Exposes backend target lookup for renderer-integrated callers.
    /// </summary>
    public bool TryGetTexture(RenderTargetHandle handle, out Texture2D texture) => _backend.TryGetTexture(handle, out texture);

    /// <summary>
    /// Exposes backend render-target lookup for renderer-integrated callers.
    /// </summary>
    public bool TryGetRenderTexture(RenderTargetHandle handle, out RenderTexture2D renderTexture) =>
        _backend.TryGetRenderTexture(handle, out renderTexture);

    /// <summary>
    /// Gets the backend render target produced by the most recent view render.
    /// </summary>
    public bool TryGetLastViewRenderTexture(out RenderTexture2D renderTexture)
    {
        if (_lastViewResult.HasValue)
            return _backend.TryGetRenderTexture(_lastViewResult.Value.FinalTarget, out renderTexture);

        renderTexture = default;
        return false;
    }

    /// <summary>
    /// Gets automatic instancing diagnostics from the most recent frame.
    /// </summary>
    public AutoInstancingFrameStats GetAutoInstancingFrameStats()
    {
        var stats = _backend.GetAutoInstancingFrameStats();
        return new AutoInstancingFrameStats(
            stats.Enabled,
            stats.BatchCount,
            stats.InstancedBatchCount,
            stats.InstancedInstanceCount,
            stats.FallbackDrawCount,
            stats.InstancedMeshDrawCalls);
    }

    /// <summary>
    /// Gets Forward+ diagnostics from the most recent frame.
    /// </summary>
    public ForwardPlusFrameStats GetForwardPlusFrameStats()
    {
        var stats = _backend.GetForwardPlusFrameStats();
        return new ForwardPlusFrameStats(
            stats.Valid,
            stats.SceneLights,
            stats.VisibleLights,
            stats.Skylights,
            stats.DirectionalLights,
            stats.PointLights,
            stats.AssignedLights,
            stats.ClippedLights,
            stats.DroppedTileLinks,
            stats.TileSize,
            stats.TilesX,
            stats.TilesY,
            stats.MaxLights,
            stats.MaxLightsPerTile,
            stats.AverageLightsPerTile,
            stats.PeakLightsPerTile);
    }
}
