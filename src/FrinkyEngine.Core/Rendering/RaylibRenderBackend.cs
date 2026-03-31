using System.Numerics;
using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.Rendering.Profiling;
using FrinkyEngine.Core.UI;
using Raylib_cs;

namespace FrinkyEngine.Core.Rendering;

/// <summary>
/// Raylib-backed renderer implementation that consumes extracted render frames.
/// </summary>
internal sealed class RaylibRenderBackend : IRenderBackend
{
    private const int LightTexelsPerLight = 4;
    private const int PackedTextureMaxWidth = 1024;
    private const int ForwardPlusWarningLogIntervalFrames = 180;
    private const float MinPerspectiveDepth = 0.01f;

    private Shader _lightingShader;
    private bool _shaderLoaded;
    private Shader _selectionMaskShader;
    private bool _selectionMaskShaderLoaded;

    private int _ambientLoc = -1;
    private int _viewPosLoc = -1;
    private int _screenSizeLoc = -1;
    private int _tileCountLoc = -1;
    private int _tileSizeLoc = -1;
    private int _totalLightsLoc = -1;
    private int _lightDataTexLoc = -1;
    private int _tileHeaderTexLoc = -1;
    private int _tileIndexTexLoc = -1;
    private int _triplanarParamsTexLoc = -1;
    private int _lightDataTexSizeLoc = -1;
    private int _tileHeaderTexSizeLoc = -1;
    private int _tileIndexTexSizeLoc = -1;
    private int _lightingUseInstancingLoc = -1;
    private int _lightingUseSkinningLoc = -1;

    private ForwardPlusSettings _forwardPlusSettings = ForwardPlusSettings.Default;
    private int _viewportWidth;
    private int _viewportHeight;
    private int _tileCountX;
    private int _tileCountY;
    private int _tileCount;

    private Texture2D _lightDataTexture;
    private Texture2D _tileHeaderTexture;
    private Texture2D _tileIndexTexture;

    private int _lightDataEntries;
    private int _tileHeaderEntries;
    private int _tileIndexEntries;
    private int _lightDataTexWidth;
    private int _lightDataTexHeight;
    private int _tileHeaderTexWidth;
    private int _tileHeaderTexHeight;
    private int _tileIndexTexWidth;
    private int _tileIndexTexHeight;

    private float[] _lightDataBuffer = Array.Empty<float>();
    private float[] _tileHeaderBuffer = Array.Empty<float>();
    private float[] _tileIndexBuffer = Array.Empty<float>();
    private int[] _tileLightCounts = Array.Empty<int>();
    private float[] _tileLightScores = Array.Empty<float>();

    private readonly List<PackedLight> _frameLights = new();
    private readonly List<PointLightCandidate> _pointCandidates = new();
    private readonly Dictionary<RenderBatchKey, InstancedBatchBucket> _instancedBatches = new();
    private readonly List<RenderObject> _instancingFallbackDraws = new();
    private readonly Dictionary<uint, int> _useInstancingLocationCache = new();
    private readonly Dictionary<uint, int> _useSkinningLocationCache = new();
    private readonly Dictionary<uint, int> _instanceTransformAttribLocationCache = new();
    private readonly RenderResourceCache _resources;
    private int _frameDrawCallCount;
    private int _frameSkinnedMeshCount;
    private int _lastAutoInstancingBatchCount;
    private int _lastAutoInstancingInstancedBatchCount;
    private int _lastAutoInstancingInstancedInstanceCount;
    private int _lastAutoInstancingFallbackDrawCount;
    private int _lastAutoInstancingInstancedMeshDrawCallCount;
    private int _forwardPlusDroppedTileLights;
    private int _forwardPlusClippedLights;
    private int _frameCounter;
    private int _lastSceneLightCount;
    private int _lastVisibleLightCount;
    private int _lastSkylightCount;
    private int _lastDirectionalLightCount;
    private int _lastPointLightCount;
    private float _lastAverageLightsPerTile;
    private int _lastMaxLightsPerTile;
    private bool _lastStatsValid;
    private readonly RenderTargetHandle _mainColorTargetHandle = new(1);
    private RenderTexture2D _mainColorTarget;
    private int _mainColorTargetWidth;
    private int _mainColorTargetHeight;

    public RaylibRenderBackend(RenderResourceCache resources)
    {
        _resources = resources;
    }

    public int LastFrameDrawCallCount { get; private set; }
    public int LastFrameSkinnedMeshCount { get; private set; }

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

    /// <summary>
    /// Gets diagnostic statistics from the most recent Forward+ render pass.
    /// </summary>
    /// <returns>A snapshot of the current frame's lighting statistics.</returns>
    public ForwardPlusFrameStats GetForwardPlusFrameStats()
    {
        return new ForwardPlusFrameStats(
            _lastStatsValid,
            _lastSceneLightCount,
            _lastVisibleLightCount,
            _lastSkylightCount,
            _lastDirectionalLightCount,
            _lastPointLightCount,
            _frameLights.Count,
            _forwardPlusClippedLights,
            _forwardPlusDroppedTileLights,
            _forwardPlusSettings.TileSize,
            _tileCountX,
            _tileCountY,
            _forwardPlusSettings.MaxLights,
            _forwardPlusSettings.MaxLightsPerTile,
            _lastAverageLightsPerTile,
            _lastMaxLightsPerTile);
    }

    /// <summary>
    /// Gets automatic instancing diagnostics from the most recent render pass.
    /// </summary>
    /// <returns>A snapshot of batching and instanced submission counts for the frame.</returns>
    public AutoInstancingFrameStats GetAutoInstancingFrameStats()
    {
        return new AutoInstancingFrameStats(
            RenderRuntimeCvars.AutoInstancingEnabled,
            _lastAutoInstancingBatchCount,
            _lastAutoInstancingInstancedBatchCount,
            _lastAutoInstancingInstancedInstanceCount,
            _lastAutoInstancingFallbackDrawCount,
            _lastAutoInstancingInstancedMeshDrawCallCount);
    }

    /// <summary>
    /// Applies new Forward+ configuration settings, reallocating tile buffers if needed.
    /// </summary>
    /// <param name="settings">The new settings to apply (values will be normalized/clamped).</param>
    public void ConfigureForwardPlus(ForwardPlusSettings settings)
    {
        var normalized = settings.Normalize();
        if (normalized == _forwardPlusSettings)
            return;

        _forwardPlusSettings = normalized;
        _tileCountX = 0;
        _tileCountY = 0;
        _tileCount = 0;
        _lightDataEntries = 0;
        _tileHeaderEntries = 0;
        _tileIndexEntries = 0;
    }

