using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Rendering;
using FrinkyEngine.Core.Rendering.PostProcessing;
using FrinkyEngine.Core.Serialization;

namespace FrinkyEngine.Editor;

public record UndoSnapshot(string SceneJson, List<Guid> SelectedEntityIds, string? HierarchyJson = null);

public class UndoRedoManager
{
    private const int MaxHistory = 50;

    private readonly List<UndoSnapshot> _undoStack = new();
    private readonly List<UndoSnapshot> _redoStack = new();

    private string? _currentSnapshot;
    private List<Guid> _currentSelectedEntityIds = new();
    private string? _currentHierarchySnapshot;

    // Batch state for continuous edits (gizmo drags, slider drags)
    private bool _isBatching;
    private string? _batchStartSnapshot;
    private List<Guid>? _batchStartSelectedEntityIds;
    private string? _batchStartHierarchySnapshot;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void SetBaseline(Core.Scene.Scene? scene, IReadOnlyList<Guid> selectedEntityIds, string? hierarchyStateJson = null)
    {
        if (scene == null) return;
        _currentSnapshot = SceneSerializer.SerializeToString(scene);
        _currentSelectedEntityIds = selectedEntityIds.ToList();
        _currentHierarchySnapshot = hierarchyStateJson ?? _currentHierarchySnapshot;
    }

    public void RecordUndo(IReadOnlyList<Guid>? currentSelectedEntityIds = null, string? hierarchyStateJson = null)
    {
        if (_currentSnapshot == null) return;

        var selectedIds = currentSelectedEntityIds?.ToList() ?? _currentSelectedEntityIds.ToList();
        var hierarchySnapshot = hierarchyStateJson ?? _currentHierarchySnapshot;
        _undoStack.Add(new UndoSnapshot(_currentSnapshot, selectedIds, hierarchySnapshot));
        if (_undoStack.Count > MaxHistory)
            _undoStack.RemoveAt(0);

        _redoStack.Clear();
    }

    public void RefreshBaseline(Core.Scene.Scene? scene, IReadOnlyList<Guid> selectedEntityIds, string? hierarchyStateJson = null)
    {
        if (scene == null) return;
        _currentSnapshot = SceneSerializer.SerializeToString(scene);
        _currentSelectedEntityIds = selectedEntityIds.ToList();
        _currentHierarchySnapshot = hierarchyStateJson ?? _currentHierarchySnapshot;
    }

    public void BeginBatch(IReadOnlyList<Guid>? currentSelectedEntityIds = null, string? hierarchyStateJson = null)
    {
        if (_isBatching) return;
        _isBatching = true;
        _batchStartSnapshot = _currentSnapshot;
        _batchStartSelectedEntityIds = currentSelectedEntityIds?.ToList() ?? _currentSelectedEntityIds.ToList();
        _batchStartHierarchySnapshot = hierarchyStateJson ?? _currentHierarchySnapshot;
    }

    public void EndBatch(Core.Scene.Scene? scene, IReadOnlyList<Guid> selectedEntityIds, string? hierarchyStateJson = null)
    {
        if (!_isBatching) return;
        _isBatching = false;

        if (_batchStartSnapshot == null) return;

        // Push the pre-batch state as the undo point
        _undoStack.Add(new UndoSnapshot(
            _batchStartSnapshot,
            _batchStartSelectedEntityIds?.ToList() ?? new List<Guid>(),
            _batchStartHierarchySnapshot));
        if (_undoStack.Count > MaxHistory)
            _undoStack.RemoveAt(0);

        _redoStack.Clear();
        _batchStartSnapshot = null;
        _batchStartSelectedEntityIds = null;
        _batchStartHierarchySnapshot = null;

        // Refresh baseline to current state
        RefreshBaseline(scene, selectedEntityIds, hierarchyStateJson);
    }

    public void Undo(EditorApplication app)
    {
        if (!CanUndo || app.CurrentScene == null) return;

        // Push current state onto redo stack
        if (_currentSnapshot != null)
            _redoStack.Add(new UndoSnapshot(
                _currentSnapshot,
                app.GetSelectedEntityIds(),
                app.SerializeCurrentHierarchyState()));

        var snapshot = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        RestoreSnapshot(app, snapshot);
        NotificationManager.Instance.Post("Undo", NotificationType.Info, 1.5f);
    }

    public void Redo(EditorApplication app)
    {
        if (!CanRedo || app.CurrentScene == null) return;

        // Push current state onto undo stack
        if (_currentSnapshot != null)
            _undoStack.Add(new UndoSnapshot(
                _currentSnapshot,
                app.GetSelectedEntityIds(),
                app.SerializeCurrentHierarchyState()));

        var snapshot = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);

