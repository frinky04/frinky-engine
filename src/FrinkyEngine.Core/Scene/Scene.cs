using System.Numerics;
using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.Audio;
using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Physics;
using FrinkyEngine.Core.Prefabs;
using FrinkyEngine.Core.Rendering.Profiling;

namespace FrinkyEngine.Core.Scene;

/// <summary>
/// A container of <see cref="Entity"/> instances that make up a game level or environment.
/// Maintains quick-access lists for cameras, lights, and renderables.
/// </summary>
public class Scene : IDisposable
{
    /// <summary>
    /// Global time scale applied to the game delta time.
    /// A value of 1.0 is normal speed, 0.5 is half speed, 0 is paused.
    /// </summary>
    public static float TimeScale { get; set; } = 1.0f;

    /// <summary>
    /// Display name of this scene.
    /// </summary>
    public string Name { get; set; } = "Untitled";

    /// <summary>
    /// File path this scene was last saved to or loaded from.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    // Editor camera metadata (persisted in .fscene)

    /// <summary>
    /// Saved editor camera position, restored when the scene is reopened in the editor.
    /// </summary>
    public System.Numerics.Vector3? EditorCameraPosition { get; set; }

    /// <summary>
    /// Saved editor camera yaw angle in degrees.
    /// </summary>
    public float? EditorCameraYaw { get; set; }

    /// <summary>
    /// Saved editor camera pitch angle in degrees.
    /// </summary>
    public float? EditorCameraPitch { get; set; }

    /// <summary>
    /// Physics settings used by this scene.
    /// </summary>
    public PhysicsSettings PhysicsSettings { get; set; } = new();

    private readonly List<Entity> _entities = new();
    private readonly List<(Entity entity, float remainingTime)> _pendingDestroys = new();

    /// <summary>
    /// All entities currently in this scene.
    /// </summary>
    public IReadOnlyList<Entity> Entities => _entities;

    private readonly ComponentRegistry _registry = new();

    /// <summary>
    /// All active <see cref="CameraComponent"/> instances in the scene.
    /// </summary>
    public IReadOnlyList<CameraComponent> Cameras => _registry.GetComponents<CameraComponent>();

    /// <summary>
    /// All active <see cref="LightComponent"/> instances in the scene.
    /// </summary>
    public IReadOnlyList<LightComponent> Lights => _registry.GetComponents<LightComponent>();

    /// <summary>
    /// All active <see cref="RenderableComponent"/> instances in the scene.
    /// </summary>
    public IReadOnlyList<RenderableComponent> Renderables => _registry.GetComponents<RenderableComponent>();

    internal PhysicsSystem? PhysicsSystem { get; private set; }
    internal AudioSystem? AudioSystem { get; private set; }
    private bool _started;

    /// <summary>
    /// Total elapsed scaled time since the scene started, in seconds.
    /// </summary>
    public float Time { get; private set; }

    /// <summary>
    /// Total elapsed unscaled (real) time since the scene started, in seconds.
    /// </summary>
    public float UnscaledTime { get; private set; }

    /// <summary>
    /// The unscaled delta time for the current frame, in seconds.
    /// </summary>
    public float UnscaledDeltaTime { get; private set; }

    /// <summary>
    /// Total number of frames elapsed since the scene started.
    /// </summary>
    public long FrameCount { get; private set; }

    /// <summary>
    /// Gets the first enabled camera marked as <see cref="CameraComponent.IsMain"/>, or <c>null</c> if none exists.
    /// </summary>
    public CameraComponent? MainCamera => _registry.GetComponents<CameraComponent>()
        .FirstOrDefault(c => c.IsMain && c.Enabled);

    /// <summary>
    /// Gets all components of type <typeparamref name="T"/> across all entities in the scene.
    /// </summary>
    /// <typeparam name="T">The component type to search for.</typeparam>
    /// <returns>A list of matching components.</returns>
    public List<T> GetComponents<T>() where T : Component => _registry.GetComponents<T>();

    /// <summary>
    /// Gets all components of the specified runtime type across all entities in the scene.
    /// </summary>
    /// <param name="type">The component type to search for.</param>
    /// <returns>A read-only list of matching components.</returns>
    public IReadOnlyList<Component> GetComponents(Type type) => _registry.GetComponentsRaw(type);

    /// <summary>
    /// Creates a new entity with the given name and adds it to the scene.
    /// </summary>
    /// <param name="name">Display name for the entity.</param>
    /// <returns>The newly created entity.</returns>
    public Entity CreateEntity(string name = "Entity")
    {
        var entity = new Entity(name);
        AddEntity(entity);
        return entity;
    }

    /// <summary>
    /// Adds an existing entity to this scene and registers all its components.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    public void AddEntity(Entity entity)
    {
        entity.Scene = this;
        _entities.Add(entity);

        foreach (var c in entity.Components)
            _registry.Register(c);

        if (_started)
            PhysicsSystem?.OnComponentStateChanged();
    }