    /// <summary>
    /// Loads the lighting shader from vertex and fragment shader files.
    /// </summary>
    /// <param name="vsPath">Path to the vertex shader file.</param>
    /// <param name="fsPath">Path to the fragment shader file.</param>
    public void LoadShader(string vsPath, string fsPath)
    {
        _lightingShader = Raylib.LoadShader(vsPath, fsPath);
        _viewPosLoc = Raylib.GetShaderLocation(_lightingShader, "viewPos");
        _ambientLoc = Raylib.GetShaderLocation(_lightingShader, "ambient");
        _screenSizeLoc = Raylib.GetShaderLocation(_lightingShader, "screenSize");
        _tileCountLoc = Raylib.GetShaderLocation(_lightingShader, "tileCount");
        _tileSizeLoc = Raylib.GetShaderLocation(_lightingShader, "tileSize");
        _totalLightsLoc = Raylib.GetShaderLocation(_lightingShader, "totalLights");
        _lightDataTexLoc = Raylib.GetShaderLocation(_lightingShader, "lightDataTex");
        _tileHeaderTexLoc = Raylib.GetShaderLocation(_lightingShader, "tileHeaderTex");
        _tileIndexTexLoc = Raylib.GetShaderLocation(_lightingShader, "tileIndexTex");
        _triplanarParamsTexLoc = Raylib.GetShaderLocation(_lightingShader, "triplanarParamsTex");
        _lightDataTexSizeLoc = Raylib.GetShaderLocation(_lightingShader, "lightDataTexSize");
        _tileHeaderTexSizeLoc = Raylib.GetShaderLocation(_lightingShader, "tileHeaderTexSize");
        _tileIndexTexSizeLoc = Raylib.GetShaderLocation(_lightingShader, "tileIndexTexSize");
        _lightingUseInstancingLoc = Raylib.GetShaderLocation(_lightingShader, "useInstancing");
        _lightingUseSkinningLoc = Raylib.GetShaderLocation(_lightingShader, "useSkinning");

        // Map forward+ sampler uniforms to unused material map slots so DrawMesh
        // binds them reliably (SetShaderValueTexture uses activeTextureId which
        // gets cleared by DrawRenderBatchActive, causing texture unit mismatches).
        unsafe
        {
            _lightingShader.Locs[(int)ShaderLocationIndex.MapOcclusion] = _lightDataTexLoc;
            _lightingShader.Locs[(int)ShaderLocationIndex.MapEmission] = _tileHeaderTexLoc;
            _lightingShader.Locs[(int)ShaderLocationIndex.MapHeight] = _tileIndexTexLoc;
            _lightingShader.Locs[(int)ShaderLocationIndex.MapBrdf] = _triplanarParamsTexLoc;
            _lightingShader.Locs[(int)ShaderLocationIndex.BoneMatrices] = Raylib.GetShaderLocation(_lightingShader, "boneMatrices");
            _lightingShader.Locs[(int)ShaderLocationIndex.VertexBoneIds] = Raylib.GetShaderLocationAttrib(_lightingShader, "vertexBoneIds");
            _lightingShader.Locs[(int)ShaderLocationIndex.VertexBoneWeights] = Raylib.GetShaderLocationAttrib(_lightingShader, "vertexBoneWeights");
        }

        var ambOvr = RenderRuntimeCvars.AmbientOverride;
        float[] ambient = ambOvr.HasValue
            ? new[] { ambOvr.Value.X, ambOvr.Value.Y, ambOvr.Value.Z, 1.0f }
            : new[] { 0.15f, 0.15f, 0.15f, 1.0f };
        Raylib.SetShaderValue(_lightingShader, _ambientLoc, ambient, ShaderUniformDataType.Vec4);

        _shaderLoaded = true;
        SetShaderBool(_lightingShader, _lightingUseInstancingLoc, false);
        SetShaderBool(_lightingShader, _lightingUseSkinningLoc, false);

        var shaderDir = Path.GetDirectoryName(vsPath) ?? "Shaders";
        var selectionMaskVsPath = Path.Combine(shaderDir, "selection_mask.vs");
        var selectionMaskFsPath = Path.Combine(shaderDir, "selection_mask.fs");

        if (File.Exists(selectionMaskVsPath) && File.Exists(selectionMaskFsPath))
        {
            _selectionMaskShader = Raylib.LoadShader(selectionMaskVsPath, selectionMaskFsPath);
            unsafe
            {
                _selectionMaskShader.Locs[(int)ShaderLocationIndex.BoneMatrices] = Raylib.GetShaderLocation(_selectionMaskShader, "boneMatrices");
                _selectionMaskShader.Locs[(int)ShaderLocationIndex.VertexBoneIds] = Raylib.GetShaderLocationAttrib(_selectionMaskShader, "vertexBoneIds");
                _selectionMaskShader.Locs[(int)ShaderLocationIndex.VertexBoneWeights] = Raylib.GetShaderLocationAttrib(_selectionMaskShader, "vertexBoneWeights");
            }
            _selectionMaskShaderLoaded = true;
        }
    }

    /// <summary>
    /// Unloads all shaders and releases Forward+ GPU textures.
    /// </summary>
    public void UnloadShader()
    {
        ReleaseForwardPlusTextures();
        ReleaseMainColorTarget();

        if (_shaderLoaded)
        {
            Raylib.UnloadShader(_lightingShader);
            _shaderLoaded = false;
        }

        if (_selectionMaskShaderLoaded)
        {
            Raylib.UnloadShader(_selectionMaskShader);
            _selectionMaskShaderLoaded = false;
        }

        _useInstancingLocationCache.Clear();
        _useSkinningLocationCache.Clear();
        _instanceTransformAttribLocationCache.Clear();
    }

    public void Dispose()
    {
        UnloadShader();
    }

    public RenderViewResult RenderView(RenderFrame frame, RenderVisibleSet visibleSet, RenderViewRequest request)
    {
        BeginFrame();

        int renderWidth = request.EffectiveRenderWidth;
        int renderHeight = request.EffectiveRenderHeight;
        EnsureMainColorTarget(renderWidth, renderHeight);

        var mainCam = request.Scene.MainCamera;
        Color clearColor = mainCam != null ? mainCam.ClearColor : new Color(30, 30, 30, 255);
        Raylib.BeginTextureMode(_mainColorTarget);
        Raylib.ClearBackground(clearColor);
        Raylib.BeginMode3D(request.Camera);

        if (_shaderLoaded)
        {
            float[] cameraPos = { request.Camera.Position.X, request.Camera.Position.Y, request.Camera.Position.Z };
            Raylib.SetShaderValue(_lightingShader, _viewPosLoc, cameraPos, ShaderUniformDataType.Vec3);
            UpdateForwardPlusData(frame, request.Camera, renderWidth, renderHeight);
            BindForwardPlusShaderData(renderWidth, renderHeight);
        }

        DrawRenderables(visibleSet.VisibleObjects, RenderPass.Main, default);

        if (request.IsEditorMode)
            Raylib.DrawGrid(20, 1.0f);

        request.DrawSceneOverlay3D?.Invoke();

        Raylib.EndMode3D();
        Raylib.EndTextureMode();

        LastFrameDrawCallCount = _frameDrawCallCount;
        LastFrameSkinnedMeshCount = _frameSkinnedMeshCount;

        var diagnostics = new RenderDiagnostics(
            frame.ActiveRenderObjectCount,
            visibleSet.VisibleObjectCount,
            visibleSet.CulledObjectCount,
            _frameDrawCallCount,
            _frameSkinnedMeshCount,
            _lastAutoInstancingInstancedBatchCount,
            _lastAutoInstancingInstancedInstanceCount,
            PostProcessPasses: 0,
            RenderTargetMemoryBytes: ComputeRenderTargetMemoryBytes());

        return new RenderViewResult(
            _mainColorTargetHandle,
            UiImageHandle.FromTexture(_mainColorTarget.Texture),
            renderWidth,
            renderHeight,
            diagnostics);
    }

