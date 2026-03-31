using System.Numerics;
using FrinkyEngine.Core.Components;
using Raylib_cs;

namespace FrinkyEngine.Core.Rendering;

internal sealed class RenderExtraction
{
    private readonly RenderResourceCache _resources;
    private ulong _frameToken;

    public RenderExtraction(RenderResourceCache resources)
    {
        _resources = resources;
    }

    public RenderFrame Extract(Core.Scene.Scene scene, bool isEditorMode)
    {
        _frameToken++;

        var objects = new List<RenderObject>(scene.Renderables.Count);
        int skinnedObjects = 0;

        foreach (var renderable in scene.Renderables)
        {
            if (!ShouldIncludeRenderable(renderable, isEditorMode))
                continue;

            var meshHandle = _resources.ResolveMeshHandle(renderable);
            if (!meshHandle.IsValid)
                continue;

            var materialHandle = _resources.ResolveMaterialHandle(renderable);
            if (!materialHandle.IsValid)
                continue;

            var bounds = _resources.GetWorldBoundingBox(renderable);
            if (!bounds.HasValue)
                continue;

            bool usesSkinning = false;
            SkinPaletteHandle skinPaletteHandle = default;

            var animator = renderable.Entity.GetComponent<SkinnedMeshAnimatorComponent>();
            if (animator != null && animator.Enabled)
            {
                animator.PrepareForRender(_frameToken);
                usesSkinning = animator.UsesSkinning;
                skinPaletteHandle = animator.CurrentSkinPaletteHandle;
                if (usesSkinning)
                    skinnedObjects++;
            }

            objects.Add(new RenderObject(
                renderable.Entity,
                renderable,
                meshHandle,
                materialHandle,
                renderable.Entity.Transform.WorldMatrix,
                bounds.Value,
                usesSkinning,
                skinPaletteHandle,
                SupportsInstancing: !usesSkinning));
        }

        var lights = new List<RenderLight>(scene.Lights.Count);
        foreach (var light in scene.Lights)
        {
            if (!light.Entity.Active || !light.Enabled)
                continue;
            if (light.EditorOnly && !isEditorMode)
                continue;

            var direction = light.Entity.Transform.Forward;
            if (direction.LengthSquared() > 1e-8f)
                direction = Vector3.Normalize(direction);
            else
                direction = -Vector3.UnitY;

            lights.Add(new RenderLight(
                light.Entity,
                light.LightType,
                light.Entity.Transform.WorldPosition,
                direction,
                new Vector3(light.LightColor.R / 255f, light.LightColor.G / 255f, light.LightColor.B / 255f),
                light.Intensity,
                light.Range,
                light.EditorOnly));
        }

        return new RenderFrame(
            _frameToken,
            objects,
            lights,
            activeRenderObjectCount: objects.Count,
            skinnedObjectCount: skinnedObjects);
    }

    private static bool ShouldIncludeRenderable(RenderableComponent renderable, bool isEditorMode)
    {
        if (!renderable.Entity.Active)
            return false;
        if (!renderable.Enabled)
            return false;
        if (renderable.EditorOnly && !isEditorMode)
            return false;
        return true;
    }
}