        RestoreSnapshot(app, snapshot);
        NotificationManager.Instance.Post("Redo", NotificationType.Info, 1.5f);
    }

    private void RestoreSnapshot(EditorApplication app, UndoSnapshot snapshot)
    {
        var restored = SceneSerializer.DeserializeFromString(snapshot.SceneJson);
        if (restored == null) return;

        if (app.CurrentScene != null)
        {
            restored.Name = app.CurrentScene.Name;
            restored.FilePath = app.CurrentScene.FilePath;

            // Transfer loaded assets from old scene to restored scene to avoid
            // expensive disk reloads and shader recompilations on undo/redo.
            TransferLoadedAssets(app.CurrentScene, restored);

            // Invalidate all old renderables so that any resources affected by the
            // asset transfer are properly refreshed.
            foreach (var renderable in app.CurrentScene.Renderables)
                renderable.Invalidate();
        }

        app.CurrentScene = restored;
        Core.Scene.SceneManager.Instance.SetActiveScene(restored);
        app.RestoreHierarchyStateFromSerialized(snapshot.HierarchyJson);

        var selectedEntities = new List<Core.ECS.Entity>();
        foreach (var selectedId in snapshot.SelectedEntityIds)
        {
            var selectedEntity = FindEntityById(restored, selectedId);
            if (selectedEntity != null)
                selectedEntities.Add(selectedEntity);
        }
        app.SetSelection(selectedEntities);

        // Update baseline
        _currentSnapshot = snapshot.SceneJson;
        _currentSelectedEntityIds = snapshot.SelectedEntityIds.ToList();
        _currentHierarchySnapshot = snapshot.HierarchyJson;
    }

    /// <summary>
    /// Transfers initialized post-process effects from the old scene to the restored scene,
    /// matching entities by ID and components by type. Renderer-owned mesh resources are
    /// rebuilt by the scene renderer instead of being copied through components.
    /// </summary>
    private static void TransferLoadedAssets(Core.Scene.Scene oldScene, Core.Scene.Scene restoredScene)
    {
        // Build a lookup of old entities by ID for fast matching
        var oldEntities = IterateAllEntities(oldScene)
            .GroupBy(e => e.Id)
            .ToDictionary(g => g.Key, g => g.First());

        // Transfer MeshRendererComponent model instances
        foreach (var entity in IterateAllEntities(restoredScene))
        {
            if (!oldEntities.TryGetValue(entity.Id, out var oldEntity))
                continue;

            // Transfer initialized post-process effects to avoid shader recompilation
            var newStack = entity.GetComponent<PostProcessStackComponent>();
            var oldStack = oldEntity.GetComponent<PostProcessStackComponent>();
            if (newStack != null && oldStack != null)
            {
                TransferPostProcessEffects(oldStack, newStack);
            }
        }
    }

    private static void TransferPostProcessEffects(PostProcessStackComponent oldStack, PostProcessStackComponent newStack)
    {
        // Match effects by type and position in the list; transfer initialized state
        for (int i = 0; i < newStack.Effects.Count && i < oldStack.Effects.Count; i++)
        {
            var newEffect = newStack.Effects[i];
            var oldEffect = oldStack.Effects[i];
            if (newEffect.GetType() == oldEffect.GetType() && oldEffect.IsInitialized)
            {
                // Replace the new (uninitialized) effect with the old (initialized) one,
                // copying over the deserialized property values.
                CopyEffectProperties(newEffect, oldEffect);
                newStack.Effects[i] = oldEffect;

                // Remove from old stack so OnDestroy won't dispose the transferred effect
                oldStack.Effects[i] = newEffect;
            }
        }
    }

    private static void CopyEffectProperties(PostProcessEffect source, PostProcessEffect target)
    {
        // Copy serialized property values from the freshly deserialized effect to the
        // initialized one so that undo/redo applies property changes while preserving
        // GPU resources (loaded shaders).
        var type = source.GetType();
        foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.Name is "DisplayName" or "IsInitialized") continue;

            try
            {
                prop.SetValue(target, prop.GetValue(source));
            }
            catch (Exception ex)
            {
                FrinkyLog.Warning($"CopyEffectProperties: failed to copy {prop.Name} on {type.Name}: {ex.Message}");
            }
        }
    }

    private static IEnumerable<Entity> IterateAllEntities(Core.Scene.Scene scene)
    {
        foreach (var entity in scene.Entities)
        {
            yield return entity;
            foreach (var descendant in IterateDescendants(entity))
                yield return descendant;
        }
    }

    private static IEnumerable<Entity> IterateDescendants(Entity entity)
    {
        foreach (var child in entity.Transform.Children)
        {
            yield return child.Entity;
            foreach (var descendant in IterateDescendants(child.Entity))
                yield return descendant;
        }
    }

    private static Core.ECS.Entity? FindEntityById(Core.Scene.Scene scene, Guid id)
    {
        return IterateAllEntities(scene).FirstOrDefault(e => e.Id == id);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _currentSnapshot = null;
        _currentSelectedEntityIds.Clear();
        _currentHierarchySnapshot = null;
        _isBatching = false;
        _batchStartSnapshot = null;
        _batchStartSelectedEntityIds = null;
        _batchStartHierarchySnapshot = null;
    }
}
