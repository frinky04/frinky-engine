using System.Numerics;
using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Rendering;
using FrinkyEngine.Core.Rendering.PostProcessing;
using FrinkyEngine.Core.Rendering.Profiling;
using FrinkyEngine.Core.Serialization;
using FrinkyEngine.Core.CanvasUI;
using FrinkyEngine.Core.UI;
using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Raylib_cs;

namespace FrinkyEngine.Editor.Panels;

public class ViewportPanel
{
    private static readonly Color SelectionOutlineColor = new(255, 170, 0, 255);
    private const float SelectionOutlineWidthPixels = 1.5f;

    private readonly EditorApplication _app;
    private RenderTexture2D _renderTexture;
    private RenderTexture2D _selectionMaskTexture;
    private RenderTexture2D _outlineCompositeTexture;
    private Shader _selectionOutlinePostShader;
    private bool _selectionOutlinePostShaderLoaded;
    private int _texelSizeLoc = -1;
    private int _outlineColorLoc = -1;
    private int _outlineWidthLoc = -1;
    private int _lastDisplayWidth;
    private int _lastDisplayHeight;
    private int _lastScaledWidth;
    private int _lastScaledHeight;
    private bool _isHovered;
    private bool _wasGizmoDragging;
    private System.Numerics.Vector3? _dragPreviewPosition;
    private readonly PostProcessPipeline _postProcessPipeline = new();
    private bool _consoleKeyboardOverrideLogged;
    private bool _consoleFocusOverrideLogged;
    private int _selectedInspectorGizmo = -1;
    private bool _isInspectorGizmoDragging;
    private bool _wasInspectorGizmoDragging;
    private bool _wasColliderEditDragging;
    private List<GizmoTarget>? _cachedGizmoTargets;

    public ViewportPanel(EditorApplication app)
    {
        _app = app;
    }

    public void EnsureRenderTexture(int displayWidth, int displayHeight)
    {
        if (displayWidth <= 0 || displayHeight <= 0) return;

        var (scaledW, scaledH) = _app.IsInRuntimeMode
            ? RenderRuntimeCvars.GetScaledDimensions(displayWidth, displayHeight)
            : (displayWidth, displayHeight);

        bool displayChanged = displayWidth != _lastDisplayWidth || displayHeight != _lastDisplayHeight;
        bool scaledChanged = scaledW != _lastScaledWidth || scaledH != _lastScaledHeight;

        if (!displayChanged && !scaledChanged) return;

        if (scaledChanged)
        {
            if (_lastScaledWidth > 0)
                Raylib.UnloadRenderTexture(_renderTexture);

            _renderTexture = PostProcessPipeline.LoadRenderTextureWithDepthTexture(scaledW, scaledH);
            bool isSupersampled = scaledW >= displayWidth && scaledH >= displayHeight;
            Raylib.SetTextureFilter(_renderTexture.Texture, isSupersampled ? TextureFilter.Bilinear : TextureFilter.Point);
            _lastScaledWidth = scaledW;
            _lastScaledHeight = scaledH;
        }

        if (displayChanged)
        {
            if (_lastDisplayWidth > 0)
            {
                Raylib.UnloadRenderTexture(_selectionMaskTexture);
                Raylib.UnloadRenderTexture(_outlineCompositeTexture);
            }

            _selectionMaskTexture = Raylib.LoadRenderTexture(displayWidth, displayHeight);
            _outlineCompositeTexture = Raylib.LoadRenderTexture(displayWidth, displayHeight);
            Raylib.SetTextureFilter(_selectionMaskTexture.Texture, TextureFilter.Point);
            _lastDisplayWidth = displayWidth;
            _lastDisplayHeight = displayHeight;
        }

        EnsureSelectionOutlineShaderLoaded();
    }