    public void RenderDepthPrePass(
        RenderVisibleSet visibleSet,
        Camera3D camera,
        RenderTexture2D depthTarget,
        Shader depthShader)
    {
        Raylib.BeginTextureMode(depthTarget);
        Raylib.ClearBackground(Color.White);
        Raylib.BeginMode3D(camera);

        DrawRenderables(visibleSet.VisibleObjects, RenderPass.Depth, depthShader);

        Raylib.EndMode3D();
        Raylib.EndTextureMode();
    }

    public void RenderSelectionMask(
        RenderVisibleSet visibleSet,
        Camera3D camera,
        RenderTexture2D renderTarget)
    {
        if (!_selectionMaskShaderLoaded || visibleSet.SelectedObjects.Count == 0)
        {
            Raylib.BeginTextureMode(renderTarget);
            Raylib.ClearBackground(new Color(0, 0, 0, 0));
            Raylib.EndTextureMode();
            return;
        }

        Raylib.BeginTextureMode(renderTarget);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
        Raylib.BeginMode3D(camera);

        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthTest();
        Rlgl.EnableDepthMask();
        Rlgl.ColorMask(false, false, false, false);
        DrawRenderables(visibleSet.VisibleObjects, RenderPass.Depth, _selectionMaskShader);

        Rlgl.DrawRenderBatchActive();
        Rlgl.ColorMask(true, true, true, true);
        Rlgl.DisableDepthMask();
        DrawRenderables(visibleSet.SelectedObjects, RenderPass.Depth, _selectionMaskShader);

        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
        Raylib.EndMode3D();
        Raylib.EndTextureMode();
    }

    public void ResolveToRenderTexture(RenderTargetHandle sourceHandle, RenderTexture2D destination)
    {
        if (!TryGetTexture(sourceHandle, out var texture))
            return;

        Raylib.BeginTextureMode(destination);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
        var src = new Rectangle(0, 0, texture.Width, -texture.Height);
        var dst = new Rectangle(0, 0, destination.Texture.Width, destination.Texture.Height);
        Raylib.DrawTexturePro(texture, src, dst, Vector2.Zero, 0f, Color.White);
        Raylib.EndTextureMode();
    }

    public bool TryGetTexture(RenderTargetHandle handle, out Texture2D texture)
    {
        if (handle == _mainColorTargetHandle && _mainColorTarget.Id != 0)
        {
            texture = _mainColorTarget.Texture;
            return true;
        }

        texture = default;
        return false;
    }

    public bool TryGetRenderTexture(RenderTargetHandle handle, out RenderTexture2D renderTexture)
    {
        if (handle == _mainColorTargetHandle && _mainColorTarget.Id != 0)
        {
            renderTexture = _mainColorTarget;
            return true;
        }

        renderTexture = default;
        return false;
    }

    private void BeginFrame()
    {
        _frameDrawCallCount = 0;
        _frameSkinnedMeshCount = 0;
        _lastAutoInstancingBatchCount = 0;
        _lastAutoInstancingInstancedBatchCount = 0;
        _lastAutoInstancingInstancedInstanceCount = 0;
        _lastAutoInstancingFallbackDrawCount = 0;
        _lastAutoInstancingInstancedMeshDrawCallCount = 0;
    }

    private void EnsureMainColorTarget(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if (_mainColorTarget.Id != 0 && _mainColorTargetWidth == width && _mainColorTargetHeight == height)
            return;

        ReleaseMainColorTarget();
        _mainColorTarget = LoadHdrRenderTextureWithDepthTexture(width, height);
        _mainColorTargetWidth = width;
        _mainColorTargetHeight = height;
        bool isSupersampled = width >= Raylib.GetScreenWidth() && height >= Raylib.GetScreenHeight();
        Raylib.SetTextureFilter(_mainColorTarget.Texture, isSupersampled ? TextureFilter.Bilinear : TextureFilter.Point);
    }

    private void ReleaseMainColorTarget()
    {
        if (_mainColorTarget.Id == 0)
            return;

        Raylib.UnloadRenderTexture(_mainColorTarget);
        _mainColorTarget = default;
        _mainColorTargetWidth = 0;
        _mainColorTargetHeight = 0;
    }

    private long ComputeRenderTargetMemoryBytes()
    {
        long bytes = 0;
        if (_mainColorTarget.Texture.Id != 0)
        {
            bytes += (long)_mainColorTarget.Texture.Width * _mainColorTarget.Texture.Height * 8L;
            if (_mainColorTarget.Depth.Id != 0)
                bytes += (long)_mainColorTarget.Depth.Width * _mainColorTarget.Depth.Height * 4L;
        }

        bytes += (long)_lightDataBuffer.Length * sizeof(float);
        bytes += (long)_tileHeaderBuffer.Length * sizeof(float);
        bytes += (long)_tileIndexBuffer.Length * sizeof(float);
        return bytes;
    }

    private void DrawRenderables(
        IReadOnlyList<RenderObject> renderObjects,
        RenderPass pass,
        Shader passShader)
    {
        if (!RenderRuntimeCvars.AutoInstancingEnabled)
        {
            DrawRenderablesSequential(renderObjects, pass, passShader);
            return;
        }

        DrawRenderablesAutoInstanced(renderObjects, pass, passShader);
    }

    private void DrawRenderablesSequential(
        IReadOnlyList<RenderObject> renderObjects,
        RenderPass pass,
        Shader passShader)
    {
        foreach (var renderObject in renderObjects)
            DrawRenderObject(renderObject, pass, passShader);
    }

