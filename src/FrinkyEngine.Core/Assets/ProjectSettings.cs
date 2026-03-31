using System.Text.Json;

namespace FrinkyEngine.Core.Assets;

/// <summary>
/// Persisted project settings stored in <c>project_settings.json</c> alongside the <c>.fproject</c> file.
/// Covers metadata, runtime configuration, and build options.
/// </summary>
public class ProjectSettings
{
    /// <summary>
    /// The settings file name on disk.
    /// </summary>
    public const string FileName = "project_settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Metadata about the project (version, author, etc.).
    /// </summary>
    public ProjectMetadataSettings Project { get; set; } = new();

    /// <summary>
    /// Runtime behavior settings (FPS, window size, Forward+ config, etc.).
    /// </summary>
    public RuntimeProjectSettings Runtime { get; set; } = new();

    /// <summary>
    /// Build and export settings (output name, version).
    /// </summary>
    public BuildProjectSettings Build { get; set; } = new();

    /// <summary>
    /// Gets the full path to the settings file within a project directory.
    /// </summary>
    /// <param name="projectDirectory">Absolute path to the project directory.</param>
    /// <returns>The full path to the settings file.</returns>
    public static string GetPath(string projectDirectory)
    {
        return Path.Combine(projectDirectory, FileName);
    }

    /// <summary>
    /// Creates a <see cref="ProjectSettings"/> populated with sensible defaults.
    /// </summary>
    /// <param name="projectName">Project name used for window title and output name.</param>
    /// <returns>A new settings instance with default values.</returns>
    public static ProjectSettings GetDefault(string projectName)
    {
        var normalizedProjectName = string.IsNullOrWhiteSpace(projectName) ? "Untitled" : projectName.Trim();

        return new ProjectSettings
        {
            Project = new ProjectMetadataSettings
            {
                Version = "0.1.0",
                Author = string.Empty,
                Company = string.Empty,
                Description = string.Empty
            },
            Runtime = new RuntimeProjectSettings
            {
                TargetFps = 120,
                VSync = true,
                WindowTitle = normalizedProjectName,
                WindowWidth = 1280,
                WindowHeight = 720,
                Resizable = true,
                Fullscreen = false,
                StartMaximized = false,
                StartupSceneOverride = string.Empty,
                ForwardPlusTileSize = 16,
                ForwardPlusMaxLights = 256,
                ForwardPlusMaxLightsPerTile = 64,
                PhysicsFixedTimestep = 1f / 60f,
                PhysicsMaxSubstepsPerFrame = 4,
                PhysicsSolverVelocityIterations = 8,
                PhysicsSolverSubsteps = 1,
                PhysicsContactSpringFrequency = 30f,
                PhysicsContactDampingRatio = 1f,
                PhysicsMaximumRecoveryVelocity = 2f,
                PhysicsDefaultFriction = 0.8f,
                PhysicsDefaultRestitution = 0f,
                PhysicsInterpolationEnabled = true,
                AudioMasterVolume = 1f,
                AudioMusicVolume = 1f,
                AudioSfxVolume = 1f,
                AudioUiVolume = 1f,
                AudioVoiceVolume = 1f,
                AudioAmbientVolume = 1f,
                AudioMaxVoices = 128,
                AudioDopplerScale = 1f,
                AudioEnableVoiceStealing = true,
                ScreenPercentage = 100
            },
            Build = new BuildProjectSettings
            {
                OutputName = normalizedProjectName,
                BuildVersion = "0.1.0"
            }
        };
    }

    /// <summary>
    /// Loads settings from disk, or creates and saves defaults if the file doesn't exist.
    /// </summary>
    /// <param name="projectDirectory">Absolute path to the project directory.</param>
    /// <param name="projectName">Fallback project name if none is stored.</param>
    /// <returns>The loaded or newly created settings.</returns>
    public static ProjectSettings LoadOrCreate(string projectDirectory, string? projectName = null)
    {
        var path = GetPath(projectDirectory);
        var fallbackProjectName = ResolveProjectName(projectName, projectDirectory);

        if (File.Exists(path))
            return Load(path, fallbackProjectName);

        var defaults = GetDefault(fallbackProjectName);
        defaults.Normalize(fallbackProjectName);
        defaults.Save(path);
        return defaults;
    }