    public void Draw()
    {
        _dragPreviewPosition = null;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.Begin("Viewport"))
        {
            var size = ImGui.GetContentRegionAvail();
            int w = (int)size.X;
            int h = (int)size.Y;

            if (w > 0 && h > 0)
            {
                EnsureRenderTexture(w, h);

                var camera = _app.Mode == EditorMode.Play && _app.CurrentScene?.MainCamera != null
                    ? _app.CurrentScene.MainCamera.BuildCamera3D()
                    : _app.EditorCamera.Camera3D;

                var gizmo = _app.GizmoSystem;
                var selected = _app.SelectedEntity;
                var selectedEntities = _app.SelectedEntities;

                if (_app.CurrentScene != null)
                {
                    bool isEditorMode = _app.CanUseEditorViewportTools && !_app.IsGameViewEnabled;
                    if (!isEditorMode || selectedEntities.Count == 0)
                        _selectedInspectorGizmo = -1;
                    _cachedGizmoTargets = isEditorMode ? EditorGizmos.CollectGizmoTargets(selectedEntities) : null;
                    var physicsHitboxDrawMode = ResolvePhysicsHitboxDrawMode();
                    bool colliderEditMode = isEditorMode && _app.IsColliderEditModeEnabled;
                    var textureToDisplay = _renderTexture;

                    using (FrameProfiler.Scope(ProfileCategory.Rendering))
                    {
                        _app.SceneRenderer.Render(_app.CurrentScene, camera, _renderTexture,
                            () =>
                            {
                                if (colliderEditMode)
                                {
                                    // Grey out the scene, then draw filled + wireframe colliders on top
                                    EditorGizmos.DrawColliderEditOverlay(camera);
                                    EditorGizmos.DrawFilledColliders(_app.CurrentScene, selectedEntities);

                                    // Wireframes should also render in front of scene geometry
                                    Rlgl.DrawRenderBatchActive();
                                    Rlgl.DisableDepthTest();
                                    EditorGizmos.DrawPhysicsHitboxes(_app.CurrentScene, selectedEntities, PhysicsHitboxDrawMode.All);
                                    Rlgl.DrawRenderBatchActive();
                                    Rlgl.EnableDepthTest();
                                }
                                else
                                {
                                    var effectiveHitboxMode = physicsHitboxDrawMode;
                                    if (effectiveHitboxMode != PhysicsHitboxDrawMode.Off)
                                    {
                                        EditorGizmos.DrawPhysicsHitboxes(_app.CurrentScene, selectedEntities, effectiveHitboxMode);
                                    }
                                }

                                if (isEditorMode)
                                {
                                    EditorGizmos.DrawAll(_app.CurrentScene, camera);
                                    EditorGizmos.DrawInspectorGizmos(_cachedGizmoTargets!);
                                    foreach (var selectedEntity in selectedEntities)
                                        EditorGizmos.DrawSelectionFallbackHighlight(selectedEntity);
                                }

                                if (_app.IsBonePreviewEnabled)
                                {
                                    EditorGizmos.DrawBones(_app.CurrentScene);
                                }

                                if (_dragPreviewPosition.HasValue)
                                    DrawDropPreview(_dragPreviewPosition.Value);
                            },
                            isEditorMode: isEditorMode);
                    }

                    // TODO: Move post-processing, selection outline, upscale, and game UI composition into SceneRenderer's render graph.
                    // Post-processing (at scaled resolution)
                    var mainCamEntity = _app.CurrentScene.MainCamera?.Entity;
                    var ppStack = mainCamEntity?.GetComponent<PostProcessStackComponent>();
                    bool runtimePostProcessEnabled = !_app.IsInRuntimeMode || RenderRuntimeCvars.PostProcessingEnabled;
                    if (ppStack != null
                        && runtimePostProcessEnabled
                        && ppStack.PostProcessingEnabled
                        && ppStack.Effects.Count > 0)
                    {
                        _postProcessPipeline.Initialize("Shaders");
                        var depthTexture = _renderTexture.Depth;
                        if (_app.SceneRenderer.TryGetLastViewRenderTexture(out var sceneViewTarget) && sceneViewTarget.Depth.Id != 0)
                            depthTexture = sceneViewTarget.Depth;

                        var postProcessedTex = _postProcessPipeline.Execute(
                            ppStack,
                            _renderTexture.Texture,
                            camera,
                            _app.CurrentScene.MainCamera,
                            _app.SceneRenderer,
                            _app.CurrentScene,
                            _lastScaledWidth, _lastScaledHeight,
                            isEditorMode,
                            depthTexture);

                        // If post-processing produced a different texture, blit it back to _renderTexture
                        if (postProcessedTex.Id != _renderTexture.Texture.Id)
                        {
                            Raylib.BeginTextureMode(_renderTexture);
                            var src = new Rectangle(0, 0, postProcessedTex.Width, -postProcessedTex.Height);
                            var dst = new Rectangle(0, 0, _lastScaledWidth, _lastScaledHeight);
                            Raylib.DrawTexturePro(postProcessedTex, src, dst, Vector2.Zero, 0f, Color.White);
                            Raylib.EndTextureMode();
                        }
                    }

                    if (isEditorMode && selectedEntities.Count > 0)
                    {
                        _app.SceneRenderer.RenderSelectionMask(
                            _app.CurrentScene,
                            camera,
                            selectedEntities,
                            _selectionMaskTexture,
                            isEditorMode: true);

                        if (_selectionOutlinePostShaderLoaded)
                        {
                            CompositeSelectionOutline(w, h);
                            textureToDisplay = _outlineCompositeTexture;
                        }
                    }

                    // When screen percentage is active and no selection outline promoted
                    // to the composite texture, upscale the scene to display resolution
                    // so that Game UI renders at full resolution (crisp text).
                    bool needsUpscale = _app.IsInRuntimeMode
                                        && RenderRuntimeCvars.ScreenPercentage != 100
                                        && textureToDisplay.Id == _renderTexture.Id;
                    if (needsUpscale)
                    {
                        Raylib.BeginTextureMode(_outlineCompositeTexture);
                        Raylib.ClearBackground(new Color(0, 0, 0, 0));
                        var upSrc = new Rectangle(0, 0, _lastScaledWidth, -_lastScaledHeight);
                        var upDst = new Rectangle(0, 0, w, h);
                        Raylib.DrawTexturePro(_renderTexture.Texture, upSrc, upDst, Vector2.Zero, 0f, Color.White);
                        Raylib.EndTextureMode();
                        textureToDisplay = _outlineCompositeTexture;
                    }

                    var imageScreenPos = ImGui.GetCursorScreenPos();
                    RenderGameUiOverlay(textureToDisplay, imageScreenPos, w, h);
                    RlImGui.ImageRenderTexture(textureToDisplay);
                    if (isEditorMode)
                        HandleAssetDropTarget(camera, imageScreenPos, w, h);
                    bool toolbarHovered = false;
                    if (isEditorMode)
                        toolbarHovered = DrawViewportToolbar(gizmo);

                    bool entityGizmoActive = isEditorMode && _selectedInspectorGizmo < 0;

                    // Draw ImGuizmo overlay — skip the entity transform gizmo while an inspector gizmo is selected
                    // In collider edit mode, draw the collider manipulation gizmo instead
                    if (colliderEditMode)
                    {
                        _app.ColliderEditSystem.DrawAndUpdate(camera, selected, imageScreenPos, new Vector2(w, h));
                    }
                    else if (entityGizmoActive)
                    {
                        gizmo.DrawAndUpdate(camera, selectedEntities, selected, imageScreenPos, new Vector2(w, h));
                    }

                    // Draw translate handle for the selected inspector gizmo target (if any).
                    // Do not gate this on gizmo.IsDragging: ImGuizmo.IsUsing() is global state.
                    if (isEditorMode && !colliderEditMode)
                        DrawSelectedInspectorGizmoHandle(camera, _cachedGizmoTargets!, imageScreenPos, new Vector2(w, h));

                    // Viewport picking: left-click selects entity, but gizmo and camera fly take priority
                    _isHovered = ImGui.IsWindowHovered();
                    if (_isHovered && !toolbarHovered && isEditorMode)
                    {
                        bool entityGizmoDragging = entityGizmoActive && gizmo.IsDragging;
                        bool entityGizmoHovered = entityGizmoActive && gizmo.HoveredAxis >= 0;

                        if (Raylib.IsMouseButtonPressed(MouseButton.Left)
                            && !entityGizmoDragging
                            && !entityGizmoHovered
                            && !_isInspectorGizmoDragging
                            && !_app.ColliderEditSystem.IsDragging
                            && !Raylib.IsMouseButtonDown(MouseButton.Right))
                        {
                            var mousePos = ImGui.GetMousePos();
                            var localMouse = mousePos - imageScreenPos;

                            // Check if an inspector gizmo sphere was clicked before doing entity picking
                            int hitGizmo = PickInspectorGizmo(camera, _cachedGizmoTargets!, localMouse, new Vector2(w, h));
                            if (hitGizmo >= 0)
                            {
                                _selectedInspectorGizmo = hitGizmo;
                            }
                            else
                            {
                                _selectedInspectorGizmo = -1;

                                var pickedEntity = colliderEditMode
                                    ? _app.PickingSystem.PickCollider(
                                        _app.CurrentScene, camera, localMouse, new Vector2(w, h))
                                    : _app.PickingSystem.Pick(
                                        _app.CurrentScene, camera, localMouse, new Vector2(w, h));

                                if (ImGui.GetIO().KeyCtrl)
                                {
                                    if (pickedEntity != null)
                                        _app.ToggleSelection(pickedEntity);
                                }
                                else
                                {
                                    _app.SetSingleSelection(pickedEntity);
                                }
                            }
                        }
                    }
                }
                else
                {
                    bool isEditorMode = _app.CanUseEditorViewportTools && !_app.IsGameViewEnabled;
                    var imageScreenPos = ImGui.GetCursorScreenPos();
                    RenderGameUiOverlay(_renderTexture, imageScreenPos, w, h);
                    RlImGui.ImageRenderTexture(_renderTexture);
                    if (isEditorMode)
                        HandleAssetDropTarget(camera, imageScreenPos, w, h);
                    if (isEditorMode)
                        DrawViewportToolbar(gizmo);

                    _isHovered = ImGui.IsWindowHovered();
                }

                // Undo batching for gizmo drags
                _wasGizmoDragging = TrackDragUndo(gizmo.IsDragging, _wasGizmoDragging);
                _wasInspectorGizmoDragging = TrackDragUndo(_isInspectorGizmoDragging, _wasInspectorGizmoDragging);
                _wasColliderEditDragging = TrackDragUndo(_app.ColliderEditSystem.IsDragging, _wasColliderEditDragging);
            }
            else
            {
                _isHovered = ImGui.IsWindowHovered();
            }
        }
        else
        {
            _isHovered = false;
        }
        ImGui.End();
        ImGui.PopStyleVar();