    private void DrawRenderablesAutoInstanced(
        IReadOnlyList<RenderObject> renderObjects,
        RenderPass pass,
        Shader passShader)
    {
        _instancedBatches.Clear();
        _instancingFallbackDraws.Clear();

        foreach (var renderObject in renderObjects)
        {
            if (!TryCreateRenderBatchKey(renderObject, pass, out var key))
            {
                _instancingFallbackDraws.Add(renderObject);
                continue;
            }

            if (!_instancedBatches.TryGetValue(key, out var batch))
            {
                batch = new InstancedBatchBucket(renderObject);
                _instancedBatches.Add(key, batch);
            }

            batch.AddInstance(Matrix4x4.Transpose(renderObject.WorldMatrix));
        }

        if (pass == RenderPass.Main)
        {
            _lastAutoInstancingBatchCount = _instancedBatches.Count;
            _lastAutoInstancingFallbackDrawCount = _instancingFallbackDraws.Count;
        }

        foreach (var fallback in _instancingFallbackDraws)
            DrawRenderObject(fallback, pass, passShader);

        foreach (var batch in _instancedBatches.Values)
        {
            if (batch.InstanceCount <= 1)
            {
                DrawRenderObject(batch.Representative, pass, passShader);
                continue;
            }

            if (pass == RenderPass.Main)
            {
                _lastAutoInstancingInstancedBatchCount++;
                _lastAutoInstancingInstancedInstanceCount += batch.InstanceCount;
            }

            DrawInstancedBatch(batch, pass, passShader);
        }
    }

    private void DrawRenderObject(RenderObject renderObject, RenderPass pass, Shader passShader)
    {
        if (!TryResolveModel(renderObject, out var model, out var meshResource, out var materialResource))
            return;

        if (pass == RenderPass.Main && renderObject.UsesSkinning)
            _frameSkinnedMeshCount++;

        var worldTransform = Matrix4x4.Transpose(renderObject.WorldMatrix);

        unsafe
        {
            for (int meshIndex = 0; meshIndex < model.MeshCount; meshIndex++)
            {
                int materialIndex = meshResource.MeshMaterialIndices.Length > meshIndex
                    ? meshResource.MeshMaterialIndices[meshIndex]
                    : 0;
                if (materialIndex < 0 || materialIndex >= model.MaterialCount)
                    continue;

                var mesh = model.Meshes[meshIndex];
                var material = model.Materials[materialIndex];
                var materialConfig = ResolveMaterialSlot(materialResource, materialIndex);
                ConfigureMaterialForPass(ref material, materialConfig, pass, passShader, renderObject.UsesSkinning, usesInstancing: false);
                Raylib.DrawMesh(mesh, material, worldTransform);
                if (pass == RenderPass.Main)
                    _frameDrawCallCount++;
            }
        }
    }

    private void DrawInstancedBatch(InstancedBatchBucket batch, RenderPass pass, Shader passShader)
    {
        if (!TryResolveModel(batch.Representative, out var model, out var meshResource, out var materialResource))
            return;

        if (model.MeshCount <= 0)
            return;

        var activeShader = pass == RenderPass.Main ? _lightingShader : passShader;
        int useInstancingLoc = pass == RenderPass.Main
            ? _lightingUseInstancingLoc
            : GetUseInstancingLocation(passShader);
        int previousMatrixModelLoc = GetShaderLocationFromLocs(activeShader, ShaderLocationIndex.MatrixModel);
        int instanceAttribLoc = GetInstanceTransformAttribLocation(activeShader);
        var transforms = batch.InstanceTransforms.ToArray();

        if (instanceAttribLoc >= 0)
            SetShaderLocationInLocs(activeShader, ShaderLocationIndex.MatrixModel, instanceAttribLoc);
        SetShaderBool(activeShader, useInstancingLoc, true);
        SetShaderBool(activeShader, GetUseSkinningLocation(activeShader), false);

        unsafe
        {
            for (int meshIndex = 0; meshIndex < model.MeshCount; meshIndex++)
            {
                int materialIndex = meshResource.MeshMaterialIndices.Length > meshIndex
                    ? meshResource.MeshMaterialIndices[meshIndex]
                    : 0;
                if (materialIndex < 0 || materialIndex >= model.MaterialCount)
                    continue;

                var mesh = model.Meshes[meshIndex];
                var material = model.Materials[materialIndex];
                var materialConfig = ResolveMaterialSlot(materialResource, materialIndex);
                ConfigureMaterialForPass(ref material, materialConfig, pass, passShader, usesSkinning: false, usesInstancing: true);
                Raylib.DrawMeshInstanced(mesh, material, transforms, transforms.Length);
                if (pass == RenderPass.Main)
                {
                    _frameDrawCallCount++;
                    _lastAutoInstancingInstancedMeshDrawCallCount++;
                }
            }
        }

        SetShaderBool(activeShader, useInstancingLoc, false);
        SetShaderBool(activeShader, GetUseSkinningLocation(activeShader), false);
        if (instanceAttribLoc >= 0)
            SetShaderLocationInLocs(activeShader, ShaderLocationIndex.MatrixModel, previousMatrixModelLoc);
    }

    private bool TryResolveModel(
        RenderObject renderObject,
        out Model model,
        out RenderResourceCache.MeshResource meshResource,
        out RenderResourceCache.MaterialResource? materialResource)
    {
        model = default;
        meshResource = null!;
        materialResource = _resources.GetMaterialResource(renderObject.MaterialHandle);

        var resolvedMesh = _resources.GetMeshResource(renderObject.MeshHandle);
        if (resolvedMesh == null)
            return false;

        meshResource = resolvedMesh;
        model = resolvedMesh.Model;

        if (renderObject.UsesSkinning && renderObject.SkinPaletteHandle.IsValid)
        {
            var skinned = _resources.GetSkinnedInstance(renderObject.SkinPaletteHandle);
            if (skinned == null)
                return false;

            model = skinned.Model;
        }

        return true;
    }

    private static Components.Material? ResolveMaterialSlot(RenderResourceCache.MaterialResource? materialResource, int materialIndex)
    {
        if (materialResource == null || materialResource.Slots.Length == 0)
            return null;

        int slotIndex = Math.Clamp(materialIndex, 0, materialResource.Slots.Length - 1);
        return materialResource.Slots[slotIndex];
    }