    /// <summary>
    /// Loads settings from the specified file path, falling back to defaults on error.
    /// </summary>
    /// <param name="path">Path to the settings JSON file.</param>
    /// <param name="projectName">Fallback project name for normalization.</param>
    /// <returns>The loaded settings.</returns>
    public static ProjectSettings Load(string path, string? projectName = null)
    {
        var defaultProjectName = ResolveProjectName(projectName, Path.GetDirectoryName(path));

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<ProjectSettings>(json, JsonOptions)
                           ?? GetDefault(defaultProjectName);
            settings.Normalize(defaultProjectName);
            return settings;
        }
        catch
        {
            var fallback = GetDefault(defaultProjectName);
            fallback.Normalize(defaultProjectName);
            return fallback;
        }
    }

    /// <summary>
    /// Saves these settings to disk as JSON.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    public void Save(string path)
    {
        var defaultProjectName = ResolveProjectName(null, Path.GetDirectoryName(path));
        Normalize(defaultProjectName);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Creates a deep copy of these settings.
    /// </summary>
    /// <returns>A new <see cref="ProjectSettings"/> with the same values.</returns>
    public ProjectSettings Clone()
    {
        return new ProjectSettings
        {
            Project = new ProjectMetadataSettings
            {
                Version = Project.Version,
                Author = Project.Author,
                Company = Project.Company,
                Description = Project.Description
            },
            Runtime = new RuntimeProjectSettings
            {
                TargetFps = Runtime.TargetFps,
                VSync = Runtime.VSync,
                WindowTitle = Runtime.WindowTitle,
                WindowWidth = Runtime.WindowWidth,
                WindowHeight = Runtime.WindowHeight,
                Resizable = Runtime.Resizable,
                Fullscreen = Runtime.Fullscreen,
                StartMaximized = Runtime.StartMaximized,
                StartupSceneOverride = Runtime.StartupSceneOverride,
                ForwardPlusTileSize = Runtime.ForwardPlusTileSize,
                ForwardPlusMaxLights = Runtime.ForwardPlusMaxLights,
                ForwardPlusMaxLightsPerTile = Runtime.ForwardPlusMaxLightsPerTile,
                PhysicsFixedTimestep = Runtime.PhysicsFixedTimestep,
                PhysicsMaxSubstepsPerFrame = Runtime.PhysicsMaxSubstepsPerFrame,
                PhysicsSolverVelocityIterations = Runtime.PhysicsSolverVelocityIterations,
                PhysicsSolverSubsteps = Runtime.PhysicsSolverSubsteps,
                PhysicsContactSpringFrequency = Runtime.PhysicsContactSpringFrequency,
                PhysicsContactDampingRatio = Runtime.PhysicsContactDampingRatio,
                PhysicsMaximumRecoveryVelocity = Runtime.PhysicsMaximumRecoveryVelocity,
                PhysicsDefaultFriction = Runtime.PhysicsDefaultFriction,
                PhysicsDefaultRestitution = Runtime.PhysicsDefaultRestitution,
                PhysicsInterpolationEnabled = Runtime.PhysicsInterpolationEnabled,
                AudioMasterVolume = Runtime.AudioMasterVolume,
                AudioMusicVolume = Runtime.AudioMusicVolume,
                AudioSfxVolume = Runtime.AudioSfxVolume,
                AudioUiVolume = Runtime.AudioUiVolume,
                AudioVoiceVolume = Runtime.AudioVoiceVolume,
                AudioAmbientVolume = Runtime.AudioAmbientVolume,
                AudioMaxVoices = Runtime.AudioMaxVoices,
                AudioDopplerScale = Runtime.AudioDopplerScale,
                AudioEnableVoiceStealing = Runtime.AudioEnableVoiceStealing,
                ScreenPercentage = Runtime.ScreenPercentage
            },
            Build = new BuildProjectSettings
            {
                OutputName = Build.OutputName,
                BuildVersion = Build.BuildVersion
            }
        };
    }

    /// <summary>
    /// Ensures all fields have valid values, clamping out-of-range numbers and filling empty strings.
    /// </summary>
    /// <param name="defaultProjectName">Project name to use as a fallback for empty fields.</param>
    public void Normalize(string defaultProjectName)
    {
        var safeProjectName = string.IsNullOrWhiteSpace(defaultProjectName) ? "Untitled" : defaultProjectName.Trim();

        Project ??= new ProjectMetadataSettings();
        Project.Version = Coalesce(Project.Version, "0.1.0");
        Project.Author = Coalesce(Project.Author, string.Empty);
        Project.Company = Coalesce(Project.Company, string.Empty);
        Project.Description = CoalesceSingleLine(Project.Description, string.Empty);

        var runtimeWasNull = Runtime == null;
        Runtime ??= new RuntimeProjectSettings();
        Runtime.TargetFps = ClampFpsAllowUncapped(Runtime.TargetFps, 30, 500, 120);
        Runtime.WindowTitle = runtimeWasNull ? safeProjectName : Coalesce(Runtime.WindowTitle, safeProjectName);
        Runtime.WindowWidth = Clamp(Runtime.WindowWidth, 320, 10000, 1280);
        Runtime.WindowHeight = Clamp(Runtime.WindowHeight, 200, 10000, 720);
        Runtime.StartupSceneOverride = NormalizeScenePath(Runtime.StartupSceneOverride);
        Runtime.ForwardPlusTileSize = Clamp(Runtime.ForwardPlusTileSize, 8, 64, 16);
        Runtime.ForwardPlusMaxLights = Clamp(Runtime.ForwardPlusMaxLights, 16, 2048, 256);
        Runtime.ForwardPlusMaxLightsPerTile = Clamp(Runtime.ForwardPlusMaxLightsPerTile, 8, 256, 64);
        Runtime.PhysicsFixedTimestep = ClampFloat(Runtime.PhysicsFixedTimestep, 1f / 240f, 1f / 15f, 1f / 60f);
        Runtime.PhysicsMaxSubstepsPerFrame = Clamp(Runtime.PhysicsMaxSubstepsPerFrame, 1, 16, 4);
        Runtime.PhysicsSolverVelocityIterations = Clamp(Runtime.PhysicsSolverVelocityIterations, 1, 32, 8);
        Runtime.PhysicsSolverSubsteps = Clamp(Runtime.PhysicsSolverSubsteps, 1, 8, 1);
        Runtime.PhysicsContactSpringFrequency = ClampFloat(Runtime.PhysicsContactSpringFrequency, 1f, 300f, 30f);
        Runtime.PhysicsContactDampingRatio = ClampFloat(Runtime.PhysicsContactDampingRatio, 0f, 10f, 1f);
        Runtime.PhysicsMaximumRecoveryVelocity = ClampFloat(Runtime.PhysicsMaximumRecoveryVelocity, 0f, 100f, 2f);
        Runtime.PhysicsDefaultFriction = ClampFloat(Runtime.PhysicsDefaultFriction, 0f, 10f, 0.8f);
        Runtime.PhysicsDefaultRestitution = ClampFloat(Runtime.PhysicsDefaultRestitution, 0f, 1f, 0f);
        Runtime.AudioMasterVolume = ClampFloat(Runtime.AudioMasterVolume, 0f, 2f, 1f);
        Runtime.AudioMusicVolume = ClampFloat(Runtime.AudioMusicVolume, 0f, 2f, 1f);
        Runtime.AudioSfxVolume = ClampFloat(Runtime.AudioSfxVolume, 0f, 2f, 1f);
        Runtime.AudioUiVolume = ClampFloat(Runtime.AudioUiVolume, 0f, 2f, 1f);
        Runtime.AudioVoiceVolume = ClampFloat(Runtime.AudioVoiceVolume, 0f, 2f, 1f);
        Runtime.AudioAmbientVolume = ClampFloat(Runtime.AudioAmbientVolume, 0f, 2f, 1f);
        Runtime.AudioMaxVoices = Clamp(Runtime.AudioMaxVoices, 16, 512, 128);
        Runtime.AudioDopplerScale = ClampFloat(Runtime.AudioDopplerScale, 0f, 10f, 1f);
        Runtime.ScreenPercentage = Clamp(Runtime.ScreenPercentage, 10, 200, 100);

        var buildWasNull = Build == null;
        Build ??= new BuildProjectSettings();
        Build.OutputName = buildWasNull ? safeProjectName : Coalesce(Build.OutputName, safeProjectName);
        Build.BuildVersion = Coalesce(Build.BuildVersion, Project.Version);
    }

    /// <summary>
    /// Resolves the startup scene path, preferring <see cref="RuntimeProjectSettings.StartupSceneOverride"/> if set.
    /// </summary>
    /// <param name="defaultScene">The default scene path from the project file.</param>
    /// <returns>The resolved scene path.</returns>
    public string ResolveStartupScene(string defaultScene)
    {
        var scene = string.IsNullOrWhiteSpace(Runtime.StartupSceneOverride)
            ? defaultScene
            : Runtime.StartupSceneOverride;
        return NormalizeScenePath(scene);
    }

    private static string ResolveProjectName(string? projectName, string? directoryPath)
    {
        if (!string.IsNullOrWhiteSpace(projectName))
            return projectName.Trim();

        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            var folderName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(folderName))
                return folderName;
        }

        return "Untitled";
    }

    private static int Clamp(int value, int min, int max, int fallback)
    {
        if (value < min || value > max)
            return fallback;
        return value;
    }

    private static int ClampFpsAllowUncapped(int value, int min, int max, int fallback)
    {
        if (value == 0)
            return 0;
        if (value < min || value > max)
            return fallback;
        return value;
    }

    private static float ClampFloat(float value, float min, float max, float fallback)
    {
        if (!float.IsFinite(value) || value < min || value > max)
            return fallback;
        return value;
    }

    private static string Coalesce(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string CoalesceSingleLine(string? value, string fallback)
    {
        var text = Coalesce(value, fallback);
        return text.Replace("\r", " ").Replace("\n", " ");
    }

    private static string NormalizeScenePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var scenePath = value.Trim().Replace('\\', '/');
        if (scenePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            scenePath = scenePath["Assets/".Length..];

        return scenePath;
    }
}