        if (_app.CanUseEditorViewportTools)
            _app.EditorCamera.Update(Raylib.GetFrameTime(), _isHovered);
    }

    private void RenderGameUiOverlay(RenderTexture2D targetTexture, Vector2 imageScreenPos, int width, int height)
    {
        if (!_app.IsInRuntimeMode)
        {
            UI.ClearFrame();
            return;
        }

        var mousePos = ImGui.GetMousePos();
        var localMouse = mousePos - imageScreenPos;
        bool hovered = ImGui.IsWindowHovered();
        bool focused = ImGui.IsWindowFocused();
        bool consoleVisible = EngineOverlays.IsConsoleVisible;
        bool allowKeyboardInput = hovered || consoleVisible;
        bool allowFocusedInput = focused || consoleVisible;

        if (consoleVisible && !hovered && !_consoleKeyboardOverrideLogged)
        {
            FrinkyLog.Info("Viewport UI: keyboard input forced on because developer console is visible.");
            _consoleKeyboardOverrideLogged = true;
        }
        else if ((!consoleVisible || hovered) && _consoleKeyboardOverrideLogged)
        {
            FrinkyLog.Info("Viewport UI: keyboard input returned to hover-gated mode.");
            _consoleKeyboardOverrideLogged = false;
        }

        if (consoleVisible && !focused && !_consoleFocusOverrideLogged)
        {
            FrinkyLog.Info("Viewport UI: focus gating bypassed because developer console is visible.");
            _consoleFocusOverrideLogged = true;
        }
        else if ((!consoleVisible || focused) && _consoleFocusOverrideLogged)
        {
            FrinkyLog.Info("Viewport UI: focus gating returned to window-focus mode.");
            _consoleFocusOverrideLogged = false;
        }

        var frameDesc = new UiFrameDesc(
            width,
            height,
            IsFocused: allowFocusedInput,
            IsHovered: hovered,
            UseMousePositionOverride: true,
            MousePosition: localMouse,
            AllowCursorChanges: false,
            AllowSetMousePos: false,
            AllowKeyboardInput: allowKeyboardInput);

        using (FrameProfiler.Scope(ProfileCategory.UI))
        {
            UI.BeginFrame(Raylib.GetFrameTime(), frameDesc);
            Raylib.BeginTextureMode(targetTexture);
            Rlgl.DrawRenderBatchActive();
            Rlgl.SetBlendMode(BlendMode.Alpha);
            Rlgl.DisableDepthTest();
            // Keep the scene RT alpha opaque so UI text edges do not pick up
            // editor-window background color when this texture is presented in ImGui.
            Rlgl.ColorMask(true, true, true, false);
            UI.EndFrame();
            CanvasUI.Update(Raylib.GetFrameTime(), width, height, localMouse);
            Rlgl.DrawRenderBatchActive();
            Rlgl.ColorMask(true, true, true, true);
            Raylib.EndTextureMode();
        }
    }

    private unsafe void HandleAssetDropTarget(Camera3D camera, Vector2 imageScreenPos, int w, int h)
    {
        if (!ImGui.BeginDragDropTarget())
            return;

        // Peek to update live preview each frame while hovering
        ImGuiPayload* peekPayload = ImGui.AcceptDragDropPayload(AssetBrowserPanel.AssetDragPayload, ImGuiDragDropFlags.AcceptPeekOnly);
        if (peekPayload != null)
        {
            var assetPath = _app.DraggedAssetPath;
            if (!string.IsNullOrEmpty(assetPath))
            {
                var asset = AssetDatabase.Instance.GetAssets()
                    .FirstOrDefault(a => string.Equals(a.RelativePath, assetPath, StringComparison.OrdinalIgnoreCase));

                if (asset != null && asset.Type is AssetType.Prefab or AssetType.Model)
                {
                    var mousePos = ImGui.GetMousePos();
                    var localMouse = mousePos - imageScreenPos;
                    _dragPreviewPosition = ComputeDropWorldPosition(camera, localMouse, new Vector2(w, h));
                }
            }
        }

        // Accept delivery for the actual drop
        ImGuiPayload* payload = ImGui.AcceptDragDropPayload(AssetBrowserPanel.AssetDragPayload);
        if (payload != null && payload->Delivery != 0)
        {
            var assetPath = _app.DraggedAssetPath;
            if (!string.IsNullOrEmpty(assetPath))
            {
                var asset = AssetDatabase.Instance.GetAssets()
                    .FirstOrDefault(a => string.Equals(a.RelativePath, assetPath, StringComparison.OrdinalIgnoreCase));

                if (asset != null)
                {
                    var mousePos = ImGui.GetMousePos();
                    var localMouse = mousePos - imageScreenPos;
                    HandleAssetDrop(asset, camera, localMouse, new Vector2(w, h));
                }
            }

            _dragPreviewPosition = null;
        }

        ImGui.EndDragDropTarget();
    }

    private void HandleAssetDrop(AssetEntry asset, Camera3D camera, Vector2 localMouse, Vector2 viewportSize)
    {
        var dropPos = ComputeDropWorldPosition(camera, localMouse, viewportSize);

        switch (asset.Type)
        {
            case AssetType.Prefab:
                _app.InstantiatePrefabAsset(asset.RelativePath);
                if (_app.SelectedEntity != null)
                    _app.SelectedEntity.Transform.LocalPosition = dropPos;
                break;

            case AssetType.Model:
                if (_app.CurrentScene == null || !_app.CanEditScene)
                    break;
                _app.RecordUndo();
                var name = Path.GetFileNameWithoutExtension(asset.FileName);
                var entity = _app.CurrentScene.CreateEntity(name);
                var meshRenderer = entity.AddComponent<MeshRendererComponent>();
                meshRenderer.ModelPath = AssetDatabase.Instance.GetCanonicalName(asset.RelativePath);
                if (AssetManager.Instance.ModelHasAnimations(meshRenderer.ModelPath.Path, out _))
                {
                    entity.TryAddComponent(typeof(SkinnedMeshAnimatorComponent), out _, out _);
                }
                entity.Transform.LocalPosition = dropPos;
                _app.SetSingleSelection(entity);
                _app.RefreshUndoBaseline();
                NotificationManager.Instance.Post($"Created: {name}", NotificationType.Info, 1.5f);
                break;

            case AssetType.Script:
                HandleScriptDrop(asset, camera, localMouse, viewportSize);
                break;
        }
    }

    private void HandleScriptDrop(AssetEntry asset, Camera3D camera, Vector2 localMouse, Vector2 viewportSize)
    {
        if (_app.CurrentScene == null || !_app.CanEditScene)
            return;

        var pickedEntity = _app.PickingSystem.Pick(_app.CurrentScene, camera, localMouse, viewportSize);
        if (pickedEntity == null)
        {
            NotificationManager.Instance.Post("Drop a script onto an entity.", NotificationType.Warning);
            return;
        }

        var typeName = Path.GetFileNameWithoutExtension(asset.FileName);
        var componentType = ComponentTypeResolver.Resolve(typeName);
        if (componentType == null)
        {
            NotificationManager.Instance.Post("Build scripts first.", NotificationType.Warning);
            return;
        }

        if (pickedEntity.GetComponent(componentType) != null)
        {
            NotificationManager.Instance.Post($"{typeName} already exists on {pickedEntity.Name}.", NotificationType.Warning);
            return;
        }

        _app.RecordUndo();
        if (pickedEntity.TryAddComponent(componentType, out _, out var failureReason))
        {
            _app.SetSingleSelection(pickedEntity);
            _app.RefreshUndoBaseline();
            NotificationManager.Instance.Post($"Added {typeName} to {pickedEntity.Name}", NotificationType.Info, 1.5f);
        }
        else
        {
            NotificationManager.Instance.Post(
                failureReason ?? $"Failed to add {typeName} to {pickedEntity.Name}.",
                NotificationType.Warning);
        }
    }

    private static Vector3 ComputeDropWorldPosition(Camera3D camera, Vector2 localMouse, Vector2 viewportSize)
    {
        var ray = RaycastUtils.GetViewportRay(camera, localMouse, viewportSize);

        // Intersect with Y=0 ground plane
        if (MathF.Abs(ray.Direction.Y) > 1e-6f)
        {
            float t = -ray.Position.Y / ray.Direction.Y;
            if (t > 0)
                return ray.Position + ray.Direction * t;
        }

        // Fallback: 10 units in front of camera
        return ray.Position + ray.Direction * 10f;
    }

    /// <summary>
    /// Draws a single translate-only ImGuizmo handle for the currently selected inspector gizmo target.
    /// </summary>
    private unsafe void DrawSelectedInspectorGizmoHandle(
        Camera3D camera,
        List<GizmoTarget> targets,
        Vector2 viewportScreenPos,
        Vector2 viewportSize)
    {
        _isInspectorGizmoDragging = false;

        if (_selectedInspectorGizmo < 0)
            return;
        if (_selectedInspectorGizmo >= targets.Count)
        {
            _selectedInspectorGizmo = -1;
            return;
        }

        var target = targets[_selectedInspectorGizmo];

        var view = Matrix4x4.CreateLookAt(camera.Position, camera.Target, camera.Up);
        float aspect = viewportSize.X / viewportSize.Y;
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(
            camera.FovY * FrinkyEngine.Core.FrinkyMath.Deg2Rad, aspect, 0.01f, 1000f);

        ImGuizmo.SetRect(viewportScreenPos.X, viewportScreenPos.Y, viewportSize.X, viewportSize.Y);
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetDrawlist();
        ImGuizmo.SetID(1000);

        var objectMatrix = Matrix4x4.CreateTranslation(target.WorldPosition);
        var deltaMatrix = Matrix4x4.Identity;

        bool changed = ImGuizmo.Manipulate(
            (float*)&view, (float*)&proj,
            ImGuizmoOperation.Translate, ImGuizmoMode.World,
            (float*)&objectMatrix, (float*)&deltaMatrix,
            (float*)null, (float*)null, (float*)null);

        _isInspectorGizmoDragging = ImGuizmo.IsUsing();

        if (changed)
        {
            var newWorldPos = new Vector3(objectMatrix.M41, objectMatrix.M42, objectMatrix.M43);

            if (target.IsLocal)
            {
                if (Matrix4x4.Invert(target.Entity.Transform.WorldMatrix, out var inv))
                    newWorldPos = Vector3.Transform(newWorldPos, inv);
            }

            target.Property.SetValue(target.Owner, newWorldPos);
        }

        ImGuizmo.SetID(0);
    }

    /// <summary>
    /// Tests if a mouse click hits any inspector gizmo sphere. Returns the index into the targets
    /// list, or -1 if nothing was hit.
    /// </summary>
    private static int PickInspectorGizmo(
        Camera3D camera,
        List<GizmoTarget> targets,
        Vector2 localMouse,
        Vector2 viewportSize)
    {
        if (targets.Count == 0)
            return -1;

        var ray = RaycastUtils.GetViewportRay(camera, localMouse, viewportSize);

        // Use a generous hit radius so the spheres are easy to click
        const float hitRadius = 0.15f;
        float closestT = float.MaxValue;
        int closestIdx = -1;

        for (int i = 0; i < targets.Count; i++)
        {
            if (RaySphereIntersect(ray, targets[i].WorldPosition, hitRadius, out float t) && t < closestT)
            {
                closestT = t;
                closestIdx = i;
            }
        }

        return closestIdx;
    }

    private static bool RaySphereIntersect(Ray ray, Vector3 center, float radius, out float t)
    {
        t = 0f;
        var oc = ray.Position - center;
        float a = Vector3.Dot(ray.Direction, ray.Direction);
        float b = 2f * Vector3.Dot(oc, ray.Direction);
        float c = Vector3.Dot(oc, oc) - radius * radius;
        float discriminant = b * b - 4f * a * c;
        if (discriminant < 0f)
            return false;

        t = (-b - MathF.Sqrt(discriminant)) / (2f * a);
        if (t < 0f)
            t = (-b + MathF.Sqrt(discriminant)) / (2f * a);
        return t >= 0f;
    }

    private static void DrawDropPreview(System.Numerics.Vector3 pos)
    {
        var previewColor = new Color(255, 220, 50, 200);

        // Flat ring on Y=0 ground plane
        Raylib.DrawCircle3D(
            new System.Numerics.Vector3(pos.X, pos.Y, pos.Z),
            0.5f,
            new System.Numerics.Vector3(1, 0, 0),
            90f,
            previewColor);

        // Vertical line from ground to indicate placement point
        Raylib.DrawLine3D(
            new System.Numerics.Vector3(pos.X, pos.Y, pos.Z),
            new System.Numerics.Vector3(pos.X, pos.Y + 1.0f, pos.Z),
            previewColor);

        // Small cross on the ground
        const float crossSize = 0.3f;
        Raylib.DrawLine3D(
            new System.Numerics.Vector3(pos.X - crossSize, pos.Y, pos.Z),
            new System.Numerics.Vector3(pos.X + crossSize, pos.Y, pos.Z),
            previewColor);
        Raylib.DrawLine3D(
            new System.Numerics.Vector3(pos.X, pos.Y, pos.Z - crossSize),
            new System.Numerics.Vector3(pos.X, pos.Y, pos.Z + crossSize),
            previewColor);
    }

    private static bool DrawViewportToolbar(GizmoSystem gizmo)
    {
        ImGui.SetCursorPos(new Vector2(8, 8));
        bool anyHovered = false;

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 0));

        // Gizmo mode buttons (T/R/S)
        anyHovered |= DrawToolbarToggle("T", gizmo.Mode == GizmoMode.Translate, () => gizmo.Mode = GizmoMode.Translate);
        ImGui.SameLine();
        anyHovered |= DrawToolbarToggle("R", gizmo.Mode == GizmoMode.Rotate, () => gizmo.Mode = GizmoMode.Rotate);
        ImGui.SameLine();
        anyHovered |= DrawToolbarToggle("S", gizmo.Mode == GizmoMode.Scale, () => gizmo.Mode = GizmoMode.Scale);

        // Separator
        ImGui.SameLine(0, 12);
        anyHovered |= DrawToolbarSeparator();

        // Space toggle (World/Local)
        ImGui.SameLine(0, 12);
        var spaceLabel = gizmo.Space == GizmoSpace.World ? "World" : "Local";
        if (ImGui.Button(spaceLabel))
            gizmo.Space = gizmo.Space == GizmoSpace.World ? GizmoSpace.Local : GizmoSpace.World;
        anyHovered |= ImGui.IsItemHovered();

        // Separator
        ImGui.SameLine(0, 12);
        anyHovered |= DrawToolbarSeparator();

        // Snap controls
        ImGui.SameLine(0, 12);
        anyHovered |= DrawSnapToggleAndPreset("Pos", ref gizmo.SnapTranslation, ref gizmo.TranslationSnapValue, GizmoSystem.TranslationSnapPresets);
        ImGui.SameLine(0, 8);
        anyHovered |= DrawSnapToggleAndPreset("Rot", ref gizmo.SnapRotation, ref gizmo.RotationSnapValue, GizmoSystem.RotationSnapPresets);
        ImGui.SameLine(0, 8);
        anyHovered |= DrawSnapToggleAndPreset("Scl", ref gizmo.SnapScale, ref gizmo.ScaleSnapValue, GizmoSystem.ScaleSnapPresets);

        // Separator
        ImGui.SameLine(0, 12);
        anyHovered |= DrawToolbarSeparator();

        // Multi-transform mode
        ImGui.SameLine(0, 12);
        var multiLabel = gizmo.MultiMode == MultiTransformMode.Independent ? "Independent" : "Relative";
        if (ImGui.Button(multiLabel))
            gizmo.MultiMode = gizmo.MultiMode == MultiTransformMode.Independent
                ? MultiTransformMode.Relative
                : MultiTransformMode.Independent;
        anyHovered |= ImGui.IsItemHovered();

        ImGui.PopStyleVar(2);

        return anyHovered;
    }

    private static bool DrawToolbarToggle(string label, bool active, Action onClick)
    {
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
        }

        if (ImGui.Button(label))
            onClick();

        if (active)
            ImGui.PopStyleColor();

        return ImGui.IsItemHovered();
    }

    private static bool DrawToolbarSeparator()
    {
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        float height = ImGui.GetFrameHeight();
        drawList.AddLine(
            new Vector2(pos.X, pos.Y),
            new Vector2(pos.X, pos.Y + height),
            ImGui.GetColorU32(ImGuiCol.Separator));
        ImGui.Dummy(new Vector2(1, height));
        return false;
    }

    private static bool DrawSnapToggleAndPreset(string label, ref bool snapEnabled, ref float snapValue, float[] presets)
    {
        bool hovered = false;

        // Toggle button
        bool wasEnabled = snapEnabled;
        if (wasEnabled)
            ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
        if (ImGui.Button(label))
            snapEnabled = !snapEnabled;
        if (wasEnabled)
            ImGui.PopStyleColor();
        hovered |= ImGui.IsItemHovered();

        // Dropdown for presets
        ImGui.SameLine(0, 2);
        ImGui.PushItemWidth(56);
        var previewText = snapValue.ToString("G4");
        if (ImGui.BeginCombo($"##{label}Snap", previewText, ImGuiComboFlags.NoArrowButton))
        {
            foreach (var preset in presets)
            {
                bool selected = MathF.Abs(preset - snapValue) < 1e-6f;
                if (ImGui.Selectable(preset.ToString("G4"), selected))
                    snapValue = preset;
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        hovered |= ImGui.IsItemHovered();
        ImGui.PopItemWidth();

        return hovered;
    }
  
    private PhysicsHitboxDrawMode ResolvePhysicsHitboxDrawMode()
    {
        if (_app.IsPhysicsHitboxPreviewEnabled)
            return PhysicsHitboxDrawMode.All;

        bool isDefaultEditorView = _app.CanUseEditorViewportTools && !_app.IsGameViewEnabled;
        return isDefaultEditorView ? PhysicsHitboxDrawMode.SelectedOnly : PhysicsHitboxDrawMode.Off;
    }

    private void EnsureSelectionOutlineShaderLoaded()
    {
        if (_selectionOutlinePostShaderLoaded)
            return;

        const string vsPath = "Shaders/selection_outline_post.vs";
        const string fsPath = "Shaders/selection_outline_post.fs";
        if (!File.Exists(vsPath) || !File.Exists(fsPath))
            return;

        _selectionOutlinePostShader = Raylib.LoadShader(vsPath, fsPath);
        if (_selectionOutlinePostShader.Id == 0)
        {
            FrinkyLog.Error("Failed to load selection outline post shader.");
            _selectionOutlinePostShaderLoaded = false;
            return;
        }

        _texelSizeLoc = Raylib.GetShaderLocation(_selectionOutlinePostShader, "texelSize");
        _outlineColorLoc = Raylib.GetShaderLocation(_selectionOutlinePostShader, "outlineColor");
        _outlineWidthLoc = Raylib.GetShaderLocation(_selectionOutlinePostShader, "outlineWidth");
        _selectionOutlinePostShaderLoaded = true;
    }

    private void CompositeSelectionOutline(int displayWidth, int displayHeight)
    {
        if (!_selectionOutlinePostShaderLoaded || _selectionOutlinePostShader.Id == 0)
            return;

        Raylib.BeginTextureMode(_outlineCompositeTexture);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));

        // Pass 1: draw scene upscaled to display resolution (Point filtering gives pixelated look).
        var sceneSrc = new Rectangle(0, 0, _lastScaledWidth, -_lastScaledHeight);
        var sceneDst = new Rectangle(0, 0, displayWidth, displayHeight);
        Raylib.DrawTexturePro(_renderTexture.Texture, sceneSrc, sceneDst, Vector2.Zero, 0f, Color.White);
        Rlgl.DrawRenderBatchActive();

        // Pass 2: draw outline overlay from mask using post shader.
        Raylib.BeginShaderMode(_selectionOutlinePostShader);

        if (_texelSizeLoc >= 0)
        {
            float[] texelSize = { 1.0f / displayWidth, 1.0f / displayHeight };
            Raylib.SetShaderValue(_selectionOutlinePostShader, _texelSizeLoc, texelSize, ShaderUniformDataType.Vec2);
        }

        if (_outlineColorLoc >= 0)
        {
            float[] outlineColor =
            {
                SelectionOutlineColor.R / 255f,
                SelectionOutlineColor.G / 255f,
                SelectionOutlineColor.B / 255f,
                SelectionOutlineColor.A / 255f
            };
            Raylib.SetShaderValue(_selectionOutlinePostShader, _outlineColorLoc, outlineColor, ShaderUniformDataType.Vec4);
        }

        if (_outlineWidthLoc >= 0)
        {
            float[] outlineWidth = { SelectionOutlineWidthPixels };
            Raylib.SetShaderValue(_selectionOutlinePostShader, _outlineWidthLoc, outlineWidth, ShaderUniformDataType.Float);
        }

        DrawFullscreenTexture(_selectionMaskTexture.Texture, displayWidth, displayHeight);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
    }

    private static void DrawFullscreenTexture(Texture2D source, int width, int height)
    {
        var src = new Rectangle(0, 0, width, -height);
        var dst = new Rectangle(0, 0, width, height);
        Raylib.DrawTexturePro(source, src, dst, Vector2.Zero, 0.0f, Color.White);
    }

    public void Shutdown()
    {
        if (_lastScaledWidth > 0)
        {
            Raylib.UnloadRenderTexture(_renderTexture);
            _lastScaledWidth = 0;
            _lastScaledHeight = 0;
        }

        if (_lastDisplayWidth > 0)
        {
            Raylib.UnloadRenderTexture(_selectionMaskTexture);
            Raylib.UnloadRenderTexture(_outlineCompositeTexture);
            _lastDisplayWidth = 0;
            _lastDisplayHeight = 0;
        }

        if (_selectionOutlinePostShaderLoaded)
        {
            Raylib.UnloadShader(_selectionOutlinePostShader);
            _selectionOutlinePostShaderLoaded = false;
        }

        _postProcessPipeline.Shutdown();
    }

    private bool TrackDragUndo(bool isDragging, bool wasDragging)
    {
        bool active = _app.Mode == EditorMode.Edit && isDragging;
        if (active && !wasDragging)
        {
            _app.UndoRedo.BeginBatch(_app.GetSelectedEntityIds());
        }
        else if (!active && wasDragging)
        {
            _app.Prefabs.RecalculateOverridesForScene();
            _app.UndoRedo.EndBatch(_app.CurrentScene, _app.GetSelectedEntityIds());
        }
        return active;
    }
}
