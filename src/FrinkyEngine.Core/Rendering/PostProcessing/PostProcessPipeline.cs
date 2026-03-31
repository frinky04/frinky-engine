using System.Numerics;
using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.Rendering.Profiling;
using Raylib_cs;

namespace FrinkyEngine.Core.Rendering.PostProcessing;

/// <summary>
/// Manages the post-processing render pipeline: ping-pong render targets, depth pre-pass,
/// temporary RT pool, and the per-frame effect execution loop.
/// </summary>
public class PostProcessPipeline
{
    // TODO: Move this pipeline onto backend-neutral render-target handles once the renderer graph owns post and final composite.
    private RenderTexture2D _pingRT;
    private RenderTexture2D _pongRT;
    private RenderTexture2D _depthRT;
    private Shader _depthShader;
    private bool _depthShaderLoaded;
    private int _depthNearPlaneLoc = -1;
    private int _depthFarPlaneLoc = -1;
    private Shader _linearizeShader;
    private bool _linearizeShaderLoaded;
    private int _linearizeDepthTexLoc = -1;
    private int _linearizeNearPlaneLoc = -1;
    private int _linearizeFarPlaneLoc = -1;
    private int _width;
    private int _height;
    private string _shaderBasePath = "Shaders";
    private readonly PostProcessContext _context = new();

    /// <summary>
    /// Initializes the pipeline, loading the depth pre-pass shader from the given directory.
    /// Safe to call every frame — only loads resources on the first call.
    /// </summary>
    /// <param name="shaderBasePath">Directory containing engine shaders (e.g. "Shaders").</param>
    public void Initialize(string shaderBasePath)
    {
        _shaderBasePath = shaderBasePath;

        if (_depthShaderLoaded)
            return;

        var vsPath = Path.Combine(shaderBasePath, "depth_prepass.vs");
        var fsPath = Path.Combine(shaderBasePath, "depth_prepass.fs");

        if (File.Exists(vsPath) && File.Exists(fsPath))
        {
            _depthShader = Raylib.LoadShader(vsPath, fsPath);
            _depthShaderLoaded = _depthShader.Id != 0;

            if (_depthShaderLoaded)
            {
                _depthNearPlaneLoc = Raylib.GetShaderLocation(_depthShader, "nearPlane");
                _depthFarPlaneLoc = Raylib.GetShaderLocation(_depthShader, "farPlane");
            }
        }

        var ppVsPath = Path.Combine(shaderBasePath, "postprocess.vs");
        var linearizeFsPath = Path.Combine(shaderBasePath, "depth_linearize.fs");

        if (File.Exists(ppVsPath) && File.Exists(linearizeFsPath))
        {
            _linearizeShader = Raylib.LoadShader(ppVsPath, linearizeFsPath);
            _linearizeShaderLoaded = _linearizeShader.Id != 0;

            if (_linearizeShaderLoaded)
            {
                _linearizeDepthTexLoc = Raylib.GetShaderLocation(_linearizeShader, "depthTex");
                _linearizeNearPlaneLoc = Raylib.GetShaderLocation(_linearizeShader, "nearPlane");
                _linearizeFarPlaneLoc = Raylib.GetShaderLocation(_linearizeShader, "farPlane");
            }
        }
    }

    /// <summary>
    /// Ensures ping-pong and depth render textures match the current viewport size.
    /// </summary>
    /// <param name="width">Viewport width.</param>
    /// <param name="height">Viewport height.</param>
    public void EnsureResources(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (width == _width && height == _height) return;

        if (_width > 0)
        {
            Raylib.UnloadRenderTexture(_pingRT);
            Raylib.UnloadRenderTexture(_pongRT);
            if (_depthRT.Id != 0)
                Raylib.UnloadRenderTexture(_depthRT);
        }

        _context.DisposePool();
        _pingRT = Raylib.LoadRenderTexture(width, height);
        _pongRT = Raylib.LoadRenderTexture(width, height);
        _depthRT = LoadFloatRenderTexture(width, height);

        Raylib.SetTextureWrap(_pingRT.Texture, TextureWrap.Clamp);
        Raylib.SetTextureWrap(_pongRT.Texture, TextureWrap.Clamp);

        _width = width;
        _height = height;
    }

