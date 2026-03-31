using FrinkyEngine.Editor.Tests.TestSupport;

namespace FrinkyEngine.Editor.Tests;

public sealed class GameExporterTests
{
    [Fact]
    public async Task ExportAsync_WithRuntimeTemplateBuildsOutputAndArchiveManifest()
    {
        using var temp = new TempDirectory();
        var projectDir = Path.Combine(temp.Path, "Project");
        var assetsDir = Path.Combine(projectDir, "Assets");
        var runtimeTemplateDir = Path.Combine(temp.Path, "RuntimeTemplate");
        var outputDir = Path.Combine(temp.Path, "Output");
        Directory.CreateDirectory(Path.Combine(assetsDir, "Scenes"));
        Directory.CreateDirectory(runtimeTemplateDir);
        Directory.CreateDirectory(Path.Combine(runtimeTemplateDir, "Shaders"));
        Directory.CreateDirectory(Path.Combine(projectDir, "bin", "Debug", "net8.0"));

        File.WriteAllText(Path.Combine(assetsDir, "Scenes", "Main.fscene"), "{\"name\":\"Main\"}");
        File.WriteAllText(Path.Combine(runtimeTemplateDir, "FrinkyEngine.Runtime.exe"), "runtime");
        File.WriteAllText(Path.Combine(runtimeTemplateDir, "FrinkyEngine.Runtime.dll"), "runtime dll");
        File.WriteAllText(Path.Combine(runtimeTemplateDir, "Shaders", "lighting.vs"), "// vs");
        File.WriteAllText(Path.Combine(runtimeTemplateDir, "Shaders", "lighting.fs"), "// fs");
        File.WriteAllText(Path.Combine(projectDir, "bin", "Debug", "net8.0", "OrbitGame.dll"), "game dll");
        File.WriteAllText(Path.Combine(projectDir, "bin", "Debug", "net8.0", "OrbitGame.Dependency.dll"), "dep");

        var projectSettings = ProjectSettings.GetDefault("Orbit");
        projectSettings.Build.OutputName = "Arcade/Build";
        projectSettings.Build.BuildVersion = "1.2.3";
        projectSettings.Runtime.StartupSceneOverride = "Scenes/Main.fscene";
        projectSettings.Runtime.WindowTitle = "Orbit Window";

        var config = new ExportConfig
        {
            ProjectName = "Orbit",
            ProjectDirectory = projectDir,
            AssetsPath = assetsDir,
            DefaultScene = "Scenes/Unused.fscene",
            GameAssemblyDll = "bin/Debug/net8.0/OrbitGame.dll",
            RuntimeTemplateDirectory = runtimeTemplateDir,
            OutputDirectory = outputDir,
            ProjectSettings = projectSettings
        };

        var result = await GameExporter.ExportAsync(config);

        result.Should().BeTrue();
        var outputExe = Path.Combine(outputDir, "Arcade_Build.exe");
        var outputArchive = Path.Combine(outputDir, "Arcade_Build.fasset");
        File.Exists(outputExe).Should().BeTrue();
        File.Exists(outputArchive).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "FrinkyEngine.Runtime.dll")).Should().BeTrue();

        var extractDir = Path.Combine(temp.Path, "Extracted");
        FAssetArchive.ExtractAll(outputArchive, extractDir);

        var manifest = ExportManifest.FromJson(File.ReadAllText(Path.Combine(extractDir, "manifest.json")));
        manifest.ProductName.Should().Be("Arcade_Build");
        manifest.BuildVersion.Should().Be("1.2.3");
        manifest.DefaultScene.Should().Be("Assets/Scenes/Main.fscene");
        manifest.WindowTitle.Should().Be("Orbit Window");
        manifest.GameAssembly.Should().Be("GameAssembly/OrbitGame.dll");
        File.Exists(Path.Combine(extractDir, "Assets", "Scenes", "Main.fscene")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "Shaders", "lighting.vs")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "GameAssembly", "OrbitGame.dll")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "GameAssembly", "OrbitGame.Dependency.dll")).Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_ReturnsFalseWhenRuntimeCannotBePrepared()
    {
        using var temp = new TempDirectory();
        var assetsDir = Path.Combine(temp.Path, "Assets");
        Directory.CreateDirectory(assetsDir);

        var result = await GameExporter.ExportAsync(new ExportConfig
        {
            ProjectName = "Orbit",
            ProjectDirectory = temp.Path,
            AssetsPath = assetsDir,
            DefaultScene = "Scenes/Main.fscene",
            OutputDirectory = Path.Combine(temp.Path, "Output")
        });

        result.Should().BeFalse();
    }
}