/// <summary>
/// Project metadata such as version, author, and description.
/// </summary>
public class ProjectMetadataSettings
{
    /// <summary>
    /// Semantic version string (e.g. "0.1.0").
    /// </summary>
    public string Version { get; set; } = "0.1.0";

    /// <summary>
    /// Project author name.
    /// </summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// Company or organization name.
    /// </summary>
    public string Company { get; set; } = string.Empty;

    /// <summary>
    /// Short project description (single line).
    /// </summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Settings that control runtime behavior (window, rendering, performance).
/// </summary>
public class RuntimeProjectSettings
{
    /// <summary>
    /// Target frames per second (0 for uncapped, otherwise clamped to 30-500; defaults to 120).
    /// </summary>
    public int TargetFps { get; set; } = 120;

    /// <summary>
    /// Whether vertical sync is enabled.
    /// </summary>
    public bool VSync { get; set; } = true;

    /// <summary>
    /// Title displayed in the window title bar.
    /// </summary>
    public string WindowTitle { get; set; } = "Untitled";

    /// <summary>
    /// Initial window width in pixels.
    /// </summary>
    public int WindowWidth { get; set; } = 1280;

    /// <summary>
    /// Initial window height in pixels.
    /// </summary>
    public int WindowHeight { get; set; } = 720;