    /// <summary>
    /// Removes an entity from this scene, destroying all its components.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    public void RemoveEntity(Entity entity)
    {
        var subtree = new List<Entity>();
        CollectEntitySubtree(entity, subtree);

        // Remove children before parents so hierarchy links are cleaned safely.
        for (int i = subtree.Count - 1; i >= 0; i--)
        {
            var current = subtree[i];
            if (current.Scene != this)
                continue;

            PhysicsSystem?.OnEntityRemoved(current);

            current.Transform.SetParent(null);

            foreach (var c in current.Components)
                _registry.Unregister(c);

            current.DestroyComponents();
            current.Scene = null;
            _entities.Remove(current);
        }
    }

    private static void CollectEntitySubtree(Entity entity, List<Entity> results)
    {
        results.Add(entity);
        foreach (var child in entity.Transform.Children.ToList())
            CollectEntitySubtree(child.Entity, results);
    }

    internal void OnComponentAdded(Entity entity, Component component)
    {
        _registry.Register(component);
        if (_started)
            PhysicsSystem?.OnComponentStateChanged();
    }

    internal void OnComponentRemoved(Entity entity, Component component)
    {
        _registry.Unregister(component);
        if (_started)
            PhysicsSystem?.OnComponentStateChanged();
    }

    internal void NotifyPhysicsStateChanged()
    {
        if (_started)
            PhysicsSystem?.OnComponentStateChanged();
    }

    /// <summary>
    /// Notifies the active physics scene that one or more asset-backed physics sources changed on disk.
    /// </summary>
    /// <param name="relativePaths">Changed asset paths relative to the asset root.</param>
    public void InvalidatePhysicsAssets(IEnumerable<string> relativePaths)
    {
        if (_started)
            PhysicsSystem?.InvalidateAssets(relativePaths);
    }

    /// <summary>
    /// Calls <see cref="Component.Start"/> on all components that haven't started yet.
    /// </summary>
    public void Start()
    {
        if (_started)
            return;

        Physics.Physics.CurrentScene = this;

        PhysicsSettings.Normalize();
        PhysicsSystem ??= new PhysicsSystem(this);
        PhysicsSystem.Initialize();
        AudioSystem ??= new AudioSystem(this);

        foreach (var entity in _entities)
        {
            if (entity.Active)
                entity.StartComponents();
        }

        _started = true;
    }

    /// <summary>
    /// Runs one frame of the game loop by calling <see cref="Component.Update"/>, stepping physics, publishing physics visual poses, then calling <see cref="Component.LateUpdate"/> on all active entities.
    /// </summary>
    /// <param name="dt">Time elapsed since the previous frame, in seconds.</param>
    public void Update(float dt)
    {
        Physics.Physics.CurrentScene = this;
        float unscaledDt = dt;
        dt *= TimeScale;

        UnscaledDeltaTime = unscaledDt;
        Time += dt;
        UnscaledTime += unscaledDt;
        FrameCount++;

        using (FrameProfiler.Scope(ProfileCategory.Game))
        {
            foreach (var entity in _entities)
            {
                if (entity.Active)
                    entity.UpdateComponents(dt, unscaledDt);
            }
        }

        using (FrameProfiler.Scope(ProfileCategory.Physics))
        {
            PhysicsSystem?.Step(dt);

            // Publish physics-driven transforms before LateUpdate so scripts observe current-frame body poses.
            PhysicsSystem?.PublishInterpolatedVisualPoses();
        }

        using (FrameProfiler.Scope(ProfileCategory.GameLate))
        {
            foreach (var entity in _entities)
            {
                if (entity.Active)
                    entity.LateUpdateComponents(dt);
            }
        }

        using (FrameProfiler.Scope(ProfileCategory.Audio))
        {
            AudioSystem?.Update(dt);
        }

        ProcessPendingDestroys(dt);
    }

