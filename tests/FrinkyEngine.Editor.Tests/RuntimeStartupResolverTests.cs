using FrinkyEngine.Runtime;
using FrinkyEngine.Editor.Tests.TestSupport;

namespace FrinkyEngine.Editor.Tests;

public sealed class RuntimeStartupResolverTests
{
    [Fact]
    public void ResolveLaunchTarget_PrefersProjectArgumentAndFallsBackToArchive()
    {
        using var temp = new TempDirectory();
        var projectPath = Path.Combine(temp.Path, "Game.fproject");
        File.WriteAllText(projectPath, "{}");
        File.WriteAllText(Path.Combine(temp.Path, "Build.fasset"), "archive");

        var fromArg = RuntimeStartupResolver.ResolveLaunchTarget([projectPath], temp.Path);
        var fromArchive = RuntimeStartupResolver.ResolveLaunchTarget([], temp.Path);

        fromArg.Mode.Should().Be(RuntimeLaunchMode.DevelopmentProject);
        fromArg.Path.Should().Be(projectPath);
        fromArchive.Mode.Should().Be(RuntimeLaunchMode.ExportedArchive);
        fromArchive.Path.Should().Be(Path.Combine(temp.Path, "Build.fasset"));
    }

    [Fact]
    public void ResolveDevelopmentStartup_UsesProjectSettingsOverridesAndProjectPaths()
    {
        using var temp = new TempDirectory();
        var projectDir = temp.Path;
        var projectPath = Path.Combine(projectDir, "Orbit.fproject");
        Directory.CreateDirectory(Path.Combine(projectDir, "Assets", "Scenes"));

        var project = new ProjectFile
        {
            ProjectName = "Orbit",
            AssetsPath = "Assets",
            DefaultScene = "Scenes/Main.fscene",
            GameAssembly = "bin/Debug/net8.0/Orbit.dll"
        };
        project.Save(projectPath);

        var settings = ProjectSettings.GetDefault("Orbit");
        settings.Runtime.StartupSceneOverride = "Scenes/Override.fscene";
        settings.Runtime.WindowTitle = "Orbit Window";
        settings.Runtime.TargetFps = 144;
        settings.Save(ProjectSettings.GetPath(projectDir));

        var startup = RuntimeStartupResolver.ResolveDevelopmentStartup(projectPath, "/runtime-base");

        startup.AssetsPath.Should().Be(Path.GetFullPath(Path.Combine(projectDir, "Assets")));
        startup.ScenePath.Should().Be(Path.GetFullPath(Path.Combine(projectDir, "Assets", "Scenes", "Override.fscene")));
        startup.GameAssemblyPath.Should().Be(Path.GetFullPath(Path.Combine(projectDir, "bin", "Debug", "net8.0", "Orbit.dll")));
        startup.EngineContentPath.Should().Be(Path.Combine("/runtime-base", "EngineContent"));
        startup.WindowTitle.Should().Be("Orbit Window");
        startup.TargetFps.Should().Be(144);
    }

    [Fact]
    public void ResolveExportedStartup_UsesManifestValuesAndValidatesRequiredFiles()
    {
        using var temp = new TempDirectory();
        var extractedDir = temp.Path;
        Directory.CreateDirectory(Path.Combine(extractedDir, "Assets", "Scenes"));

        var manifest = new ExportManifest
        {
            ProjectName = "Orbit",
            ProductName = "Orbit Build",
            DefaultScene = "Assets/Scenes/Main.fscene",
            GameAssembly = "GameAssembly/Orbit.dll",
            TargetFps = 165,
            VSync = false,
            ScreenPercentage = 90
        };

        File.WriteAllText(Path.Combine(extractedDir, "manifest.json"), manifest.ToJson());
        File.WriteAllText(Path.Combine(extractedDir, "Assets", "Scenes", "Main.fscene"), "{}");

        var startup = RuntimeStartupResolver.ResolveExportedStartup(extractedDir);

        startup.ScenePath.Should().Be(Path.Combine(extractedDir, "Assets", "Scenes", "Main.fscene"));
        startup.AssetsPath.Should().Be(Path.Combine(extractedDir, "Assets"));
        startup.EngineContentPath.Should().Be(Path.Combine(extractedDir, "EngineContent"));
        startup.GameAssemblyPath.Should().Be(Path.Combine(extractedDir, "GameAssembly/Orbit.dll"));
        startup.WindowTitle.Should().Be("Orbit Build");
        startup.TargetFps.Should().Be(165);
        startup.VSync.Should().BeFalse();
        startup.ScreenPercentage.Should().Be(90);
    }

    [Fact]
    public void ResolveExportedStartup_MissingManifestOrDefaultSceneThrows()
    {
        using var temp = new TempDirectory();

        Action missingManifest = () => RuntimeStartupResolver.ResolveExportedStartup(temp.Path);
        missingManifest.Should().Throw<FileNotFoundException>();

        var manifest = new ExportManifest
        {
            ProjectName = "Orbit",
            DefaultScene = "Assets/Scenes/Missing.fscene"
        };
        File.WriteAllText(Path.Combine(temp.Path, "manifest.json"), manifest.ToJson());

        Action missingScene = () => RuntimeStartupResolver.ResolveExportedStartup(temp.Path);
        missingScene.Should().Throw<FileNotFoundException>();
    }
}
