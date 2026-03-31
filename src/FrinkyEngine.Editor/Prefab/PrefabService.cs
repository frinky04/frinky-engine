using System.Text.Json;
using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Prefabs;
using FrinkyEngine.Core.Serialization;

namespace FrinkyEngine.Editor.Prefab;

public class PrefabService
{
    private readonly EditorApplication _app;

    public PrefabService(EditorApplication app)
    {
        _app = app;
    }

    public bool IsPrefabInstance(Entity? entity)
    {
        return entity?.Prefab != null && !entity.Prefab.AssetPath.IsEmpty;
    }

    public bool IsPrefabRoot(Entity? entity)
    {
        return entity?.Prefab != null
               && entity.Prefab.IsRoot
               && !entity.Prefab.AssetPath.IsEmpty;
    }

    public Entity? GetPrefabRoot(Entity? entity)
    {
        var current = entity;
        while (current != null)
        {
            if (IsPrefabRoot(current))
                return current;

            current = current.Transform.Parent?.Entity;
        }

        return null;
    }

    public bool CreatePrefabFromEntity(Entity entity, string? relativePath = null, bool relinkEntity = true)
    {
        relativePath = NormalizePath(relativePath);
        if (string.IsNullOrWhiteSpace(relativePath))
            relativePath = GenerateUniquePrefabPath(entity.Name);

        var prefab = PrefabSerializer.CreateFromEntity(entity, preserveStableIds: true);
        prefab.Name = entity.Name;
        if (!PrefabDatabase.Instance.Save(relativePath, prefab))
            return false;

        if (relinkEntity)
        {
            BindMetadataToTree(entity, prefab.Root, relativePath, isRoot: true, new PrefabOverridesData());
            RecalculateOverridesForRoot(entity);
        }

        AssetDatabase.Instance.Refresh();
        return true;
    }

    public Entity? InstantiatePrefab(string relativePath, TransformComponent? parent = null)
    {
        if (_app.CurrentScene == null)
            return null;

        relativePath = NormalizePath(relativePath);
        return InstantiatePrefabInternal(
            relativePath,
            _app.CurrentScene,
            parent,
            new PrefabOverridesData(),
            forcedRootId: null);
    }

