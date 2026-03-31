using System.Reflection;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Rendering;

namespace FrinkyEngine.Core.Assets;

/// <summary>
/// Scans all entities in a scene for <see cref="AssetReference"/> properties
/// and logs warnings for broken (non-empty but non-existent) references.
/// </summary>
public static class AssetReferenceValidator
{
    private static readonly HashSet<string> ExcludedProperties = new()
    {
        "Entity", "HasStarted", "Enabled", "EditorOnly",
        "EulerAngles", "WorldPosition", "WorldRotation"
    };

    /// <summary>
    /// Validates all asset references in the given scene against the asset database.
    /// </summary>
    public static void ValidateScene(Scene.Scene? scene)
    {
        if (scene == null)
            return;

        var db = AssetDatabase.Instance;

        foreach (var entity in scene.Entities)
        {
            foreach (var component in entity.Components)
            {
                ValidateComponent(entity, component, db);
            }
        }
    }

    private static void ValidateComponent(Entity entity, Component component, AssetDatabase db)
    {
        var type = component.GetType();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || ExcludedProperties.Contains(prop.Name))
                continue;

            if (prop.PropertyType == typeof(AssetReference))
            {
                var value = (AssetReference)prop.GetValue(component)!;
                if (!value.IsEmpty && !db.AssetExistsByName(value.Path))
                {
                    FrinkyLog.Warning(
                        $"Broken asset reference: '{value.Path}' on {entity.Name}.{type.Name}.{prop.Name}");
                }
            }
            else if (prop.PropertyType.IsGenericType &&
                     prop.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elementType = prop.PropertyType.GetGenericArguments()[0];
                if (elementType == typeof(AssetReference))
                {
                    ValidateAssetReferenceListProperty(entity, component, prop, type, db);
                }
                else
                {
                    ValidateListProperty(entity, component, prop, type, db);
                }
            }
        }
    }

    private static void ValidateAssetReferenceListProperty(
        Entity entity, Component component, PropertyInfo listProp, Type componentType, AssetDatabase db)
    {
        if (listProp.GetValue(component) is not List<AssetReference> list) return;

        for (int i = 0; i < list.Count; i++)
        {
            var value = list[i];
            if (!value.IsEmpty && !db.AssetExistsByName(value.Path))
            {
                FrinkyLog.Warning(
                    $"Broken asset reference: '{value.Path}' on {entity.Name}.{componentType.Name}.{listProp.Name}[{i}]");
            }
        }
    }

    private static void ValidateListProperty(
        Entity entity, Component component, PropertyInfo listProp, Type componentType, AssetDatabase db)
    {
        var elementType = listProp.PropertyType.GetGenericArguments()[0];
        var assetRefProps = elementType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.PropertyType == typeof(AssetReference))
            .ToList();

        if (assetRefProps.Count == 0)
            return;

        var list = listProp.GetValue(component) as System.Collections.IList;
        if (list == null)
            return;

        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (item == null) continue;

            foreach (var prop in assetRefProps)
            {
                var value = (AssetReference)prop.GetValue(item)!;
                if (!value.IsEmpty && !db.AssetExistsByName(value.Path))
                {
                    FrinkyLog.Warning(
                        $"Broken asset reference: '{value.Path}' on {entity.Name}.{componentType.Name}.{listProp.Name}[{i}].{prop.Name}");
                }
            }
        }
    }
}
