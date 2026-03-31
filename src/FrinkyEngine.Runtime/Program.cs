using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.Audio;
using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.Physics;
using FrinkyEngine.Core.Rendering;
using FrinkyEngine.Core.Rendering.PostProcessing;
using FrinkyEngine.Core.Rendering.Profiling;
using FrinkyEngine.Core.Scene;
using FrinkyEngine.Core.Scripting;
using FrinkyEngine.Core.CanvasUI;
using FrinkyEngine.Core.UI;
using Raylib_cs;

namespace FrinkyEngine.Runtime;

public static class Program
{
    private sealed class RuntimeLaunchSettings
    {
        public int TargetFps { get; init; } = 60;
        public bool VSync { get; init; }
        public int WindowWidth { get; init; } = 1280;
        public int WindowHeight { get; init; } = 720;
        public bool Resizable { get; init; } = true;
        public bool Fullscreen { get; init; }
        public bool StartMaximized { get; init; }
        public int ForwardPlusTileSize { get; init; } = ForwardPlusSettings.DefaultTileSize;
        public int ForwardPlusMaxLights { get; init; } = ForwardPlusSettings.DefaultMaxLights;
        public int ForwardPlusMaxLightsPerTile { get; init; } = ForwardPlusSettings.DefaultMaxLightsPerTile;
        public int ScreenPercentage { get; init; } = 100;
    }

    public static void Main(string[] args)
    {
        var target = RuntimeStartupResolver.ResolveLaunchTarget(args, AppContext.BaseDirectory);
        if (target.Mode == RuntimeLaunchMode.DevelopmentProject && target.Path != null)
        {
            RunDevMode(target.Path);
        }
        else if (target.Mode == RuntimeLaunchMode.ExportedArchive && target.Path != null)
        {
            RunExportedMode(target.Path);
        }
        else
        {
            Console.WriteLine("Usage: FrinkyEngine.Runtime <path-to-.fproject>");
            Console.WriteLine("  Or place a .fasset file next to the executable.");
        }
    }

    private static void RunDevMode(string fprojectPath)
    {
        var startup = RuntimeStartupResolver.ResolveDevelopmentStartup(fprojectPath, AppContext.BaseDirectory);

        AssetManager.Instance.AssetsPath = startup.AssetsPath;
        AssetDatabase.Instance.Scan(AssetManager.Instance.AssetsPath);
        AssetManager.Instance.EngineContentPath = startup.EngineContentPath;
        AssetDatabase.Instance.ScanEngineContent(startup.EngineContentPath);
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(fprojectPath))!;
        var project = ProjectFile.Load(fprojectPath);
        var settings = ProjectSettings.LoadOrCreate(projectDir, project.ProjectName);
        PhysicsProjectSettings.ApplyFrom(settings.Runtime);
        AudioProjectSettings.ApplyFrom(settings.Runtime);

        var assemblyLoader = new GameAssemblyLoader();
        if (!string.IsNullOrEmpty(startup.GameAssemblyPath))
            assemblyLoader.LoadAssembly(startup.GameAssemblyPath);

