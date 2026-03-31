using System.Numerics;
using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.Rendering;
using FrinkyEngine.Core.Scene;
using FrinkyEngine.Editor.Assets.Creation;
using Hexa.NET.ImGui;
using NativeFileDialogSharp;

namespace FrinkyEngine.Editor.Panels;

public class MenuBar
{
    private readonly EditorApplication _app;
    private string _newProjectName = string.Empty;
    private string _newProjectParentDir = string.Empty;
    private int _selectedTemplateIndex;
    private ProjectTemplate[]? _cachedTemplates;
    private readonly AssetCreationModal _assetCreationModal;

    private bool _openNewProject;
    private bool _openProjectSettings;
    private bool _openKeybindEditor;
    private ProjectSettings? _projectSettingsDraft;
    private ProjectSettings? _projectSettingsBaseline;
    private EditorProjectSettings? _editorProjectSettingsDraft;
    private EditorProjectSettings? _editorProjectSettingsBaseline;

    // Keybind editor state
    private bool _isCapturingKeybind;
    private EditorAction? _capturingAction;
    private string _keybindSearchFilter = string.Empty;

    public MenuBar(EditorApplication app)
    {
        _app = app;
        _assetCreationModal = new AssetCreationModal(app);
    }

    public void Draw()
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New Scene", KeybindManager.Instance.GetShortcutText(EditorAction.NewScene)))
                {
                    _app.NewScene();
                }

                if (ImGui.MenuItem("Open Scene...", KeybindManager.Instance.GetShortcutText(EditorAction.OpenScene)))
                {
                    OpenSceneDialog();
                }

                var canSaveScene = _app.Mode == EditorMode.Edit;
                ImGui.BeginDisabled(!canSaveScene);
                if (ImGui.MenuItem("Save Scene", KeybindManager.Instance.GetShortcutText(EditorAction.SaveScene)))
                {
                    if (_app.CurrentScene != null)
                    {
                        _app.StoreEditorCameraInScene();
                        var path = !string.IsNullOrEmpty(_app.CurrentScene.FilePath)
                            ? _app.CurrentScene.FilePath
                            : "scene.fscene";
                        SceneManager.Instance.SaveScene(path);
                        _app.ClearSceneDirty();
                        FrinkyLog.Info($"Scene saved to: {path}");
                        NotificationManager.Instance.Post("Scene saved", NotificationType.Success);
                    }
                }

                if (ImGui.MenuItem("Save Scene As...", KeybindManager.Instance.GetShortcutText(EditorAction.SaveSceneAs)))
                {
                    SaveSceneAs();
                }
                ImGui.EndDisabled();

                ImGui.Separator();

                if (ImGui.MenuItem("New Project...", KeybindManager.Instance.GetShortcutText(EditorAction.NewProject)))
                    _openNewProject = true;

                if (ImGui.MenuItem("Open Project..."))
                {
                    OpenProjectDialog();
                }

                var hasProjectSettings = _app.ProjectDirectory != null
                                         && _app.ProjectSettings != null
                                         && _app.ProjectEditorSettings != null;
                ImGui.BeginDisabled(!hasProjectSettings);
                if (ImGui.MenuItem("Project Settings..."))
                {
                    OpenProjectSettingsPopup();
                }
                ImGui.EndDisabled();

                ImGui.Separator();