    private void ProcessPendingDestroys(float dt)
    {
        for (int i = _pendingDestroys.Count - 1; i >= 0; i--)
        {
            var (entity, remaining) = _pendingDestroys[i];
            remaining -= dt;
            if (remaining <= 0f)
            {
                _pendingDestroys.RemoveAt(i);
                if (entity.Scene == this)
                    RemoveEntity(entity);
            }
            else
            {
                _pendingDestroys[i] = (entity, remaining);
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of physics diagnostics for the current frame.
    /// </summary>
    public PhysicsFrameStats GetPhysicsFrameStats()
    {
        return PhysicsSystem?.GetFrameStats() ?? default;
    }

    /// <summary>
    /// Returns a snapshot of audio diagnostics for the current frame.
    /// </summary>
    public AudioFrameStats GetAudioFrameStats()
    {
        return AudioSystem?.GetFrameStats() ?? default;
    }

    /// <summary>
    /// Finds the first entity in this scene with the specified name.
    /// </summary>
    /// <param name="name">The name to search for.</param>
    /// <returns>The first matching entity, or <c>null</c> if not found.</returns>
    public Entity? FindEntityByName(string name)
    {
        foreach (var entity in _entities)
        {
            if (string.Equals(entity.Name, name, StringComparison.Ordinal))
                return entity;
        }
        return null;
    }

    /// <summary>
    /// Finds all entities in this scene with the specified name.
    /// </summary>
    /// <param name="name">The name to search for.</param>
    /// <returns>A list of all matching entities.</returns>
    public List<Entity> FindEntitiesByName(string name)
    {
        var results = new List<Entity>();
        foreach (var entity in _entities)
        {
            if (string.Equals(entity.Name, name, StringComparison.Ordinal))
                results.Add(entity);
        }
        return results;
    }

    /// <summary>
    /// Finds all entities that have a component of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The component type to search for.</typeparam>
    /// <returns>A list of entities with a matching component.</returns>
    public List<Entity> FindEntitiesWithComponent<T>() where T : Component
    {
        var components = _registry.GetComponents<T>();
        var results = new List<Entity>(components.Count);
        foreach (var c in components)
            results.Add(c.Entity);
        return results;
    }

    /// <summary>
    /// Finds an entity in this scene by its <see cref="Entity.Id"/>.
    /// </summary>
    /// <param name="id">The GUID to search for.</param>
    /// <returns>The matching entity, or <c>null</c> if not found.</returns>
    public Entity? FindEntityById(Guid id)
    {
        foreach (var entity in _entities)
        {
            if (entity.Id == id)
                return entity;
        }
        return null;
    }

    /// <summary>
    /// Queues an entity for deferred destruction after a delay.
    /// </summary>
    /// <param name="entity">The entity to destroy.</param>
    /// <param name="delaySeconds">Time in seconds before the entity is removed.</param>
    internal void QueueDestroy(Entity entity, float delaySeconds)
    {
        _pendingDestroys.Add((entity, delaySeconds));
    }

    /// <summary>
    /// Instantiates a prefab into this scene.
    /// </summary>
    /// <param name="prefabPath">The asset-relative path to the prefab file.</param>
    /// <param name="parent">Optional parent transform.</param>
    /// <returns>The root entity of the instantiated prefab, or <c>null</c> if the prefab was not found.</returns>
    public Entity? Instantiate(string prefabPath, TransformComponent? parent = null)
    {
        return PrefabInstantiator.Instantiate(this, prefabPath, parent);
    }

    /// <summary>
    /// Instantiates a prefab into this scene at a specific position and rotation.
    /// </summary>
    /// <param name="prefabPath">The asset-relative path to the prefab file.</param>
    /// <param name="position">World position for the instantiated entity.</param>
    /// <param name="rotation">World rotation for the instantiated entity.</param>
    /// <param name="parent">Optional parent transform.</param>
    /// <returns>The root entity of the instantiated prefab, or <c>null</c> if the prefab was not found.</returns>
    public Entity? Instantiate(string prefabPath, Vector3 position, Quaternion rotation, TransformComponent? parent = null)
    {
        return PrefabInstantiator.Instantiate(this, prefabPath, position, rotation, parent);
    }

    /// <summary>
    /// Instantiates a prefab into this scene using an asset reference.
    /// </summary>
    /// <param name="prefab">The asset reference pointing to the prefab file.</param>
    /// <param name="parent">Optional parent transform.</param>
    /// <returns>The root entity of the instantiated prefab, or <c>null</c> if the prefab was not found.</returns>
    public Entity? Instantiate(AssetReference prefab, TransformComponent? parent = null)
    {
        return PrefabInstantiator.Instantiate(this, prefab, parent);
    }

    /// <summary>
    /// Instantiates a prefab into this scene at a specific position and rotation using an asset reference.
    /// </summary>
    /// <param name="prefab">The asset reference pointing to the prefab file.</param>
    /// <param name="position">World position for the instantiated entity.</param>
    /// <param name="rotation">World rotation for the instantiated entity.</param>
    /// <param name="parent">Optional parent transform.</param>
    /// <returns>The root entity of the instantiated prefab, or <c>null</c> if the prefab was not found.</returns>
    public Entity? Instantiate(AssetReference prefab, Vector3 position, Quaternion rotation, TransformComponent? parent = null)
    {
        return PrefabInstantiator.Instantiate(this, prefab, position, rotation, parent);
    }

    /// <summary>
    /// Releases runtime resources associated with this scene (for example physics simulation state).
    /// </summary>
    public void Dispose()
    {
        PhysicsSystem?.Dispose();
        PhysicsSystem = null;
        AudioSystem?.Dispose();
        AudioSystem = null;
        _pendingDestroys.Clear();
        Time = 0f;
        UnscaledTime = 0f;
        UnscaledDeltaTime = 0f;
        FrameCount = 0;
        _started = false;
    }
}