    /// <summary>
    /// Whether the window can be resized by the user.
    /// </summary>
    public bool Resizable { get; set; } = true;

    /// <summary>
    /// Whether the game starts in fullscreen mode.
    /// </summary>
    public bool Fullscreen { get; set; }

    /// <summary>
    /// Whether the game window starts maximized.
    /// </summary>
    public bool StartMaximized { get; set; }

    /// <summary>
    /// Optional scene path that overrides the project's default scene on startup.
    /// </summary>
    public string StartupSceneOverride { get; set; } = string.Empty;

    /// <summary>
    /// Forward+ tile size in pixels (clamped to 8–64, defaults to 16).
    /// </summary>
    public int ForwardPlusTileSize { get; set; } = 16;

    /// <summary>
    /// Maximum total lights processed by the Forward+ renderer (clamped to 16–2048, defaults to 256).
    /// </summary>
    public int ForwardPlusMaxLights { get; set; } = 256;

    /// <summary>
    /// Maximum lights assigned to a single tile (clamped to 8–256, defaults to 64).
    /// </summary>
    public int ForwardPlusMaxLightsPerTile { get; set; } = 64;

    // --- Physics ---

    /// <summary>Fixed simulation step duration in seconds (clamped to 1/240–1/15, defaults to 1/60).</summary>
    public float PhysicsFixedTimestep { get; set; } = 1f / 60f;