    private unsafe void ConfigureMaterialForPass(
        ref Raylib_cs.Material material,
        Components.Material? materialConfig,
        RenderPass pass,
        Shader passShader,
        bool usesSkinning,
        bool usesInstancing)
    {
        if (materialConfig != null)
            MaterialApplicator.ApplyToMaterial(ref material, materialConfig);

        if (pass == RenderPass.Depth)
        {
            material.Shader = passShader;
            SetShaderBool(passShader, GetUseInstancingLocation(passShader), usesInstancing);
            SetShaderBool(passShader, GetUseSkinningLocation(passShader), usesSkinning);
            return;
        }

        if (_shaderLoaded)
        {
            material.Shader = _lightingShader;
            material.Maps[(int)MaterialMapIndex.Occlusion].Texture = _lightDataTexture;
            material.Maps[(int)MaterialMapIndex.Emission].Texture = _tileHeaderTexture;
            material.Maps[(int)MaterialMapIndex.Height].Texture = _tileIndexTexture;
            SetShaderBool(_lightingShader, _lightingUseInstancingLoc, usesInstancing);
            SetShaderBool(_lightingShader, _lightingUseSkinningLoc, usesSkinning);
        }
    }

    private static bool TryCreateRenderBatchKey(RenderObject renderObject, RenderPass pass, out RenderBatchKey key)
    {
        if (!renderObject.SupportsInstancing || renderObject.UsesSkinning)
        {
            key = default;
            return false;
        }

        var variant = pass == RenderPass.Main ? RenderShaderVariant.Lit : RenderShaderVariant.Depth;
        key = new RenderBatchKey(renderObject.MeshHandle, renderObject.MaterialHandle, variant, UsesSkinning: false, UsesInstancing: true);
        return renderObject.MeshHandle.IsValid && renderObject.MaterialHandle.IsValid;
    }

    private static RenderTexture2D LoadHdrRenderTextureWithDepthTexture(int width, int height)
    {
        uint fboId = Rlgl.LoadFramebuffer();
        if (fboId == 0)
            return PostProcessing.PostProcessPipeline.LoadRenderTextureWithDepthTexture(width, height);

        Rlgl.EnableFramebuffer(fboId);

        uint colorId;
        unsafe
        {
            colorId = Rlgl.LoadTexture(null, width, height, PixelFormat.UncompressedR16G16B16A16, 1);
        }

        if (colorId == 0)
        {
            Rlgl.UnloadFramebuffer(fboId);
            return PostProcessing.PostProcessPipeline.LoadRenderTextureWithDepthTexture(width, height);
        }

        Rlgl.FramebufferAttach(fboId, colorId, FramebufferAttachType.ColorChannel0, FramebufferAttachTextureType.Texture2D, 0);

        uint depthId = Rlgl.LoadTextureDepth(width, height, false);
        Rlgl.FramebufferAttach(fboId, depthId, FramebufferAttachType.Depth, FramebufferAttachTextureType.Texture2D, 0);

        if (!Rlgl.FramebufferComplete(fboId))
        {
            Rlgl.UnloadTexture(colorId);
            Rlgl.UnloadTexture(depthId);
            Rlgl.UnloadFramebuffer(fboId);
            return PostProcessing.PostProcessPipeline.LoadRenderTextureWithDepthTexture(width, height);
        }

        Rlgl.DisableFramebuffer();

        return new RenderTexture2D
        {
            Id = fboId,
            Texture = new Texture2D
            {
                Id = colorId,
                Width = width,
                Height = height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR16G16B16A16
            },
            Depth = new Texture2D
            {
                Id = depthId,
                Width = width,
                Height = height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedGrayscale
            }
        };
    }

    private void UpdateForwardPlusData(RenderFrame frame, Camera3D camera, int viewportWidth, int viewportHeight)
    {
        EnsureForwardPlusResources(viewportWidth, viewportHeight);
        BuildFrameLights(frame, camera);
        BuildTileLightLists(camera);
        PackLightBuffer();
        PackTileHeaderBuffer();
        UploadForwardPlusBuffers();
        MaybeLogForwardPlusWarnings();
    }

    private void BindForwardPlusShaderData(int viewportWidth, int viewportHeight)
    {
        // Raylib sampler binding remains backend-specific. Texture binding is kept here
        // so renderer-facing code does not depend on material-map remapping details.

        SetShaderIVec2(_screenSizeLoc, viewportWidth, viewportHeight);
        SetShaderIVec2(_tileCountLoc, _tileCountX, _tileCountY);
        SetShaderIVec2(_lightDataTexSizeLoc, _lightDataTexWidth, _lightDataTexHeight);
        SetShaderIVec2(_tileHeaderTexSizeLoc, _tileHeaderTexWidth, _tileHeaderTexHeight);
        SetShaderIVec2(_tileIndexTexSizeLoc, _tileIndexTexWidth, _tileIndexTexHeight);

        if (_tileSizeLoc >= 0)
            Raylib.SetShaderValue(_lightingShader, _tileSizeLoc, _forwardPlusSettings.TileSize, ShaderUniformDataType.Int);
        if (_totalLightsLoc >= 0)
            Raylib.SetShaderValue(_lightingShader, _totalLightsLoc, _frameLights.Count, ShaderUniformDataType.Int);
    }

    private void BuildFrameLights(RenderFrame frame, Camera3D camera)
    {
        _frameLights.Clear();
        _pointCandidates.Clear();
        _forwardPlusDroppedTileLights = 0;
        _forwardPlusClippedLights = 0;

        var ambOvr = RenderRuntimeCvars.AmbientOverride;
        float[] ambient = ambOvr.HasValue
            ? new[] { ambOvr.Value.X, ambOvr.Value.Y, ambOvr.Value.Z, 1.0f }
            : new[] { 0.15f, 0.15f, 0.15f, 1.0f };
        bool skylightFound = false;
        int eligibleLights = 0;
        int visibleLights = 0;
        int skylightCount = 0;
        int directionalCount = 0;
        int pointCount = 0;

        var cameraPos = camera.Position;
        _lastSceneLightCount = frame.Lights.Count;

        foreach (var light in frame.Lights)
        {
            visibleLights++;

            if (light.LightType == LightType.Skylight)
            {
                skylightCount++;
                if (!skylightFound)
                {
                    skylightFound = true;
                    float intensity = light.Intensity;
                    ambient = new[]
                    {
                        light.Color.X * intensity,
                        light.Color.Y * intensity,
                        light.Color.Z * intensity,
                        1.0f
                    };
                }
                continue;
            }

            eligibleLights++;

            var packed = CreatePackedLight(light);
            if (packed.Type == PackedLightType.Directional)
            {
                directionalCount++;
                if (_frameLights.Count < _forwardPlusSettings.MaxLights)
                    _frameLights.Add(packed);
                continue;
            }

            pointCount++;
            var offset = packed.Position - cameraPos;
            var distanceSquared = offset.LengthSquared();
            TryInsertPointCandidate(new PointLightCandidate(packed, distanceSquared));
        }

        _pointCandidates.Sort(static (a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));
        foreach (var candidate in _pointCandidates)
        {
            if (_frameLights.Count >= _forwardPlusSettings.MaxLights)
                break;
            _frameLights.Add(candidate.Light);
        }

        _forwardPlusClippedLights = Math.Max(0, eligibleLights - _frameLights.Count);
        _lastVisibleLightCount = visibleLights;
        _lastSkylightCount = skylightCount;
        _lastDirectionalLightCount = directionalCount;
        _lastPointLightCount = pointCount;
        Raylib.SetShaderValue(_lightingShader, _ambientLoc, ambient, ShaderUniformDataType.Vec4);
    }

