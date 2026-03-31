using System.Text.Json;
using FrinkyEngine.Core.Tests.TestSupport;

namespace FrinkyEngine.Core.Tests.Assets;

public sealed class ProjectSettingsTests
{
    [Fact]
    public void LoadOrCreate_MissingFileCreatesNormalizedDefaults()
    {
        using var temp = new TempDirectory();

        var settings = ProjectSettings.LoadOrCreate(temp.Path, "Orbit");

        settings.Runtime.WindowTitle.Should().Be("Orbit");
        settings.Build.OutputName.Should().Be("Orbit");
        settings.ResolveStartupScene("Assets\\Scenes\\MainScene.fscene").Should().Be("Scenes/MainScene.fscene");
        File.Exists(ProjectSettings.GetPath(temp.Path)).Should().BeTrue();
    }

    [Fact]
    public void Load_InvalidJsonFallsBackToDefaults()
    {
        using var temp = new TempDirectory();
        var path = ProjectSettings.GetPath(temp.Path);
        File.WriteAllText(path, "{ broken");

        var settings = ProjectSettings.Load(path, "BrokenGame");

        settings.Runtime.WindowTitle.Should().Be("BrokenGame");
        settings.Build.OutputName.Should().Be("BrokenGame");
        settings.Project.Version.Should().Be("0.1.0");
    }

    [Fact]
    public void Normalize_FixesNullSectionsAndClampsValues()
    {
        var settings = new ProjectSettings
        {
            Project = null!,
            Runtime = new RuntimeProjectSettings
            {
                TargetFps = 9999,
                WindowWidth = 100,
                WindowHeight = 100,
                StartupSceneOverride = @"Assets\Scenes\Intro.fscene",
                PhysicsFixedTimestep = float.NaN,
                AudioMasterVolume = -5f,
                ScreenPercentage = 999
            },
            Build = null!
        };

        settings.Normalize("Clamped");

        settings.Project.Should().NotBeNull();
        settings.Runtime.TargetFps.Should().Be(120);
        settings.Runtime.WindowWidth.Should().Be(1280);
        settings.Runtime.WindowHeight.Should().Be(720);
        settings.Runtime.StartupSceneOverride.Should().Be("Scenes/Intro.fscene");
        settings.Runtime.PhysicsFixedTimestep.Should().BeApproximately(1f / 60f, 0.0001f);
        settings.Runtime.AudioMasterVolume.Should().Be(1f);
        settings.Runtime.ScreenPercentage.Should().Be(100);
        settings.Build.OutputName.Should().Be("Clamped");
    }

    [Fact]
    public void ResolveStartupScene_PrefersOverrideAndNormalizesAssetsPrefix()
    {
        var settings = ProjectSettings.GetDefault("Orbit");
        settings.Runtime.StartupSceneOverride = @"Assets\Scenes\Boss.fscene";

        settings.ResolveStartupScene("Scenes/Main.fscene").Should().Be("Scenes/Boss.fscene");
    }

    [Fact]
    public void Clone_CreatesDeepCopy()
    {
        var original = ProjectSettings.GetDefault("Orbit");
        original.Project.Author = "Alice";
        original.Runtime.WindowTitle = "Orbit Runtime";
        original.Build.OutputName = "OrbitBuild";

        var clone = original.Clone();
        clone.Project.Author = "Bob";
        clone.Runtime.WindowTitle = "Different";
        clone.Build.OutputName = "Other";

        original.Project.Author.Should().Be("Alice");
        original.Runtime.WindowTitle.Should().Be("Orbit Runtime");
        original.Build.OutputName.Should().Be("OrbitBuild");
    }
}
