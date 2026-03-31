using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Physics;
using FrinkyEngine.Core.Prefabs;
using FrinkyEngine.Core.Rendering;
using Raylib_cs;

namespace FrinkyEngine.Core.Serialization;

/// <summary>
/// Handles saving and loading scenes in the <c>.fscene</c> JSON format.
/// Also provides entity duplication via serialization round-trips.
/// </summary>
public static class SceneSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new Vector3Converter(),
            new QuaternionConverter(),
            new ColorConverter(),
            new EntityReferenceConverter(),
            new AssetReferenceConverter(),
            new FObjectConverterFactory(),
            new JsonStringEnumConverter()
        }
    };

    /// <summary>
    /// Saves a scene to a <c>.fscene</c> file.
    /// </summary>
    /// <param name="scene">The scene to save.</param>
    /// <param name="path">Destination file path.</param>
    public static void Save(Scene.Scene scene, string path)
    {
        var data = SerializeScene(scene);
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads a scene from a <c>.fscene</c> file.
    /// </summary>
    /// <param name="path">Path to the scene file.</param>
    /// <returns>The loaded scene, or <c>null</c> if the file doesn't exist or is invalid.</returns>
    public static Scene.Scene? Load(string path)
    {
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<SceneData>(json, JsonOptions);
        if (data == null) return null;
        return DeserializeScene(data);
    }

    /// <summary>
    /// Serializes a scene to a JSON string (useful for snapshots and clipboard operations).
    /// </summary>
    /// <param name="scene">The scene to serialize.</param>
    /// <returns>The JSON string.</returns>
    public static string SerializeToString(Scene.Scene scene)
    {
        var data = SerializeScene(scene);
        return JsonSerializer.Serialize(data, JsonOptions);
    }

    /// <summary>
    /// Deserializes a scene from a JSON string.
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    /// <returns>The deserialized scene, or <c>null</c> if the JSON is invalid.</returns>
    public static Scene.Scene? DeserializeFromString(string json)
    {
        var data = JsonSerializer.Deserialize<SceneData>(json, JsonOptions);
        if (data == null) return null;
        return DeserializeScene(data);
    }

    private static SceneData SerializeScene(Scene.Scene scene)
    {
        var data = new SceneData
        {
            Name = scene.Name,
            EditorCameraPosition = scene.EditorCameraPosition,
            EditorCameraYaw = scene.EditorCameraYaw,
            EditorCameraPitch = scene.EditorCameraPitch,
            Physics = scene.PhysicsSettings.Clone()
        };
        foreach (var entity in scene.Entities)
        {
            if (entity.Transform.Parent != null) continue;
            data.Entities.Add(SerializeEntity(entity));
        }
        return data;
    }

    private static EntityData SerializeEntity(Entity entity)
    {
        var data = new EntityData
        {
            Name = entity.Name,
            Id = entity.Id,
            Active = entity.Active,
            Prefab = entity.Prefab?.Clone()
        };

        foreach (var component in entity.Components)
        {
            data.Components.Add(SerializeComponent(component));
        }

        foreach (var unresolved in entity.UnresolvedComponents)
        {
            data.Components.Add(unresolved);
        }

        foreach (var child in entity.Transform.Children)
        {
            data.Children.Add(SerializeEntity(child.Entity));
        }

        return data;
    }

    private static ComponentData SerializeComponent(Component component)
    {
        var data = new ComponentData
        {
            Type = ComponentTypeResolver.GetTypeName(component.GetType()),
            Enabled = component.Enabled,
            EditorOnly = component.EditorOnly
        };

        var type = component.GetType();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.Name is "Entity" or "HasStarted" or "Enabled" or "EditorOnly") continue;
            if (prop.Name is "EulerAngles" or "WorldPosition" or "WorldRotation") continue;

            try
            {
                var value = prop.GetValue(component);
                if (value != null)
                {
                    var jsonElement = JsonSerializer.SerializeToElement(value, prop.PropertyType, JsonOptions);
                    data.Properties[prop.Name] = jsonElement;
                }
            }
            catch
            {
                // Skip properties that can't be serialized
            }
        }

        return data;
    }

    private static Scene.Scene DeserializeScene(SceneData data)
    {
        var scene = new Scene.Scene
        {
            Name = data.Name,
            EditorCameraPosition = data.EditorCameraPosition,
            EditorCameraYaw = data.EditorCameraYaw,
            EditorCameraPitch = data.EditorCameraPitch,
            PhysicsSettings = data.Physics?.Clone() ?? new PhysicsSettings()
        };
        scene.PhysicsSettings.Normalize();

        foreach (var entityData in data.Entities)
        {
            DeserializeEntityTree(entityData, scene, null);
        }

        return scene;
    }

    private static Entity DeserializeEntityTree(EntityData data, Scene.Scene scene, TransformComponent? parent)
    {
        var entity = new Entity(data.Name)
        {
            Id = data.Id,
            Active = data.Active,
            Prefab = data.Prefab?.Clone()
        };

        foreach (var componentData in data.Components)
        {
            DeserializeComponent(entity, componentData);
        }

        scene.AddEntity(entity);

        if (parent != null)
            entity.Transform.SetParent(parent);

        foreach (var childData in data.Children)
        {
            DeserializeEntityTree(childData, scene, entity.Transform);
        }

        return entity;
    }

    /// <summary>
    /// Creates a deep copy of an entity (and its children) and adds it to the scene.
    /// </summary>
    /// <param name="source">The entity to duplicate.</param>
    /// <param name="scene">The scene to add the duplicate to.</param>
    /// <returns>The duplicated entity, or <c>null</c> if duplication failed.</returns>
    public static Entity? DuplicateEntity(Entity source, Scene.Scene scene)
    {
        var data = SerializeEntity(source);
        var oldToNew = AssignNewIds(data);
        RemapEntityReferences(data, oldToNew);
        data.Name = GenerateDuplicateName(data.Name);

        // Find the parent of the source entity
        var parent = source.Transform.Parent;

        return DeserializeEntityTree(data, scene, parent);
    }

    private static Dictionary<Guid, Guid> AssignNewIds(EntityData data)
    {
        var mapping = new Dictionary<Guid, Guid>();
        AssignNewIdsRecursive(data, mapping);
        return mapping;
    }

    private static void AssignNewIdsRecursive(EntityData data, Dictionary<Guid, Guid> mapping)
    {
        var oldId = data.Id;
        var newId = Guid.NewGuid();
        mapping[oldId] = newId;
        data.Id = newId;
        foreach (var child in data.Children)
            AssignNewIdsRecursive(child, mapping);
    }

    private static void RemapEntityReferences(EntityData data, Dictionary<Guid, Guid> oldToNew)
    {
        foreach (var component in data.Components)
        {
            var keysToUpdate = new List<(string key, JsonElement remapped)>();
            foreach (var (propName, jsonElement) in component.Properties)
            {
                var remapped = RemapJsonElement(jsonElement, oldToNew);
                if (remapped.HasValue)
                    keysToUpdate.Add((propName, remapped.Value));
            }

            foreach (var (key, remapped) in keysToUpdate)
                component.Properties[key] = remapped;
        }

        foreach (var child in data.Children)
            RemapEntityReferences(child, oldToNew);
    }

    private static JsonElement? RemapJsonElement(JsonElement element, Dictionary<Guid, Guid> oldToNew)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                var str = element.GetString();
                if (str != null && Guid.TryParse(str, out var guid) && oldToNew.TryGetValue(guid, out var newGuid))
                    return JsonSerializer.SerializeToElement(newGuid.ToString(), JsonOptions);
                return null;
            }
            case JsonValueKind.Object:
            {
                if (element.TryGetProperty("$type", out _) && element.TryGetProperty("properties", out var props))
                {
                    bool changed = false;
                    using var doc = JsonDocument.Parse(element.GetRawText());
                    using var ms = new MemoryStream();
                    using (var writer = new Utf8JsonWriter(ms))
                    {
                        writer.WriteStartObject();
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            if (prop.Name == "properties")
                            {
                                writer.WritePropertyName("properties");
                                writer.WriteStartObject();
                                foreach (var innerProp in prop.Value.EnumerateObject())
                                {
                                    var remapped = RemapJsonElement(innerProp.Value, oldToNew);
                                    writer.WritePropertyName(innerProp.Name);
                                    if (remapped.HasValue)
                                    {
                                        remapped.Value.WriteTo(writer);
                                        changed = true;
                                    }
                                    else
                                    {
                                        innerProp.Value.WriteTo(writer);
                                    }
                                }
                                writer.WriteEndObject();
                            }
                            else
                            {
                                prop.WriteTo(writer);
                            }
                        }
                        writer.WriteEndObject();
                    }

                    if (changed)
                    {
                        var newDoc = JsonDocument.Parse(ms.ToArray());
                        return newDoc.RootElement.Clone();
                    }
                }
                return null;
            }
            case JsonValueKind.Array:
            {
                bool changed = false;
                var elements = new List<(JsonElement original, JsonElement? remapped)>();
                foreach (var item in element.EnumerateArray())
                {
                    var remapped = RemapJsonElement(item, oldToNew);
                    elements.Add((item, remapped));
                    if (remapped.HasValue)
                        changed = true;
                }

                if (changed)
                {
                    using var ms = new MemoryStream();
                    using (var writer = new Utf8JsonWriter(ms))
                    {
                        writer.WriteStartArray();
                        foreach (var (original, remapped) in elements)
                            (remapped ?? original).WriteTo(writer);
                        writer.WriteEndArray();
                    }
                    var newDoc = JsonDocument.Parse(ms.ToArray());
                    return newDoc.RootElement.Clone();
                }
                return null;
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Generates a duplicate name by appending or incrementing a " (N)" suffix.
    /// </summary>
    /// <param name="name">The original entity name.</param>
    /// <returns>The new name with an incremented suffix.</returns>
    public static string GenerateDuplicateName(string name)
    {
        // Check if name ends with " (N)" pattern
        var match = System.Text.RegularExpressions.Regex.Match(name, @"^(.*) \((\d+)\)$");
        if (match.Success)
        {
            var baseName = match.Groups[1].Value;
            var number = int.Parse(match.Groups[2].Value);
            return $"{baseName} ({number + 1})";
        }
        return $"{name} (1)";
    }

    private static void DeserializeComponent(Entity entity, ComponentData data)
    {
        var type = ComponentTypeResolver.Resolve(data.Type);
        if (type == null)
        {
            entity.AddUnresolvedComponent(data);
            FrinkyLog.Warning($"Unresolved component type '{data.Type}' on entity '{entity.Name}' — data preserved");
            return;
        }

        Component component;
        if (type == typeof(TransformComponent))
        {
            component = entity.Transform;
        }
        else
        {
            if (!entity.TryAddComponent(type, out var created, out var failureReason))
            {
                entity.AddUnresolvedComponent(data);
                FrinkyLog.Warning(
                    $"Skipped component '{data.Type}' on entity '{entity.Name}' (scene deserialize): {failureReason} — data preserved");
                return;
            }

            component = created!;
        }

        component.Enabled = data.Enabled;
        component.EditorOnly = data.EditorOnly;

        // Migrate legacy flat material properties on PrimitiveComponent to nested Material
        if (component is PrimitiveComponent primitive
            && data.Properties.ContainsKey("MaterialType")
            && !data.Properties.ContainsKey("Material"))
        {
            MigrateLegacyPrimitiveMaterial(primitive, data.Properties);
        }

        foreach (var (propName, jsonElement) in data.Properties)
        {
            var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite) continue;
            if (prop.Name is "Entity" or "HasStarted" or "Enabled" or "EditorOnly" or "EulerAngles" or "WorldPosition" or "WorldRotation") continue;

            try
            {
                var value = JsonSerializer.Deserialize(jsonElement.GetRawText(), prop.PropertyType, JsonOptions);
                prop.SetValue(component, value);
            }
            catch
            {
                // Skip properties that can't be deserialized
            }
        }

        if (component is MeshRendererComponent meshRenderer)
            meshRenderer.SyncMaterialSlotsWithModel();

        // Migrate legacy component-level Tint into Material(s)
        if (component is RenderableComponent && data.Properties.TryGetValue("Tint", out var tintEl))
        {
            try
            {
                var tint = JsonSerializer.Deserialize<Color>(tintEl.GetRawText(), JsonOptions);
                if (component is PrimitiveComponent primTint)
                    primTint.Material.Tint = tint;
                else if (component is MeshRendererComponent meshTint)
                {
                    foreach (var slot in meshTint.MaterialSlots)
                        slot.Tint = tint;
                }
            }
            catch { }
        }
    }

    private static void MigrateLegacyPrimitiveMaterial(PrimitiveComponent primitive, Dictionary<string, JsonElement> properties)
    {
        var material = new Components.Material();

        if (properties.TryGetValue("MaterialType", out var matTypeEl))
        {
            try { material.MaterialType = JsonSerializer.Deserialize<MaterialType>(matTypeEl.GetRawText(), JsonOptions); } catch { }
            properties.Remove("MaterialType");
        }
        if (properties.TryGetValue("TexturePath", out var texEl))
        {
            try { material.TexturePath = JsonSerializer.Deserialize<AssetReference>(texEl.GetRawText(), JsonOptions); } catch { }
            properties.Remove("TexturePath");
        }
        if (properties.TryGetValue("TriplanarScale", out var scaleEl))
        {
            try { material.TriplanarScale = scaleEl.GetSingle(); } catch { }
            properties.Remove("TriplanarScale");
        }
        if (properties.TryGetValue("TriplanarBlendSharpness", out var sharpEl))
        {
            try { material.TriplanarBlendSharpness = sharpEl.GetSingle(); } catch { }
            properties.Remove("TriplanarBlendSharpness");
        }
        if (properties.TryGetValue("TriplanarUseWorldSpace", out var wsEl))
        {
            try { material.TriplanarUseWorldSpace = wsEl.GetBoolean(); } catch { }
            properties.Remove("TriplanarUseWorldSpace");
        }

        primitive.Material = material;
    }
}