    /// <summary>
    /// Executes the post-processing stack on the given scene color texture.
    /// Returns the final post-processed texture, or the original if no effects run.
    /// </summary>
    /// <param name="stack">The post-process stack component on the camera.</param>
    /// <param name="sceneColor">The rendered scene color texture.</param>
    /// <param name="camera">The camera used for rendering.</param>
    /// <param name="cam">Optional camera component for near/far plane data.</param>
    /// <param name="sceneRenderer">Scene renderer for depth pre-pass.</param>
    /// <param name="scene">The current scene.</param>
    /// <param name="width">Viewport width.</param>
    /// <param name="height">Viewport height.</param>
    /// <param name="isEditorMode">Whether we're in editor mode.</param>
    /// <param name="sceneDepthTexture">Optional hardware depth texture from the scene RT. When provided, depth is linearized via a fullscreen blit instead of a geometry re-render.</param>
    /// <returns>The final output texture.</returns>
    public Texture2D Execute(
        PostProcessStackComponent stack,
        Texture2D sceneColor,
        Camera3D camera,
        CameraComponent? cam,
        SceneRenderer? sceneRenderer,
        Scene.Scene? scene,
        int width,
        int height,
        bool isEditorMode,
        Texture2D sceneDepthTexture = default)
    {
        if (!stack.PostProcessingEnabled)
            return sceneColor;

        var enabledEffects = new List<PostProcessEffect>();
        foreach (var effect in stack.Effects)
        {
            if (effect.Enabled)
                enabledEffects.Add(effect);
        }

        if (enabledEffects.Count == 0)
            return sceneColor;

        EnsureResources(width, height);

        // Initialize any uninitialized effects
        foreach (var effect in enabledEffects)
        {
            if (!effect.IsInitialized)
            {
                try { effect.Initialize(_shaderBasePath); }
                catch (Exception ex)
                {
                    FrinkyLog.Error($"Failed to initialize post-process effect '{effect.DisplayName}': {ex.Message}");
                }
            }
        }

        // Check if any effect needs depth
        bool needsDepth = false;
        foreach (var effect in enabledEffects)
        {
            if (effect.NeedsDepth)
            {
                needsDepth = true;
                break;
            }
        }

        // Produce linear depth if needed
        float nearPlane = cam?.NearPlane ?? 0.1f;
        float farPlane = cam?.FarPlane ?? 1000f;

        if (needsDepth)
        {
            if (sceneDepthTexture.Id != 0 && _linearizeShaderLoaded)
            {
                // Fast path: linearize the existing hardware depth texture via a fullscreen blit
                using (FrameProfiler.ScopeNamed(ProfileCategory.PostProcessing, "Depth Linearize"))
                {
                    if (_linearizeNearPlaneLoc >= 0)
                        Raylib.SetShaderValue(_linearizeShader, _linearizeNearPlaneLoc, nearPlane, ShaderUniformDataType.Float);
                    if (_linearizeFarPlaneLoc >= 0)
                        Raylib.SetShaderValue(_linearizeShader, _linearizeFarPlaneLoc, farPlane, ShaderUniformDataType.Float);

                    if (_linearizeDepthTexLoc >= 0)
                    {
                        Raylib.SetShaderValue(_linearizeShader, _linearizeDepthTexLoc, 1, ShaderUniformDataType.Int);
                        Rlgl.ActiveTextureSlot(1);
                        Rlgl.EnableTexture(sceneDepthTexture.Id);
                    }

                    PostProcessContext.Blit(sceneColor, _depthRT, _linearizeShader);

                    Rlgl.ActiveTextureSlot(1);
                    Rlgl.DisableTexture();
                    Rlgl.ActiveTextureSlot(0);
                }
            }
            else if (_depthShaderLoaded && sceneRenderer != null && scene != null)
            {
                // Fallback: full geometry re-render depth pre-pass
                if (_depthNearPlaneLoc >= 0)
                    Raylib.SetShaderValue(_depthShader, _depthNearPlaneLoc, nearPlane, ShaderUniformDataType.Float);
                if (_depthFarPlaneLoc >= 0)
                    Raylib.SetShaderValue(_depthShader, _depthFarPlaneLoc, farPlane, ShaderUniformDataType.Float);

                sceneRenderer.RenderDepthPrePass(scene, camera, _depthRT, _depthShader, isEditorMode);
            }
        }

        // Set up context
        _context.Width = width;
        _context.Height = height;
        _context.DepthTexture = needsDepth ? _depthRT.Texture : default;
        _context.NearPlane = nearPlane;
        _context.FarPlane = farPlane;
        _context.CameraPosition = camera.Position;
        _context.FieldOfViewDegrees = camera.FovY;
        _context.AspectRatio = (float)width / height;

        // Ping-pong execution
        Texture2D currentSource = sceneColor;
        bool writeToPing = true;

        int passCount = 0;
        for (int i = 0; i < enabledEffects.Count; i++)
        {
            var effect = enabledEffects[i];
            if (!effect.IsInitialized)
                continue;

            var dest = writeToPing ? _pingRT : _pongRT;

            try
            {
                using (FrameProfiler.ScopeNamed(ProfileCategory.PostProcessing, effect.DisplayName))
                {
                    effect.Render(currentSource, dest, _context);
                }
                passCount++;
            }
            catch (Exception ex)
            {
                FrinkyLog.Error($"Post-process effect '{effect.DisplayName}' failed: {ex.Message}");
                _context.ReleaseTemporaryRTs();
                continue;
            }

            _context.ReleaseTemporaryRTs();
            currentSource = dest.Texture;
            writeToPing = !writeToPing;
        }

        FrameProfiler.ReportGpuStats(new GpuFrameStats(0, passCount, 0));

        return currentSource;
    }

