using System.Diagnostics;
using System.Text.Json;
using FrinkyEngine.Core.Audio;
using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Rendering;
using FrinkyEngine.Core.Scene;
using FrinkyEngine.Core.Scripting;
using FrinkyEngine.Core.Serialization;
using FrinkyEngine.Core.CanvasUI;
using FrinkyEngine.Core.UI;
using FrinkyEngine.Editor.Assets.Creation;
using FrinkyEngine.Editor.Panels;
using FrinkyEngine.Editor.Prefab;
using Raylib_cs;

namespace FrinkyEngine.Editor;

public enum EditorMode
{
    Edit,
    Play,
    Simulate
}

public class EditorApplication
{
    private static readonly JsonSerializerOptions HierarchyJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static EditorApplication Instance { get; private set; } = null!;

    public Core.Scene.Scene? CurrentScene { get; set; }
    private readonly List<Entity> _selectedEntities = new();
    public IReadOnlyList<Entity> SelectedEntities => _selectedEntities;
    public Entity? SelectedEntity
    {
        get => _selectedEntities.Count > 0 ? _selectedEntities[^1] : null;
        set => SetSingleSelection(value);
    }
    public EditorMode Mode { get; private set; } = EditorMode.Edit;
    public EditorCamera EditorCamera { get; } = new();
    public SceneRenderer SceneRenderer { get; } = new();
    public AssetIconService AssetIcons { get; } = new();
    public GizmoSystem GizmoSystem { get; } = new();
    public ColliderEditSystem ColliderEditSystem { get; } = new();
    public PickingSystem PickingSystem { get; } = new();
    public GameAssemblyLoader AssemblyLoader { get; } = new();
    public UndoRedoManager UndoRedo { get; } = new();
    public PrefabService Prefabs { get; }

    public string? ProjectDirectory { get; private set; }
    public ProjectFile? ProjectFile { get; private set; }
    public ProjectSettings? ProjectSettings { get; private set; }
    public EditorProjectSettings? ProjectEditorSettings { get; private set; }
    public AssetTagDatabase? TagDatabase { get; private set; }
    public bool ShouldResetLayout { get; set; }
    public bool IsGameViewEnabled { get; private set; }
    public bool IsSceneDirty { get; private set; }
    public bool IsPhysicsHitboxPreviewEnabled { get; private set; }
    public bool IsColliderEditModeEnabled { get; private set; }
    public bool IsBonePreviewEnabled { get; private set; }
    public bool IsFullscreenViewport { get; private set; }
    public string? DraggedAssetPath { get; set; }
    public Guid? DraggedEntityId { get; set; }
    public bool IsInRuntimeMode => Mode is EditorMode.Play or EditorMode.Simulate;
    public bool CanEditScene => Mode is EditorMode.Edit or EditorMode.Simulate;
    public bool CanUseEditorViewportTools => Mode is EditorMode.Edit or EditorMode.Simulate;
    public bool IsCursorLocked => _cursorLockedByPlayMode || EditorCamera.IsCursorDisabled;

    private string? _runtimeModeSnapshot;
    private List<Guid>? _runtimeModeSelectionSnapshot;
    private bool _cursorLockedByPlayMode;
    private System.Numerics.Vector2 _savedPlayModeCursorPos;
    private Task<bool>? _buildTask;
    private EditorNotification? _buildNotification;
    private Task<bool>? _exportTask;
    private EditorNotification? _exportNotification;
    private string? _exportOutputDirectory;
    private AssetFileWatcher? _assetFileWatcher;
    private bool _deferredScriptsChanged;
    private HashSet<string>? _deferredChangedPaths;
    private readonly Dictionary<string, HierarchySceneState> _sessionHierarchyStates = new(StringComparer.OrdinalIgnoreCase);
    private bool _hierarchyStateDirty;
    private int _lastCleanupEntityCount = -1;
    private string? _lastCleanupSceneKey;
    private bool _wasFocused = true;
    private int _errorCount;
    private EditorNotification? _errorCountNotification;

    public ViewportPanel ViewportPanel { get; }
    public HierarchyPanel HierarchyPanel { get; }
    public InspectorPanel InspectorPanel { get; }
    public ConsolePanel ConsolePanel { get; }
    public AssetBrowserPanel AssetBrowserPanel { get; }
    public PerformancePanel PerformancePanel { get; }
    public MenuBar MenuBar { get; }

    public EditorApplication()
    {
        Instance = this;
        Prefabs = new PrefabService(this);
        ViewportPanel = new ViewportPanel(this);
        HierarchyPanel = new HierarchyPanel(this);
        InspectorPanel = new InspectorPanel(this);
        ConsolePanel = new ConsolePanel(this);
        AssetBrowserPanel = new AssetBrowserPanel(this);
        PerformancePanel = new PerformancePanel(this);
        MenuBar = new MenuBar(this);
        AssetCreationRegistry.EnsureDefaultsRegistered();
        RegisterKeybindActions();
    }

    public void Initialize()
    {
        AudioDeviceService.EnsureInitialized();
        SceneRenderer.LoadShader("Shaders/lighting.vs", "Shaders/lighting.fs");
        SceneRenderer.ConfigureForwardPlus(ForwardPlusSettings.Default);
        EngineOverlays.Renderer = SceneRenderer;
        EngineOverlays.DebugDrawEnabled = true;
        EditorIcons.Load();
        LoadErrorAssets();
        ProjectTemplateRegistry.Discover();
        NewScene();
        SubscribeToLogErrors();
        FrinkyLog.Info("FrinkyEngine Editor initialized.");
    }

    private static unsafe void LoadErrorAssets()
    {
        const string errorTexturePath = "EditorAssets/Textures/ErrorTexture.png";
        const string errorModelPath = "EditorAssets/Meshes/ErrorMesh.glb";

        if (File.Exists(errorTexturePath))
            AssetManager.Instance.ErrorTexture = Raylib.LoadTexture(errorTexturePath);

        if (File.Exists(errorModelPath))
        {
            var model = Raylib.LoadModel(errorModelPath);
            if (AssetManager.Instance.ErrorTexture.HasValue)
            {
                var tex = AssetManager.Instance.ErrorTexture.Value;
                for (int i = 0; i < model.MaterialCount; i++)
                    model.Materials[i].Maps[(int)MaterialMapIndex.Albedo].Texture = tex;
            }
            AssetManager.Instance.ErrorModel = model;
        }
    }


    public void NewScene()
    {
        ClearSelection();
        CurrentScene = SceneManager.Instance.NewScene("Untitled");

        var cameraEntity = CurrentScene.CreateEntity("Main Camera");
        cameraEntity.Transform.LocalPosition = new System.Numerics.Vector3(0, 5, 10);
        cameraEntity.Transform.EulerAngles = new System.Numerics.Vector3(-20, 0, 0);
        cameraEntity.AddComponent<Core.Components.CameraComponent>();

        var lightEntity = CurrentScene.CreateEntity("Directional Light");
        lightEntity.Transform.LocalPosition = new System.Numerics.Vector3(2, 10, 2);
        lightEntity.AddComponent<Core.Components.LightComponent>();

        EditorCamera.Reset();
        IsSceneDirty = false;
        UpdateWindowTitle();
        ResetHierarchyStateForCurrentScene();
        UndoRedo.Clear();
        UndoRedo.SetBaseline(CurrentScene, Array.Empty<Guid>(), SerializeCurrentHierarchyState());
        NotificationManager.Instance.Post("New scene created", NotificationType.Info);
    }

