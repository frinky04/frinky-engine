using System.Numerics;
using System.Reflection;
using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.ECS;
using Raylib_cs;

namespace FrinkyEngine.Editor.Panels;

internal static class InspectorReflectionHelpers
{
    public static bool IsInspectableComponentProperty(PropertyInfo prop)
    {
        if (!prop.CanRead)
            return false;
        if (prop.GetCustomAttribute<InspectorHiddenAttribute>() != null)
            return false;
        if (prop.Name is "Entity" or "HasStarted" or "Enabled")
            return false;
        if (prop.CanWrite)
            return true;
        return prop.GetCustomAttribute<InspectorReadOnlyAttribute>() != null;
    }

    public static bool IsListType(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
    }

    public static bool IsInlineObjectType(Type type)
    {
        if (type == typeof(string)
            || type.IsPrimitive
            || type.IsEnum
            || type == typeof(decimal)
            || type == typeof(Vector2)
            || type == typeof(Vector3)
            || type == typeof(Quaternion)
            || type == typeof(Color)
            || type == typeof(EntityReference)
            || type == typeof(AssetReference)
            || typeof(FObject).IsAssignableFrom(type))
        {
            return false;
        }

        return type.IsClass || (type.IsValueType && !type.IsPrimitive && !type.IsEnum);
    }

    public static bool TryEvaluateBoolMember(object target, string memberName, out bool value)
    {
        value = false;
        var type = target.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var property = GetPropertyInHierarchy(type, memberName, flags);
        if (property != null && property.CanRead && property.PropertyType == typeof(bool))
        {
            value = (bool?)property.GetValue(target) ?? false;
            return true;
        }

        var field = GetFieldInHierarchy(type, memberName, flags);
        if (field != null && field.FieldType == typeof(bool))
        {
            value = (bool?)field.GetValue(target) ?? false;
            return true;
        }

        var method = GetMethodInHierarchy(type, memberName, flags);
        if (method != null && method.ReturnType == typeof(bool))
        {
            value = (bool?)method.Invoke(target, null) ?? false;
            return true;
        }

        return false;
    }

    public static bool TryEvaluateEnumMember(object target, string memberName, out Enum? value)
    {
        value = null;
        var type = target.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var property = GetPropertyInHierarchy(type, memberName, flags);
        if (property != null && property.CanRead && property.PropertyType.IsEnum)
        {
            value = property.GetValue(target) as Enum;
            return value != null;
        }

        var field = GetFieldInHierarchy(type, memberName, flags);
        if (field != null && field.FieldType.IsEnum)
        {
            value = field.GetValue(target) as Enum;
            return value != null;
        }

        var method = GetMethodInHierarchy(type, memberName, flags);
        if (method != null && method.ReturnType.IsEnum)
        {
            value = method.Invoke(target, null) as Enum;
            return value != null;
        }

        return false;
    }

    private static PropertyInfo? GetPropertyInHierarchy(Type type, string name, BindingFlags flags)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var prop = t.GetProperty(name, flags | BindingFlags.DeclaredOnly);
            if (prop != null) return prop;
        }
        return null;
    }

    private static FieldInfo? GetFieldInHierarchy(Type type, string name, BindingFlags flags)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var field = t.GetField(name, flags | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        return null;
    }

    private static MethodInfo? GetMethodInHierarchy(Type type, string name, BindingFlags flags)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var method = t.GetMethod(name, flags | BindingFlags.DeclaredOnly, binder: null, types: Type.EmptyTypes, modifiers: null);
            if (method != null) return method;
        }
        return null;
    }
}
