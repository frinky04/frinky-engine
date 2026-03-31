using System.Numerics;
using FrinkyEngine.Core.Components;

namespace FrinkyEngine.Core.Rendering;

internal sealed class RenderVisibility
{
    private readonly List<Plane> _frustumPlanes = new(6);

    public RenderVisibleSet Cull(RenderFrame frame, RenderViewRequest request)
    {
        BuildFrustumPlanes(request);

        var visible = new List<RenderObject>(frame.Objects.Count);
        var selected = new List<RenderObject>();
        HashSet<Guid>? selectedEntityIds = request.SelectedEntities != null && request.SelectedEntities.Count > 0
            ? request.SelectedEntities.Select(static x => x.Id).ToHashSet()
            : null;

        foreach (var renderObject in frame.Objects)
        {
            if (!IntersectsFrustum(renderObject.WorldBounds))
                continue;

            visible.Add(renderObject);
            if (selectedEntityIds != null && selectedEntityIds.Contains(renderObject.Entity.Id))
                selected.Add(renderObject);
        }

        return new RenderVisibleSet
        {
            VisibleObjects = visible,
            SelectedObjects = selected,
            VisibleObjectCount = visible.Count,
            CulledObjectCount = Math.Max(0, frame.ActiveRenderObjectCount - visible.Count)
        };
    }

    private void BuildFrustumPlanes(RenderViewRequest request)
    {
        _frustumPlanes.Clear();

        float aspect = request.EffectiveRenderWidth / (float)Math.Max(1, request.EffectiveRenderHeight);
        var camera = request.Camera;
        var view = Matrix4x4.CreateLookAt(camera.Position, camera.Target, camera.Up);

        float nearPlane = request.CameraComponent?.NearPlane ?? 0.1f;
        float farPlane = request.CameraComponent?.FarPlane ?? 1000f;

        Matrix4x4 projection;
        if (camera.Projection == Raylib_cs.CameraProjection.Perspective)
        {
            float fovY = MathF.Max(1e-4f, camera.FovY * (MathF.PI / 180f));
            projection = Matrix4x4.CreatePerspectiveFieldOfView(fovY, MathF.Max(1e-4f, aspect), nearPlane, farPlane);
        }
        else
        {
            float halfHeight = MathF.Max(0.01f, camera.FovY * 0.5f);
            float halfWidth = halfHeight * MathF.Max(1e-4f, aspect);
            projection = Matrix4x4.CreateOrthographic(halfWidth * 2f, halfHeight * 2f, nearPlane, farPlane);
        }

        var viewProjection = view * projection;
        AddPlane(new Plane(
            viewProjection.M14 + viewProjection.M11,
            viewProjection.M24 + viewProjection.M21,
            viewProjection.M34 + viewProjection.M31,
            viewProjection.M44 + viewProjection.M41));
        AddPlane(new Plane(
            viewProjection.M14 - viewProjection.M11,
            viewProjection.M24 - viewProjection.M21,
            viewProjection.M34 - viewProjection.M31,
            viewProjection.M44 - viewProjection.M41));
        AddPlane(new Plane(
            viewProjection.M14 + viewProjection.M12,
            viewProjection.M24 + viewProjection.M22,
            viewProjection.M34 + viewProjection.M32,
            viewProjection.M44 + viewProjection.M42));
        AddPlane(new Plane(
            viewProjection.M14 - viewProjection.M12,
            viewProjection.M24 - viewProjection.M22,
            viewProjection.M34 - viewProjection.M32,
            viewProjection.M44 - viewProjection.M42));
        AddPlane(new Plane(
            viewProjection.M13,
            viewProjection.M23,
            viewProjection.M33,
            viewProjection.M43));
        AddPlane(new Plane(
            viewProjection.M14 - viewProjection.M13,
            viewProjection.M24 - viewProjection.M23,
            viewProjection.M34 - viewProjection.M33,
            viewProjection.M44 - viewProjection.M43));
    }

    private void AddPlane(Plane plane)
    {
        float length = plane.Normal.Length();
        if (length <= 1e-6f)
            return;

        _frustumPlanes.Add(new Plane(plane.Normal / length, plane.D / length));
    }

    private bool IntersectsFrustum(Raylib_cs.BoundingBox bounds)
    {
        foreach (var plane in _frustumPlanes)
        {
            var positive = new Vector3(
                plane.Normal.X >= 0f ? bounds.Max.X : bounds.Min.X,
                plane.Normal.Y >= 0f ? bounds.Max.Y : bounds.Min.Y,
                plane.Normal.Z >= 0f ? bounds.Max.Z : bounds.Min.Z);

            if (Vector3.Dot(plane.Normal, positive) + plane.D < 0f)
                return false;
        }

        return true;
    }
}