    public void Update(float dt)
    {
        NotificationManager.Instance.Update(dt);

        if (_buildTask is { IsCompleted: true })
        {
            var success = _buildTask.Result;
            _buildTask = null;

            if (_buildNotification != null)
            {
                if (success)
                    NotificationManager.Instance.Complete(_buildNotification, "Build Succeeded!", NotificationType.Success);
                else
                    NotificationManager.Instance.Complete(_buildNotification, "Build Failed.", NotificationType.Error);
                _buildNotification = null;
            }

            if (success)
                ReloadGameAssembly();
        }

        if (_exportTask is { IsCompleted: true })
        {
            var success = _exportTask.Result;
            _exportTask = null;

            if (_exportNotification != null)
            {
                if (success)
                    NotificationManager.Instance.Complete(_exportNotification, "Export Succeeded!", NotificationType.Success);
                else
                    NotificationManager.Instance.Complete(_exportNotification, "Export Failed.", NotificationType.Error);
                _exportNotification = null;
            }

            if (success && _exportOutputDirectory != null && Directory.Exists(_exportOutputDirectory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _exportOutputDirectory,
                    UseShellExecute = true
                });
            }
            _exportOutputDirectory = null;
        }

        if (_assetFileWatcher != null && _assetFileWatcher.PollChanges(out bool scriptsChanged, out var changedPaths))
        {
            if (!Raylib.IsWindowFocused())
            {
                _deferredScriptsChanged |= scriptsChanged;
                if (changedPaths != null)
                    (_deferredChangedPaths ??= new(StringComparer.OrdinalIgnoreCase)).UnionWith(changedPaths);
            }
            else
            {
                PerformAssetRefresh(scriptsChanged, changedPaths);
            }
        }

        FlushHierarchyStateIfDirty();

        // Handle focus transitions for play-mode cursor lock
        bool isFocused = Raylib.IsWindowFocused();
        if (_cursorLockedByPlayMode)
        {
            if (isFocused && !_wasFocused)
                Raylib.DisableCursor();
            else if (!isFocused && _wasFocused)
                Raylib.EnableCursor();
        }
        if (isFocused && !_wasFocused && _deferredChangedPaths != null)
        {
            // Merge any additional changes that arrived since the last poll
            if (_assetFileWatcher != null && _assetFileWatcher.PollChanges(out bool latestScripts, out var latestPaths))
            {
                _deferredScriptsChanged |= latestScripts;
                if (latestPaths != null)
                    _deferredChangedPaths.UnionWith(latestPaths);
            }

            PerformAssetRefresh(_deferredScriptsChanged, _deferredChangedPaths);
            _deferredScriptsChanged = false;
            _deferredChangedPaths = null;
        }
        _wasFocused = isFocused;

        AssetIcons.Tick(SceneRenderer);