    /// <summary>Maximum simulation steps per frame (clamped to 1–16, defaults to 4).</summary>
    public int PhysicsMaxSubstepsPerFrame { get; set; } = 4;

    /// <summary>Solver velocity iterations per substep (clamped to 1–32, defaults to 8).</summary>
    public int PhysicsSolverVelocityIterations { get; set; } = 8;

    /// <summary>Solver substep count (clamped to 1–8, defaults to 1).</summary>
    public int PhysicsSolverSubsteps { get; set; } = 1;

    /// <summary>Contact spring angular frequency (clamped to 1–300, defaults to 30).</summary>
    public float PhysicsContactSpringFrequency { get; set; } = 30f;

    /// <summary>Contact spring damping ratio (clamped to 0–10, defaults to 1).</summary>
    public float PhysicsContactDampingRatio { get; set; } = 1f;

    /// <summary>Recovery velocity cap before restitution scaling (clamped to 0–100, defaults to 2).</summary>
    public float PhysicsMaximumRecoveryVelocity { get; set; } = 2f;

    /// <summary>Default friction for colliders without overrides (clamped to 0–10, defaults to 0.8).</summary>
    public float PhysicsDefaultFriction { get; set; } = 0.8f;

    /// <summary>Default restitution for colliders without overrides (clamped to 0–1, defaults to 0).</summary>
    public float PhysicsDefaultRestitution { get; set; } = 0f;

    /// <summary>Enables visual interpolation for eligible dynamic rigidbodies.</summary>
    public bool PhysicsInterpolationEnabled { get; set; } = true;

    // --- Audio ---

    /// <summary>Master bus volume (clamped to 0–2, defaults to 1).</summary>
    public float AudioMasterVolume { get; set; } = 1f;

    /// <summary>Music bus volume (clamped to 0–2, defaults to 1).</summary>
    public float AudioMusicVolume { get; set; } = 1f;

    /// <summary>SFX bus volume (clamped to 0–2, defaults to 1).</summary>
    public float AudioSfxVolume { get; set; } = 1f;

    /// <summary>UI bus volume (clamped to 0–2, defaults to 1).</summary>
    public float AudioUiVolume { get; set; } = 1f;

    /// <summary>Voice bus volume (clamped to 0–2, defaults to 1).</summary>
    public float AudioVoiceVolume { get; set; } = 1f;

    /// <summary>Ambient bus volume (clamped to 0–2, defaults to 1).</summary>
    public float AudioAmbientVolume { get; set; } = 1f;

    /// <summary>Maximum active voices (clamped to 16–512, defaults to 128).</summary>
    public int AudioMaxVoices { get; set; } = 128;

    /// <summary>Doppler scalar reserved for advanced spatialization (clamped to 0–10, defaults to 1).</summary>
    public float AudioDopplerScale { get; set; } = 1f;

    /// <summary>Allows low-priority voice stealing when the voice budget is full.</summary>
    public bool AudioEnableVoiceStealing { get; set; } = true;

    // --- Rendering ---

    /// <summary>Screen percentage (10-200, defaults to 100). Below 100 renders at lower resolution for a pixelated look.</summary>
    public int ScreenPercentage { get; set; } = 100;
}

/// <summary>
/// Settings that control game export and packaging.
/// </summary>
public class BuildProjectSettings
{
    /// <summary>
    /// Name of the exported executable (without extension).
    /// </summary>
    public string OutputName { get; set; } = "Untitled";

    /// <summary>
    /// Version string embedded in the export.
    /// </summary>
    public string BuildVersion { get; set; } = "0.1.0";
}