                var hasProjectForAssets = _app.ProjectDirectory != null;
                ImGui.BeginDisabled(!hasProjectForAssets);
                if (ImGui.MenuItem("Open Assets Folder", KeybindManager.Instance.GetShortcutText(EditorAction.OpenAssetsFolder)))
                {
                    if (_app.ProjectDirectory != null && _app.ProjectFile != null)
                    {
                        var assetsPath = _app.ProjectFile.GetAbsoluteAssetsPath(_app.ProjectDirectory);
                        if (Directory.Exists(assetsPath))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = assetsPath,
                                UseShellExecute = true
                            });
                        }
                    }
                }

                if (ImGui.MenuItem("Open Project in VS Code", KeybindManager.Instance.GetShortcutText(EditorAction.OpenProjectInVSCode)))
                {
                    _app.OpenProjectInVSCode();
                }
                ImGui.EndDisabled();

                ImGui.Separator();

                var canBuildScripts = _app.ProjectDirectory != null && !ScriptBuilder.IsBuilding;
                ImGui.BeginDisabled(!canBuildScripts);
                if (ImGui.MenuItem("Build Scripts", KeybindManager.Instance.GetShortcutText(EditorAction.BuildScripts)))
                    _app.BuildScripts();
                ImGui.EndDisabled();
                if (ScriptBuilder.IsBuilding)
                    ImGui.TextDisabled("Building...");

                ImGui.Separator();

                var canExport = _app.ProjectDirectory != null
                    && _app.Mode == EditorMode.Edit
                    && !GameExporter.IsExporting
                    && !ScriptBuilder.IsBuilding;
                ImGui.BeginDisabled(!canExport);
                if (ImGui.MenuItem("Export Game...", KeybindManager.Instance.GetShortcutText(EditorAction.ExportGame)))
                {
                    ExportGameDialog();
                }
                ImGui.EndDisabled();

                if (GameExporter.IsExporting)
                    ImGui.TextDisabled("Exporting...");

                ImGui.Separator();

                if (ImGui.MenuItem("Exit"))
                {
                    Raylib_cs.Raylib.CloseWindow();
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Edit"))
            {
                bool canUndo = _app.UndoRedo.CanUndo && _app.Mode == EditorMode.Edit;
                bool canRedo = _app.UndoRedo.CanRedo && _app.Mode == EditorMode.Edit;

                if (ImGui.MenuItem("Undo", KeybindManager.Instance.GetShortcutText(EditorAction.Undo), false, canUndo))
                    _app.UndoRedo.Undo(_app);
                if (ImGui.MenuItem("Redo", KeybindManager.Instance.GetShortcutText(EditorAction.Redo), false, canRedo))
                    _app.UndoRedo.Redo(_app);

                ImGui.Separator();

                if (ImGui.MenuItem("Select All", KeybindManager.Instance.GetShortcutText(EditorAction.SelectAllEntities)))
                {
                    if (_app.CurrentScene != null)
                        _app.SetSelection(_app.CurrentScene.Entities);
                }

                if (ImGui.MenuItem("Hierarchy Search", KeybindManager.Instance.GetShortcutText(EditorAction.FocusHierarchySearch)))
                {
                    _app.HierarchyPanel.FocusSearch();
                }

                ImGui.Separator();

                var hasSelection = _app.SelectedEntities.Count > 0;
                var hasSingleSelection = _app.SelectedEntities.Count == 1;

                ImGui.BeginDisabled(!hasSelection);
                if (ImGui.MenuItem("Delete", KeybindManager.Instance.GetShortcutText(EditorAction.DeleteEntity)))
                {
                    _app.DeleteSelectedEntities();
                }

                if (ImGui.MenuItem("Duplicate", KeybindManager.Instance.GetShortcutText(EditorAction.DuplicateEntity)))
                {
                    _app.DuplicateSelectedEntities();
                }
                ImGui.EndDisabled();

                ImGui.Separator();

                ImGui.BeginDisabled(!hasSingleSelection);
                if (ImGui.MenuItem("Create Prefab from Selection", KeybindManager.Instance.GetShortcutText(EditorAction.CreatePrefabFromSelection)))
                    _app.CreatePrefabFromSelection();

                var prefabRoot = _app.Prefabs.GetPrefabRoot(_app.SelectedEntity);
                bool isPrefabRootSelection = prefabRoot != null
                                             && _app.SelectedEntity != null
                                             && prefabRoot.Id == _app.SelectedEntity.Id;

                ImGui.BeginDisabled(!isPrefabRootSelection);
                if (ImGui.MenuItem("Apply Prefab", KeybindManager.Instance.GetShortcutText(EditorAction.ApplyPrefab)))
                    _app.ApplySelectedPrefab();
                if (ImGui.MenuItem("Revert Prefab", KeybindManager.Instance.GetShortcutText(EditorAction.RevertPrefab)))
                    _app.RevertSelectedPrefab();
                if (ImGui.MenuItem("Make Unique Prefab", KeybindManager.Instance.GetShortcutText(EditorAction.MakeUniquePrefab)))
                    _app.MakeUniqueSelectedPrefab();
                if (ImGui.MenuItem("Unpack Prefab", KeybindManager.Instance.GetShortcutText(EditorAction.UnpackPrefab)))
                    _app.UnpackSelectedPrefab();
                ImGui.EndDisabled();
                ImGui.EndDisabled();

                ImGui.BeginDisabled(!hasSingleSelection);
                if (ImGui.MenuItem("Rename", KeybindManager.Instance.GetShortcutText(EditorAction.RenameEntity)))
                {
                    _app.InspectorPanel.FocusNameField = true;
                }
                ImGui.EndDisabled();

                ImGui.Separator();

                var hasUnresolved = _app.CanEditScene && _app.CurrentScene != null && HasUnresolvedComponents(_app.CurrentScene);
                var hasScenePrefabs = _app.CanEditScene && _app.CurrentScene != null && _app.CurrentScene.Entities.Any(_app.Prefabs.IsPrefabRoot);
                ImGui.BeginDisabled(!hasScenePrefabs);
                if (ImGui.MenuItem("Fix Scene Prefabs"))
                {
                    _app.RefreshScenePrefabInstances();
                }
                ImGui.EndDisabled();

                ImGui.Separator();

                ImGui.BeginDisabled(!hasUnresolved);
                if (ImGui.MenuItem("Clean Up Unresolved Components"))
                {
                    CleanUpUnresolvedComponents();
                }
                ImGui.EndDisabled();

                ImGui.Separator();

                var hasProject = _app.ProjectDirectory != null;
                ImGui.BeginDisabled(!hasProject);
                if (ImGui.MenuItem("Keybindings..."))
                    _openKeybindEditor = true;
                ImGui.EndDisabled();

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Create"))
            {
                var hasProject = _app.ProjectDirectory != null;

                ImGui.BeginDisabled(!hasProject);
                foreach (var factory in AssetCreationRegistry.GetFactories())
                {
                    if (ImGui.MenuItem($"Create {factory.DisplayName}..."))
                        _assetCreationModal.Open(factory.Id);
                }
                ImGui.EndDisabled();

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.MenuItem(
                        "Game View",
                        KeybindManager.Instance.GetShortcutText(EditorAction.ToggleGameView),
                        _app.IsGameViewEnabled))
                {
                    _app.ToggleGameView();
                }

                if (ImGui.MenuItem(
                        "Physics Hitboxes",
                        KeybindManager.Instance.GetShortcutText(EditorAction.TogglePhysicsHitboxPreview),
                        _app.IsPhysicsHitboxPreviewEnabled))
                {
                    _app.TogglePhysicsHitboxPreview();
                }

                if (ImGui.MenuItem(
                        "Collider Edit Mode",
                        KeybindManager.Instance.GetShortcutText(EditorAction.ToggleColliderEditMode),
                        _app.IsColliderEditModeEnabled))
                {
                    _app.ToggleColliderEditMode();
                }

                if (ImGui.MenuItem(
                        "Bone Preview",
                        KeybindManager.Instance.GetShortcutText(EditorAction.ToggleBonePreview),
                        _app.IsBonePreviewEnabled))
                {
                    _app.ToggleBonePreview();
                }

                ImGui.Separator();

                if (ImGui.BeginMenu("Theme"))
                {
                    foreach (var themeId in Enum.GetValues<EditorThemeId>())
                    {
                        var isSelected = EditorTheme.Current == themeId;
                        if (ImGui.MenuItem(EditorTheme.FormatThemeName(themeId), (string?)null, isSelected))
                        {
                            EditorPreferences.Instance.SetTheme(themeId);
                        }
                    }
                    ImGui.EndMenu();
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Window"))
            {
                ImGui.MenuItem("Viewport", (string?)null, true);
                ImGui.MenuItem("Hierarchy", (string?)null, true);
                ImGui.MenuItem("Inspector", (string?)null, true);
                ImGui.MenuItem("Console", (string?)null, true);
                ImGui.MenuItem("Assets", (string?)null, true);
                if (ImGui.MenuItem("Performance", (string?)null, _app.PerformancePanel.IsVisible))
                    _app.PerformancePanel.IsVisible = !_app.PerformancePanel.IsVisible;
                ImGui.Separator();
                if (ImGui.MenuItem("Reset Layout"))
                {
                    _app.ShouldResetLayout = true;
                }
                ImGui.EndMenu();
            }

            ImGui.Separator();

            var playShortcut = KeybindManager.Instance.GetShortcutText(EditorAction.PlayStop);
            var simulateShortcut = KeybindManager.Instance.GetShortcutText(EditorAction.SimulateStop);
            if (_app.Mode == EditorMode.Edit)
            {
                ImGui.BeginDisabled(ScriptBuilder.IsBuilding);
                if (ImGui.MenuItem("Play", playShortcut))
                    _app.EnterPlayMode();

                ImGui.SameLine();
                if (ImGui.MenuItem("Simulate", simulateShortcut))
                    _app.EnterSimulateMode();
                ImGui.EndDisabled();
            }
            else
            {
                if (ImGui.MenuItem("Stop", playShortcut))
                    _app.ExitRuntimeMode();
            }

            ImGui.EndMainMenuBar();
        }

        // Open popups at this scope level (outside the menu) so BeginPopup can find them
        if (_openNewProject)
        {
            ImGui.OpenPopup("New Project");
            _openNewProject = false;
        }

        if (_openProjectSettings)
        {
            ImGui.OpenPopup("ProjectSettings");
            _openProjectSettings = false;
        }

        if (_openKeybindEditor)
        {
            ImGui.OpenPopup("KeybindEditor");
            _openKeybindEditor = false;
        }

        DrawNewProjectPopup();
        _assetCreationModal.Draw();
        DrawProjectSettingsPopup();
        DrawKeybindEditorPopup();
    }

    private void OpenSceneDialog()
    {
        string? defaultPath = null;
        if (_app.ProjectDirectory != null && _app.ProjectFile != null)
        {
            var assetsDir = _app.ProjectFile.GetAbsoluteAssetsPath(_app.ProjectDirectory);
            var scenesDir = Path.Combine(assetsDir, "Scenes");
            if (Directory.Exists(scenesDir))
                defaultPath = scenesDir;
            else if (Directory.Exists(assetsDir))
                defaultPath = assetsDir;
        }

        var result = Dialog.FileOpen("fscene", defaultPath);
        if (!result.IsOk) return;

        int logCursor = _app.CaptureLogCursor();
        SceneManager.Instance.LoadScene(result.Path);
        _app.CurrentScene = SceneManager.Instance.ActiveScene;
        _app.Prefabs.RecalculateOverridesForScene();
        _app.ClearSelection();
        _app.RestoreEditorCameraFromScene();
        _app.UpdateWindowTitle();
        _app.UndoRedo.Clear();
        _app.UndoRedo.SetBaseline(_app.CurrentScene, _app.GetSelectedEntityIds(), _app.SerializeCurrentHierarchyState());
        _app.NotifySkippedComponentWarningsSince(logCursor, "Scene open");
        FrinkyLog.Info($"Opened scene: {result.Path}");
        NotificationManager.Instance.Post($"Scene opened: {_app.CurrentScene?.Name ?? "scene"}", NotificationType.Success);
    }

    private void SaveSceneAs()
    {
        if (_app.CurrentScene == null) return;
        _app.StoreEditorCameraInScene();

        // Default to the project's assets directory if a project is open
        string? defaultPath = null;
        if (_app.ProjectDirectory != null && _app.ProjectFile != null)
        {
            var assetsDir = _app.ProjectFile.GetAbsoluteAssetsPath(_app.ProjectDirectory);
            var scenesDir = Path.Combine(assetsDir, "Scenes");
            if (Directory.Exists(scenesDir))
                defaultPath = scenesDir;
            else if (Directory.Exists(assetsDir))
                defaultPath = assetsDir;
        }

        var result = Dialog.FileSave("fscene", defaultPath);
        if (!result.IsOk) return;

        var path = result.Path;

        // Auto-append .fscene if missing
        if (!path.EndsWith(".fscene", StringComparison.OrdinalIgnoreCase))
            path += ".fscene";

        SceneManager.Instance.SaveScene(path);
        _app.ClearSceneDirty();
        FrinkyLog.Info($"Scene saved to: {path}");
        NotificationManager.Instance.Post("Scene saved", NotificationType.Success);
    }

    private void OpenProjectDialog()
    {
        var result = Dialog.FileOpen("fproject");
        if (!result.IsOk) return;

        _app.OpenProject(result.Path);
    }

    private void DrawNewProjectPopup()
    {
        // Cache templates on first access
        _cachedTemplates ??= ProjectTemplateRegistry.Templates.ToArray();

        var viewport = ImGui.GetMainViewport();
        var center = new Vector2(viewport.WorkPos.X + viewport.WorkSize.X * 0.5f,
                                 viewport.WorkPos.Y + viewport.WorkSize.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(600, 0), ImGuiCond.Appearing);

        if (ImGui.BeginPopupModal("New Project", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove))
        {
            // --- Template selection ---
            if (_cachedTemplates.Length > 0)
            {
                ImGui.TextUnformatted("Template");
                ImGui.Spacing();

                var contentWidth = ImGui.GetContentRegionAvail().X;
                var cardCount = _cachedTemplates.Length;
                const float cardSpacing = 8;
                var cardWidth = (contentWidth - cardSpacing * (cardCount - 1)) / cardCount;
                const float cardHeight = 80;

                for (var i = 0; i < _cachedTemplates.Length; i++)
                {
                    if (i > 0)
                        ImGui.SameLine(0, cardSpacing);

                    var template = _cachedTemplates[i];
                    var isSelected = i == _selectedTemplateIndex;

                    // Selected: accent border + slightly brighter background
                    if (isSelected)
                    {
                        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.22f, 0.30f, 0.45f, 1.0f));
                        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.35f, 0.55f, 0.85f, 1.0f));
                    }
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetColorU32(ImGuiCol.FrameBg));
                        ImGui.PushStyleColor(ImGuiCol.Border, ImGui.GetColorU32(ImGuiCol.FrameBg));
                    }

                    ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, isSelected ? 2.0f : 1.0f);
                    ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6.0f);

                    if (ImGui.BeginChild($"##template_{i}", new Vector2(cardWidth, cardHeight), ImGuiChildFlags.Borders))
                    {
                        // Center the text vertically and horizontally
                        var textSize = ImGui.CalcTextSize(template.Name);
                        var regionAvail = ImGui.GetContentRegionAvail();
                        var cursorStart = ImGui.GetCursorPos();
                        ImGui.SetCursorPosX(cursorStart.X + (regionAvail.X - textSize.X) * 0.5f);
                        ImGui.SetCursorPosY(cursorStart.Y + (regionAvail.Y - textSize.Y) * 0.5f);
                        ImGui.TextUnformatted(template.Name);

                        // Make the entire child area clickable
                        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                            _selectedTemplateIndex = i;
                    }
                    ImGui.EndChild();

                    ImGui.PopStyleVar(2);
                    ImGui.PopStyleColor(2);
                }

                // Description text below the cards
                ImGui.Spacing();
                if (_selectedTemplateIndex >= 0 && _selectedTemplateIndex < _cachedTemplates.Length)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
                    ImGui.TextWrapped(_cachedTemplates[_selectedTemplateIndex].Description);
                    ImGui.PopStyleColor();
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            // --- Project details ---
            const float labelWidth = 110;

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Project Name");
            ImGui.SameLine(labelWidth);
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##projectName", ref _newProjectName, 256);

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Location");
            ImGui.SameLine(labelWidth);
            var browseWidth = ImGui.CalcTextSize("Browse...").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
            ImGui.SetNextItemWidth(-browseWidth);
            ImGui.InputText("##location", ref _newProjectParentDir, 512);
            ImGui.SameLine();
            if (ImGui.Button("Browse..."))
            {
                var result = Dialog.FolderPicker(
                    string.IsNullOrWhiteSpace(_newProjectParentDir) ? null : _newProjectParentDir);
                if (result.IsOk)
                    _newProjectParentDir = result.Path;
            }

            if (!string.IsNullOrWhiteSpace(_newProjectName) && !string.IsNullOrWhiteSpace(_newProjectParentDir))
            {
                var targetPath = Path.Combine(_newProjectParentDir, _newProjectName);
                ImGui.TextUnformatted("Target");
                ImGui.SameLine(labelWidth);
                ImGui.TextDisabled(targetPath);

                if (Directory.Exists(targetPath))
                {
                    ImGui.Dummy(new Vector2(0, 2));
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.3f, 0.3f, 1));
                    ImGui.TextWrapped("Target directory already exists!");
                    ImGui.PopStyleColor();
                }
            }

            // --- Buttons ---
            ImGui.Spacing();
            ImGui.Spacing();

            var parentExists = !string.IsNullOrWhiteSpace(_newProjectParentDir) && Directory.Exists(_newProjectParentDir);
            var nameValid = !string.IsNullOrWhiteSpace(_newProjectName);
            var targetExists = nameValid && parentExists && Directory.Exists(Path.Combine(_newProjectParentDir, _newProjectName));
            var hasTemplate = _cachedTemplates.Length > 0 && _selectedTemplateIndex >= 0 && _selectedTemplateIndex < _cachedTemplates.Length;

            // Right-align the buttons
            var style = ImGui.GetStyle();
            var createBtnWidth = ImGui.CalcTextSize("Create Project").X + style.FramePadding.X * 2 + 16;
            var cancelBtnWidth = ImGui.CalcTextSize("Cancel").X + style.FramePadding.X * 2 + 16;
            var totalWidth = cancelBtnWidth + style.ItemSpacing.X + createBtnWidth;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - totalWidth);

            if (ImGui.Button("Cancel", new Vector2(cancelBtnWidth, 0)))
                ImGui.CloseCurrentPopup();

            ImGui.SameLine();

            ImGui.BeginDisabled(!nameValid || !parentExists || targetExists || !hasTemplate);
            if (ImGui.Button("Create Project", new Vector2(createBtnWidth, 0)))
            {
                _app.CreateAndOpenProject(_newProjectParentDir, _newProjectName, _cachedTemplates[_selectedTemplateIndex]);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();

            ImGui.EndPopup();
        }
    }

    public void TriggerOpenScene() => OpenSceneDialog();
    public void TriggerSaveSceneAs() => SaveSceneAs();
    public void TriggerNewProject() => _openNewProject = true;

    public void TriggerExportGame()
    {
        if (_app.ProjectDirectory != null && _app.Mode == EditorMode.Edit
            && !GameExporter.IsExporting && !ScriptBuilder.IsBuilding)
        {
            ExportGameDialog();
        }
    }

    private void ExportGameDialog()
    {
        var result = Dialog.FolderPicker(_app.ProjectDirectory);
        if (!result.IsOk) return;

        // Auto-save scene if it has a file path
        if (_app.CurrentScene != null && !string.IsNullOrEmpty(_app.CurrentScene.FilePath))
        {
            _app.StoreEditorCameraInScene();
            SceneManager.Instance.SaveScene(_app.CurrentScene.FilePath);
        }

        _app.ExportGame(result.Path);
    }

    private void OpenProjectSettingsPopup()
    {
        if (_app.ProjectSettings == null || _app.ProjectEditorSettings == null)
            return;

        _projectSettingsDraft = _app.ProjectSettings.Clone();
        _projectSettingsBaseline = _app.ProjectSettings.Clone();
        _editorProjectSettingsDraft = _app.ProjectEditorSettings.Clone();
        _editorProjectSettingsBaseline = _app.ProjectEditorSettings.Clone();
        _openProjectSettings = true;
    }

    private void DrawProjectSettingsPopup()
    {
        var viewport = ImGui.GetMainViewport();
        var center = new Vector2(viewport.WorkPos.X + viewport.WorkSize.X * 0.5f,
                                 viewport.WorkPos.Y + viewport.WorkSize.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(620, 0), ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(new Vector2(520, 0), new Vector2(float.MaxValue, float.MaxValue));

        if (!ImGui.BeginPopupModal("ProjectSettings", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (_projectSettingsDraft == null
            || _projectSettingsBaseline == null
            || _editorProjectSettingsDraft == null
            || _editorProjectSettingsBaseline == null)
        {
            ImGui.TextDisabled("Project settings are not loaded.");
            if (ImGui.Button("Close"))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        var draft = _projectSettingsDraft;
        var editorDraft = _editorProjectSettingsDraft;

        if (ImGui.CollapsingHeader("Project", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var version = draft.Project.Version;
            EditText("Version", ref version, 64);
            draft.Project.Version = version;

            var author = draft.Project.Author;
            EditText("Author", ref author, 128);
            draft.Project.Author = author;

            var company = draft.Project.Company;
            EditText("Company", ref company, 128);
            draft.Project.Company = company;

            var description = draft.Project.Description;
            EditTextMultiline("Description", ref description, 512, new Vector2(560, 64));
            draft.Project.Description = description;
        }

        if (ImGui.CollapsingHeader("Editor"))
        {
            int editorFps = editorDraft.TargetFps;
            if (ImGui.InputInt("Editor Target FPS", ref editorFps))
                editorDraft.TargetFps = editorFps;

            bool editorVSync = editorDraft.VSync;
            if (ImGui.Checkbox("Editor VSync", ref editorVSync))
                editorDraft.VSync = editorVSync;
        }

        if (ImGui.CollapsingHeader("Runtime"))
        {
            int runtimeFps = draft.Runtime.TargetFps;
            if (ImGui.InputInt("Runtime Target FPS", ref runtimeFps))
                draft.Runtime.TargetFps = runtimeFps;

            bool vSync = draft.Runtime.VSync;
            if (ImGui.Checkbox("VSync", ref vSync))
                draft.Runtime.VSync = vSync;

            var windowTitle = draft.Runtime.WindowTitle;
            EditText("Window Title", ref windowTitle, 256);
            draft.Runtime.WindowTitle = windowTitle;

            int windowWidth = draft.Runtime.WindowWidth;
            if (ImGui.InputInt("Window Width", ref windowWidth))
                draft.Runtime.WindowWidth = windowWidth;

            int windowHeight = draft.Runtime.WindowHeight;
            if (ImGui.InputInt("Window Height", ref windowHeight))
                draft.Runtime.WindowHeight = windowHeight;

            bool resizable = draft.Runtime.Resizable;
            if (ImGui.Checkbox("Resizable", ref resizable))
                draft.Runtime.Resizable = resizable;

            bool fullscreen = draft.Runtime.Fullscreen;
            if (ImGui.Checkbox("Fullscreen", ref fullscreen))
                draft.Runtime.Fullscreen = fullscreen;

            bool startMaximized = draft.Runtime.StartMaximized;
            if (ImGui.Checkbox("Start Maximized", ref startMaximized))
                draft.Runtime.StartMaximized = startMaximized;

            DrawStartupSceneSelector(draft);
            ImGui.TextDisabled("Use .fproject defaultScene or choose any scene asset.");
        }

        if (ImGui.CollapsingHeader("Rendering"))
        {
            int screenPercentage = draft.Runtime.ScreenPercentage;
            if (ImGui.SliderInt("Screen Percentage", ref screenPercentage, 10, 200))
            {
                draft.Runtime.ScreenPercentage = screenPercentage;
                RenderRuntimeCvars.ScreenPercentage = screenPercentage;
            }

            ImGui.Separator();

            int forwardPlusTileSize = draft.Runtime.ForwardPlusTileSize;
            if (ImGui.InputInt("Forward+ Tile Size", ref forwardPlusTileSize))
                draft.Runtime.ForwardPlusTileSize = forwardPlusTileSize;

            int forwardPlusMaxLights = draft.Runtime.ForwardPlusMaxLights;
            if (ImGui.InputInt("Forward+ Max Lights", ref forwardPlusMaxLights))
                draft.Runtime.ForwardPlusMaxLights = forwardPlusMaxLights;

            int forwardPlusMaxLightsPerTile = draft.Runtime.ForwardPlusMaxLightsPerTile;
            if (ImGui.InputInt("Forward+ Max Lights Per Tile", ref forwardPlusMaxLightsPerTile))
                draft.Runtime.ForwardPlusMaxLightsPerTile = forwardPlusMaxLightsPerTile;
        }

        if (ImGui.CollapsingHeader("Physics"))
        {
            float fixedTimestep = draft.Runtime.PhysicsFixedTimestep;
            if (ImGui.InputFloat("Fixed Timestep", ref fixedTimestep, 0f, 0f, "%.6f"))
                draft.Runtime.PhysicsFixedTimestep = fixedTimestep;

            int maxSubsteps = draft.Runtime.PhysicsMaxSubstepsPerFrame;
            if (ImGui.InputInt("Max Substeps Per Frame", ref maxSubsteps))
                draft.Runtime.PhysicsMaxSubstepsPerFrame = maxSubsteps;

            int solverVelIter = draft.Runtime.PhysicsSolverVelocityIterations;
            if (ImGui.InputInt("Solver Velocity Iterations", ref solverVelIter))
                draft.Runtime.PhysicsSolverVelocityIterations = solverVelIter;

            int solverSubsteps = draft.Runtime.PhysicsSolverSubsteps;
            if (ImGui.InputInt("Solver Substeps", ref solverSubsteps))
                draft.Runtime.PhysicsSolverSubsteps = solverSubsteps;

            float springFreq = draft.Runtime.PhysicsContactSpringFrequency;
            if (ImGui.InputFloat("Contact Spring Frequency", ref springFreq))
                draft.Runtime.PhysicsContactSpringFrequency = springFreq;

            float dampingRatio = draft.Runtime.PhysicsContactDampingRatio;
            if (ImGui.InputFloat("Contact Damping Ratio", ref dampingRatio))
                draft.Runtime.PhysicsContactDampingRatio = dampingRatio;

            float maxRecovery = draft.Runtime.PhysicsMaximumRecoveryVelocity;
            if (ImGui.InputFloat("Max Recovery Velocity", ref maxRecovery))
                draft.Runtime.PhysicsMaximumRecoveryVelocity = maxRecovery;

            float friction = draft.Runtime.PhysicsDefaultFriction;
            if (ImGui.InputFloat("Default Friction", ref friction))
                draft.Runtime.PhysicsDefaultFriction = friction;

            float restitution = draft.Runtime.PhysicsDefaultRestitution;
            if (ImGui.InputFloat("Default Restitution", ref restitution))
                draft.Runtime.PhysicsDefaultRestitution = restitution;

            bool interpolationEnabled = draft.Runtime.PhysicsInterpolationEnabled;
            if (ImGui.Checkbox("Interpolation Enabled", ref interpolationEnabled))
                draft.Runtime.PhysicsInterpolationEnabled = interpolationEnabled;
        }

        if (ImGui.CollapsingHeader("Audio"))
        {
            float master = draft.Runtime.AudioMasterVolume;
            if (ImGui.InputFloat("Master Volume", ref master))
                draft.Runtime.AudioMasterVolume = master;

            float music = draft.Runtime.AudioMusicVolume;
            if (ImGui.InputFloat("Music Volume", ref music))
                draft.Runtime.AudioMusicVolume = music;

            float sfx = draft.Runtime.AudioSfxVolume;
            if (ImGui.InputFloat("SFX Volume", ref sfx))
                draft.Runtime.AudioSfxVolume = sfx;

            float ui = draft.Runtime.AudioUiVolume;
            if (ImGui.InputFloat("UI Volume", ref ui))
                draft.Runtime.AudioUiVolume = ui;

            float voice = draft.Runtime.AudioVoiceVolume;
            if (ImGui.InputFloat("Voice Volume", ref voice))
                draft.Runtime.AudioVoiceVolume = voice;

            float ambient = draft.Runtime.AudioAmbientVolume;
            if (ImGui.InputFloat("Ambient Volume", ref ambient))
                draft.Runtime.AudioAmbientVolume = ambient;

            int maxVoices = draft.Runtime.AudioMaxVoices;
            if (ImGui.InputInt("Max Voices", ref maxVoices))
                draft.Runtime.AudioMaxVoices = maxVoices;

            float doppler = draft.Runtime.AudioDopplerScale;
            if (ImGui.InputFloat("Doppler Scale", ref doppler))
                draft.Runtime.AudioDopplerScale = doppler;

            bool voiceStealing = draft.Runtime.AudioEnableVoiceStealing;
            if (ImGui.Checkbox("Enable Voice Stealing", ref voiceStealing))
                draft.Runtime.AudioEnableVoiceStealing = voiceStealing;
        }

        if (ImGui.CollapsingHeader("Build"))
        {
            var outputName = draft.Build.OutputName;
            EditText("Output Name", ref outputName, 128);
            draft.Build.OutputName = outputName;

            var buildVersion = draft.Build.BuildVersion;
            EditText("Build Version", ref buildVersion, 64);
            draft.Build.BuildVersion = buildVersion;
        }

        ImGui.Separator();

        if (ImGui.Button("Apply"))
        {
            var requiresRestartNotice = HasDeferredRuntimeWindowChanges(_projectSettingsBaseline, draft);
            var editorSettingsChanged = _editorProjectSettingsBaseline.TargetFps != editorDraft.TargetFps
                                        || _editorProjectSettingsBaseline.VSync != editorDraft.VSync;
            _app.SaveProjectSettings(draft.Clone());
            _app.SaveEditorProjectSettings(editorDraft.Clone());

            if (requiresRestartNotice)
            {
                NotificationManager.Instance.Post(
                    "Settings saved. Runtime window mode/size changes apply on next launch.",
                    NotificationType.Info, 4.0f);
            }
            else
            {
                NotificationManager.Instance.Post("Project settings saved.", NotificationType.Success);
            }

            if (editorSettingsChanged)
            {
                NotificationManager.Instance.Post(
                    "Editor FPS/VSync applied immediately.",
                    NotificationType.Info, 2.5f);
            }

            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private static bool HasDeferredRuntimeWindowChanges(ProjectSettings before, ProjectSettings after)
    {
        return before.Runtime.VSync != after.Runtime.VSync
               || before.Runtime.WindowWidth != after.Runtime.WindowWidth
               || before.Runtime.WindowHeight != after.Runtime.WindowHeight
               || before.Runtime.Resizable != after.Runtime.Resizable
               || before.Runtime.Fullscreen != after.Runtime.Fullscreen
               || before.Runtime.StartMaximized != after.Runtime.StartMaximized
               || !string.Equals(before.Runtime.WindowTitle, after.Runtime.WindowTitle, StringComparison.Ordinal)
               || !string.Equals(before.Runtime.StartupSceneOverride, after.Runtime.StartupSceneOverride, StringComparison.Ordinal);
    }

    private static void DrawStartupSceneSelector(ProjectSettings draft)
    {
        var current = NormalizeAssetPath(draft.Runtime.StartupSceneOverride);
        var sceneAssets = AssetDatabase.Instance.GetAssets(AssetType.Scene)
            .Select(a => a.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var hasCurrent = !string.IsNullOrWhiteSpace(current)
                         && sceneAssets.Any(path => string.Equals(path, current, StringComparison.OrdinalIgnoreCase));

        string preview;
        if (string.IsNullOrWhiteSpace(current))
            preview = "Use .fproject default";
        else if (hasCurrent)
            preview = current;
        else
            preview = $"{current} (missing)";

        if (ImGui.BeginCombo("Startup Scene Override", preview))
        {
            var isDefaultSelected = string.IsNullOrWhiteSpace(current);
            if (ImGui.Selectable("Use .fproject default", isDefaultSelected))
                draft.Runtime.StartupSceneOverride = string.Empty;

            foreach (var scene in sceneAssets)
            {
                var selected = string.Equals(scene, current, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(scene, selected))
                    draft.Runtime.StartupSceneOverride = scene;
            }

            if (!string.IsNullOrWhiteSpace(current) && !hasCurrent)
            {
                ImGui.Separator();
                ImGui.TextDisabled($"Missing: {current}");
            }

            ImGui.EndCombo();
        }
    }

    private static string NormalizeAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = path.Trim().Replace('\\', '/');
        if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["Assets/".Length..];
        return normalized;
    }

    private void DrawKeybindEditorPopup()
    {
        var viewport = ImGui.GetMainViewport();
        var center = new Vector2(viewport.WorkPos.X + viewport.WorkSize.X * 0.5f,
                                 viewport.WorkPos.Y + viewport.WorkSize.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.Appearing);

        if (!ImGui.BeginPopupModal("KeybindEditor"))
        {
            // Popup was dismissed externally — clean up capture state
            if (_isCapturingKeybind)
                StopKeybindCapture();
            return;
        }

        var km = KeybindManager.Instance;

        // Top bar: search + reset all
        ImGui.SetNextItemWidth(-160);
        ImGui.InputTextWithHint("##keybindSearch", "Search actions...", ref _keybindSearchFilter, 256);
        ImGui.SameLine();
        if (ImGui.Button("Reset All to Defaults"))
        {
            km.ResetToDefaults();
            km.SaveConfig();
        }

        ImGui.Separator();

        // Handle key capture
        if (_isCapturingKeybind && _capturingAction.HasValue)
        {
            // Escape cancels capture without closing popup
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                StopKeybindCapture();
            }
            else
            {
                var pressedKey = PollKeyPress();
                if (pressedKey.HasValue)
                {
                    var io = ImGui.GetIO();
                    var newKeybind = new Keybind(pressedKey.Value, io.KeyCtrl, io.KeyShift, io.KeyAlt);
                    km.SetBinding(_capturingAction.Value, newKeybind);
                    StopKeybindCapture();
                }
            }
        }

        // Scrollable table
        if (ImGui.BeginChild("KeybindTable", new Vector2(0, -ImGui.GetFrameHeightWithSpacing())))
        {
            if (ImGui.BeginTable("keybinds", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Binding", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("##rebind", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("##reset", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableHeadersRow();

                var searchLower = _keybindSearchFilter.ToLowerInvariant();

                foreach (var action in Enum.GetValues<EditorAction>())
                {
                    var displayName = KeybindManager.FormatActionName(action);
                    if (!string.IsNullOrEmpty(searchLower) &&
                        !displayName.ToLowerInvariant().Contains(searchLower))
                        continue;

                    ImGui.TableNextRow();

                    var currentBinding = km.GetBinding(action);
                    var defaultBinding = km.GetDefaultBinding(action);
                    var isModified = currentBinding != defaultBinding;
                    var conflicts = km.FindConflicts(action, currentBinding);
                    var hasConflict = conflicts.Count > 0;
                    var isThisCapturing = _isCapturingKeybind && _capturingAction == action;

                    // Action column
                    ImGui.TableNextColumn();
                    if (isModified)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.8f, 1.0f, 1.0f));
                        ImGui.TextUnformatted(displayName);
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        ImGui.TextUnformatted(displayName);
                    }

                    // Binding column
                    ImGui.TableNextColumn();
                    if (isThisCapturing)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 0.0f, 1.0f));
                        ImGui.TextUnformatted("Press a key...");
                        ImGui.PopStyleColor();
                    }
                    else if (hasConflict)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.3f, 0.3f, 1.0f));
                        ImGui.TextUnformatted(currentBinding.ToDisplayString());
                        ImGui.PopStyleColor();
                        if (ImGui.IsItemHovered())
                        {
                            var conflictNames = string.Join(", ",
                                conflicts.Select(KeybindManager.FormatActionName));
                            ImGui.SetTooltip($"Conflicts with: {conflictNames}");
                        }
                    }
                    else
                    {
                        ImGui.TextUnformatted(currentBinding.ToDisplayString());
                    }

                    // Rebind column
                    ImGui.TableNextColumn();
                    ImGui.PushID((int)action);
                    if (isThisCapturing)
                    {
                        if (ImGui.SmallButton("Cancel"))
                            StopKeybindCapture();
                    }
                    else
                    {
                        ImGui.BeginDisabled(_isCapturingKeybind);
                        if (ImGui.SmallButton("Rebind"))
                            StartKeybindCapture(action);
                        ImGui.EndDisabled();
                    }

                    // Reset column
                    ImGui.TableNextColumn();
                    ImGui.BeginDisabled(!isModified);
                    if (ImGui.SmallButton("Reset"))
                        km.ResetBinding(action);
                    ImGui.EndDisabled();
                    ImGui.PopID();
                }

                ImGui.EndTable();
            }
        }
        ImGui.EndChild();

        // Bottom bar
        if (ImGui.Button("Close") || (!_isCapturingKeybind && ImGui.IsKeyPressed(ImGuiKey.Escape)))
        {
            StopKeybindCapture();
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void StartKeybindCapture(EditorAction action)
    {
        _isCapturingKeybind = true;
        _capturingAction = action;
        KeybindManager.Instance.IsCapturingKeybind = true;
    }

    private void StopKeybindCapture()
    {
        _isCapturingKeybind = false;
        _capturingAction = null;
        KeybindManager.Instance.IsCapturingKeybind = false;
    }

    private static ImGuiKey? PollKeyPress()
    {
        // ImGui named keys range from Tab (512) to NamedKey_END.
        // Keys outside this range are legacy indices and cause assertions.
        for (var i = (int)ImGuiKey.Tab; i < (int)ImGuiKey.NamedKeyEnd; i++)
        {
            var key = (ImGuiKey)i;
            // Skip modifier keys
            if (key is ImGuiKey.LeftCtrl or ImGuiKey.RightCtrl
                or ImGuiKey.LeftShift or ImGuiKey.RightShift
                or ImGuiKey.LeftAlt or ImGuiKey.RightAlt
                or ImGuiKey.LeftSuper or ImGuiKey.RightSuper)
                continue;

            // Skip non-keyboard keys
            var name = key.ToString();
            if (name.StartsWith("Gamepad") || name.StartsWith("Mouse")
                || name.StartsWith("Reserved") || name.StartsWith("Mod"))
                continue;

            if (ImGui.IsKeyPressed(key))
                return key;
        }
        return null;
    }

    private static void EditText(string label, ref string value, uint maxLength)
    {
        var local = value;
        if (ImGui.InputText(label, ref local, maxLength))
            value = local;
    }

    private static void EditTextMultiline(string label, ref string value, uint maxLength, Vector2 size)
    {
        var local = value;
        if (ImGui.InputTextMultiline(label, ref local, maxLength, size))
            value = local;
    }

    private static bool HasUnresolvedComponents(Core.Scene.Scene scene)
    {
        foreach (var entity in scene.Entities)
        {
            if (HasUnresolvedComponentsRecursive(entity))
                return true;
        }
        return false;
    }

    private static bool HasUnresolvedComponentsRecursive(Core.ECS.Entity entity)
    {
        if (entity.HasUnresolvedComponents)
            return true;
        foreach (var child in entity.Transform.Children)
        {
            if (HasUnresolvedComponentsRecursive(child.Entity))
                return true;
        }
        return false;
    }

    private void CleanUpUnresolvedComponents()
    {
        if (!_app.CanEditScene || _app.CurrentScene == null) return;

        _app.RecordUndo();
        int removed = 0;
        foreach (var entity in _app.CurrentScene.Entities)
            removed += CleanUpUnresolvedComponentsRecursive(entity);

        _app.RefreshUndoBaseline();

        if (removed > 0)
        {
            NotificationManager.Instance.Post(
                $"Removed {removed} unresolved component(s)",
                NotificationType.Success);
        }
    }

    private static int CleanUpUnresolvedComponentsRecursive(Core.ECS.Entity entity)
    {
        int count = entity.UnresolvedComponents.Count;
        entity.ClearUnresolvedComponents();
        foreach (var child in entity.Transform.Children)
            count += CleanUpUnresolvedComponentsRecursive(child.Entity);
        return count;
    }
}