    public bool ApplyPrefab(Entity? selectedEntity)
    {
        var root = GetPrefabRoot(selectedEntity);
        if (root?.Prefab == null)
            return false;

        var relativePath = NormalizePath(root.Prefab.AssetPath.Path);
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        if (_app.CurrentScene == null)
            return false;

        var preApplySource = PrefabDatabase.Instance.Load(relativePath, resolveVariants: true);
        if (preApplySource == null)
            return false;

        var preApplyRoots = _app.CurrentScene.Entities
            .Where(e => IsPrefabRoot(e) && string.Equals(NormalizePath(e.Prefab!.AssetPath.Path), relativePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var overridesByRoot = new Dictionary<Guid, PrefabOverridesData>();
        foreach (var instanceRoot in preApplyRoots)
        {
            PrefabOverridesData localOverrides;
            if (instanceRoot.Prefab?.Overrides != null)
            {
                // Preserve the user-visible override snapshot as-is.
                // Recomputing here can produce false positives from serialization noise.
                localOverrides = instanceRoot.Prefab.Overrides.Clone();
            }
            else
            {
                // Fallback for legacy/missing metadata.
                var instanceNode = PrefabSerializer.SerializeNodeNormalized(instanceRoot, preserveStableIds: true);
                localOverrides = PrefabOverrideUtility.ComputeOverrides(preApplySource.Root, instanceNode);
            }

            overridesByRoot[instanceRoot.Id] = localOverrides;
        }

        // The source instance becomes the new truth, so it should not keep its previous local overrides.
        overridesByRoot[root.Id] = new PrefabOverridesData();

        var prefab = PrefabSerializer.CreateFromEntity(root, preserveStableIds: true);
        prefab.Name = root.Name;

        var rawExisting = LoadRawPrefab(relativePath);
        if (rawExisting != null)
        {
            prefab.SourcePrefab = rawExisting.SourcePrefab;
            prefab.VariantOverrides = rawExisting.VariantOverrides.Clone();
        }

        if (!PrefabDatabase.Instance.Save(relativePath, prefab))
            return false;

        PrefabDatabase.Instance.Invalidate(relativePath);

        // Unity-like behavior: every instance picks up source updates.
        // Existing local edits on other instances are preserved from pre-apply diff state.
        SyncInstancesForAsset(relativePath, rootsToRefresh: null, recomputeFromSourceRoot: null, explicitOverridesByRoot: overridesByRoot);
        AssetDatabase.Instance.Refresh();
        return true;
    }

    public bool RevertPrefab(Entity? selectedEntity, bool skipUndo = false)
    {
        var root = GetPrefabRoot(selectedEntity);
        if (root?.Prefab == null)
            return false;

        if (!skipUndo) _app.RecordUndo();
        var reverted = ReplacePrefabRoot(root, new PrefabOverridesData());
        if (reverted == null)
            return false;

        if (!skipUndo)
        {
            _app.SetSingleSelection(reverted);
            _app.RefreshUndoBaseline();
        }
        return true;
    }

    public bool MakeUnique(Entity? selectedEntity, bool skipUndo = false)
    {
        var root = GetPrefabRoot(selectedEntity);
        if (root == null)
            return false;

        var newPath = GenerateUniquePrefabPath(SceneSerializer.GenerateDuplicateName(root.Name));
        var prefab = PrefabSerializer.CreateFromEntity(root, preserveStableIds: true);
        prefab.Name = root.Name;

        if (!PrefabDatabase.Instance.Save(newPath, prefab))
            return false;

        if (!skipUndo) _app.RecordUndo();
        BindMetadataToTree(root, prefab.Root, newPath, isRoot: true, new PrefabOverridesData());
        RecalculateOverridesForRoot(root);
        if (!skipUndo) _app.RefreshUndoBaseline();

        AssetDatabase.Instance.Refresh();
        NotificationManager.Instance.Post($"Prefab made unique: {newPath}", NotificationType.Success);
        return true;
    }

    public bool UnpackPrefab(Entity? selectedEntity, bool skipUndo = false)
    {
        var root = GetPrefabRoot(selectedEntity);
        if (root == null)
            return false;

        if (!skipUndo) _app.RecordUndo();
        foreach (var entity in EnumerateEntityTree(root))
            entity.Prefab = null;
        if (!skipUndo) _app.RefreshUndoBaseline();
        return true;
    }

    /// <summary>
    /// Unpacks all prefab instances whose asset path matches any of the given deleted paths.
    /// Caller is responsible for undo recording.
    /// </summary>
    public int UnpackByAssetPaths(IReadOnlySet<string> deletedPrefabPaths)
    {
        if (_app.CurrentScene == null)
            return 0;

        var roots = _app.CurrentScene.Entities
            .Where(e => IsPrefabRoot(e) && deletedPrefabPaths.Contains(NormalizePath(e.Prefab!.AssetPath.Path)))
            .ToList();

        foreach (var root in roots)
        {
            foreach (var entity in EnumerateEntityTree(root))
                entity.Prefab = null;
        }

        return roots.Count;
    }

    public void RecalculateOverridesForScene()
    {
        if (_app.CurrentScene == null)
            return;

        foreach (var root in _app.CurrentScene.Entities.Where(IsPrefabRoot).ToList())
            RecalculateOverridesForRoot(root);
    }

    public int RefreshPrefabInstancesInScene()
    {
        if (_app.CurrentScene == null)
            return 0;

        var roots = _app.CurrentScene.Entities.Where(IsPrefabRoot).ToList();
        if (roots.Count == 0)
            return 0;

        var selectedMappings = BuildSelectedMappings(roots);
        var replacedRoots = new List<Entity>(roots.Count);

        foreach (var root in roots)
        {
            var replacementOverrides = root.Prefab?.Overrides?.Clone() ?? new PrefabOverridesData();
            var replacement = ReplacePrefabRoot(root, replacementOverrides);
            if (replacement != null)
                replacedRoots.Add(replacement);
        }

        RestoreSelectionAfterSync(selectedMappings, replacedRoots);

        foreach (var root in replacedRoots)
            RecalculateOverridesForRoot(root);

        return replacedRoots.Count;
    }

    public void RecalculateOverridesForRoot(Entity root)
    {
        if (!IsPrefabRoot(root) || root.Prefab == null)
            return;

        var sourcePrefab = PrefabDatabase.Instance.Load(root.Prefab.AssetPath.Path, resolveVariants: true);
        if (sourcePrefab == null)
            return;

        var instanceNode = PrefabSerializer.SerializeNodeNormalized(root, preserveStableIds: true);
        var overrides = PrefabOverrideUtility.ComputeOverrides(sourcePrefab.Root, instanceNode);
        root.Prefab.Overrides = overrides;
    }

    public void SyncInstancesForAssets(IEnumerable<string> changedAssetPaths)
    {
        var paths = changedAssetPaths
            .Select(NormalizePath)
            .Where(p => p.EndsWith(".fprefab", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var path in paths)
            SyncInstancesForAsset(path);
    }

    public void SyncInstancesForAsset(string assetPath)
    {
        SyncInstancesForAsset(assetPath, rootsToRefresh: null, recomputeFromSourceRoot: null, explicitOverridesByRoot: null);
    }

    public void SyncInstancesForAsset(
        string assetPath,
        HashSet<Guid>? rootsToRefresh,
        PrefabNodeData? recomputeFromSourceRoot = null,
        Dictionary<Guid, PrefabOverridesData>? explicitOverridesByRoot = null)
    {
        if (_app.CurrentScene == null)
            return;

        assetPath = NormalizePath(assetPath);
        PrefabDatabase.Instance.Invalidate(assetPath);

        var roots = _app.CurrentScene.Entities
            .Where(e => IsPrefabRoot(e) && string.Equals(NormalizePath(e.Prefab!.AssetPath.Path), assetPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (roots.Count == 0)
            return;

        if (rootsToRefresh != null)
            roots = roots.Where(r => rootsToRefresh.Contains(r.Id)).ToList();
        if (roots.Count == 0)
            return;

        var selectedMappings = BuildSelectedMappings(roots);
        var replacedRoots = new List<Entity>();

        foreach (var root in roots)
        {
            PrefabOverridesData? replacementOverrides;
            if (explicitOverridesByRoot != null && explicitOverridesByRoot.TryGetValue(root.Id, out var explicitOverrides))
            {
                replacementOverrides = explicitOverrides;
            }
            else if (recomputeFromSourceRoot != null)
            {
                var instanceNode = PrefabSerializer.SerializeNodeNormalized(root, preserveStableIds: true);
                replacementOverrides = PrefabOverrideUtility.ComputeOverrides(recomputeFromSourceRoot, instanceNode);
            }
            else
            {
                // Targeted refresh paths can intentionally clear local overrides.
                // Generic sync paths preserve existing overrides.
                replacementOverrides = rootsToRefresh == null ? root.Prefab?.Overrides : new PrefabOverridesData();
            }

            var replacement = ReplacePrefabRoot(root, replacementOverrides);
            if (replacement != null)
                replacedRoots.Add(replacement);
        }

        RestoreSelectionAfterSync(selectedMappings, replacedRoots);
        _app.RefreshUndoBaseline();
    }

    public string GenerateUniquePrefabPath(string baseName)
    {
        var sanitized = SanitizeFileName(baseName);
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "Prefab";

        var relativeDir = "Prefabs";
        var absoluteDir = Path.Combine(AssetManager.Instance.AssetsPath, relativeDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(absoluteDir);

        string relativePath = $"{relativeDir}/{sanitized}.fprefab";
        string absolutePath = Path.Combine(AssetManager.Instance.AssetsPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        int suffix = 1;
        while (File.Exists(absolutePath))
        {
            relativePath = $"{relativeDir}/{sanitized}_{suffix}.fprefab";
            absolutePath = Path.Combine(AssetManager.Instance.AssetsPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            suffix++;
        }

        return relativePath;
    }

    private Entity? InstantiatePrefabInternal(
        string assetPath,
        Core.Scene.Scene scene,
        TransformComponent? parent,
        PrefabOverridesData? overrides,
        Guid? forcedRootId)
    {
        var result = PrefabInstantiator.InstantiatePrefabInternal(assetPath, scene, parent, overrides, forcedRootId);
        if (result == null)
            NotificationManager.Instance.Post($"Prefab not found: {assetPath}", NotificationType.Warning);
        return result;
    }

    private Entity? ReplacePrefabRoot(Entity root, PrefabOverridesData? overrides)
    {
        if (_app.CurrentScene == null || root.Prefab == null)
            return null;

        var scene = _app.CurrentScene;
        var parent = root.Transform.Parent;
        var rootFolder = parent == null ? _app.GetRootEntityFolder(root) : null;
        var rootId = root.Id;
        var assetPath = NormalizePath(root.Prefab.AssetPath.Path);
        var preservedRootName = root.Name;
        var preservedRootPosition = root.Transform.LocalPosition;
        var preservedRootRotation = root.Transform.LocalRotation;
        var preservedRootScale = root.Transform.LocalScale;
        var prefab = PrefabDatabase.Instance.Load(assetPath, resolveVariants: true);
        if (prefab == null)
            return null;

        RemoveEntityTree(root);
        var replacement = InstantiatePrefabInternal(assetPath, scene, parent, overrides?.Clone(), rootId);
        if (replacement == null)
            return null;

        replacement.Name = preservedRootName;
        replacement.Transform.LocalPosition = preservedRootPosition;
        replacement.Transform.LocalRotation = preservedRootRotation;
        replacement.Transform.LocalScale = preservedRootScale;

        if (parent == null && !string.IsNullOrWhiteSpace(rootFolder))
            _app.SetRootEntityFolder(replacement, rootFolder);

        return replacement;
    }

    private void RemoveEntityTree(Entity root)
    {
        if (_app.CurrentScene == null)
            return;

        var scene = _app.CurrentScene;
        var entities = EnumerateEntityTree(root).ToList();
        for (int i = entities.Count - 1; i >= 0; i--)
        {
            if (entities[i].Scene == scene)
                scene.RemoveEntity(entities[i]);
        }
    }

    private static IEnumerable<Entity> EnumerateEntityTree(Entity root)
    {
        yield return root;
        foreach (var child in root.Transform.Children)
        {
            foreach (var descendant in EnumerateEntityTree(child.Entity))
                yield return descendant;
        }
    }

    private void BindMetadataToTree(Entity entity, PrefabNodeData node, string assetPath, bool isRoot, PrefabOverridesData? rootOverrides)
    {
        entity.Prefab = new PrefabInstanceMetadata
        {
            IsRoot = isRoot,
            AssetPath = assetPath,
            SourceNodeId = node.StableId,
            Overrides = isRoot ? rootOverrides?.Clone() : null
        };

        int pairCount = Math.Min(entity.Transform.Children.Count, node.Children.Count);
        for (int i = 0; i < pairCount; i++)
            BindMetadataToTree(entity.Transform.Children[i].Entity, node.Children[i], assetPath, false, null);

        for (int i = pairCount; i < entity.Transform.Children.Count; i++)
        {
            var child = entity.Transform.Children[i].Entity;
            AssignGeneratedMetadata(child, assetPath);
        }
    }

    private static void AssignGeneratedMetadata(Entity entity, string assetPath)
    {
        entity.Prefab = new PrefabInstanceMetadata
        {
            IsRoot = false,
            AssetPath = assetPath,
            SourceNodeId = Guid.NewGuid().ToString("N")
        };

        foreach (var child in entity.Transform.Children)
            AssignGeneratedMetadata(child.Entity, assetPath);
    }

    private PrefabAssetData? LoadRawPrefab(string relativePath)
    {
        var absolute = Path.Combine(AssetManager.Instance.AssetsPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return PrefabSerializer.Load(absolute);
    }

    private List<SelectionMapping> BuildSelectedMappings(IReadOnlyList<Entity> replacedRoots)
    {
        var rootSet = new HashSet<Guid>(replacedRoots.Select(r => r.Id));
        var mappings = new List<SelectionMapping>();
        foreach (var selected in _app.SelectedEntities)
        {
            var root = GetPrefabRoot(selected);
            if (root == null || !rootSet.Contains(root.Id))
                continue;

            mappings.Add(new SelectionMapping
            {
                RootId = root.Id,
                SourceNodeId = selected.Prefab?.SourceNodeId
            });
        }

        return mappings;
    }

    private void RestoreSelectionAfterSync(List<SelectionMapping> mappings, IReadOnlyList<Entity> replacedRoots)
    {
        if (mappings.Count == 0)
        {
            _app.CleanupHierarchyStateForCurrentScene();
            return;
        }

        var rootsById = replacedRoots.ToDictionary(r => r.Id);
        var newSelection = new List<Entity>();

        foreach (var mapping in mappings)
        {
            if (!rootsById.TryGetValue(mapping.RootId, out var root))
                continue;

            if (string.IsNullOrWhiteSpace(mapping.SourceNodeId))
            {
                newSelection.Add(root);
                continue;
            }

            var mapped = FindBySourceNodeId(root, mapping.SourceNodeId);
            if (mapped != null)
                newSelection.Add(mapped);
        }

        _app.SetSelection(newSelection);
        _app.CleanupHierarchyStateForCurrentScene();
    }

    private static Entity? FindBySourceNodeId(Entity root, string sourceNodeId)
    {
        foreach (var entity in EnumerateEntityTree(root))
        {
            if (string.Equals(entity.Prefab?.SourceNodeId, sourceNodeId, StringComparison.OrdinalIgnoreCase))
                return entity;
        }

        return null;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        return path.Trim().Replace('\\', '/');
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var filtered = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());
        filtered = filtered.Trim();
        return string.IsNullOrWhiteSpace(filtered) ? "Prefab" : filtered;
    }

    private sealed class SelectionMapping
    {
        public required Guid RootId { get; init; }
        public string? SourceNodeId { get; init; }
    }
}