    private void TryInsertPointCandidate(PointLightCandidate candidate)
    {
        int directionalBudget = _frameLights.Count;
        int pointBudget = Math.Max(0, _forwardPlusSettings.MaxLights - directionalBudget);
        if (pointBudget == 0)
            return;

        if (_pointCandidates.Count < pointBudget)
        {
            _pointCandidates.Add(candidate);
            return;
        }

        int worstIndex = 0;
        float worstDistance = _pointCandidates[0].DistanceSquared;
        for (int i = 1; i < _pointCandidates.Count; i++)
        {
            if (_pointCandidates[i].DistanceSquared > worstDistance)
            {
                worstDistance = _pointCandidates[i].DistanceSquared;
                worstIndex = i;
            }
        }

        if (candidate.DistanceSquared >= worstDistance)
            return;

        _pointCandidates[worstIndex] = candidate;
    }

    private void BuildTileLightLists(Camera3D camera)
    {
        Array.Fill(_tileLightCounts, 0);
        Array.Fill(_tileLightScores, float.PositiveInfinity);
        Array.Fill(_tileIndexBuffer, -1f);

        for (int lightIndex = 0; lightIndex < _frameLights.Count; lightIndex++)
        {
            var light = _frameLights[lightIndex];
            if (light.Type == PackedLightType.Directional)
            {
                for (int tileIndex = 0; tileIndex < _tileCount; tileIndex++)
                    TryInsertTileLight(tileIndex, lightIndex, -1f);
                continue;
            }

            if (!TryProjectPointLight(camera, light.Position, light.Range, out var projected))
                continue;

            for (int ty = projected.MinTileY; ty <= projected.MaxTileY; ty++)
            {
                for (int tx = projected.MinTileX; tx <= projected.MaxTileX; tx++)
                {
                    int tileIndex = ty * _tileCountX + tx;
                    float tileCenterX = tx * _forwardPlusSettings.TileSize + _forwardPlusSettings.TileSize * 0.5f;
                    float tileCenterY = ty * _forwardPlusSettings.TileSize + _forwardPlusSettings.TileSize * 0.5f;
                    float dx = tileCenterX - projected.CenterPixelX;
                    float dy = tileCenterY - projected.CenterPixelY;
                    float score = dx * dx + dy * dy + projected.DepthScore;
                    TryInsertTileLight(tileIndex, lightIndex, score);
                }
            }
        }

        if (_tileCount > 0)
        {
            int sum = 0;
            int peak = 0;
            for (int i = 0; i < _tileCount; i++)
            {
                int count = _tileLightCounts[i];
                sum += count;
                if (count > peak)
                    peak = count;
            }

            _lastAverageLightsPerTile = sum / (float)_tileCount;
            _lastMaxLightsPerTile = peak;
        }
        else
        {
            _lastAverageLightsPerTile = 0f;
            _lastMaxLightsPerTile = 0;
        }

        _lastStatsValid = true;
    }

    private void TryInsertTileLight(int tileIndex, int lightIndex, float score)
    {
        int baseSlot = tileIndex * _forwardPlusSettings.MaxLightsPerTile;
        int count = _tileLightCounts[tileIndex];

        if (count < _forwardPlusSettings.MaxLightsPerTile)
        {
            int slot = baseSlot + count;
            SetTileIndexValue(slot, lightIndex);
            _tileLightScores[slot] = score;
            _tileLightCounts[tileIndex] = count + 1;
            return;
        }

        _forwardPlusDroppedTileLights++;

        int worstSlot = baseSlot;
        float worstScore = _tileLightScores[baseSlot];
        for (int i = 1; i < _forwardPlusSettings.MaxLightsPerTile; i++)
        {
            int slot = baseSlot + i;
            float currentScore = _tileLightScores[slot];
            if (currentScore > worstScore)
            {
                worstScore = currentScore;
                worstSlot = slot;
            }
        }

        if (score >= worstScore)
            return;

        SetTileIndexValue(worstSlot, lightIndex);
        _tileLightScores[worstSlot] = score;
    }

    private void SetTileIndexValue(int slot, int lightIndex)
    {
        int dataOffset = slot * 4;
        _tileIndexBuffer[dataOffset] = lightIndex;
        _tileIndexBuffer[dataOffset + 1] = 0f;
        _tileIndexBuffer[dataOffset + 2] = 0f;
        _tileIndexBuffer[dataOffset + 3] = 0f;
    }

    private void PackLightBuffer()
    {
        Array.Fill(_lightDataBuffer, 0f);

        for (int i = 0; i < _frameLights.Count; i++)
        {
            var light = _frameLights[i];
            int lightStartTexel = i * LightTexelsPerLight;

            WritePackedVec4(_lightDataBuffer, lightStartTexel + 0,
                (int)light.Type, 1f, light.Range, 0f);
            WritePackedVec4(_lightDataBuffer, lightStartTexel + 1,
                light.Position.X, light.Position.Y, light.Position.Z, 0f);
            WritePackedVec4(_lightDataBuffer, lightStartTexel + 2,
                light.Direction.X, light.Direction.Y, light.Direction.Z, 0f);
            WritePackedVec4(_lightDataBuffer, lightStartTexel + 3,
                light.Color.X, light.Color.Y, light.Color.Z, 1f);
        }
    }

    private void PackTileHeaderBuffer()
    {
        for (int tileIndex = 0; tileIndex < _tileCount; tileIndex++)
        {
            int start = tileIndex * _forwardPlusSettings.MaxLightsPerTile;
            int count = _tileLightCounts[tileIndex];
            WritePackedVec4(_tileHeaderBuffer, tileIndex, start, count, 0f, 0f);
        }
    }

    private void UploadForwardPlusBuffers()
    {
        if (_lightDataTexture.Id != 0)
            Raylib.UpdateTexture(_lightDataTexture, _lightDataBuffer);
        if (_tileHeaderTexture.Id != 0)
            Raylib.UpdateTexture(_tileHeaderTexture, _tileHeaderBuffer);
        if (_tileIndexTexture.Id != 0)
            Raylib.UpdateTexture(_tileIndexTexture, _tileIndexBuffer);
    }

    private void EnsureForwardPlusResources(int viewportWidth, int viewportHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0)
            return;

        bool viewportChanged = _viewportWidth != viewportWidth || _viewportHeight != viewportHeight;
        if (viewportChanged)
        {
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;
        }