    /// <summary>
    /// Shuts down the pipeline, releasing all GPU resources.
    /// </summary>
    public void Shutdown()
    {
        _context.DisposePool();

        if (_width > 0)
        {
            Raylib.UnloadRenderTexture(_pingRT);
            Raylib.UnloadRenderTexture(_pongRT);
            if (_depthRT.Id != 0)
                Raylib.UnloadRenderTexture(_depthRT);
            _width = 0;
            _height = 0;
        }

        if (_depthShaderLoaded)
        {
            Raylib.UnloadShader(_depthShader);
            _depthShaderLoaded = false;
        }

        if (_linearizeShaderLoaded)
        {
            Raylib.UnloadShader(_linearizeShader);
            _linearizeShaderLoaded = false;
        }
    }

    /// <summary>
    /// Creates a render texture with 16-bit float (half) precision per channel.
    /// Falls back to standard 8-bit RT if float format is not supported.
    /// </summary>
    private static RenderTexture2D LoadFloatRenderTexture(int width, int height)
    {
        uint fboId = Rlgl.LoadFramebuffer();
        if (fboId == 0)
            return Raylib.LoadRenderTexture(width, height);

        Rlgl.EnableFramebuffer(fboId);

        uint texId;
        unsafe
        {
            texId = Rlgl.LoadTexture(null, width, height, PixelFormat.UncompressedR16G16B16A16, 1);
        }

        if (texId == 0)
        {
            Rlgl.UnloadFramebuffer(fboId);
            return Raylib.LoadRenderTexture(width, height);
        }

        Rlgl.FramebufferAttach(fboId, texId,
            FramebufferAttachType.ColorChannel0,
            FramebufferAttachTextureType.Texture2D, 0);

        uint depthId = Rlgl.LoadTextureDepth(width, height, true);
        Rlgl.FramebufferAttach(fboId, depthId,
            FramebufferAttachType.Depth,
            FramebufferAttachTextureType.Renderbuffer, 0);

        if (!Rlgl.FramebufferComplete(fboId))
        {
            Rlgl.UnloadTexture(texId);
            Rlgl.UnloadFramebuffer(fboId);
            return Raylib.LoadRenderTexture(width, height);
        }

        Rlgl.DisableFramebuffer();

        var rt = new RenderTexture2D
        {
            Id = fboId,
            Texture = new Texture2D
            {
                Id = texId,
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

        Raylib.SetTextureFilter(rt.Texture, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(rt.Texture, TextureWrap.Clamp);

        return rt;
    }

    /// <summary>
    /// Creates a render texture whose depth attachment is a sampleable texture instead of
    /// a renderbuffer. This allows post-processing effects to read the scene depth directly
    /// via a fullscreen linearize blit, eliminating the need for a geometry depth pre-pass.
    /// </summary>
    /// <param name="width">Texture width.</param>
    /// <param name="height">Texture height.</param>
    /// <returns>A render texture with a sampleable depth texture attachment.</returns>
    public static RenderTexture2D LoadRenderTextureWithDepthTexture(int width, int height)
    {
        uint fboId = Rlgl.LoadFramebuffer();
        if (fboId == 0)
            return Raylib.LoadRenderTexture(width, height);

        Rlgl.EnableFramebuffer(fboId);

        // Color attachment — standard RGBA8
        uint colorId;
        unsafe
        {
            colorId = Rlgl.LoadTexture(null, width, height, PixelFormat.UncompressedR8G8B8A8, 1);
        }

        if (colorId == 0)
        {
            Rlgl.UnloadFramebuffer(fboId);
            return Raylib.LoadRenderTexture(width, height);
        }

        Rlgl.FramebufferAttach(fboId, colorId,
            FramebufferAttachType.ColorChannel0,
            FramebufferAttachTextureType.Texture2D, 0);

        // Depth attachment — texture (not renderbuffer) so it can be sampled
        uint depthId = Rlgl.LoadTextureDepth(width, height, false);
        Rlgl.FramebufferAttach(fboId, depthId,
            FramebufferAttachType.Depth,
            FramebufferAttachTextureType.Texture2D, 0);

        if (!Rlgl.FramebufferComplete(fboId))
        {
            Rlgl.UnloadTexture(colorId);
            Rlgl.UnloadTexture(depthId);
            Rlgl.UnloadFramebuffer(fboId);
            return Raylib.LoadRenderTexture(width, height);
        }

        Rlgl.DisableFramebuffer();

        var rt = new RenderTexture2D
        {
            Id = fboId,
            Texture = new Texture2D
            {
                Id = colorId,
                Width = width,
                Height = height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR8G8B8A8
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

        Raylib.SetTextureFilter(rt.Texture, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(rt.Texture, TextureWrap.Clamp);
        Raylib.SetTextureFilter(rt.Depth, TextureFilter.Point);
        Raylib.SetTextureWrap(rt.Depth, TextureWrap.Clamp);

        return rt;
    }
}