/// <summary>
/// JSON-serializable representation of a scene.
/// </summary>
public class SceneData
{
    /// <summary>
    /// Scene display name.
    /// </summary>
    public string Name { get; set; } = "Untitled";

    /// <summary>
    /// Saved editor camera position.
    /// </summary>
    public System.Numerics.Vector3? EditorCameraPosition { get; set; }

    /// <summary>
    /// Saved editor camera yaw angle.
    /// </summary>
    public float? EditorCameraYaw { get; set; }

    /// <summary>
    /// Saved editor camera pitch angle.
    /// </summary>
    public float? EditorCameraPitch { get; set; }

    /// <summary>
    /// Scene physics configuration.
    /// </summary>
    public PhysicsSettings? Physics { get; set; } = new();

    /// <summary>
    /// Serialized root entities (children are nested within each entity).
    /// </summary>
    public List<EntityData> Entities { get; set; } = new();
}

/// <summary>
/// JSON-serializable representation of an entity.
/// </summary>
public class EntityData
{
    /// <summary>
    /// Entity display name.
    /// </summary>
    public string Name { get; set; } = "Entity";

    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Whether the entity is active.
    /// </summary>
    public bool Active { get; set; } = true;

    /// <summary>
    /// Optional prefab instance metadata.
    /// </summary>
    public PrefabInstanceMetadata? Prefab { get; set; }

    /// <summary>
    /// Serialized components attached to this entity.
    /// </summary>
    public List<ComponentData> Components { get; set; } = new();

    /// <summary>
    /// Serialized child entities.
    /// </summary>
    public List<EntityData> Children { get; set; } = new();
}

/// <summary>
/// JSON-serializable representation of a component, discriminated by the <c>$type</c> field.
/// </summary>
public class ComponentData
{
    /// <summary>
    /// Fully qualified type name used by <see cref="ComponentTypeResolver"/> for deserialization.
    /// </summary>
    [JsonPropertyName("$type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Whether the component is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether the component is editor-only.
    /// </summary>
    public bool EditorOnly { get; set; }

    /// <summary>
    /// Serialized public properties as key-value pairs of JSON elements.
    /// </summary>
    public Dictionary<string, JsonElement> Properties { get; set; } = new();
}