        int requiredTileCountX = (_viewportWidth + _forwardPlusSettings.TileSize - 1) / _forwardPlusSettings.TileSize;
        int requiredTileCountY = (_viewportHeight + _forwardPlusSettings.TileSize - 1) / _forwardPlusSettings.TileSize;
        int requiredTileCount = Math.Max(1, requiredTileCountX * requiredTileCountY);

        bool tilesChanged = requiredTileCountX != _tileCountX
                            || requiredTileCountY != _tileCountY
                            || requiredTileCount != _tileCount;

        _tileCountX = requiredTileCountX;
        _tileCountY = requiredTileCountY;
        _tileCount = requiredTileCount;

        int requiredLightEntries = Math.Max(1, _forwardPlusSettings.MaxLights * LightTexelsPerLight);
        int requiredHeaderEntries = Math.Max(1, _tileCount);
        int requiredIndexEntries = Math.Max(1, _tileCount * _forwardPlusSettings.MaxLightsPerTile);

        EnsurePackedTexture(
            ref _lightDataTexture,
            ref _lightDataBuffer,
            ref _lightDataEntries,
            ref _lightDataTexWidth,
            ref _lightDataTexHeight,
            requiredLightEntries);

        bool tileStorageChanged = tilesChanged
                                  || requiredHeaderEntries != _tileHeaderEntries
                                  || requiredIndexEntries != _tileIndexEntries;

        EnsurePackedTexture(
            ref _tileHeaderTexture,
            ref _tileHeaderBuffer,
            ref _tileHeaderEntries,
            ref _tileHeaderTexWidth,
            ref _tileHeaderTexHeight,
            requiredHeaderEntries);

        EnsurePackedTexture(
            ref _tileIndexTexture,
            ref _tileIndexBuffer,
            ref _tileIndexEntries,
            ref _tileIndexTexWidth,
            ref _tileIndexTexHeight,
            requiredIndexEntries);

