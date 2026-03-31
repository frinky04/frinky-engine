using System.Numerics;
using FrinkyEngine.Core.Animation.IK;
using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.Components;
using ComponentMaterial = FrinkyEngine.Core.Components.Material;
using Raylib_cs;

namespace FrinkyEngine.Core.Rendering;

internal sealed class RenderResourceCache : IRenderGeometryQueries, IDisposable
{
    private const float HitDistanceEpsilon = 1e-5f;
    private const float FrontFaceDotThreshold = -1e-4f;

    private int _nextMeshHandle = 1;
    private int _nextMaterialHandle = 1;
    private int _nextSkinHandle = 1;
    private int _lastAssetGeneration = -1;

    private readonly Dictionary<string, MeshResource> _meshResourcesByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, MeshResource> _meshResourcesById = new();
    private readonly Dictionary<string, MaterialResource> _materialResourcesByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<int, MaterialResource> _materialResourcesById = new();
    private readonly Dictionary<MeshRendererComponent, SkinnedInstanceResource> _skinnedInstances = new();
    private readonly Dictionary<int, SkinnedInstanceResource> _skinnedInstancesById = new();
    private readonly Dictionary<RenderableComponent, string> _lastRenderableMeshKeys = new();
    private readonly Dictionary<RenderableComponent, CachedWorldBounds> _worldBoundsCache = new();

    public RenderResourceCache()
    {
        RenderGeometryQueries.Install(this);
    }

    public void Dispose()
    {
        Clear();
        RenderGeometryQueries.Uninstall(this);
    }

    internal RenderMeshHandle ResolveMeshHandle(RenderableComponent renderable)
    {
        RefreshInvalidatedAssetsIfNeeded();

        string key = GetMeshResourceKey(renderable);
        if (string.IsNullOrEmpty(key))
            return default;

        _lastRenderableMeshKeys[renderable] = key;
        if (_meshResourcesByKey.TryGetValue(key, out var existing))
            return existing.Handle;

        MeshResource resource;
        switch (renderable)
        {
            case MeshRendererComponent meshRenderer:
                resource = CreateMeshRendererResource(meshRenderer, key);
                break;
            case PrimitiveComponent primitive:
                resource = CreatePrimitiveResource(primitive, key);
                break;
            default:
                return default;
        }

        _meshResourcesByKey[key] = resource;
        _meshResourcesById[resource.Handle.Id] = resource;
        return resource.Handle;
    }

    internal RenderMaterialHandle ResolveMaterialHandle(RenderableComponent renderable)
    {
        var meshHandle = ResolveMeshHandle(renderable);
        if (!meshHandle.IsValid || !_meshResourcesById.TryGetValue(meshHandle.Id, out var meshResource))
            return default;

        string key = GetMaterialResourceKey(renderable, meshResource.MaterialCount);
        if (string.IsNullOrEmpty(key))
            return default;

        if (_materialResourcesByKey.TryGetValue(key, out var existing))
            return existing.Handle;

        var resource = CreateMaterialResource(renderable, meshResource.MaterialCount, key);
        _materialResourcesByKey[key] = resource;
        _materialResourcesById[resource.Handle.Id] = resource;
        return resource.Handle;
    }

    internal MeshResource? GetMeshResource(RenderMeshHandle handle)
    {
        return handle.IsValid && _meshResourcesById.TryGetValue(handle.Id, out var resource)
            ? resource
            : null;
    }

    internal MaterialResource? GetMaterialResource(RenderMaterialHandle handle)
    {
        return handle.IsValid && _materialResourcesById.TryGetValue(handle.Id, out var resource)
            ? resource
            : null;
    }

    internal SkinnedInstanceResource? GetSkinnedInstance(SkinPaletteHandle handle)
    {
        return handle.IsValid && _skinnedInstancesById.TryGetValue(handle.Id, out var resource)
            ? resource
            : null;
    }

