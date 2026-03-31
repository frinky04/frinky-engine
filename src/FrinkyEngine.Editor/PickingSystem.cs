using System.Numerics;
using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.ECS;
using Raylib_cs;

namespace FrinkyEngine.Editor;

public class PickingSystem
{
    private const float IconPickRadius = 0.5f;
    private const float HitDistanceEpsilon = 1e-5f;

    public Entity? Pick(Core.Scene.Scene scene, Camera3D camera, Vector2 mousePos, Vector2 viewportSize)
    {
        var ray = RaycastUtils.GetViewportRay(camera, mousePos, viewportSize);

        Entity? closest = null;
        float closestDist = float.MaxValue;

        foreach (var entity in scene.Entities)
        {
            if (!entity.Active) continue;

            if (TryPickRenderable(ray, entity, ref closestDist, ref closest))
                continue;

            TryPickIcon(ray, entity, ref closestDist, ref closest);
        }

        return closest;
    }

    /// <summary>
    /// Picks entities by raycasting against collider shapes. Entities with both
    /// mesh renderers and colliders are tested against their collider geometry.
    /// Entities with only colliders (no mesh renderer) are also included.
    /// Falls back to normal mesh picking for entities without colliders.
    /// </summary>
    public Entity? PickCollider(Core.Scene.Scene scene, Camera3D camera, Vector2 mousePos, Vector2 viewportSize)
    {
        var ray = RaycastUtils.GetViewportRay(camera, mousePos, viewportSize);

        Entity? closest = null;
        float closestDist = float.MaxValue;

        foreach (var entity in scene.Entities)
        {
            if (!entity.Active) continue;

            var collider = entity.GetComponent<ColliderComponent>();
            if (collider != null && collider.Enabled)
            {
                if (RaycastCollider(ray, collider, out float hitDist)
                    && hitDist > HitDistanceEpsilon
                    && hitDist < closestDist)
                {
                    closestDist = hitDist;
                    closest = entity;
                }
                continue;
            }

            if (TryPickRenderable(ray, entity, ref closestDist, ref closest))
                continue;

            TryPickIcon(ray, entity, ref closestDist, ref closest);
        }

        return closest;
    }

    private static bool TryPickRenderable(Ray ray, Entity entity, ref float closestDist, ref Entity? closest)
    {
        var renderable = entity.GetComponent<RenderableComponent>();
        if (renderable == null || !renderable.Enabled)
            return false;

        var bb = renderable.GetWorldBoundingBox();
        if (!bb.HasValue)
            return true;

        var broadphaseCollision = Raylib.GetRayCollisionBox(ray, bb.Value);
        if (!broadphaseCollision.Hit)
            return true;

        var preciseCollision = renderable.GetWorldRayCollision(ray, out bool hasMeshData, frontFacesOnly: true);
        if (preciseCollision.HasValue)
        {
            float hitDistance = preciseCollision.Value.Distance;
            if (hitDistance > HitDistanceEpsilon && hitDistance < closestDist)
            {
                closestDist = hitDistance;
                closest = entity;
            }
        }
        else if (!hasMeshData)
        {
            float hitDistance = broadphaseCollision.Distance;
            if (hitDistance > HitDistanceEpsilon && hitDistance < closestDist)
            {
                closestDist = hitDistance;
                closest = entity;
            }
        }

        return true;
    }

    private static void TryPickIcon(Ray ray, Entity entity, ref float closestDist, ref Entity? closest)
    {
        var cameraComponent = entity.GetComponent<CameraComponent>();
        var lightComponent = entity.GetComponent<LightComponent>();
        bool hasVisualComponent = (cameraComponent != null && cameraComponent.Enabled)
                               || (lightComponent != null && lightComponent.Enabled);
        if (!hasVisualComponent)
            return;

        var pos = entity.Transform.WorldPosition;
        var collision = Raylib.GetRayCollisionSphere(ray, pos, IconPickRadius);
        if (collision.Hit && collision.Distance > HitDistanceEpsilon && collision.Distance < closestDist)
        {
            closestDist = collision.Distance;
            closest = entity;
        }
    }

