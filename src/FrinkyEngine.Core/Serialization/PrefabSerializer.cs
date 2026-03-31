using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Prefabs;
using FrinkyEngine.Core.Rendering;

namespace FrinkyEngine.Core.Serialization;

public static class PrefabSerializer
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

    public static void Save(PrefabAssetData prefab, string path)
    {
        var json = JsonSerializer.Serialize(prefab, JsonOptions);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, json);
    }

    public static PrefabAssetData? Load(string path)
    {
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        var prefab = JsonSerializer.Deserialize<PrefabAssetData>(json, JsonOptions);
        if (prefab?.Root == null)
            return null;

        EnsureStableIds(prefab.Root, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        SanitizeRootNode(prefab.Root);
        return prefab;
    }

    public static PrefabAssetData CreateFromEntity(Entity root, bool preserveStableIds = true)
    {
        var usedStableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entityIdToStableId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var data = new PrefabAssetData
        {
            Name = root.Name,
            Root = SerializeNode(root, preserveStableIds, isSerializationRoot: true, usedStableIds, entityIdToStableId)
        };
        NormalizeInternalEntityReferences(data.Root, entityIdToStableId);
        return data;
    }

    public static PrefabNodeData SerializeNode(Entity entity, bool preserveStableIds = true)
    {
        var usedStableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return SerializeNode(entity, preserveStableIds, isSerializationRoot: true, usedStableIds);
    }

    public static PrefabNodeData SerializeNodeNormalized(Entity entity, bool preserveStableIds = true)
    {
        var usedStableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entityIdToStableId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var node = SerializeNode(entity, preserveStableIds, isSerializationRoot: true, usedStableIds, entityIdToStableId);
        NormalizeInternalEntityReferences(node, entityIdToStableId);
        return node;
    }

    private static PrefabNodeData SerializeNode(
        Entity entity,
        bool preserveStableIds,
        bool isSerializationRoot,
        HashSet<string> usedStableIds,
        Dictionary<string, string>? entityIdToStableId = null)
    {
        var stableId = ResolveStableId(entity, preserveStableIds);
        if (string.IsNullOrWhiteSpace(stableId) || usedStableIds.Contains(stableId))
            stableId = Guid.NewGuid().ToString("N");
        usedStableIds.Add(stableId);

        entityIdToStableId?.TryAdd(entity.Id.ToString("N"), stableId);

        var node = new PrefabNodeData
        {
            StableId = stableId,
            Name = entity.Name,
            Active = entity.Active
        };

        foreach (var component in entity.Components)
            node.Components.Add(SerializeComponent(component));

        foreach (var unresolved in entity.UnresolvedComponents)
        {
            node.Components.Add(new PrefabComponentData
            {
                Type = unresolved.Type,
                Enabled = unresolved.Enabled,
                EditorOnly = unresolved.EditorOnly,
                Properties = new Dictionary<string, JsonElement>(unresolved.Properties)
            });
        }

        foreach (var child in entity.Transform.Children)
            node.Children.Add(SerializeNode(child.Entity, preserveStableIds, isSerializationRoot: false, usedStableIds, entityIdToStableId));

        // The root transform is scene placement, not prefab content.
        if (isSerializationRoot)
            SanitizeRootNode(node);

        return node;
    }

    private static void NormalizeInternalEntityReferences(PrefabNodeData node, Dictionary<string, string> entityIdToStableId)
    {
        foreach (var component in node.Components)
        {
            var keysToUpdate = new List<(string key, JsonElement normalized)>();
            foreach (var (propName, jsonElement) in component.Properties)
            {
                var normalized = NormalizeJsonElement(jsonElement, entityIdToStableId);
                if (normalized.HasValue)
                    keysToUpdate.Add((propName, normalized.Value));
            }

            foreach (var (key, normalized) in keysToUpdate)
                component.Properties[key] = normalized;
        }

        foreach (var child in node.Children)
            NormalizeInternalEntityReferences(child, entityIdToStableId);
    }

    private static JsonElement? NormalizeJsonElement(JsonElement element, Dictionary<string, string> entityIdToStableId)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                var str = element.GetString();
                if (str != null && Guid.TryParse(str, out var guid))
                {
                    var key = guid.ToString("N");
                    if (entityIdToStableId.TryGetValue(key, out var stableId) &&
                        !string.Equals(key, stableId, StringComparison.OrdinalIgnoreCase))
                    {
                        var stableGuid = Guid.Parse(stableId);
                        return JsonSerializer.SerializeToElement(stableGuid.ToString(), JsonOptions);
                    }
                }
                return null;
            }
            case JsonValueKind.Object:
            {
                if (element.TryGetProperty("$type", out _) && element.TryGetProperty("properties", out _))
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
                                    var normalized = NormalizeJsonElement(innerProp.Value, entityIdToStableId);
                                    writer.WritePropertyName(innerProp.Name);
                                    if (normalized.HasValue)
                                    {
                                        normalized.Value.WriteTo(writer);
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
                var elements = new List<(JsonElement original, JsonElement? normalized)>();
                foreach (var item in element.EnumerateArray())
                {
                    var normalized = NormalizeJsonElement(item, entityIdToStableId);
                    elements.Add((item, normalized));
                    if (normalized.HasValue)
                        changed = true;
                }

                if (changed)
                {
                    using var ms = new MemoryStream();
                    using (var writer = new Utf8JsonWriter(ms))
                    {
                        writer.WriteStartArray();
                        foreach (var (original, normalized) in elements)
                            (normalized ?? original).WriteTo(writer);
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

    public static PrefabComponentData SerializeComponent(Component component)
    {
        var data = new PrefabComponentData
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
                // Skip properties that cannot be serialized.
            }
        }

        return data;
    }

    public static bool ApplyComponentData(Entity entity, PrefabComponentData data)
    {
        var type = ComponentTypeResolver.Resolve(data.Type);
        if (type == null)
        {
            entity.AddUnresolvedComponent(new ComponentData
            {
                Type = data.Type,
                Enabled = data.Enabled,
                EditorOnly = data.EditorOnly,
                Properties = new Dictionary<string, JsonElement>(data.Properties)
            });
            FrinkyLog.Warning($"Unresolved component type '{data.Type}' on entity '{entity.Name}' — data preserved");
            return false;
        }

        Component component;
        if (type == typeof(Components.TransformComponent))
        {
            component = entity.Transform;
        }
        else
        {
            var existing = entity.GetComponent(type);
            if (existing != null)
            {
                component = existing;
            }
            else if (!entity.TryAddComponent(type, out var created, out var failureReason))
            {
                entity.AddUnresolvedComponent(new ComponentData
                {
                    Type = data.Type,
                    Enabled = data.Enabled,
                    EditorOnly = data.EditorOnly,
                    Properties = new Dictionary<string, JsonElement>(data.Properties)
                });
                FrinkyLog.Warning(
                    $"Skipped component '{data.Type}' on entity '{entity.Name}' (prefab apply): {failureReason} — data preserved");
                return false;
            }
            else
            {
                component = created!;
            }
        }

        component.Enabled = data.Enabled;
        component.EditorOnly = data.EditorOnly;

        // Migrate legacy flat material properties on PrimitiveComponent to nested Material
        if (component is Components.PrimitiveComponent primitive
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
                // Skip values that cannot be deserialized into this component.
            }
        }

        if (component is Components.MeshRendererComponent meshRenderer)
            meshRenderer.SyncMaterialSlotsWithModel();

        // Migrate legacy component-level Tint into Material(s)
        if (component is Components.RenderableComponent && data.Properties.TryGetValue("Tint", out var tintEl))
        {
            try
            {
                var tint = JsonSerializer.Deserialize<Raylib_cs.Color>(tintEl.GetRawText(), JsonOptions);
                if (component is Components.PrimitiveComponent primTint)
                    primTint.Material.Tint = tint;
                else if (component is Components.MeshRendererComponent meshTint)
                {
                    foreach (var slot in meshTint.MaterialSlots)
                        slot.Tint = tint;
                }
            }
            catch { }
        }

        return true;
    }

    private static void MigrateLegacyPrimitiveMaterial(Components.PrimitiveComponent primitive, Dictionary<string, JsonElement> properties)
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

    public static object? DeserializeValue(JsonElement value, Type targetType)
    {
        try
        {
            return JsonSerializer.Deserialize(value.GetRawText(), targetType, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static JsonElement SerializeValue(object value, Type valueType)
    {
        return JsonSerializer.SerializeToElement(value, valueType, JsonOptions);
    }

    private static string ResolveStableId(Entity entity, bool preserveStableIds)
    {
        if (preserveStableIds && entity.Prefab != null && !string.IsNullOrWhiteSpace(entity.Prefab.SourceNodeId))
            return entity.Prefab.SourceNodeId;

        if (preserveStableIds)
            return entity.Id.ToString("N");

        return Guid.NewGuid().ToString("N");
    }

    private static void SanitizeRootNode(PrefabNodeData root)
    {
        foreach (var component in root.Components)
        {
            if (!IsTransformComponentType(component.Type))
                continue;

            component.Properties.Remove("LocalPosition");
            component.Properties.Remove("LocalRotation");
            component.Properties.Remove("LocalScale");
            break;
        }
    }

    private static bool IsTransformComponentType(string componentType)
    {
        if (string.Equals(componentType, typeof(Components.TransformComponent).FullName, StringComparison.Ordinal) ||
            string.Equals(componentType, nameof(Components.TransformComponent), StringComparison.Ordinal))
        {
            return true;
        }

        var resolved = ComponentTypeResolver.Resolve(componentType);
        return resolved == typeof(Components.TransformComponent);
    }

    private static void EnsureStableIds(PrefabNodeData node, HashSet<string> usedStableIds)
    {
        if (string.IsNullOrWhiteSpace(node.StableId) || usedStableIds.Contains(node.StableId))
            node.StableId = Guid.NewGuid().ToString("N");
        usedStableIds.Add(node.StableId);

        foreach (var child in node.Children)
            EnsureStableIds(child, usedStableIds);
    }
}