    public void Invalidate(RenderableComponent renderable)
    {
        _worldBoundsCache.Remove(renderable);

        if (_lastRenderableMeshKeys.Remove(renderable, out var lastKey)
            && renderable is PrimitiveComponent
            && _meshResourcesByKey.TryGetValue(lastKey, out var primitiveResource))
        {
            RemoveMeshResource(lastKey, primitiveResource);
        }

        if (renderable is MeshRendererComponent meshRenderer
            && _skinnedInstances.TryGetValue(meshRenderer, out var skinned))
        {
            RemoveSkinnedInstance(meshRenderer, skinned);
        }
    }

    public void InvalidateAssets(IEnumerable<string> relativePaths)
    {
        var normalized = new HashSet<string>(
            relativePaths.Select(static x => x.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);

        if (normalized.Count == 0)
            return;

        var meshKeysToRemove = _meshResourcesByKey
            .Where(static pair => pair.Value.AssetKey.Length > 0)
            .Where(pair => normalized.Contains(pair.Value.AssetKey))
            .Select(static pair => pair.Key)
            .ToList();

        foreach (var key in meshKeysToRemove)
        {
            if (_meshResourcesByKey.TryGetValue(key, out var resource))
                RemoveMeshResource(key, resource);
        }

        var skinnedToRemove = _skinnedInstances
            .Where(pair => normalized.Contains(pair.Value.AssetKey))
            .Select(static pair => pair.Key)
            .ToList();

        foreach (var meshRenderer in skinnedToRemove)
        {
            if (_skinnedInstances.TryGetValue(meshRenderer, out var resource))
                RemoveSkinnedInstance(meshRenderer, resource);
        }
    }

    public void Clear()
    {
        foreach (var resource in _meshResourcesByKey.Values)
        {
            if (resource.OwnsModel)
                Raylib.UnloadModel(resource.Model);
        }

        foreach (var resource in _skinnedInstances.Values)
        {
            if (resource.OwnsModel)
                Raylib.UnloadModel(resource.Model);
        }

        _meshResourcesByKey.Clear();
        _meshResourcesById.Clear();
        _materialResourcesByKey.Clear();
        _materialResourcesById.Clear();
        _skinnedInstances.Clear();
        _skinnedInstancesById.Clear();
        _lastRenderableMeshKeys.Clear();
        _worldBoundsCache.Clear();
        _lastAssetGeneration = AssetManager.Instance.AssetGeneration;
    }

    public BoundingBox? GetWorldBoundingBox(RenderableComponent renderable)
    {
        var meshHandle = ResolveMeshHandle(renderable);
        if (!meshHandle.IsValid || !_meshResourcesById.TryGetValue(meshHandle.Id, out var resource))
            return null;

        int renderVersion = renderable.RenderVersion;
        int transformVersion = renderable.Entity.Transform.TransformVersion;

        if (_worldBoundsCache.TryGetValue(renderable, out var cached)
            && cached.RenderVersion == renderVersion
            && cached.TransformVersion == transformVersion)
        {
            return cached.Bounds;
        }

        var bounds = TransformBounds(resource.LocalBounds, renderable.Entity.Transform.WorldMatrix);
        _worldBoundsCache[renderable] = new CachedWorldBounds(renderVersion, transformVersion, bounds);
        return bounds;
    }

    public RayCollision? GetWorldRayCollision(RenderableComponent renderable, Ray ray, out bool hasMeshData, bool frontFacesOnly)
    {
        var meshHandle = ResolveMeshHandle(renderable);
        if (!meshHandle.IsValid || !_meshResourcesById.TryGetValue(meshHandle.Id, out var resource))
        {
            hasMeshData = false;
            return null;
        }

        var model = resource.Model;
        if (model.MeshCount <= 0)
        {
            hasMeshData = false;
            return null;
        }

        hasMeshData = true;
        var worldTransform = Matrix4x4.Transpose(renderable.Entity.Transform.WorldMatrix);

        RayCollision? closestCollision = null;
        float closestDistance = float.MaxValue;

        unsafe
        {
            for (int m = 0; m < model.MeshCount; m++)
            {
                var collision = Raylib.GetRayCollisionMesh(ray, model.Meshes[m], worldTransform);
                if (!collision.Hit) continue;
                if (collision.Distance <= HitDistanceEpsilon) continue;

                if (frontFacesOnly && collision.Normal.LengthSquared() > 1e-8f)
                {
                    float facing = Vector3.Dot(collision.Normal, ray.Direction);
                    if (facing >= FrontFaceDotThreshold)
                        continue;
                }

                if (collision.Distance < closestDistance)
                {
                    closestDistance = collision.Distance;
                    closestCollision = collision;
                }
            }
        }

        return closestCollision;
    }

    public bool TryGetSharedModel(MeshRendererComponent meshRenderer, out Model model)
    {
        var handle = ResolveMeshHandle(meshRenderer);
        if (handle.IsValid && _meshResourcesById.TryGetValue(handle.Id, out var resource))
        {
            model = resource.Model;
            return true;
        }

        model = default;
        return false;
    }

    public bool TryGetAnimationModel(MeshRendererComponent meshRenderer, out Model model, out SkinPaletteHandle skinPaletteHandle)
    {
        RefreshInvalidatedAssetsIfNeeded();

        var sharedHandle = ResolveMeshHandle(meshRenderer);
        if (!sharedHandle.IsValid || !_meshResourcesById.TryGetValue(sharedHandle.Id, out var resource))
        {
            model = default;
            skinPaletteHandle = default;
            return false;
        }

        string assetKey = resource.AssetKey;
        if (string.IsNullOrEmpty(assetKey))
        {
            model = resource.Model;
            skinPaletteHandle = default;
            return true;
        }

        int modelVersion = meshRenderer.ModelVersion;
        if (_skinnedInstances.TryGetValue(meshRenderer, out var existing)
            && existing.AssetKey == assetKey
            && existing.ModelVersion == modelVersion
            && existing.AssetGeneration == AssetManager.Instance.AssetGeneration)
        {
            model = existing.Model;
            skinPaletteHandle = existing.Handle;
            return true;
        }

        if (existing != null)
            RemoveSkinnedInstance(meshRenderer, existing);

        var uniqueModel = AssetManager.Instance.LoadModelUnique(assetKey);
        bool ownsModel = File.Exists(AssetManager.Instance.ResolvePath(assetKey));
        if (!ownsModel)
            uniqueModel = resource.Model;

        var skinned = new SkinnedInstanceResource(
            new SkinPaletteHandle(_nextSkinHandle++),
            assetKey,
            modelVersion,
            AssetManager.Instance.AssetGeneration,
            uniqueModel,
            ownsModel);

        _skinnedInstances[meshRenderer] = skinned;
        _skinnedInstancesById[skinned.Handle.Id] = skinned;
        model = skinned.Model;
        skinPaletteHandle = skinned.Handle;
        return true;
    }

    public BoneHierarchy? GetBoneHierarchy(MeshRendererComponent meshRenderer)
    {
        var handle = ResolveMeshHandle(meshRenderer);
        return handle.IsValid && _meshResourcesById.TryGetValue(handle.Id, out var resource)
            ? resource.BoneHierarchy
            : null;
    }

    private MeshResource CreateMeshRendererResource(MeshRendererComponent meshRenderer, string key)
    {
        var model = AssetManager.Instance.LoadModel(key);
        bool ownsModel = false;
        if (model.MeshCount <= 0 && AssetManager.Instance.ErrorModel is Model errorModel)
            model = errorModel;

        return CreateMeshResource(new RenderMeshHandle(_nextMeshHandle++), key, model, ownsModel);
    }

    private MeshResource CreatePrimitiveResource(PrimitiveComponent primitive, string key)
    {
        var mesh = primitive.CreateMesh();
        var model = Raylib.LoadModelFromMesh(mesh);
        return CreateMeshResource(new RenderMeshHandle(_nextMeshHandle++), string.Empty, model, ownsModel: true);
    }

    private static MeshResource CreateMeshResource(RenderMeshHandle handle, string assetKey, Model model, bool ownsModel)
    {
        var localBounds = ComputeLocalBounds(model);
        var boneHierarchy = model.BoneCount > 0 ? new BoneHierarchy(model) : null;
        var meshMaterial = CopyMeshMaterialIndices(model);

        return new MeshResource(
            handle,
            assetKey,
            model,
            ownsModel,
            localBounds,
            model.MaterialCount,
            meshMaterial,
            boneHierarchy);
    }

    private MaterialResource CreateMaterialResource(RenderableComponent renderable, int materialCount, string key)
    {
        ComponentMaterial[] slots;

        if (renderable is MeshRendererComponent meshRenderer)
        {
            slots = new ComponentMaterial[Math.Max(1, materialCount)];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = i < meshRenderer.MaterialSlots.Count && meshRenderer.MaterialSlots[i] != null
                    ? meshRenderer.MaterialSlots[i].Clone()
                    : new ComponentMaterial();
            }
        }
        else if (renderable is PrimitiveComponent primitive)
        {
            slots = [primitive.Material?.Clone() ?? new ComponentMaterial()];
        }
        else
        {
            slots = [new ComponentMaterial()];
        }

        return new MaterialResource(new RenderMaterialHandle(_nextMaterialHandle++), key, slots);
    }

