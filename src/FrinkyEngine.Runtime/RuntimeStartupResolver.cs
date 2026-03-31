using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.Rendering;

namespace FrinkyEngine.Runtime;

internal enum RuntimeLaunchMode
{
    Usage = 0,
    DevelopmentProject = 1,
    ExportedArchive = 2
}

internal sealed record RuntimeLaunchTarget(RuntimeLaunchMode Mode, string? Path = null);

internal sealed record RuntimeStartupInfo(
    string ScenePath,
    string AssetsPath,
    string EngineContentPath,
    string? GameAssemblyPath,
    string WindowTitle,
    int TargetFps,
    bool VSync,
    int WindowWidth,
    int WindowHeight,
    bool Resizable,
    bool Fullscreen,
    bool StartMaximized,
    int ForwardPlusTileSize,
    int ForwardPlusMaxLights,
    int ForwardPlusMaxLightsPerTile,
    int ScreenPercentage);

internal static class RuntimeStartupResolver
{
    internal static RuntimeLaunchTarget ResolveLaunchTarget(string[] args, string baseDirectory)
    {
        if (args.Length > 0 &&
            File.Exists(args[0]) &&
            args[0].EndsWith(".fproject", StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeLaunchTarget(RuntimeLaunchMode.DevelopmentProject, args[0]);
        }

        var fassetPath = FindFassetNextToDirectory(baseDirectory);
        return fassetPath != null
            ? new RuntimeLaunchTarget(RuntimeLaunchMode.ExportedArchive, fassetPath)
            : new RuntimeLaunchTarget(RuntimeLaunchMode.Usage);
    }

    internal static RuntimeStartupInfo ResolveDevelopmentStartup(string fprojectPath, string baseDirectory)
    {
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(fprojectPath))!;
        var project = ProjectFile.Load(fprojectPath);
        var settings = ProjectSettings.LoadOrCreate(projectDir, project.ProjectName);
        var sceneRelativePath = settings.ResolveStartupScene(project.DefaultScene);
        var assetsPath = project.GetAbsoluteAssetsPath(projectDir);
        var scenePath = Path.GetFullPath(Path.Combine(assetsPath, sceneRelativePath));
        var gameAssemblyPath = string.IsNullOrWhiteSpace(project.GameAssembly)
            ? null
            : project.GetAbsoluteGameAssemblyPath(projectDir);
        var engineContentPath = Path.Combine(baseDirectory, "EngineContent");

        return new RuntimeStartupInfo(
            scenePath,
            assetsPath,
            engineContentPath,
            gameAssemblyPath,
            string.IsNullOrWhiteSpace(settings.Runtime.WindowTitle) ? project.ProjectName : settings.Runtime.WindowTitle,
            settings.Runtime.TargetFps,
            settings.Runtime.VSync,
            settings.Runtime.WindowWidth,
            settings.Runtime.WindowHeight,
            settings.Runtime.Resizable,
            settings.Runtime.Fullscreen,
            settings.Runtime.StartMaximized,
            settings.Runtime.ForwardPlusTileSize,
            settings.Runtime.ForwardPlusMaxLights,
            settings.Runtime.ForwardPlusMaxLightsPerTile,
            settings.Runtime.ScreenPercentage);
    }

    internal static RuntimeStartupInfo ResolveExportedStartup(string extractedDirectory)
    {
        var manifestPath = Path.Combine(extractedDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Exported runtime manifest was not found.", manifestPath);

        var manifest = ExportManifest.FromJson(File.ReadAllText(manifestPath));
        var scenePath = Path.Combine(extractedDirectory, manifest.DefaultScene);
        if (!File.Exists(scenePath))
            throw new FileNotFoundException("Exported runtime default scene was not found.", scenePath);

        var gameAssemblyPath = string.IsNullOrWhiteSpace(manifest.GameAssembly)
            ? null
            : Path.Combine(extractedDirectory, manifest.GameAssembly);

        return new RuntimeStartupInfo(
            scenePath,
            Path.Combine(extractedDirectory, "Assets"),
            Path.Combine(extractedDirectory, "EngineContent"),
            gameAssemblyPath,
            !string.IsNullOrWhiteSpace(manifest.WindowTitle)
                ? manifest.WindowTitle
                : (!string.IsNullOrWhiteSpace(manifest.ProductName) ? manifest.ProductName : manifest.ProjectName),
            manifest.TargetFps ?? 120,
            manifest.VSync ?? true,
            manifest.WindowWidth ?? 1280,
            manifest.WindowHeight ?? 720,
            manifest.Resizable ?? true,
            manifest.Fullscreen ?? false,
            manifest.StartMaximized ?? false,
            manifest.ForwardPlusTileSize ?? ForwardPlusSettings.DefaultTileSize,
            manifest.ForwardPlusMaxLights ?? ForwardPlusSettings.DefaultMaxLights,
            manifest.ForwardPlusMaxLightsPerTile ?? ForwardPlusSettings.DefaultMaxLightsPerTile,
            manifest.ScreenPercentage ?? 100);
    }

    internal static string? FindFassetNextToDirectory(string baseDirectory)
    {
        var fassetFiles = Directory.GetFiles(baseDirectory, "*.fasset");
        return fassetFiles.Length > 0 ? fassetFiles[0] : null;
    }
}
