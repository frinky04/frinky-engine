using System.Text.Json;

namespace FrinkyEngine.Core.Tests.Assets;

public sealed class ExportManifestTests
{
    [Fact]
    public void ToJsonAndFromJson_RoundTripRuntimePhysicsAndAudioFields()
    {
        var manifest = new ExportManifest
        {
            ProjectName = "Orbit",
            ProductName = "Orbit Build",
            DefaultScene = "Assets/Scenes/Main.fscene",
            GameAssembly = "GameAssembly/Orbit.dll",
            TargetFps = 144,
            VSync = false,
            WindowTitle = "Orbit",
            ForwardPlusTileSize = 32,
            ForwardPlusMaxLights = 512,
            ForwardPlusMaxLightsPerTile = 128,
            PhysicsFixedTimestep = 1f / 120f,
            PhysicsDefaultFriction = 0.6f,
            PhysicsInterpolationEnabled = false,
            AudioMasterVolume = 0.7f,
            AudioMaxVoices = 96,
            AudioEnableVoiceStealing = false,
            ScreenPercentage = 85
        };

        var json = manifest.ToJson();
        var restored = ExportManifest.FromJson(json);

        restored.ProjectName.Should().Be("Orbit");
        restored.ProductName.Should().Be("Orbit Build");
        restored.DefaultScene.Should().Be("Assets/Scenes/Main.fscene");
        restored.GameAssembly.Should().Be("GameAssembly/Orbit.dll");
        restored.TargetFps.Should().Be(144);
        restored.ForwardPlusTileSize.Should().Be(32);
        restored.PhysicsFixedTimestep.Should().BeApproximately(1f / 120f, 0.0001f);
        restored.AudioMasterVolume.Should().Be(0.7f);
        restored.ScreenPercentage.Should().Be(85);
    }

    [Fact]
    public void FromJson_BackwardCompatibleWithMinimalManifest()
    {
        const string json = """
            {
              "projectName": "Legacy",
              "defaultScene": "Assets/Scenes/Main.fscene"
            }
            """;

        var manifest = ExportManifest.FromJson(json);

        manifest.ProjectName.Should().Be("Legacy");
        manifest.DefaultScene.Should().Be("Assets/Scenes/Main.fscene");
        manifest.TargetFps.Should().BeNull();
        manifest.AudioMasterVolume.Should().BeNull();
    }
}