        RunGameLoop("Shaders/lighting.vs", "Shaders/lighting.fs",
            startup.ScenePath, assemblyLoader, startup.WindowTitle, new RuntimeLaunchSettings
            {
                TargetFps = startup.TargetFps,
                VSync = startup.VSync,
                WindowWidth = startup.WindowWidth,
                WindowHeight = startup.WindowHeight,
                Resizable = startup.Resizable,
                Fullscreen = startup.Fullscreen,
                StartMaximized = startup.StartMaximized,
                ForwardPlusTileSize = startup.ForwardPlusTileSize,
                ForwardPlusMaxLights = startup.ForwardPlusMaxLights,
                ForwardPlusMaxLightsPerTile = startup.ForwardPlusMaxLightsPerTile,
                ScreenPercentage = startup.ScreenPercentage
            });
    }

    private static void RunExportedMode(string fassetPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"FrinkyRuntime_{Guid.NewGuid():N}");

        try
        {
            FAssetArchive.ExtractAll(fassetPath, tempDir);

            var startup = RuntimeStartupResolver.ResolveExportedStartup(tempDir);
            var manifest = ExportManifest.FromJson(File.ReadAllText(Path.Combine(tempDir, "manifest.json")));

            AssetManager.Instance.AssetsPath = startup.AssetsPath;
            AssetDatabase.Instance.Scan(AssetManager.Instance.AssetsPath);

            AssetManager.Instance.EngineContentPath = startup.EngineContentPath;
            AssetDatabase.Instance.ScanEngineContent(startup.EngineContentPath);

            PhysicsProjectSettings.ApplyFrom(manifest);
            AudioProjectSettings.ApplyFrom(manifest);

            var assemblyLoader = new GameAssemblyLoader();
            if (!string.IsNullOrEmpty(startup.GameAssemblyPath))
            {
                if (File.Exists(startup.GameAssemblyPath))
                    assemblyLoader.LoadAssembly(startup.GameAssemblyPath);
            }

            var shaderVs = Path.Combine(tempDir, "Shaders", "lighting.vs");
            var shaderFs = Path.Combine(tempDir, "Shaders", "lighting.fs");
            RunGameLoop(shaderVs, shaderFs, startup.ScenePath, assemblyLoader, startup.WindowTitle,
                new RuntimeLaunchSettings
                {
                    TargetFps = startup.TargetFps,
                    VSync = startup.VSync,
                    WindowWidth = startup.WindowWidth,
                    WindowHeight = startup.WindowHeight,
                    Resizable = startup.Resizable,
                    Fullscreen = startup.Fullscreen,
                    StartMaximized = startup.StartMaximized,
                    ForwardPlusTileSize = startup.ForwardPlusTileSize,
                    ForwardPlusMaxLights = startup.ForwardPlusMaxLights,
                    ForwardPlusMaxLightsPerTile = startup.ForwardPlusMaxLightsPerTile,
                    ScreenPercentage = startup.ScreenPercentage
                });
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    private static void RunGameLoop(string shaderVsPath, string shaderFsPath,
        string scenePath, GameAssemblyLoader assemblyLoader, string runtimeWindowTitle, RuntimeLaunchSettings launchSettings)
    {
        RaylibLogger.Install();
        AudioDeviceService.EnsureInitialized();
        launchSettings = SanitizeLaunchSettings(launchSettings);
        var flags = ConfigFlags.Msaa4xHint;
        if (launchSettings.Resizable)
            flags |= ConfigFlags.ResizableWindow;
        if (launchSettings.VSync)
            flags |= ConfigFlags.VSyncHint;
        if (launchSettings.Fullscreen)
            flags |= ConfigFlags.FullscreenMode;
        if (launchSettings.StartMaximized)
            flags |= ConfigFlags.MaximizedWindow;

        Raylib.SetConfigFlags(flags);
        Raylib.InitWindow(launchSettings.WindowWidth, launchSettings.WindowHeight, runtimeWindowTitle);
        Raylib.SetTargetFPS(launchSettings.TargetFps);
        UI.Initialize();
        CanvasUI.Initialize();

        var sceneRenderer = new SceneRenderer();
        sceneRenderer.LoadShader(shaderVsPath, shaderFsPath);
        EngineOverlays.Renderer = sceneRenderer;
        sceneRenderer.ConfigureForwardPlus(new ForwardPlusSettings(
            launchSettings.ForwardPlusTileSize,
            launchSettings.ForwardPlusMaxLights,
            launchSettings.ForwardPlusMaxLightsPerTile));

        var scene = SceneManager.Instance.LoadScene(scenePath);

        if (scene == null)
        {
            Console.WriteLine($"Failed to load scene: {scenePath}");
            CanvasUI.Shutdown();
            UI.Shutdown();
            AudioDeviceService.ShutdownIfUnused();
            Raylib.CloseWindow();
            return;
        }

        scene.Start();
        Raylib.DisableCursor();

        var postProcessPipeline = new PostProcessPipeline();
        var shaderDir = Path.GetDirectoryName(shaderVsPath) ?? "Shaders";
        postProcessPipeline.Initialize(shaderDir);

        RenderTexture2D sceneRT = default;
        int lastScaledW = 0;
        int lastScaledH = 0;

        RenderRuntimeCvars.ScreenPercentage = launchSettings.ScreenPercentage;

        FrameProfiler.Enabled = true;

        while (!Raylib.WindowShouldClose())
        {
            var activeScene = SceneManager.Instance.ActiveScene;
            if (activeScene != null && !ReferenceEquals(activeScene, scene))
            {
                scene = activeScene;
                scene.Start();
            }

            float dt = Raylib.GetFrameTime();
            FrameProfiler.BeginFrame();
            scene.Update(dt);
            EngineOverlays.Update(dt);

            var mainCamera = scene.MainCamera;
            if (mainCamera == null)
            {
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.Black);
                Raylib.DrawText("No MainCamera found in scene.", 10, 10, 20, Color.Red);
                UI.BeginFrame(dt, new UiFrameDesc(
                    Raylib.GetScreenWidth(),
                    Raylib.GetScreenHeight(),
                    IsFocused: Raylib.IsWindowFocused(),
                    IsHovered: true,
                    AllowCursorChanges: false));
                UI.EndFrame();
                FrameProfiler.EndFrame();
                FrameProfiler.BeginIdle();
                Raylib.EndDrawing();
                FrameProfiler.EndIdle();
                continue;
            }

            int screenW = Raylib.GetScreenWidth();
            int screenH = Raylib.GetScreenHeight();
            var (scaledW, scaledH) = RenderRuntimeCvars.GetScaledDimensions(screenW, screenH);

            if (scaledW != lastScaledW || scaledH != lastScaledH)
            {
                if (lastScaledW > 0)
                    Raylib.UnloadRenderTexture(sceneRT);
                sceneRT = PostProcessPipeline.LoadRenderTextureWithDepthTexture(scaledW, scaledH);
                bool isSupersampled = scaledW >= screenW && scaledH >= screenH;
                Raylib.SetTextureFilter(sceneRT.Texture, isSupersampled ? TextureFilter.Bilinear : TextureFilter.Point);
                lastScaledW = scaledW;
                lastScaledH = scaledH;
            }

            var camera3D = mainCamera.BuildCamera3D();
            var ppStack = mainCamera.Entity.GetComponent<PostProcessStackComponent>();
            bool hasPostProcess = ppStack != null
                                  && RenderRuntimeCvars.PostProcessingEnabled
                                  && ppStack.PostProcessingEnabled
                                  && ppStack.Effects.Count > 0;

            using (FrameProfiler.Scope(ProfileCategory.Rendering))
            {
                sceneRenderer.Render(scene, camera3D, sceneRT, isEditorMode: false);
            }

            // TODO: Route runtime post-processing and final UI composite through SceneRenderer once the render graph owns the full frame.
            Texture2D blitTex;
            if (hasPostProcess)
            {
                var depthTexture = sceneRT.Depth;
                if (sceneRenderer.TryGetLastViewRenderTexture(out var sceneViewTarget) && sceneViewTarget.Depth.Id != 0)
                    depthTexture = sceneViewTarget.Depth;

                blitTex = postProcessPipeline.Execute(
                    ppStack!,
                    sceneRT.Texture,
                    camera3D,
                    mainCamera,
                    sceneRenderer,
                    scene,
                    scaledW, scaledH,
                    isEditorMode: false,
                    depthTexture);
            }
            else
            {
                blitTex = sceneRT.Texture;
            }

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            var src = new Rectangle(0, 0, blitTex.Width, -blitTex.Height);
            var dst = new Rectangle(0, 0, screenW, screenH);
            Raylib.DrawTexturePro(blitTex, src, dst, System.Numerics.Vector2.Zero, 0f, Color.White);
            using (FrameProfiler.Scope(ProfileCategory.UI))
            {
                UI.BeginFrame(dt, new UiFrameDesc(screenW, screenH, IsFocused: Raylib.IsWindowFocused(), IsHovered: true, AllowCursorChanges: false));
                UI.EndFrame();
                CanvasUI.Update(dt, screenW, screenH);
            }
            FrameProfiler.EndFrame();
            FrameProfiler.BeginIdle();
            Raylib.EndDrawing();
            FrameProfiler.EndIdle();
        }

        CanvasUI.Shutdown();
        UI.Shutdown();
        postProcessPipeline.Shutdown();
        if (lastScaledW > 0)
            Raylib.UnloadRenderTexture(sceneRT);
        sceneRenderer.UnloadShader();
        AssetManager.Instance.UnloadAll();
        assemblyLoader.Unload();
        AudioDeviceService.ShutdownIfUnused();
        Raylib.CloseWindow();
    }

    private static RuntimeLaunchSettings SanitizeLaunchSettings(RuntimeLaunchSettings settings)
    {
        static int ClampOrDefault(int value, int min, int max, int fallback)
        {
            if (value < min || value > max)
                return fallback;
            return value;
        }

        return new RuntimeLaunchSettings
        {
            TargetFps = settings.TargetFps == 0 ? 0 : ClampOrDefault(settings.TargetFps, 30, 500, 120),
            VSync = settings.VSync,
            WindowWidth = ClampOrDefault(settings.WindowWidth, 320, 10000, 1280),
            WindowHeight = ClampOrDefault(settings.WindowHeight, 200, 10000, 720),
            Resizable = settings.Resizable,
            Fullscreen = settings.Fullscreen,
            StartMaximized = settings.StartMaximized,
            ForwardPlusTileSize = ClampOrDefault(settings.ForwardPlusTileSize, 8, 64, ForwardPlusSettings.DefaultTileSize),
            ForwardPlusMaxLights = ClampOrDefault(settings.ForwardPlusMaxLights, 16, 2048, ForwardPlusSettings.DefaultMaxLights),
            ForwardPlusMaxLightsPerTile = ClampOrDefault(settings.ForwardPlusMaxLightsPerTile, 8, 256, ForwardPlusSettings.DefaultMaxLightsPerTile),
            ScreenPercentage = ClampOrDefault(settings.ScreenPercentage, 10, 200, 100)
        };
    }
}