    private static string GetMeshResourceKey(RenderableComponent renderable)
    {
        if (renderable is MeshRendererComponent meshRenderer)
        {
            if (meshRenderer.ModelPath.IsEmpty)
                return string.Empty;

            var resolved = AssetDatabase.Instance.ResolveAssetPath(meshRenderer.ModelPath.Path) ?? meshRenderer.ModelPath.Path;
            return resolved.Replace('\\', '/');
        }

        if (renderable is PrimitiveComponent primitive)
        {
            return primitive.GetPrimitiveResourceKey();
        }

        return string.Empty;
    }

    private static string GetMaterialResourceKey(RenderableComponent renderable, int materialCount)
    {
        var hash = new HashCode();
        hash.Add(renderable.GetType());

        if (renderable is MeshRendererComponent meshRenderer)
        {
            hash.Add(materialCount);
            for (int i = 0; i < materialCount; i++)
            {
                int slotHash = i < meshRenderer.MaterialSlots.Count && meshRenderer.MaterialSlots[i] != null
                    ? meshRenderer.MaterialSlots[i].GetConfigurationHash()
                    : 0;
                hash.Add(slotHash);
            }
        }
        else if (renderable is PrimitiveComponent primitive)
        {
            hash.Add(primitive.Material?.GetConfigurationHash() ?? 0);
        }

        return hash.ToHashCode().ToString("X8");
    }

