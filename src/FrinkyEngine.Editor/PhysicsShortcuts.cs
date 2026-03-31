using System.Numerics;
using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.ECS;
using Hexa.NET.ImGui;

namespace FrinkyEngine.Editor;

/// <summary>
/// Provides quick-add shortcuts for common physics component combinations.
/// </summary>
public static class PhysicsShortcuts
{
    /// <summary>
    /// Adds a collider (auto-detected from mesh/primitive) with no rigidbody, making an engine-supported static collidable.
    /// </summary>
    public static bool AddStaticBody(Entity entity, EditorApplication app)
    {
        if (entity.GetComponent<ColliderComponent>() != null)
        {
            NotificationManager.Instance.Post(
                $"{entity.Name} already has a collider.", NotificationType.Warning);
            return false;
        }

        app.RecordUndo();
        AddAutoCollider(entity, preferMeshCollider: true);
        app.RefreshUndoBaseline();
        NotificationManager.Instance.Post(
            $"Added static collidable to {entity.Name}", NotificationType.Info, 1.5f);
        return true;
    }

    /// <summary>
    /// Adds a collider and a dynamic rigidbody to the entity.
    /// </summary>
    public static bool AddDynamicBody(Entity entity, EditorApplication app)
    {
        return AddBodyWithMotionType(entity, app, BodyMotionType.Dynamic, "dynamic body");
    }

    /// <summary>
    /// Adds a collider and a kinematic rigidbody to the entity.
    /// </summary>
    public static bool AddKinematicBody(Entity entity, EditorApplication app)
    {
        return AddBodyWithMotionType(entity, app, BodyMotionType.Kinematic, "kinematic body");
    }

    /// <summary>
    /// Adds a collider and a rigidbody with the given motion type, skipping duplicates.
    /// </summary>
    public static bool AddBodyWithMotionType(Entity entity, EditorApplication app, BodyMotionType motionType, string label)
    {
        bool hasCollider = entity.GetComponent<ColliderComponent>() != null;
        bool hasRigidbody = entity.GetComponent<RigidbodyComponent>() != null;

        if (hasCollider && hasRigidbody)
        {
            NotificationManager.Instance.Post(
                $"{entity.Name} already has a collider and rigidbody.", NotificationType.Warning);
            return false;
        }

        app.RecordUndo();

        if (!hasCollider)
            AddAutoCollider(entity, preferMeshCollider: motionType != BodyMotionType.Dynamic);

        var rb = hasRigidbody
            ? entity.GetComponent<RigidbodyComponent>()!
            : entity.AddComponent<RigidbodyComponent>();
        rb.MotionType = motionType;

        app.RefreshUndoBaseline();
        NotificationManager.Instance.Post(
            $"Added {label} to {entity.Name}", NotificationType.Info, 1.5f);
        return true;
    }

    /// <summary>
    /// Adds a collider whose shape and size are auto-detected from the entity's primitive component.
    /// Falls back to a unit box collider if no primitive is found.
    /// </summary>
    private static void AddAutoCollider(Entity entity, bool preferMeshCollider)
    {
        if (preferMeshCollider && entity.GetComponent<MeshRendererComponent>() != null)
        {
            entity.AddComponent<MeshColliderComponent>();
            return;
        }

        // Try to match primitive type to collider shape
        var primitive = entity.GetComponent<PrimitiveComponent>();

        switch (primitive)
        {
            case SpherePrimitive sphere:
                var sc = entity.AddComponent<SphereColliderComponent>();
                sc.Radius = sphere.Radius;
                return;

            case CubePrimitive cube:
                var bc = entity.AddComponent<BoxColliderComponent>();
                bc.Size = new Vector3(cube.Width, cube.Height, cube.Depth);
                return;

            case CylinderPrimitive cylinder:
                var cc = entity.AddComponent<CapsuleColliderComponent>();
                cc.Radius = cylinder.Radius;
                cc.Length = cylinder.Height;
                return;

            case PlanePrimitive plane:
                var pc = entity.AddComponent<BoxColliderComponent>();
                pc.Size = new Vector3(plane.Width, 0.01f, plane.Depth);
                return;

            default:
                // No primitive or unknown type: add a default box collider
                entity.AddComponent<BoxColliderComponent>();
                return;
        }
    }

    /// <summary>
    /// Draws an "Add Physics" submenu with static/dynamic/kinematic options.
    /// </summary>
    public static void DrawAddPhysicsMenu(Entity entity, EditorApplication app)
    {
        if (ImGui.MenuItem("Static Body"))
            AddStaticBody(entity, app);

        if (ImGui.MenuItem("Dynamic Body"))
            AddDynamicBody(entity, app);

        if (ImGui.MenuItem("Kinematic Body"))
            AddKinematicBody(entity, app);
    }
}
