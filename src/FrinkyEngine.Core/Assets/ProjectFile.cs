using System.Text.Json;

namespace FrinkyEngine.Core.Assets;

/// <summary>
/// Represents a <c>.fproject</c> file that defines a FrinkyEngine game project's configuration.
/// </summary>
public class ProjectFile
{
    /// <summary>
    /// Display name of the project.
    /// </summary>
    public string ProjectName { get; set; } = "Untitled";

    /// <summary>
    /// Asset-relative path to the scene loaded on startup.
    /// </summary>
    public string DefaultScene { get; set; } = "Scenes/MainScene.fscene";

    /// <summary>
    /// Relative path to the assets root directory.
    /// </summary>
    public string AssetsPath { get; set; } = "Assets";

    /// <summary>
    /// Relative path to the compiled game assembly DLL, or empty if none.
    /// </summary>
    public string GameAssembly { get; set; } = string.Empty;

    /// <summary>
    /// Relative path to the game's <c>.csproj</c> file, used for building before play/export.
    /// </summary>
    public string GameProject { get; set; } = string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Loads a project file from disk.
    /// </summary>
    /// <param name="path">Path to the <c>.fproject</c> file.</param>
    /// <returns>The deserialized project file.</returns>
    public static ProjectFile Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ProjectFile>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize project file.");
    }

    /// <summary>
    /// Saves this project file to disk as JSON.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Resolves the absolute path to the assets directory from a project root.
    /// </summary>
    /// <param name="projectDir">Absolute path to the project directory.</param>
    /// <returns>The absolute path to the assets folder.</returns>
    public string GetAbsoluteAssetsPath(string projectDir)
    {
        return Path.GetFullPath(Path.Combine(projectDir, AssetsPath));
    }

    /// <summary>
    /// Resolves the absolute path to the default scene file from a project root.
    /// </summary>
    /// <param name="projectDir">Absolute path to the project directory.</param>
    /// <returns>The absolute path to the default scene file.</returns>
    public string GetAbsoluteScenePath(string projectDir)
    {
        return Path.GetFullPath(Path.Combine(projectDir, AssetsPath, DefaultScene));
    }

    /// <summary>
    /// Resolves the absolute path to the game's compiled assembly DLL from a project root.
    /// </summary>
    /// <param name="projectDir">Absolute path to the project directory.</param>
    /// <returns>The absolute path to the game assembly DLL.</returns>
    public string GetAbsoluteGameAssemblyPath(string projectDir)
    {
        return Path.GetFullPath(Path.Combine(projectDir, GameAssembly));
    }

    /// <summary>
    /// Resolves the absolute path to the game's <c>.csproj</c> file from a project root.
    /// </summary>
    /// <param name="projectDir">Absolute path to the project directory.</param>
    /// <returns>The absolute path to the game project file.</returns>
    public string GetAbsoluteGameProjectPath(string projectDir)
    {
        return Path.GetFullPath(Path.Combine(projectDir, GameProject));
    }
}