    private void RefreshInvalidatedAssetsIfNeeded()
    {
        int assetGeneration = AssetManager.Instance.AssetGeneration;
        if (assetGeneration == _lastAssetGeneration)
            return;

        _lastAssetGeneration = assetGeneration;
    }

    private void RemoveMeshResource(string key, MeshResource resource)
    {
        _meshResourcesByKey.Remove(key);
        _meshResourcesById.Remove(resource.Handle.Id);
        if (resource.OwnsModel)
            Raylib.UnloadModel(resource.Model);
    }

    private void RemoveSkinnedInstance(MeshRendererComponent meshRenderer, SkinnedInstanceResource resource)
    {
        _skinnedInstances.Remove(meshRenderer);
        _skinnedInstancesById.Remove(resource.Handle.Id);
        if (resource.OwnsModel)
            Raylib.UnloadModel(resource.Model);
    }

    private static BoundingBox ComputeLocalBounds(Model model)
    {
        if (model.MeshCount <= 0)
            return new BoundingBox(Vector3.Zero, Vector3.Zero);

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        unsafe
        {
            for (int m = 0; m < model.MeshCount; m++)
            {
                var meshBB = Raylib.GetMeshBoundingBox(model.Meshes[m]);
                if (meshBB.Min.X < minX) minX = meshBB.Min.X;
                if (meshBB.Min.Y < minY) minY = meshBB.Min.Y;
                if (meshBB.Min.Z < minZ) minZ = meshBB.Min.Z;
                if (meshBB.Max.X > maxX) maxX = meshBB.Max.X;
                if (meshBB.Max.Y > maxY) maxY = meshBB.Max.Y;
                if (meshBB.Max.Z > maxZ) maxZ = meshBB.Max.Z;
            }
        }

        return new BoundingBox(
            new Vector3(minX, minY, minZ),
            new Vector3(maxX, maxY, maxZ));
    }

