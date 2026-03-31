using FrinkyEngine.Editor.Tests.TestSupport;

namespace FrinkyEngine.Editor.Tests;

public sealed class EditorPreferencesTests
{
    [Fact]
    public void LoadConfig_MissingFileCreatesDefaultsFile()
    {
        using var temp = new TempDirectory();
        var preferences = EditorPreferences.Instance;
        preferences.AssetBrowserGridView = false;

        preferences.LoadConfig(temp.Path);

        var path = Path.Combine(temp.Path, ".frinky", "editor_preferences.json");
        File.Exists(path).Should().BeTrue();
        File.ReadAllText(path).Should().Contain("assetBrowserGridView");
    }

    [Fact]
    public void LoadConfig_InvalidJsonKeepsCurrentState()
    {
        using var temp = new TempDirectory();
        var preferences = EditorPreferences.Instance;
        preferences.AssetBrowserGridView = false;

        var configDir = Path.Combine(temp.Path, ".frinky");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, "editor_preferences.json"), "{ broken");

        preferences.LoadConfig(temp.Path);

        preferences.AssetBrowserGridView.Should().BeFalse();
    }
}