        if (IsInRuntimeMode && CurrentScene != null)
        {
            var activeScene = SceneManager.Instance.ActiveScene;
            if (activeScene != null && !ReferenceEquals(activeScene, CurrentScene))
            {
                CurrentScene = activeScene;
                CurrentScene.Start();
            }

            CurrentScene.Update(dt);
            EngineOverlays.Update(dt);
        }
    }

    public void DrawUI()
    {
        if (Mode == EditorMode.Play && Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            ExitRuntimeMode();
            return;
        }

        KeybindManager.Instance.ProcessKeybinds();

        if (IsFullscreenViewport)
        {
            ViewportPanel.Draw();
            NotificationManager.Instance.Draw();
            return;
        }

        MenuBar.Draw();
        ViewportPanel.Draw();
        HierarchyPanel.Draw();
        InspectorPanel.Draw();
        ConsolePanel.Draw();
        AssetBrowserPanel.Draw();
        PerformancePanel.Draw();
        NotificationManager.Instance.Draw();
    }

    public void EnterPlayMode()
    {
        EnterRuntimeMode(EditorMode.Play);
    }

    public void EnterSimulateMode()
    {
        EnterRuntimeMode(EditorMode.Simulate);
    }

    private void EnterRuntimeMode(EditorMode targetMode)
    {
        if (targetMode is not (EditorMode.Play or EditorMode.Simulate))
            return;
        if (Mode != EditorMode.Edit || CurrentScene == null)
            return;
        if (_buildTask is { IsCompleted: false })
        {
            NotificationManager.Instance.Post("Cannot enter play mode while scripts are building.", NotificationType.Error);
            return;
        }

        UI.ClearFrame();
        _runtimeModeSelectionSnapshot = _selectedEntities.Select(e => e.Id).ToList();
        _runtimeModeSnapshot = SceneSerializer.SerializeToString(CurrentScene);
        CurrentScene.Start();
        Mode = targetMode;
        SceneManager.Instance.IsSaveDisabled = true;

        if (targetMode == EditorMode.Play)
        {
            _savedPlayModeCursorPos = Raylib.GetMousePosition();
            EditorCamera.ForceReleaseCursorState();
            Raylib.DisableCursor();
            _cursorLockedByPlayMode = true;
        }

        var label = targetMode == EditorMode.Play ? "Play" : "Simulate";
        FrinkyLog.Info($"Entered {label} mode.");
        NotificationManager.Instance.Post($"{label} mode", NotificationType.Info);
    }

    public void ExitPlayMode()
    {
        ExitRuntimeMode();
    }

    public void ExitRuntimeMode()
    {
        if (!IsInRuntimeMode)
            return;

        UI.ClearFrame();
        CanvasUI.Reset();
        EngineOverlays.Reset();
        var exitingMode = Mode;
        if (_runtimeModeSnapshot != null)
        {
            var restored = SceneSerializer.DeserializeFromString(_runtimeModeSnapshot);
            if (restored != null)
            {
                restored.FilePath = CurrentScene?.FilePath ?? string.Empty;
                CurrentScene = restored;
                SceneManager.Instance.SetActiveScene(restored);
            }
            _runtimeModeSnapshot = null;
        }

        if (_cursorLockedByPlayMode)
        {
            Raylib.EnableCursor();
            Raylib.SetMousePosition((int)_savedPlayModeCursorPos.X, (int)_savedPlayModeCursorPos.Y);
            _cursorLockedByPlayMode = false;
        }

        _selectedEntities.Clear();
        if (_runtimeModeSelectionSnapshot != null && CurrentScene != null)
        {
            var savedIds = new HashSet<Guid>(_runtimeModeSelectionSnapshot);
            foreach (var entity in CurrentScene.Entities)
            {
                if (savedIds.Contains(entity.Id))
                    _selectedEntities.Add(entity);
            }
            _runtimeModeSelectionSnapshot = null;
        }
        Mode = EditorMode.Edit;
        SceneManager.Instance.IsSaveDisabled = false;
        UndoRedo.SetBaseline(CurrentScene, GetSelectedEntityIds(), SerializeCurrentHierarchyState());
        var label = exitingMode == EditorMode.Play ? "Play" : "Simulate";
        FrinkyLog.Info($"Exited {label} mode.");
        NotificationManager.Instance.Post("Edit mode", NotificationType.Info);
    }

    public void TogglePlayModeCursorLock()
    {
        if (Mode != EditorMode.Play)
            return;

        if (_cursorLockedByPlayMode)
        {
            Raylib.EnableCursor();
            _cursorLockedByPlayMode = false;
            NotificationManager.Instance.Post("Cursor unlocked (Shift+F1 to re-lock)", NotificationType.Info, 2.0f);
        }
        else
        {
            Raylib.DisableCursor();
            _cursorLockedByPlayMode = true;
            NotificationManager.Instance.Post("Cursor locked", NotificationType.Info, 2.0f);
        }
    }

    public void ToggleGameView()
    {
        IsGameViewEnabled = !IsGameViewEnabled;
        NotificationManager.Instance.Post(
            IsGameViewEnabled ? "Game view enabled" : "Game view disabled",
            NotificationType.Info,
            2.0f);
    }

    public void FrameSelected()
    {
        if (!CanUseEditorViewportTools || _selectedEntities.Count == 0) return;

        var min = new System.Numerics.Vector3(float.MaxValue);
        var max = new System.Numerics.Vector3(float.MinValue);
        bool hasBounds = false;

        foreach (var entity in _selectedEntities)
        {
            var renderable = entity.GetComponent<Core.Components.RenderableComponent>();
            if (renderable != null && renderable.Enabled)
            {
                var bb = renderable.GetWorldBoundingBox();
                if (bb.HasValue)
                {
                    min = System.Numerics.Vector3.Min(min, bb.Value.Min);
                    max = System.Numerics.Vector3.Max(max, bb.Value.Max);
                    hasBounds = true;
                    continue;
                }
            }

            // Fallback: 1-unit cube around world position
            var pos = entity.Transform.WorldPosition;
            min = System.Numerics.Vector3.Min(min, pos - System.Numerics.Vector3.One * 0.5f);
            max = System.Numerics.Vector3.Max(max, pos + System.Numerics.Vector3.One * 0.5f);
            hasBounds = true;
        }

        if (!hasBounds) return;

        var center = (min + max) * 0.5f;
        var extents = (max - min) * 0.5f;
        float radius = extents.Length();
        if (radius < 0.01f) radius = 1f;

        EditorCamera.FocusOn(center, radius);
    }
  
    public void TogglePhysicsHitboxPreview()
    {
        IsPhysicsHitboxPreviewEnabled = !IsPhysicsHitboxPreviewEnabled;

        NotificationManager.Instance.Post(
            IsPhysicsHitboxPreviewEnabled ? "Physics hitbox preview enabled" : "Physics hitbox preview disabled",
            NotificationType.Info,
            2.0f);
    }

    public void ToggleColliderEditMode()
    {
        IsColliderEditModeEnabled = !IsColliderEditModeEnabled;

        NotificationManager.Instance.Post(
            IsColliderEditModeEnabled ? "Collider edit mode enabled" : "Collider edit mode disabled",
            NotificationType.Info,
            2.0f);
    }

    public void ToggleBonePreview()
    {
        IsBonePreviewEnabled = !IsBonePreviewEnabled;

        NotificationManager.Instance.Post(
            IsBonePreviewEnabled ? "Bone preview enabled" : "Bone preview disabled",
            NotificationType.Info,
            2.0f);
    }

    public void ToggleFullscreenViewport()
    {
        IsFullscreenViewport = !IsFullscreenViewport;
        NotificationManager.Instance.Post(
            IsFullscreenViewport ? "Entered Fullscreen Viewport (F11 to exit)" : "Exited Fullscreen Viewport",
            NotificationType.Info,
            2.0f);
    }

    public void CreateAndOpenProject(string parentDirectory, string projectName, ProjectTemplate template)
    {
        try
        {
            var fprojectPath = ProjectScaffolder.CreateProject(parentDirectory, projectName, template);
            FrinkyLog.Info($"Created project: {projectName}");
            OpenProject(fprojectPath);
        }
        catch (Exception ex)
        {
            FrinkyLog.Error($"Failed to create project: {ex.Message}");
        }
    }

    public void CreateAndOpenProject(string parentDirectory, string projectName)
    {
        var template = ProjectTemplateRegistry.GetById("3d-starter")
            ?? ProjectTemplateRegistry.Templates.FirstOrDefault()
            ?? throw new InvalidOperationException("No project templates found.");
        CreateAndOpenProject(parentDirectory, projectName, template);
    }

    public void OpenProject(string fprojectPath)
    {
        var loadLogCursor = CaptureLogCursor();

        ProjectDirectory = Path.GetDirectoryName(fprojectPath);
        ProjectFile = Core.Assets.ProjectFile.Load(fprojectPath);
        PrefabDatabase.Instance.Clear();
        _sessionHierarchyStates.Clear();
        _hierarchyStateDirty = false;
        IsPhysicsHitboxPreviewEnabled = false;
        IsColliderEditModeEnabled = false;
        IsBonePreviewEnabled = false;

        if (ProjectDirectory != null)
        {
            ProjectSettings = Core.Assets.ProjectSettings.LoadOrCreate(ProjectDirectory, ProjectFile.ProjectName);
            Core.Physics.PhysicsProjectSettings.ApplyFrom(ProjectSettings.Runtime);
            AudioProjectSettings.ApplyFrom(ProjectSettings.Runtime);
            ProjectEditorSettings = EditorProjectSettings.LoadOrCreate(ProjectDirectory);
            TagDatabase = AssetTagDatabase.LoadOrCreate(ProjectDirectory);
            var assetsPath = ProjectFile.GetAbsoluteAssetsPath(ProjectDirectory);
            AssetManager.Instance.AssetsPath = assetsPath;
            AssetDatabase.Instance.Scan(assetsPath);

            var engineContentPath = Path.Combine(AppContext.BaseDirectory, "EngineContent");
            AssetManager.Instance.EngineContentPath = engineContentPath;
            AssetDatabase.Instance.ScanEngineContent(engineContentPath);

            _assetFileWatcher?.Dispose();
            _assetFileWatcher = new AssetFileWatcher();
            _assetFileWatcher.Watch(assetsPath);
            AssetIcons.Initialize(ProjectDirectory);

            if (!string.IsNullOrEmpty(ProjectFile.GameProject))
            {
                var csprojName = Path.GetFileNameWithoutExtension(ProjectFile.GameProject);
                ProjectScaffolder.EnsureProjectFiles(ProjectDirectory, csprojName);
            }

            if (!string.IsNullOrEmpty(ProjectFile.GameAssembly))
            {
                var dllPath = Path.Combine(ProjectDirectory, ProjectFile.GameAssembly);
                AssemblyLoader.LoadAssembly(dllPath);
            }

            var scenePath = ProjectFile.GetAbsoluteScenePath(ProjectDirectory);
            if (File.Exists(scenePath))
            {
                SceneManager.Instance.LoadScene(scenePath);
                CurrentScene = SceneManager.Instance.ActiveScene;
                Prefabs.RecalculateOverridesForScene();
                RestoreEditorCameraFromScene();
            }
        }

        ApplyRuntimeRenderSettingsImmediate();
        ApplyEditorSettingsImmediate();

        KeybindManager.Instance.LoadConfig(ProjectDirectory);
        EditorPreferences.Instance.LoadConfig(ProjectDirectory);
        UndoRedo.Clear();
        ClearSelection();
        UndoRedo.SetBaseline(CurrentScene, GetSelectedEntityIds(), SerializeCurrentHierarchyState());
        IsSceneDirty = false;
        FrinkyLog.Info($"Opened project: {ProjectFile.ProjectName}");
        NotificationManager.Instance.Post($"Opened: {ProjectFile.ProjectName}", NotificationType.Success);
        NotifySkippedComponentWarningsSince(loadLogCursor, "Project open");
        UpdateWindowTitle();
        BuildScripts();
    }

    public void UpdateWindowTitle()
    {
        var title = "FrinkyEngine Editor";
        if (ProjectFile != null)
            title += $" - {ProjectFile.ProjectName}";
        if (CurrentScene != null)
            title += $" - {CurrentScene.Name}";
        if (IsSceneDirty)
            title += " *";
        Raylib.SetWindowTitle(title);
    }

    public int CaptureLogCursor()
    {
        return FrinkyLog.Entries.Count;
    }

    public void NotifySkippedComponentWarningsSince(int logCursor, string operationLabel)
    {
        if (logCursor < 0)
            logCursor = 0;

        var entries = FrinkyLog.Entries;
        if (logCursor >= entries.Count)
            return;

        int skippedCount = 0;
        for (int i = logCursor; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Level == LogLevel.Warning &&
                entry.Message.StartsWith("Skipped component '", StringComparison.Ordinal))
            {
                skippedCount++;
            }
        }

        if (skippedCount > 0)
        {
            NotificationManager.Instance.Post(
                $"{operationLabel} completed with {skippedCount} skipped component(s). See log.",
                NotificationType.Warning);
        }
    }

    public bool OpenProjectInVSCode()
    {
        if (ProjectDirectory == null)
        {
            NotificationManager.Instance.Post("No project is open.", NotificationType.Warning);
            return false;
        }

        return LaunchVSCode(new[] { ProjectDirectory }, "Project opened in VS Code.");
    }

    public bool OpenFileInVSCode(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
        {
            NotificationManager.Instance.Post("Script file not found.", NotificationType.Warning);
            return false;
        }

        return LaunchVSCode(new[] { "-g", absolutePath }, successMessage: null);
    }

    private void PerformAssetRefresh(bool scriptsChanged, HashSet<string>? changedPaths)
    {
        AssetDatabase.Instance.Refresh();

        bool assetsReloaded = false;
        var changedPrefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (changedPaths != null)
        {
            var assetsPath = AssetManager.Instance.AssetsPath;
            foreach (var fullPath in changedPaths)
            {
                if (fullPath.StartsWith(assetsPath, StringComparison.OrdinalIgnoreCase))
                {
                    var rel = Path.GetRelativePath(assetsPath, fullPath).Replace('\\', '/');
                    relativePaths.Add(rel);
                    if (rel.EndsWith(".fprefab", StringComparison.OrdinalIgnoreCase))
                        changedPrefabs.Add(rel);
                }
            }
        }

        AssetIcons.OnAssetDatabaseRefreshed(changedPaths != null ? relativePaths : null);

        if (relativePaths.Count > 0 && CurrentScene != null)
        {
            // Invalidate component-backed resource keys first, then drop backend-owned GPU state.
            foreach (var renderable in CurrentScene.Renderables)
            {
                bool shouldInvalidate = false;
                if (renderable is MeshRendererComponent meshRenderer)
                {
                    var resolvedModelPath = AssetDatabase.Instance.ResolveAssetPath(meshRenderer.ModelPath.Path);
                    if (resolvedModelPath != null && relativePaths.Contains(resolvedModelPath))
                        shouldInvalidate = true;
                    else
                    {
                        foreach (var slot in meshRenderer.MaterialSlots)
                        {
                            if (slot.TexturePath.IsEmpty) continue;
                            var resolvedTexPath = AssetDatabase.Instance.ResolveAssetPath(slot.TexturePath.Path);
                            if (resolvedTexPath != null && relativePaths.Contains(resolvedTexPath))
                            {
                                shouldInvalidate = true;
                                break;
                            }
                        }
                    }
                }
                else if (renderable is PrimitiveComponent primitive)
                {
                    if (!primitive.Material.TexturePath.IsEmpty)
                    {
                        var resolvedTexPath = AssetDatabase.Instance.ResolveAssetPath(primitive.Material.TexturePath.Path);
                        if (resolvedTexPath != null && relativePaths.Contains(resolvedTexPath))
                            shouldInvalidate = true;
                    }
                }

                if (shouldInvalidate)
                {
                    renderable.Invalidate();
                    assetsReloaded = true;
                }
            }

            // Now unload stale GPU resources from the cache
            foreach (var rel in relativePaths)
                AssetManager.Instance.InvalidateAsset(rel);

            SceneRenderer.InvalidateAssets(relativePaths);
        }

        if (changedPrefabs.Count > 0)
            Prefabs.SyncInstancesForAssets(changedPrefabs);

        NotificationManager.Instance.Post(
            assetsReloaded ? "Assets reloaded" : "Assets refreshed",
            NotificationType.Info, 2.5f);

        if (CurrentScene != null)
            AssetReferenceValidator.ValidateScene(CurrentScene);

        if (scriptsChanged)
            BuildScripts();
    }

    public void BuildScripts()
    {
        if (ScriptBuilder.IsBuilding || ProjectDirectory == null || ProjectFile == null)
            return;

        var csprojPath = FindGameCsproj();
        if (csprojPath == null)
        {
            FrinkyLog.Error("No game .csproj found in project directory.");
            return;
        }

        AssemblyLoader.Unload();
        _buildNotification = NotificationManager.Instance.PostPersistent("Building Scripts...", NotificationType.Info);
        _buildTask = Task.Run(() => ScriptBuilder.BuildAsync(csprojPath));
    }

    public void ExportGame(string outputDirectory)
    {
        if (GameExporter.IsExporting || ProjectDirectory == null || ProjectFile == null)
            return;

        var runtimeCsproj = GameExporter.FindRuntimeCsproj();
        var runtimeTemplateDir = GameExporter.FindRuntimeTemplateDirectory();
        if (runtimeCsproj == null && runtimeTemplateDir == null)
        {
            FrinkyLog.Error("Could not locate Runtime source project or bundled runtime template.");
            NotificationManager.Instance.Post("Export failed: Runtime not found.", NotificationType.Error);
            return;
        }

        var config = new ExportConfig
        {
            ProjectName = ProjectFile.ProjectName,
            ProjectDirectory = ProjectDirectory,
            AssetsPath = ProjectFile.GetAbsoluteAssetsPath(ProjectDirectory),
            DefaultScene = ProjectFile.DefaultScene,
            GameCsprojPath = FindGameCsproj(),
            GameAssemblyDll = !string.IsNullOrEmpty(ProjectFile.GameAssembly) ? ProjectFile.GameAssembly : null,
            RuntimeCsprojPath = runtimeCsproj ?? string.Empty,
            RuntimeTemplateDirectory = runtimeTemplateDir,
            OutputDirectory = outputDirectory,
            ProjectSettings = ProjectSettings
        };

        _exportOutputDirectory = outputDirectory;
        _exportNotification = NotificationManager.Instance.PostPersistent("Exporting Game...", NotificationType.Info);
        _exportTask = Task.Run(() => GameExporter.ExportAsync(config));
    }

    private string? FindGameCsproj()
    {
        if (ProjectDirectory == null || ProjectFile == null)
            return null;

        if (!string.IsNullOrEmpty(ProjectFile.GameProject))
        {
            var path = Path.Combine(ProjectDirectory, ProjectFile.GameProject);
            if (File.Exists(path))
                return path;
        }

        var csprojFiles = Directory.GetFiles(ProjectDirectory, "*.csproj", SearchOption.TopDirectoryOnly);
        return csprojFiles.Length > 0 ? csprojFiles[0] : null;
    }

    private void ReloadGameAssembly()
    {
        if (ProjectDirectory == null || ProjectFile == null)
            return;

        var dllPath = !string.IsNullOrEmpty(ProjectFile.GameAssembly)
            ? Path.Combine(ProjectDirectory, ProjectFile.GameAssembly)
            : null;

        if (dllPath == null || !File.Exists(dllPath))
        {
            FrinkyLog.Warning("Game assembly DLL not found after build.");
            return;
        }

        if (!AssemblyLoader.ReloadAssembly(dllPath))
        {
            FrinkyLog.Error("Game assembly reload failed.");
            NotificationManager.Instance.Post("Game assembly reload failed.", NotificationType.Error);
            return;
        }

        FrinkyLog.Info("Game assembly reloaded.");
        NotificationManager.Instance.Post("Game assembly reloaded.", NotificationType.Info);

        // Re-serialize and deserialize current scene to refresh component instances
        if (CurrentScene != null)
        {
            var savedSelection = _selectedEntities.Select(e => e.Id).ToHashSet();
            var snapshot = SceneSerializer.SerializeToString(CurrentScene);

            // Release unique model instances owned by old scene components before replacing
            foreach (var renderable in CurrentScene.Renderables)
                renderable.Invalidate();

            var refreshed = SceneSerializer.DeserializeFromString(snapshot);
            if (refreshed != null)
            {
                refreshed.Name = CurrentScene.Name;
                refreshed.FilePath = CurrentScene.FilePath;
                CurrentScene = refreshed;
                SceneManager.Instance.SetActiveScene(refreshed);
                _selectedEntities.Clear();
                foreach (var entity in refreshed.Entities)
                {
                    if (savedSelection.Contains(entity.Id))
                        _selectedEntities.Add(entity);
                }
            }
            UndoRedo.SetBaseline(CurrentScene, GetSelectedEntityIds(), SerializeCurrentHierarchyState());
        }
    }

    private void RegisterKeybindActions()
    {
        var km = KeybindManager.Instance;

        km.RegisterAction(EditorAction.NewScene, () => NewScene());

        km.RegisterAction(EditorAction.OpenScene, () => MenuBar.TriggerOpenScene());

        km.RegisterAction(EditorAction.SaveScene, () =>
        {
            if (CurrentScene != null)
            {
                StoreEditorCameraInScene();
                var path = !string.IsNullOrEmpty(CurrentScene.FilePath)
                    ? CurrentScene.FilePath
                    : "scene.fscene";
                SceneManager.Instance.SaveScene(path);
                ClearSceneDirty();
                FrinkyLog.Info($"Scene saved to: {path}");
                NotificationManager.Instance.Post("Scene saved", NotificationType.Success);
            }
        });

        km.RegisterAction(EditorAction.SaveSceneAs, () => MenuBar.TriggerSaveSceneAs());

        km.RegisterAction(EditorAction.Undo, () =>
        {
            if (Mode == EditorMode.Edit)
                UndoRedo.Undo(this);
        });
        km.RegisterAction(EditorAction.Redo, () =>
        {
            if (Mode == EditorMode.Edit)
                UndoRedo.Redo(this);
        });

        km.RegisterAction(EditorAction.BuildScripts, () => BuildScripts());

        km.RegisterAction(EditorAction.PlayStop, () =>
        {
            if (Mode == EditorMode.Edit)
                EnterPlayMode();
            else if (IsInRuntimeMode)
                ExitRuntimeMode();
        });

        km.RegisterAction(EditorAction.SimulateStop, () =>
        {
            if (Mode == EditorMode.Edit)
                EnterSimulateMode();
            else if (IsInRuntimeMode)
                ExitRuntimeMode();
        });

        km.RegisterAction(EditorAction.DeleteEntity, () =>
        {
            if (AssetBrowserPanel.IsWindowFocused)
                AssetBrowserPanel.DeleteSelectedAssets();
            else
                DeleteSelectedEntities();
        });

        km.RegisterAction(EditorAction.DuplicateEntity, () =>
        {
            DuplicateSelectedEntities();
        });

        km.RegisterAction(EditorAction.RenameEntity, () =>
        {
            if (!CanEditScene)
                return;

            if (HierarchyPanel.IsWindowFocused)
                HierarchyPanel.BeginRenameSelected();
            else if (AssetBrowserPanel.IsWindowFocused)
                AssetBrowserPanel.BeginRenameSelected();
            else if (SelectedEntities.Count == 1)
                InspectorPanel.FocusNameField = true;
        });

        km.RegisterAction(EditorAction.NewProject, () => MenuBar.TriggerNewProject());

        km.RegisterAction(EditorAction.GizmoTranslate, () => GizmoSystem.Mode = GizmoMode.Translate);
        km.RegisterAction(EditorAction.GizmoRotate, () => GizmoSystem.Mode = GizmoMode.Rotate);
        km.RegisterAction(EditorAction.GizmoScale, () => GizmoSystem.Mode = GizmoMode.Scale);
        km.RegisterAction(EditorAction.GizmoToggleSpace, () =>
            GizmoSystem.Space = GizmoSystem.Space == GizmoSpace.World ? GizmoSpace.Local : GizmoSpace.World);

        km.RegisterAction(EditorAction.DeselectEntity, () =>
        {
            ClearSelection();
        });
        km.RegisterAction(EditorAction.SelectAllEntities, () =>
        {
            if (HierarchyPanel.IsWindowFocused)
                HierarchyPanel.SelectAllVisibleEntities();
            else if (CurrentScene != null)
                SetSelection(CurrentScene.Entities);
        });
        km.RegisterAction(EditorAction.ExpandSelection, () =>
        {
            if (HierarchyPanel.IsWindowFocused)
                HierarchyPanel.ExpandSelection();
        });
        km.RegisterAction(EditorAction.CollapseSelection, () =>
        {
            if (HierarchyPanel.IsWindowFocused)
                HierarchyPanel.CollapseSelection();
        });
        km.RegisterAction(EditorAction.FocusHierarchySearch, () =>
        {
            HierarchyPanel.FocusSearch();
        });
        km.RegisterAction(EditorAction.ToggleGameView, () => ToggleGameView());
        km.RegisterAction(EditorAction.TogglePhysicsHitboxPreview, () => TogglePhysicsHitboxPreview());
        km.RegisterAction(EditorAction.ToggleBonePreview, () => ToggleBonePreview());

        km.RegisterAction(EditorAction.OpenAssetsFolder, () =>
        {
            if (ProjectDirectory == null || ProjectFile == null) return;
            var assetsPath = ProjectFile.GetAbsoluteAssetsPath(ProjectDirectory);
            if (Directory.Exists(assetsPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = assetsPath,
                    UseShellExecute = true
                });
            }
        });

        km.RegisterAction(EditorAction.OpenProjectInVSCode, () => OpenProjectInVSCode());

        km.RegisterAction(EditorAction.ExportGame, () => MenuBar.TriggerExportGame());
        km.RegisterAction(EditorAction.CreatePrefabFromSelection, () => CreatePrefabFromSelection());
        km.RegisterAction(EditorAction.ApplyPrefab, () => ApplySelectedPrefab());
        km.RegisterAction(EditorAction.RevertPrefab, () => RevertSelectedPrefab());
        km.RegisterAction(EditorAction.MakeUniquePrefab, () => MakeUniqueSelectedPrefab());
        km.RegisterAction(EditorAction.UnpackPrefab, () => UnpackSelectedPrefab());
        km.RegisterAction(EditorAction.TogglePlayModeCursorLock, () => TogglePlayModeCursorLock());
        km.RegisterAction(EditorAction.FrameSelected, () => FrameSelected());
        km.RegisterAction(EditorAction.ToggleColliderEditMode, () => ToggleColliderEditMode());
        km.RegisterAction(EditorAction.ToggleFullscreenViewport, () => ToggleFullscreenViewport());
    }

    public void StoreEditorCameraInScene()
    {
        if (CurrentScene == null) return;
        CurrentScene.EditorCameraPosition = EditorCamera.Position;
        CurrentScene.EditorCameraYaw = EditorCamera.Yaw;
        CurrentScene.EditorCameraPitch = EditorCamera.Pitch;
    }

    public void RestoreEditorCameraFromScene()
    {
        if (CurrentScene?.EditorCameraPosition != null &&
            CurrentScene.EditorCameraYaw != null &&
            CurrentScene.EditorCameraPitch != null)
        {
            EditorCamera.SetState(
                CurrentScene.EditorCameraPosition.Value,
                CurrentScene.EditorCameraYaw.Value,
                CurrentScene.EditorCameraPitch.Value);
        }
        else
        {
            EditorCamera.Reset();
        }
    }

    public void MarkSceneDirty()
    {
        if (!IsSceneDirty)
        {
            IsSceneDirty = true;
            UpdateWindowTitle();
        }
    }

    public void ClearSceneDirty()
    {
        IsSceneDirty = false;
        UpdateWindowTitle();
    }

    public void RecordUndo()
    {
        if (Mode != EditorMode.Edit || CurrentScene == null) return;
        UndoRedo.RecordUndo(GetSelectedEntityIds(), SerializeCurrentHierarchyState());
        MarkSceneDirty();
    }

    public void RefreshUndoBaseline()
    {
        if (Mode != EditorMode.Edit || CurrentScene == null) return;
        Prefabs.RecalculateOverridesForScene();
        UndoRedo.RefreshBaseline(CurrentScene, GetSelectedEntityIds(), SerializeCurrentHierarchyState());
    }

    public void CreatePrefabFromSelection()
    {
        if (!CanEditScene || SelectedEntity == null)
            return;

        RecordUndo();
        var created = Prefabs.CreatePrefabFromEntity(SelectedEntity);
        if (!created)
            return;

        RefreshUndoBaseline();
        NotificationManager.Instance.Post("Created prefab from selection.", NotificationType.Success);
    }

    public void InstantiatePrefabAsset(string assetPath)
    {
        if (!CanEditScene || CurrentScene == null)
            return;

        RecordUndo();
        var instance = Prefabs.InstantiatePrefab(assetPath);
        if (instance == null)
            return;

        SetSingleSelection(instance);
        RefreshUndoBaseline();
        NotificationManager.Instance.Post("Prefab instantiated.", NotificationType.Info, 1.5f);
    }

    public void ApplySelectedPrefab()
    {
        if (!CanEditScene) return;
        var prefabRoots = GetSelectedPrefabRoots();
        if (prefabRoots.Count == 0) return;

        RecordUndo();
        int applied = 0;
        var seenAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in prefabRoots)
        {
            var assetPath = root.Prefab?.AssetPath.Path;
            if (assetPath == null || !seenAssets.Add(assetPath))
                continue;
            if (Prefabs.ApplyPrefab(root)) applied++;
        }
        if (applied > 0)
        {
            RefreshUndoBaseline();
            NotificationManager.Instance.Post(
                applied == 1 ? "Prefab applied." : $"{applied} prefabs applied.",
                NotificationType.Success, 1.5f);
        }
    }

    public void RevertSelectedPrefab()
    {
        if (!CanEditScene) return;
        var prefabRoots = GetSelectedPrefabRoots();
        if (prefabRoots.Count == 0) return;

        RecordUndo();
        int reverted = 0;
        foreach (var root in prefabRoots)
            if (Prefabs.RevertPrefab(root, skipUndo: true)) reverted++;
        if (reverted > 0)
        {
            RefreshUndoBaseline();
            NotificationManager.Instance.Post(
                reverted == 1 ? "Prefab reverted." : $"{reverted} prefabs reverted.",
                NotificationType.Info, 1.5f);
        }
    }

    public void MakeUniqueSelectedPrefab()
    {
        if (!CanEditScene) return;
        var prefabRoots = GetSelectedPrefabRoots();
        if (prefabRoots.Count == 0) return;

        RecordUndo();
        int made = 0;
        foreach (var root in prefabRoots)
            if (Prefabs.MakeUnique(root, skipUndo: true)) made++;
        if (made > 0)
            RefreshUndoBaseline();
    }

    public void UnpackSelectedPrefab()
    {
        if (!CanEditScene) return;
        var prefabRoots = GetSelectedPrefabRoots();
        if (prefabRoots.Count == 0) return;

        RecordUndo();
        int unpacked = 0;
        foreach (var root in prefabRoots)
            if (Prefabs.UnpackPrefab(root, skipUndo: true)) unpacked++;
        if (unpacked > 0)
        {
            RefreshUndoBaseline();
            NotificationManager.Instance.Post(
                unpacked == 1 ? "Prefab unpacked." : $"{unpacked} prefabs unpacked.",
                NotificationType.Info, 1.5f);
        }
    }

    private List<Entity> GetSelectedPrefabRoots()
    {
        var seen = new HashSet<Guid>();
        var roots = new List<Entity>();
        foreach (var entity in _selectedEntities)
        {
            var root = Prefabs.GetPrefabRoot(entity);
            if (root != null && seen.Add(root.Id))
                roots.Add(root);
        }
        return roots;
    }

    public bool IsSelected(Entity entity)
    {
        return _selectedEntities.Any(e => e.Id == entity.Id);
    }

    public void ClearSelection()
    {
        _selectedEntities.Clear();
    }

    public void SetSingleSelection(Entity? entity)
    {
        if (entity == null)
        {
            ClearSelection();
            return;
        }

        SetSelection(new[] { entity });
    }

    public void SetSelection(IEnumerable<Entity> entities)
    {
        _selectedEntities.Clear();

        if (CurrentScene == null)
            return;

        var seen = new HashSet<Guid>();
        foreach (var entity in entities)
        {
            if (entity.Scene != CurrentScene)
                continue;
            if (!seen.Add(entity.Id))
                continue;

            _selectedEntities.Add(entity);
        }
    }

    public void ToggleSelection(Entity entity)
    {
        if (CurrentScene == null || entity.Scene != CurrentScene)
            return;

        var index = _selectedEntities.FindIndex(e => e.Id == entity.Id);
        if (index >= 0)
        {
            _selectedEntities.RemoveAt(index);
            return;
        }

        _selectedEntities.Add(entity);
    }

    public List<Guid> GetSelectedEntityIds()
    {
        return _selectedEntities.Select(e => e.Id).ToList();
    }

    public void DeleteSelectedEntities()
    {
        if (!CanEditScene || CurrentScene == null || _selectedEntities.Count == 0)
            return;

        var entitiesToDelete = _selectedEntities.Where(e => e.Scene == CurrentScene).ToList();
        if (entitiesToDelete.Count == 0)
            return;

        RecordUndo();
        foreach (var entity in entitiesToDelete)
            CurrentScene.RemoveEntity(entity);
        CleanupHierarchyStateForCurrentScene();
        ClearSelection();
        RefreshUndoBaseline();
    }

    public void DuplicateSelectedEntities()
    {
        if (!CanEditScene || CurrentScene == null || _selectedEntities.Count == 0)
            return;

        var selected = _selectedEntities.Where(e => e.Scene == CurrentScene).ToList();
        if (selected.Count == 0)
            return;

        var selectedIds = new HashSet<Guid>(selected.Select(e => e.Id));
        var rootsOnly = selected
            .Where(entity => !HasSelectedAncestor(entity, selectedIds))
            .ToList();

        RecordUndo();
        var duplicates = new List<Entity>();
        foreach (var entity in rootsOnly)
        {
            var duplicate = SceneSerializer.DuplicateEntity(entity, CurrentScene);
            if (duplicate != null)
                duplicates.Add(duplicate);
        }

        SetSelection(duplicates);
        if (duplicates.Count > 0)
        {
            var message = duplicates.Count == 1
                ? $"Duplicated: {duplicates[0].Name}"
                : $"Duplicated {duplicates.Count} entities";
            NotificationManager.Instance.Post(message, NotificationType.Info, 1.5f);
        }
        RefreshUndoBaseline();
    }

    private static bool HasSelectedAncestor(Entity entity, HashSet<Guid> selectedIds)
    {
        var parent = entity.Transform.Parent;
        while (parent != null)
        {
            if (selectedIds.Contains(parent.Entity.Id))
                return true;
            parent = parent.Parent;
        }

        return false;
    }

    public Entity? FindEntityById(Guid entityId)
    {
        return CurrentScene?.FindEntityById(entityId);
    }

    public HierarchySceneState GetOrCreateHierarchySceneState()
    {
        if (CurrentScene == null)
            return new HierarchySceneState();

        var key = GetCurrentHierarchySceneKey();
        if (string.IsNullOrWhiteSpace(key))
            return new HierarchySceneState();

        var stateMap = GetHierarchyStateMapForKey(key);
        if (!stateMap.TryGetValue(key, out var state))
        {
            state = new HierarchySceneState();
            stateMap[key] = state;
            if (IsPersistedHierarchyKey(key))
                MarkHierarchyStateDirty();
        }

        return state;
    }

    public string? SerializeCurrentHierarchyState()
    {
        if (CurrentScene == null)
            return null;

        var state = GetOrCreateHierarchySceneState();
        return JsonSerializer.Serialize(state, HierarchyJsonOptions);
    }

    public void RestoreHierarchyStateFromSerialized(string? hierarchyJson)
    {
        if (CurrentScene == null)
            return;

        var key = GetCurrentHierarchySceneKey();
        if (string.IsNullOrWhiteSpace(key))
            return;

        var stateMap = GetHierarchyStateMapForKey(key);
        if (string.IsNullOrWhiteSpace(hierarchyJson))
        {
            if (stateMap.Remove(key) && IsPersistedHierarchyKey(key))
                MarkHierarchyStateDirty();
            return;
        }

        try
        {
            var state = JsonSerializer.Deserialize<HierarchySceneState>(hierarchyJson, HierarchyJsonOptions);
            if (state == null)
                return;

            state.Normalize();
            stateMap[key] = state;
            if (IsPersistedHierarchyKey(key))
                MarkHierarchyStateDirty();
        }
        catch (Exception ex)
        {
            FrinkyLog.Warning($"Failed to restore hierarchy state: {ex.Message}");
        }
    }

    public bool ReparentEntity(Entity child, Entity? newParent)
    {
        if (CurrentScene == null || child.Scene != CurrentScene)
            return false;

        if (newParent != null && newParent.Scene != CurrentScene)
            return false;
        if (newParent != null && newParent.Id == child.Id)
            return false;

        var currentParent = child.Transform.Parent?.Entity;
        if ((newParent == null && currentParent == null) ||
            (newParent != null && currentParent?.Id == newParent.Id))
        {
            return false;
        }

        if (newParent != null)
        {
            var current = newParent.Transform;
            while (current != null)
            {
                if (current.Entity.Id == child.Id)
                    return false;
                current = current.Parent;
            }
        }

        child.Transform.SetParent(newParent?.Transform);
        return true;
    }

    public string? GetRootEntityFolder(Entity entity)
    {
        if (CurrentScene == null || entity.Scene != CurrentScene)
            return null;

        var state = GetOrCreateHierarchySceneState();
        var key = entity.Id.ToString("N");
        return state.RootEntityFolders.TryGetValue(key, out var folderId) ? folderId : null;
    }

    public bool SetRootEntityFolder(Entity entity, string? folderId)
    {
        if (CurrentScene == null || entity.Scene != CurrentScene)
            return false;
        if (entity.Transform.Parent != null)
            return false;

        var state = GetOrCreateHierarchySceneState();
        folderId = string.IsNullOrWhiteSpace(folderId) ? null : folderId.Trim();
        if (!string.IsNullOrEmpty(folderId) &&
            !state.Folders.Any(f => string.Equals(f.Id, folderId, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var key = entity.Id.ToString("N");
        if (folderId == null)
        {
            if (!state.RootEntityFolders.Remove(key))
                return false;
        }
        else
        {
            if (state.RootEntityFolders.TryGetValue(key, out var existing) &&
                string.Equals(existing, folderId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            state.RootEntityFolders[key] = folderId;
        }

        MarkHierarchyStateDirty();
        return true;
    }

    public void CleanupHierarchyStateForCurrentScene()
    {
        if (CurrentScene == null)
            return;

        var sceneKey = GetCurrentHierarchySceneKey();
        var entityCount = CurrentScene.Entities.Count;
        if (sceneKey == _lastCleanupSceneKey &&
            entityCount == _lastCleanupEntityCount)
            return;

        _lastCleanupSceneKey = sceneKey;
        _lastCleanupEntityCount = entityCount;

        var state = GetOrCreateHierarchySceneState();
        bool changed = false;

        var validFolderIds = new HashSet<string>(
            state.Folders.Select(f => f.Id),
            StringComparer.OrdinalIgnoreCase);
        var validEntityIds = new HashSet<string>(
            CurrentScene.Entities.Select(e => e.Id.ToString("N")),
            StringComparer.OrdinalIgnoreCase);

        var staleAssignments = state.RootEntityFolders
            .Where(kvp => !validEntityIds.Contains(kvp.Key) || !validFolderIds.Contains(kvp.Value))
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var entityId in staleAssignments)
        {
            state.RootEntityFolders.Remove(entityId);
            changed = true;
        }

        int beforeExpandedFolders = state.ExpandedFolderIds.Count;
        state.ExpandedFolderIds = state.ExpandedFolderIds
            .Where(validFolderIds.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        changed |= beforeExpandedFolders != state.ExpandedFolderIds.Count;

        int beforeExpandedEntities = state.ExpandedEntityIds.Count;
        state.ExpandedEntityIds = state.ExpandedEntityIds
            .Where(validEntityIds.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        changed |= beforeExpandedEntities != state.ExpandedEntityIds.Count;

        if (!string.IsNullOrWhiteSpace(state.RequiredComponentType) &&
            ComponentTypeResolver.Resolve(state.RequiredComponentType) == null)
        {
            state.RequiredComponentType = string.Empty;
            changed = true;
        }

        var folderCount = state.Folders.Count;
        state.Normalize();
        changed |= state.Folders.Count != folderCount;

        if (changed)
            MarkHierarchyStateDirty();
    }

    public void MarkHierarchyStateDirty()
    {
        _hierarchyStateDirty = true;
        _lastCleanupEntityCount = -1;
    }

    private void ResetHierarchyStateForCurrentScene()
    {
        if (CurrentScene == null)
            return;

        var key = GetCurrentHierarchySceneKey();
        if (string.IsNullOrWhiteSpace(key))
            return;

        var stateMap = GetHierarchyStateMapForKey(key);
        if (stateMap.Remove(key) && IsPersistedHierarchyKey(key))
            MarkHierarchyStateDirty();
    }

    private void FlushHierarchyStateIfDirty()
    {
        if (!_hierarchyStateDirty || ProjectDirectory == null || ProjectEditorSettings == null)
            return;

        ProjectEditorSettings.Save(ProjectDirectory);
        _hierarchyStateDirty = false;
    }

    private string GetCurrentHierarchySceneKey()
    {
        if (CurrentScene == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(ProjectDirectory) &&
            !string.IsNullOrWhiteSpace(CurrentScene.FilePath))
        {
            var relative = Path.GetRelativePath(ProjectDirectory, CurrentScene.FilePath).Replace('\\', '/');
            return $"scene:{relative}";
        }

        return $"session:{CurrentScene.Name}";
    }

    private Dictionary<string, HierarchySceneState> GetHierarchyStateMapForKey(string key)
    {
        if (IsPersistedHierarchyKey(key) && ProjectEditorSettings != null)
        {
            ProjectEditorSettings.Hierarchy ??= new HierarchyEditorSettings();
            return ProjectEditorSettings.Hierarchy.Scenes;
        }

        return _sessionHierarchyStates;
    }

    private static bool IsPersistedHierarchyKey(string key)
    {
        return key.StartsWith("scene:", StringComparison.OrdinalIgnoreCase);
    }

    public void Shutdown()
    {
        FrinkyLog.OnLog -= OnLogEntry;
        FrinkyLog.OnCleared -= OnLogCleared;
        UI.ClearFrame();
        _assetFileWatcher?.Dispose();
        _assetFileWatcher = null;
        AssetIcons.Shutdown();
        EditorIcons.Unload();
        ViewportPanel.Shutdown();
        SceneRenderer.UnloadShader();
        AssetManager.Instance.UnloadAll();
        AssetDatabase.Instance.Clear();
        PrefabDatabase.Instance.Clear();
        AssemblyLoader.Unload();
        AudioDeviceService.ShutdownIfUnused();
    }

    private void SubscribeToLogErrors()
    {
        // Count existing errors
        foreach (var entry in FrinkyLog.Entries)
        {
            if (entry.Level == LogLevel.Error)
                _errorCount++;
        }

        if (_errorCount > 0)
            UpdateErrorNotification();

        FrinkyLog.OnLog += OnLogEntry;
        FrinkyLog.OnCleared += OnLogCleared;
    }

    private void OnLogEntry(LogEntry entry)
    {
        if (entry.Level == LogLevel.Error)
        {
            _errorCount++;
            UpdateErrorNotification();
        }
    }

    private void OnLogCleared()
    {
        _errorCount = 0;
        if (_errorCountNotification != null)
        {
            _errorCountNotification.Duration = 0.01f;
            _errorCountNotification.Elapsed = 0f;
            _errorCountNotification.IsCompleted = true;
            _errorCountNotification = null;
        }
    }

    private void UpdateErrorNotification()
    {
        var text = _errorCount == 1 ? "1 error in log" : $"{_errorCount} errors in log";

        if (_errorCountNotification == null)
        {
            _errorCountNotification = NotificationManager.Instance.Post(text, NotificationType.Error, 8f);
        }
        else
        {
            _errorCountNotification.Message = text;
            _errorCountNotification.Elapsed = 0f;
            _errorCountNotification.Duration = 8f;
        }
    }

    public void SaveProjectSettings(ProjectSettings settings)
    {
        if (ProjectDirectory == null || ProjectFile == null)
            return;

        var path = Core.Assets.ProjectSettings.GetPath(ProjectDirectory);
        settings.Normalize(ProjectFile.ProjectName);
        settings.Save(path);
        ProjectSettings = settings;
        Core.Physics.PhysicsProjectSettings.ApplyFrom(settings.Runtime);
        AudioProjectSettings.ApplyFrom(settings.Runtime);
        Audio.SetBusVolume(AudioBusId.Master, settings.Runtime.AudioMasterVolume);
        Audio.SetBusVolume(AudioBusId.Music, settings.Runtime.AudioMusicVolume);
        Audio.SetBusVolume(AudioBusId.Sfx, settings.Runtime.AudioSfxVolume);
        Audio.SetBusVolume(AudioBusId.Ui, settings.Runtime.AudioUiVolume);
        Audio.SetBusVolume(AudioBusId.Voice, settings.Runtime.AudioVoiceVolume);
        Audio.SetBusVolume(AudioBusId.Ambient, settings.Runtime.AudioAmbientVolume);
        ApplyRuntimeRenderSettingsImmediate();
    }

    public void SaveTagDatabase()
    {
        if (ProjectDirectory == null || TagDatabase == null)
            return;

        TagDatabase.Save(ProjectDirectory);
    }

    public void SaveEditorProjectSettings(EditorProjectSettings settings)
    {
        if (ProjectDirectory == null)
            return;

        settings.Normalize();
        settings.Save(ProjectDirectory);
        ProjectEditorSettings = settings;
        _hierarchyStateDirty = false;
        ApplyEditorSettingsImmediate();
    }

    private void ApplyEditorSettingsImmediate()
    {
        var settings = ProjectEditorSettings;
        if (settings == null)
            return;

        Raylib.SetTargetFPS(settings.TargetFps);

        if (settings.VSync)
            Raylib.SetWindowState(ConfigFlags.VSyncHint);
        else
            Raylib.ClearWindowState(ConfigFlags.VSyncHint);
    }

    private void ApplyRuntimeRenderSettingsImmediate()
    {
        var runtime = ProjectSettings?.Runtime;
        if (runtime == null)
        {
            SceneRenderer.ConfigureForwardPlus(ForwardPlusSettings.Default);
            return;
        }

        SceneRenderer.ConfigureForwardPlus(new ForwardPlusSettings(
            runtime.ForwardPlusTileSize,
            runtime.ForwardPlusMaxLights,
            runtime.ForwardPlusMaxLightsPerTile));
    }

    private bool LaunchVSCode(IEnumerable<string> arguments, string? successMessage)
    {
        try
        {
            var quotedArgs = string.Join(" ", arguments.Select(a => $"\"{a}\""));
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                Arguments = OperatingSystem.IsWindows()
                    ? $"/c code {quotedArgs}"
                    : $"-c \"code {quotedArgs}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // The .NET runtime sets environment variables (DOTNET_ROOT, MSBuild paths, etc.)
            // that pollute child processes. C# Dev Kit in VS Code picks these up instead of
            // the system SDK and fails. Strip them so VS Code gets a clean environment.
            var keysToRemove = psi.Environment.Keys
                .Where(k => k.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) ||
                            k.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var key in keysToRemove)
                psi.Environment.Remove(key);

            using var process = Process.Start(psi);
            if (process == null)
            {
                FrinkyLog.Error("Failed to launch VS Code: Process.Start returned null.");
                NotificationManager.Instance.Post(
                    "Failed to launch VS Code. Ensure the 'code' command is available in your PATH.",
                    NotificationType.Error, 5.0f);
                return false;
            }

            FrinkyLog.Info($"Launched VS Code: code {quotedArgs}");
            if (!string.IsNullOrEmpty(successMessage))
                NotificationManager.Instance.Post(successMessage, NotificationType.Info, 2.0f);
            return true;
        }
        catch (Exception ex)
        {
            FrinkyLog.Error($"Failed to launch VS Code: {ex.Message}");
            NotificationManager.Instance.Post(
                "Failed to launch VS Code. Ensure the 'code' command is available in your PATH.",
                NotificationType.Error, 5.0f);
            return false;
        }
    }
}