    private static BoundingBox TransformBounds(BoundingBox localBounds, Matrix4x4 worldMatrix)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        var bMin = localBounds.Min;
        var bMax = localBounds.Max;

        for (int c = 0; c < 8; c++)
        {
            var corner = new Vector3(
                (c & 1) == 0 ? bMin.X : bMax.X,
                (c & 2) == 0 ? bMin.Y : bMax.Y,
                (c & 4) == 0 ? bMin.Z : bMax.Z);

            var world = Vector3.Transform(corner, worldMatrix);
            if (world.X < minX) minX = world.X;
            if (world.Y < minY) minY = world.Y;
            if (world.Z < minZ) minZ = world.Z;
            if (world.X > maxX) maxX = world.X;
            if (world.Y > maxY) maxY = world.Y;
            if (world.Z > maxZ) maxZ = world.Z;
        }

        return new BoundingBox(
            new Vector3(minX, minY, minZ),
            new Vector3(maxX, maxY, maxZ));
    }

    private static int[] CopyMeshMaterialIndices(Model model)
    {
        unsafe
        {
            if (model.MeshMaterial == null || model.MeshCount <= 0)
                return Array.Empty<int>();

            var result = new int[model.MeshCount];
            for (int i = 0; i < model.MeshCount; i++)
                result[i] = model.MeshMaterial[i];
            return result;
        }
    }

    internal sealed class MeshResource
    {
        public MeshResource(
            RenderMeshHandle handle,
            string assetKey,
            Model model,
            bool ownsModel,
            BoundingBox localBounds,
            int materialCount,
            int[] meshMaterialIndices,
            BoneHierarchy? boneHierarchy)
        {
            Handle = handle;
            AssetKey = assetKey;
            Model = model;
            OwnsModel = ownsModel;
            LocalBounds = localBounds;
            MaterialCount = materialCount;
            MeshMaterialIndices = meshMaterialIndices;
            BoneHierarchy = boneHierarchy;
        }

        public RenderMeshHandle Handle { get; }
        public string AssetKey { get; }
        public Model Model { get; }
        public bool OwnsModel { get; }
        public BoundingBox LocalBounds { get; }
        public int MaterialCount { get; }
        public int[] MeshMaterialIndices { get; }
        public BoneHierarchy? BoneHierarchy { get; }
    }

    internal sealed class MaterialResource
    {
        public MaterialResource(RenderMaterialHandle handle, string key, ComponentMaterial[] slots)
        {
            Handle = handle;
            Key = key;
            Slots = slots;
        }

        public RenderMaterialHandle Handle { get; }
        public string Key { get; }
        public ComponentMaterial[] Slots { get; }
    }

    internal sealed class SkinnedInstanceResource
    {
        public SkinnedInstanceResource(
            SkinPaletteHandle handle,
            string assetKey,
            int modelVersion,
            int assetGeneration,
            Model model,
            bool ownsModel)
        {
            Handle = handle;
            AssetKey = assetKey;
            ModelVersion = modelVersion;
            AssetGeneration = assetGeneration;
            Model = model;
            OwnsModel = ownsModel;
        }

        public SkinPaletteHandle Handle { get; }
        public string AssetKey { get; }
        public int ModelVersion { get; }
        public int AssetGeneration { get; }
        public Model Model { get; }
        public bool OwnsModel { get; }
    }

    private readonly record struct CachedWorldBounds(int RenderVersion, int TransformVersion, BoundingBox Bounds);
}