        if (tileStorageChanged || _tileLightCounts.Length != _tileCount)
        {
            _tileLightCounts = new int[_tileCount];
            _tileLightScores = new float[_tileCount * _forwardPlusSettings.MaxLightsPerTile];
        }
    }

    private void EnsurePackedTexture(
        ref Texture2D texture,
        ref float[] buffer,
        ref int entryCount,
        ref int textureWidth,
        ref int textureHeight,
        int requiredEntries)
    {
        if (requiredEntries <= 0)
            requiredEntries = 1;

        var (requiredWidth, requiredHeight) = ComputePackedTextureSize(requiredEntries);
        bool recreateTexture = texture.Id == 0
                               || requiredWidth != textureWidth
                               || requiredHeight != textureHeight;

        if (recreateTexture)
        {
            if (texture.Id != 0)
                Raylib.UnloadTexture(texture);

            texture = CreateFloatTexture(requiredWidth, requiredHeight);
            textureWidth = requiredWidth;
            textureHeight = requiredHeight;
        }

        int requiredBufferLength = requiredWidth * requiredHeight * 4;
        if (buffer.Length != requiredBufferLength)
            buffer = new float[requiredBufferLength];

        entryCount = requiredEntries;
    }

    private static (int Width, int Height) ComputePackedTextureSize(int entries)
    {
        entries = Math.Max(entries, 1);
        int width = Math.Min(PackedTextureMaxWidth, entries);
        int height = (entries + width - 1) / width;
        return (width, Math.Max(1, height));
    }

    private static unsafe Texture2D CreateFloatTexture(int width, int height)
    {
        var initial = new float[width * height * 4];
        fixed (float* data = initial)
        {
            uint textureId = Rlgl.LoadTexture(data, width, height, PixelFormat.UncompressedR32G32B32A32, 1);
            var texture = new Texture2D
            {
                Id = textureId,
                Width = width,
                Height = height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR32G32B32A32
            };

            if (texture.Id != 0)
            {
                Raylib.SetTextureFilter(texture, TextureFilter.Point);
                Raylib.SetTextureWrap(texture, TextureWrap.Clamp);
            }

            return texture;
        }
    }

    private static PackedLight CreatePackedLight(RenderLight light)
    {
        float intensity = light.Intensity;
        var color = light.Color * intensity;

        if (light.LightType == LightType.Directional)
        {
            var direction = light.Direction;
            if (direction.LengthSquared() > 1e-8f)
                direction = Vector3.Normalize(direction);
            else
                direction = -Vector3.UnitY;

            return new PackedLight(
                PackedLightType.Directional,
                Vector3.Zero,
                direction,
                color,
                0f);
        }

        return new PackedLight(
            PackedLightType.Point,
            light.Position,
            Vector3.Zero,
            color,
            MathF.Max(0f, light.Range));
    }

    private bool TryProjectPointLight(Camera3D camera, Vector3 position, float range, out ProjectedPointLight projected)
    {
        projected = default;
        if (range <= 0f || _tileCountX <= 0 || _tileCountY <= 0)
            return false;

        var view = Matrix4x4.CreateLookAt(camera.Position, camera.Target, camera.Up);
        var viewSpacePosition = Vector3.Transform(position, view);

        float aspect = MathF.Max(1e-5f, _viewportWidth / (float)Math.Max(1, _viewportHeight));
        float ndcX;
        float ndcY;
        float radiusNdcX;
        float radiusNdcY;
        float depthForScore;

        if (camera.Projection == CameraProjection.Perspective)
        {
            float depth = -viewSpacePosition.Z;
            if (depth <= -range)
                return false;

            depthForScore = MathF.Max(depth, MinPerspectiveDepth);

            float halfFovRad = MathF.Max(1e-4f, camera.FovY * 0.5f * (MathF.PI / 180f));
            float halfHeight = depthForScore * MathF.Tan(halfFovRad);
            if (halfHeight <= 1e-5f)
                return false;

            float halfWidth = halfHeight * aspect;
            ndcX = viewSpacePosition.X / halfWidth;
            ndcY = viewSpacePosition.Y / halfHeight;
            radiusNdcX = range / halfWidth;
            radiusNdcY = range / halfHeight;
        }
        else
        {
            float halfHeight = MathF.Max(0.01f, camera.FovY * 0.5f);
            float halfWidth = halfHeight * aspect;
            ndcX = viewSpacePosition.X / halfWidth;
            ndcY = viewSpacePosition.Y / halfHeight;
            radiusNdcX = range / halfWidth;
            radiusNdcY = range / halfHeight;
            depthForScore = 1f;
        }

        if (ndcX + radiusNdcX < -1f || ndcX - radiusNdcX > 1f || ndcY + radiusNdcY < -1f || ndcY - radiusNdcY > 1f)
            return false;

        float centerPixelX = (ndcX * 0.5f + 0.5f) * _viewportWidth;
        float centerPixelY = (-ndcY * 0.5f + 0.5f) * _viewportHeight;
        float radiusPixelsX = radiusNdcX * 0.5f * _viewportWidth;
        float radiusPixelsY = radiusNdcY * 0.5f * _viewportHeight;

        float minPixelX = centerPixelX - radiusPixelsX;
        float maxPixelX = centerPixelX + radiusPixelsX;
        float minPixelY = centerPixelY - radiusPixelsY;
        float maxPixelY = centerPixelY + radiusPixelsY;

        if (maxPixelX < 0f || minPixelX > _viewportWidth || maxPixelY < 0f || minPixelY > _viewportHeight)
            return false;

        int clampedMinPixelX = Math.Clamp((int)MathF.Floor(minPixelX), 0, Math.Max(0, _viewportWidth - 1));
        int clampedMaxPixelX = Math.Clamp((int)MathF.Ceiling(maxPixelX), 0, Math.Max(0, _viewportWidth - 1));
        int clampedMinPixelY = Math.Clamp((int)MathF.Floor(minPixelY), 0, Math.Max(0, _viewportHeight - 1));
        int clampedMaxPixelY = Math.Clamp((int)MathF.Ceiling(maxPixelY), 0, Math.Max(0, _viewportHeight - 1));

        if (clampedMinPixelX > clampedMaxPixelX || clampedMinPixelY > clampedMaxPixelY)
            return false;

        projected = new ProjectedPointLight(
            clampedMinPixelX / _forwardPlusSettings.TileSize,
            clampedMaxPixelX / _forwardPlusSettings.TileSize,
            clampedMinPixelY / _forwardPlusSettings.TileSize,
            clampedMaxPixelY / _forwardPlusSettings.TileSize,
            centerPixelX,
            centerPixelY,
            depthForScore * depthForScore * 0.001f);
        return true;
    }

    private void MaybeLogForwardPlusWarnings()
    {
        _frameCounter++;
        if ((_forwardPlusClippedLights <= 0 && _forwardPlusDroppedTileLights <= 0)
            || _frameCounter % ForwardPlusWarningLogIntervalFrames != 0)
            return;

        FrinkyLog.Warning(
            $"Forward+ budget pressure: clippedLights={_forwardPlusClippedLights}, droppedTileLinks={_forwardPlusDroppedTileLights}, " +
            $"maxLights={_forwardPlusSettings.MaxLights}, maxLightsPerTile={_forwardPlusSettings.MaxLightsPerTile}, " +
            $"tiles={_tileCountX}x{_tileCountY}");
    }

    private static void WritePackedVec4(float[] buffer, int texelIndex, float x, float y, float z, float w)
    {
        int dataIndex = texelIndex * 4;
        buffer[dataIndex + 0] = x;
        buffer[dataIndex + 1] = y;
        buffer[dataIndex + 2] = z;
        buffer[dataIndex + 3] = w;
    }

    private void SetShaderIVec2(int location, int x, int y)
    {
        if (location < 0)
            return;

        Span<int> vec2 = stackalloc int[2];
        vec2[0] = x;
        vec2[1] = y;
        Raylib.SetShaderValue(_lightingShader, location, vec2, ShaderUniformDataType.IVec2);
    }

    private void ReleaseForwardPlusTextures()
    {
        if (_lightDataTexture.Id != 0)
            Raylib.UnloadTexture(_lightDataTexture);
        if (_tileHeaderTexture.Id != 0)
            Raylib.UnloadTexture(_tileHeaderTexture);
        if (_tileIndexTexture.Id != 0)
            Raylib.UnloadTexture(_tileIndexTexture);

        _lightDataTexture = default;
        _tileHeaderTexture = default;
        _tileIndexTexture = default;
    }

    private int GetUseInstancingLocation(Shader shader)
    {
        if (shader.Id == 0)
            return -1;

        if (_useInstancingLocationCache.TryGetValue(shader.Id, out var cached))
            return cached;

        int loc = Raylib.GetShaderLocation(shader, "useInstancing");
        _useInstancingLocationCache[shader.Id] = loc;
        return loc;
    }

    private int GetUseSkinningLocation(Shader shader)
    {
        if (shader.Id == 0)
            return -1;

        if (_useSkinningLocationCache.TryGetValue(shader.Id, out var cached))
            return cached;

        int loc = Raylib.GetShaderLocation(shader, "useSkinning");
        _useSkinningLocationCache[shader.Id] = loc;
        return loc;
    }

    private int GetInstanceTransformAttribLocation(Shader shader)
    {
        if (shader.Id == 0)
            return -1;

        if (_instanceTransformAttribLocationCache.TryGetValue(shader.Id, out var cached))
            return cached;

        int loc = Raylib.GetShaderLocationAttrib(shader, "instanceTransform");
        _instanceTransformAttribLocationCache[shader.Id] = loc;
        return loc;
    }

    private static unsafe int GetShaderLocationFromLocs(Shader shader, ShaderLocationIndex index)
    {
        if (shader.Locs == null)
            return -1;

        return shader.Locs[(int)index];
    }

    private static unsafe void SetShaderLocationInLocs(Shader shader, ShaderLocationIndex index, int location)
    {
        if (shader.Locs == null)
            return;

        shader.Locs[(int)index] = location;
    }

    private static void SetShaderBool(Shader shader, int location, bool enabled)
    {
        if (shader.Id == 0 || location < 0)
            return;

        int value = enabled ? 1 : 0;
        Raylib.SetShaderValue(shader, location, value, ShaderUniformDataType.Int);
    }

    private enum RenderPass
    {
        Main = 0,
        Depth = 1
    }

    private readonly record struct RenderBatchKey(
        RenderMeshHandle MeshHandle,
        RenderMaterialHandle MaterialHandle,
        RenderShaderVariant ShaderVariant,
        bool UsesSkinning,
        bool UsesInstancing);

    private sealed class InstancedBatchBucket
    {
        public InstancedBatchBucket(RenderObject representative)
        {
            Representative = representative;
        }

        public RenderObject Representative { get; }
        public List<Matrix4x4> InstanceTransforms { get; } = new();
        public int InstanceCount => InstanceTransforms.Count;

        public void AddInstance(Matrix4x4 transform)
        {
            InstanceTransforms.Add(transform);
        }
    }

    private enum PackedLightType
    {
        Directional = 0,
        Point = 1
    }

    private readonly record struct PackedLight(
        PackedLightType Type,
        Vector3 Position,
        Vector3 Direction,
        Vector3 Color,
        float Range);

    private readonly record struct PointLightCandidate(PackedLight Light, float DistanceSquared);

    private readonly record struct ProjectedPointLight(
        int MinTileX,
        int MaxTileX,
        int MinTileY,
        int MaxTileY,
        float CenterPixelX,
        float CenterPixelY,
        float DepthScore);
}