    private static bool RaycastCollider(Ray ray, ColliderComponent collider, out float distance)
    {
        distance = float.MaxValue;

        EditorGizmos.TryGetWorldBasis(collider.Entity.Transform, out var position, out var rotation, out var absScale);
        var worldCenter = EditorGizmos.ComputeWorldCenter(collider, position, rotation, absScale);

        switch (collider)
        {
            case BoxColliderComponent box:
                return RaycastBox(ray, worldCenter, rotation, absScale, box.Size, out distance);

            case SphereColliderComponent sphere:
            {
                float radiusScale = MathF.Max(absScale.X, MathF.Max(absScale.Y, absScale.Z));
                float radius = MathF.Max(0.001f, sphere.Radius * radiusScale);
                var collision = Raylib.GetRayCollisionSphere(ray, worldCenter, radius);
                if (collision.Hit)
                {
                    distance = collision.Distance;
                    return true;
                }
                return false;
            }

            case CapsuleColliderComponent capsule:
            {
                float radiusScale = MathF.Max(absScale.X, absScale.Z);
                float radius = MathF.Max(0.001f, capsule.Radius * radiusScale);
                float halfLength = MathF.Max(0.001f, capsule.Length * absScale.Y * 0.5f);
                var up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, rotation));
                var top = worldCenter + up * halfLength;
                var bottom = worldCenter - up * halfLength;
                return RaycastCapsule(ray, top, bottom, radius, out distance);
            }

            case MeshColliderComponent meshCollider:
                return RaycastMeshCollider(ray, meshCollider, out distance);

            default:
                return false;
        }
    }

    private static unsafe bool RaycastMeshCollider(Ray ray, MeshColliderComponent collider, out float distance)
    {
        distance = float.MaxValue;
        if (!EditorGizmos.TryGetMeshColliderModel(collider, out var model, out var worldTransform))
            return false;

        bool hit = false;
        for (int meshIndex = 0; meshIndex < model.MeshCount; meshIndex++)
        {
            var collision = Raylib.GetRayCollisionMesh(ray, model.Meshes[meshIndex], worldTransform);
            if (!collision.Hit || collision.Distance >= distance)
                continue;

            distance = collision.Distance;
            hit = true;
        }

        return hit;
    }

    private static bool RaycastBox(Ray ray, Vector3 center, Quaternion rotation, Vector3 absScale, Vector3 size, out float distance)
    {
        distance = float.MaxValue;

        var halfExtents = new Vector3(
            size.X * absScale.X * 0.5f,
            size.Y * absScale.Y * 0.5f,
            size.Z * absScale.Z * 0.5f);

        // Transform ray into the box's local space (axis-aligned)
        var invRotation = Quaternion.Inverse(rotation);
        var localOrigin = Vector3.Transform(ray.Position - center, invRotation);
        var localDir = Vector3.Transform(ray.Direction, invRotation);

        var bb = new BoundingBox(-halfExtents, halfExtents);
        var localRay = new Ray(localOrigin, localDir);
        var collision = Raylib.GetRayCollisionBox(localRay, bb);
        if (collision.Hit)
        {
            distance = collision.Distance;
            return true;
        }
        return false;
    }

    private static bool RaycastCapsule(Ray ray, Vector3 top, Vector3 bottom, float radius, out float distance)
    {
        distance = float.MaxValue;
        bool hit = false;

        // Test against top sphere
        var topCollision = Raylib.GetRayCollisionSphere(ray, top, radius);
        if (topCollision.Hit && topCollision.Distance < distance)
        {
            distance = topCollision.Distance;
            hit = true;
        }

        // Test against bottom sphere
        var bottomCollision = Raylib.GetRayCollisionSphere(ray, bottom, radius);
        if (bottomCollision.Hit && bottomCollision.Distance < distance)
        {
            distance = bottomCollision.Distance;
            hit = true;
        }

        // Test against cylinder body
        if (RaycastCylinder(ray, bottom, top, radius, out float cylDist) && cylDist < distance)
        {
            distance = cylDist;
            hit = true;
        }

        return hit;
    }

    private static bool RaycastCylinder(Ray ray, Vector3 p1, Vector3 p2, float radius, out float distance)
    {
        distance = float.MaxValue;

        var axis = p2 - p1;
        float axisLenSq = Vector3.Dot(axis, axis);
        if (axisLenSq < 1e-10f)
            return false;

        var dp = ray.Position - p1;
        var dxa = Vector3.Cross(ray.Direction, axis);
        var dpxa = Vector3.Cross(dp, axis);

        float a = Vector3.Dot(dxa, dxa);
        float b = 2f * Vector3.Dot(dxa, dpxa);
        float c = Vector3.Dot(dpxa, dpxa) - radius * radius * axisLenSq;

        float discriminant = b * b - 4f * a * c;
        if (discriminant < 0f)
            return false;

        float sqrtDisc = MathF.Sqrt(discriminant);
        float t0 = (-b - sqrtDisc) / (2f * a);
        float t1 = (-b + sqrtDisc) / (2f * a);

        for (int i = 0; i < 2; i++)
        {
            float t = i == 0 ? t0 : t1;
            if (t < 0f) continue;

            var hitPoint = ray.Position + ray.Direction * t;
            var proj = Vector3.Dot(hitPoint - p1, axis) / axisLenSq;
            if (proj >= 0f && proj <= 1f && t < distance)
            {
                distance = t;
                return true;
            }
        }

        return false;
    }
}
