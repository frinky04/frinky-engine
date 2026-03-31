using System.Numerics;
using FrinkyEngine.Core.Rendering;
using FrinkyEngine.Core.Rendering.Profiling;
using Hexa.NET.ImGui;
using Raylib_cs;

namespace FrinkyEngine.Editor.Panels;

public class PerformancePanel
{
    private const int SmoothingFrames = 30;

    private readonly EditorApplication _app;
    private bool _ignoreEditor = true;

    // Category colors (ABGR packed for ImGui)
    private static readonly uint ColorGame = ImGui.ColorConvertFloat4ToU32(new Vector4(0.30f, 0.78f, 0.30f, 1.0f));
    private static readonly uint ColorGameLate = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 0.45f, 1.0f));
    private static readonly uint ColorPhysics = ImGui.ColorConvertFloat4ToU32(new Vector4(0.30f, 0.50f, 0.90f, 1.0f));
    private static readonly uint ColorAudio = ImGui.ColorConvertFloat4ToU32(new Vector4(0.65f, 0.35f, 0.85f, 1.0f));
    private static readonly uint ColorRendering = ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.60f, 0.20f, 1.0f));
    private static readonly uint ColorSkinning = ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.45f, 0.55f, 1.0f));
    private static readonly uint ColorPostProcessing = ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.90f, 0.25f, 1.0f));
    private static readonly uint ColorUI = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.85f, 0.85f, 1.0f));
    private static readonly uint ColorEditor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.55f, 0.55f, 1.0f));
    private static readonly uint ColorOther = ImGui.ColorConvertFloat4ToU32(new Vector4(0.30f, 0.30f, 0.30f, 1.0f));
    private static readonly uint ColorIdle = ImGui.ColorConvertFloat4ToU32(new Vector4(0.45f, 0.55f, 0.70f, 1.0f));

    private static readonly (string Name, uint Color)[] CategoryInfo =
    [
        ("Game", ColorGame),
        ("Game (Late)", ColorGameLate),
        ("Physics", ColorPhysics),
        ("Audio", ColorAudio),
        ("Rendering", ColorRendering),
        ("Skinning", ColorSkinning),
        ("PostProcessing", ColorPostProcessing),
        ("UI", ColorUI),
        ("Editor", ColorEditor),
    ];

    public bool IsVisible { get; set; } = true;

    public PerformancePanel(EditorApplication app)
    {
        _app = app;
    }

    public void Draw()
    {
        if (!IsVisible) return;

        var flags = ImGuiWindowFlags.NoCollapse
                  | ImGuiWindowFlags.NoFocusOnAppearing
                  | ImGuiWindowFlags.NoNav;

        bool open = IsVisible;
        if (ImGui.Begin("Performance", ref open, flags))
        {
            DrawHeader();
            DrawStackedBarChart();
            DrawBreakdownTable();
            DrawPostProcessDetails();
            DrawGpuStats();
            DrawLightingStats();
            DrawPhysicsStats();
            DrawAudioStats();
            DrawAssetIconStats();
            DrawLegend();
        }
        ImGui.End();

        if (!open) IsVisible = false;
    }

    private void DrawHeader()
    {
        bool enabled = FrameProfiler.Enabled;
        if (ImGui.Checkbox("Enable Profiling", ref enabled))
            FrameProfiler.Enabled = enabled;
        ImGui.SameLine();
        ImGui.Checkbox("Ignore Editor", ref _ignoreEditor);

        // Use Raylib's actual FPS/frame time (accounts for frame limiter wait)
        int fps = Raylib.GetFPS();
        float frameMs = Raylib.GetFrameTime() * 1000f;
        ImGui.TextWrapped($"FPS: {fps}   Frame: {frameMs:F1} ms");

        // Smoothed CPU time from profiler (excludes frame limiter wait)
        var history = FrameProfiler.GetHistory();
        if (history.Length > 0)
        {
            int count = Math.Min(SmoothingFrames, history.Length);
            int start = history.Length - count;
            double cpuSum = 0;
            double cpuMin = double.MaxValue;
            double cpuMax = double.MinValue;
            for (int i = start; i < history.Length; i++)
            {
                double t = GetDisplayedTotalMs(history[i]);
                cpuSum += t;
                if (t > 0 && t < cpuMin) cpuMin = t;
                if (t > cpuMax) cpuMax = t;
            }
            if (cpuMin == double.MaxValue) cpuMin = 0;
            double cpuAvg = cpuSum / count;

            var cpuLabel = _ignoreEditor ? "CPU (no editor)" : "CPU";
            ImGui.TextWrapped($"{cpuLabel}: {cpuAvg:F1} ms  Min: {cpuMin:F1}  Max: {cpuMax:F1}");

            // Smoothed idle time
            var idleBuffer = new double[FrameProfiler.HistorySize];
            int idleCount = FrameProfiler.GetOrderedIdleHistory(idleBuffer);
            if (idleCount > 0)
            {
                int idleSmoothCount = Math.Min(SmoothingFrames, idleCount);
                int idleStart = idleCount - idleSmoothCount;
                double idleSum = 0;
                for (int i = idleStart; i < idleCount; i++)
                    idleSum += idleBuffer[i];
                double idleAvg = idleSum / idleSmoothCount;

                bool isUncapped = RenderRuntimeCvars.TargetFps == 0
                                  && !Raylib.IsWindowState(ConfigFlags.VSyncHint);
                var idleLabel = isUncapped ? "GPU (est)" : "Idle";
                ImGui.TextWrapped($"{idleLabel}: {idleAvg:F1} ms");
            }
        }

        int entityCount = _app.CurrentScene?.Entities.Count ?? 0;
        ImGui.TextWrapped($"Entities: {entityCount}");
        ImGui.Separator();
    }

    private void DrawStackedBarChart()
    {
        var history = FrameProfiler.GetHistory();
        if (history.Length == 0)
        {
            ImGui.TextDisabled("No profiling data yet.");
            return;
        }

        float availWidth = ImGui.GetContentRegionAvail().X;
        float chartHeight = 120f;

        // Fetch idle history for stacking
        var idleBuffer = new double[FrameProfiler.HistorySize];
        int idleCount = FrameProfiler.GetOrderedIdleHistory(idleBuffer);

        // Compute max frame time for Y-axis scale (CPU + idle)
        double maxFrameMs = 16.67; // minimum scale: 60fps line visible
        for (int i = 0; i < history.Length; i++)
        {
            double frameMs = GetDisplayedTotalMs(history[i]);
            if (i < idleCount)
                frameMs += idleBuffer[i];
            if (frameMs > maxFrameMs)
                maxFrameMs = frameMs;
        }
        maxFrameMs *= 1.15; // 15% headroom

        // Reserve region
        var cursorPos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##profilerChart", new Vector2(availWidth, chartHeight));
        bool isHovered = ImGui.IsItemHovered();
        var drawList = ImGui.GetWindowDrawList();

        // Background
        var bgColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.12f, 0.12f, 0.12f, 1.0f));
        drawList.AddRectFilled(cursorPos, cursorPos + new Vector2(availWidth, chartHeight), bgColor);

        // Draw bars
        float barWidth = availWidth / Math.Max(1, history.Length);
        int hoveredFrame = -1;

        for (int i = 0; i < history.Length; i++)
        {
            float x = cursorPos.X + i * barWidth;
            float baseY = cursorPos.Y + chartHeight;
            var snap = history[i];

            // Draw each category as a stacked segment
            float yOffset = 0f;
            for (int c = 0; c < (int)ProfileCategory.Count; c++)
            {
                var category = (ProfileCategory)c;
                if (!ShouldShowCategory(category))
                    continue;

                double ms = GetDisplayedCategoryMs(snap, category);
                if (ms <= 0) continue;

                float segmentHeight = (float)(ms / maxFrameMs * chartHeight);
                float top = baseY - yOffset - segmentHeight;
                float bottom = baseY - yOffset;

                drawList.AddRectFilled(
                    new Vector2(x, top),
                    new Vector2(x + Math.Max(barWidth - 1, 1), bottom),
                    CategoryInfo[c].Color);

                yOffset += segmentHeight;
            }

            // "Other" segment
            double otherMs = GetDisplayedOtherMs(snap);
            if (otherMs > 0)
            {
                float segmentHeight = (float)(otherMs / maxFrameMs * chartHeight);
                float top = baseY - yOffset - segmentHeight;
                float bottom = baseY - yOffset;
                drawList.AddRectFilled(
                    new Vector2(x, top),
                    new Vector2(x + Math.Max(barWidth - 1, 1), bottom),
                    ColorOther);
                yOffset += segmentHeight;
            }

            // Idle segment (GPU sync + frame limiter)
            if (i < idleCount && idleBuffer[i] > 0)
            {
                float segmentHeight = (float)(idleBuffer[i] / maxFrameMs * chartHeight);
                float top = baseY - yOffset - segmentHeight;
                float bottom = baseY - yOffset;
                drawList.AddRectFilled(
                    new Vector2(x, top),
                    new Vector2(x + Math.Max(barWidth - 1, 1), bottom),
                    ColorIdle);
            }
        }

        // Reference lines
        DrawReferenceLine(drawList, cursorPos, availWidth, chartHeight, maxFrameMs, 16.67, "60 FPS");
        DrawReferenceLine(drawList, cursorPos, availWidth, chartHeight, maxFrameMs, 33.33, "30 FPS");

        // Hover tooltip
        if (isHovered)
        {
            var mousePos = ImGui.GetMousePos();
            hoveredFrame = (int)((mousePos.X - cursorPos.X) / barWidth);
            hoveredFrame = Math.Clamp(hoveredFrame, 0, history.Length - 1);

            var snap = history[hoveredFrame];
            double displayedTotalMs = GetDisplayedTotalMs(snap);
            ImGui.BeginTooltip();
            ImGui.Text($"Frame {hoveredFrame}  Total: {displayedTotalMs:F2} ms");
            ImGui.Separator();
            for (int c = 0; c < (int)ProfileCategory.Count; c++)
            {
                var category = (ProfileCategory)c;
                if (!ShouldShowCategory(category))
                    continue;

                double ms = GetDisplayedCategoryMs(snap, category);
                if (ms > 0.001)
                {
                    double pct = displayedTotalMs > 0 ? ms / displayedTotalMs * 100.0 : 0;
                    ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(CategoryInfo[c].Color),
                        $"  {CategoryInfo[c].Name}: {ms:F2} ms ({pct:F1}%%)");
                }
            }
            double otherMs = GetDisplayedOtherMs(snap);
            if (otherMs > 0.001)
            {
                double pct = displayedTotalMs > 0 ? otherMs / displayedTotalMs * 100.0 : 0;
                ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(ColorOther),
                    $"  Other: {otherMs:F2} ms ({pct:F1}%%)");
            }
            if (hoveredFrame < idleCount && idleBuffer[hoveredFrame] > 0.001)
            {
                bool isUncapped = RenderRuntimeCvars.TargetFps == 0
                                  && !Raylib.IsWindowState(ConfigFlags.VSyncHint);
                var idleLabel = isUncapped ? "GPU (est)" : "Idle";
                ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(ColorIdle),
                    $"  {idleLabel}: {idleBuffer[hoveredFrame]:F2} ms");
            }
            ImGui.EndTooltip();

            // Highlight the hovered bar
            float hx = cursorPos.X + hoveredFrame * barWidth;
            var highlightColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.15f));
            drawList.AddRectFilled(
                new Vector2(hx, cursorPos.Y),
                new Vector2(hx + barWidth, cursorPos.Y + chartHeight),
                highlightColor);
        }
    }

    private static void DrawReferenceLine(ImDrawListPtr drawList, Vector2 origin, float width, float height, double maxMs, double targetMs, string label)
    {
        if (targetMs > maxMs) return;

        float y = origin.Y + height - (float)(targetMs / maxMs * height);
        var lineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.25f));
        var textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.5f));

        drawList.AddLine(new Vector2(origin.X, y), new Vector2(origin.X + width, y), lineColor);
        drawList.AddText(new Vector2(origin.X + 4, y - 14), textColor, label);
    }

    private void DrawBreakdownTable()
    {
        var history = FrameProfiler.GetHistory();
        if (history.Length == 0) return;

        // Average over last N frames for stable readout
        int count = Math.Min(SmoothingFrames, history.Length);
        int start = history.Length - count;
        Span<double> avgCategoryMs = stackalloc double[(int)ProfileCategory.Count];
        double avgTotalMs = 0;
        double avgOtherMs = 0;

        // Idle averaging
        var tableIdleBuffer = new double[FrameProfiler.HistorySize];
        int tableIdleCount = FrameProfiler.GetOrderedIdleHistory(tableIdleBuffer);
        double avgIdleMs = 0;

        for (int i = start; i < history.Length; i++)
        {
            var snap = history[i];
            avgTotalMs += GetDisplayedTotalMs(snap);
            avgOtherMs += GetDisplayedOtherMs(snap);
            if (i < tableIdleCount)
                avgIdleMs += tableIdleBuffer[i];
            for (int c = 0; c < (int)ProfileCategory.Count; c++)
            {
                var category = (ProfileCategory)c;
                if (!ShouldShowCategory(category))
                    continue;

                avgCategoryMs[c] += GetDisplayedCategoryMs(snap, category);
            }
        }
        avgTotalMs /= count;
        avgOtherMs /= count;
        avgIdleMs /= count;
        for (int c = 0; c < (int)ProfileCategory.Count; c++)
            avgCategoryMs[c] /= count;

        if (avgTotalMs <= 0) return;

        ImGui.Separator();
        if (ImGui.BeginTable("##breakdown", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Time (ms)", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("% Frame", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableHeadersRow();

            for (int c = 0; c < (int)ProfileCategory.Count; c++)
            {
                var category = (ProfileCategory)c;
                if (!ShouldShowCategory(category))
                    continue;

                double ms = avgCategoryMs[c];
                if (ms < 0.001) continue;
                double pct = ms / avgTotalMs * 100.0;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                // Color indicator
                var color = CategoryInfo[c].Color;
                var pos = ImGui.GetCursorScreenPos();
                var dl = ImGui.GetWindowDrawList();
                float sz = ImGui.GetTextLineHeight() - 2;
                dl.AddRectFilled(pos, pos + new Vector2(sz, sz), color, 2f);
                ImGui.Dummy(new Vector2(sz + 4, sz));
                ImGui.SameLine();
                ImGui.Text(CategoryInfo[c].Name);

                ImGui.TableNextColumn();
                ImGui.Text($"{ms:F2}");
                ImGui.TableNextColumn();
                ImGui.Text($"{pct:F1}%%");
            }

            // Other row
            if (avgOtherMs > 0.001)
            {
                double pct = avgOtherMs / avgTotalMs * 100.0;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var pos = ImGui.GetCursorScreenPos();
                var dl = ImGui.GetWindowDrawList();
                float sz = ImGui.GetTextLineHeight() - 2;
                dl.AddRectFilled(pos, pos + new Vector2(sz, sz), ColorOther, 2f);
                ImGui.Dummy(new Vector2(sz + 4, sz));
                ImGui.SameLine();
                ImGui.Text("Other");
                ImGui.TableNextColumn();
                ImGui.Text($"{avgOtherMs:F2}");
                ImGui.TableNextColumn();
                ImGui.Text($"{pct:F1}%%");
            }

            // Idle / GPU (est) row
            if (avgIdleMs > 0.001)
            {
                bool isUncapped = RenderRuntimeCvars.TargetFps == 0
                                  && !Raylib.IsWindowState(ConfigFlags.VSyncHint);
                var idleLabel = isUncapped ? "GPU (est.)" : "Idle";
                double pct = avgTotalMs > 0 ? avgIdleMs / (avgTotalMs + avgIdleMs) * 100.0 : 0;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var pos = ImGui.GetCursorScreenPos();
                var dl = ImGui.GetWindowDrawList();
                float sz = ImGui.GetTextLineHeight() - 2;
                dl.AddRectFilled(pos, pos + new Vector2(sz, sz), ColorIdle, 2f);
                ImGui.Dummy(new Vector2(sz + 4, sz));
                ImGui.SameLine();
                ImGui.Text(idleLabel);
                ImGui.TableNextColumn();
                ImGui.Text($"{avgIdleMs:F2}");
                ImGui.TableNextColumn();
                ImGui.Text($"{pct:F1}%%");
            }

            ImGui.EndTable();
        }
    }

    private static void DrawPostProcessDetails()
    {
        var history = FrameProfiler.GetHistory();
        if (history.Length == 0) return;

        // Find latest frame with sub-timings for effect names
        var latest = FrameProfiler.GetLatest();
        if (latest.SubTimings == null || latest.SubTimings.Length == 0) return;

        if (ImGui.CollapsingHeader("Post-Process Effects"))
        {
            // Average sub-timings over last N frames
            int count = Math.Min(SmoothingFrames, history.Length);
            int start = history.Length - count;
            var avgByName = new Dictionary<string, double>();
            var nameOrder = new List<string>();

            for (int i = start; i < history.Length; i++)
            {
                var snap = history[i];
                if (snap.SubTimings == null) continue;
                foreach (var sub in snap.SubTimings)
                {
                    if (!avgByName.ContainsKey(sub.Name))
                    {
                        avgByName[sub.Name] = 0;
                        nameOrder.Add(sub.Name);
                    }
                    avgByName[sub.Name] += sub.ElapsedMs;
                }
            }

            if (avgByName.Count == 0) return;
            foreach (var name in nameOrder)
                avgByName[name] /= count;

            if (ImGui.BeginTable("##ppeffects", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Effect", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Time (ms)", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableHeadersRow();

                foreach (var name in nameOrder)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(name);
                    ImGui.TableNextColumn();
                    ImGui.Text($"{avgByName[name]:F3}");
                }

                ImGui.EndTable();
            }
        }
    }

    private void DrawGpuStats()
    {
        if (ImGui.CollapsingHeader("GPU Stats"))
        {
            var latest = FrameProfiler.GetLatest();
            var instancing = _app.SceneRenderer.GetAutoInstancingFrameStats();
            var diagnostics = _app.SceneRenderer.LastViewDiagnostics;
            ImGui.Text($"GPU: {FrameProfiler.GpuRenderer}");
            ImGui.Text($"Vendor: {FrameProfiler.GpuVendor}");
            ImGui.Text($"Draw Calls: {_app.SceneRenderer.LastFrameDrawCallCount}");
            ImGui.Text($"Skinned Meshes: {_app.SceneRenderer.LastFrameSkinnedMeshCount}");
            ImGui.Text($"Visible Objects: {diagnostics.VisibleRenderObjects}/{diagnostics.ActiveRenderObjects}");
            ImGui.Text($"Culled Objects: {diagnostics.CulledRenderObjects}");
            ImGui.Text($"Auto-Instancing: {(instancing.Enabled ? "On" : "Off")}");
            ImGui.Text($"Instanced Batches: {instancing.InstancedBatchCount}/{instancing.BatchCount}");
            ImGui.Text($"Instanced Instances: {instancing.InstancedInstanceCount}");
            ImGui.Text($"Instanced Mesh Draws: {instancing.InstancedMeshDrawCalls}");
            ImGui.Text($"Fallback Singles: {instancing.FallbackDrawCount}");
            ImGui.Text($"RT Memory: {diagnostics.RenderTargetMemoryBytes / (1024f * 1024f):F2} MB");
            ImGui.Text($"PP Passes: {latest.GpuStats.PostProcessPasses}");
        }
    }

    private void DrawLightingStats()
    {
        var lightStats = _app.SceneRenderer.GetForwardPlusFrameStats();
        if (!lightStats.Valid) return;

        if (ImGui.CollapsingHeader("Lighting (Forward+)"))
        {
            ImGui.Text($"Scene: {lightStats.SceneLights}  Visible: {lightStats.VisibleLights}");
            ImGui.Text($"Dir: {lightStats.DirectionalLights}  Pt: {lightStats.PointLights}  Sky: {lightStats.Skylights}");
            ImGui.Text($"Assigned: {lightStats.AssignedLights}/{lightStats.MaxLights}");
            ImGui.Text($"Peak/tile: {lightStats.PeakLightsPerTile}/{lightStats.MaxLightsPerTile}  Avg: {lightStats.AverageLightsPerTile:F1}");

            if (lightStats.ClippedLights > 0)
                ImGui.TextColored(new Vector4(1, 0.7f, 0.3f, 1), $"Clipped: {lightStats.ClippedLights}");
            if (lightStats.DroppedTileLinks > 0)
                ImGui.TextColored(new Vector4(1, 0.7f, 0.3f, 1), $"Dropped tile links: {lightStats.DroppedTileLinks}");
        }
    }

    private void DrawPhysicsStats()
    {
        var physStats = _app.CurrentScene?.GetPhysicsFrameStats() ?? default;
        if (!physStats.Valid) return;

        if (ImGui.CollapsingHeader("Physics"))
        {
            ImGui.Text($"Dyn: {physStats.DynamicBodies}  Kin: {physStats.KinematicBodies}  Static: {physStats.StaticBodies}");
            ImGui.Text($"CC: {physStats.ActiveCharacterControllers}");
            ImGui.Text($"Substeps: {physStats.SubstepsThisFrame}  Step: {physStats.StepTimeMs:F2} ms");
        }
    }

    private void DrawAudioStats()
    {
        var audioStats = _app.CurrentScene?.GetAudioFrameStats() ?? default;
        if (!audioStats.Valid) return;

        if (ImGui.CollapsingHeader("Audio"))
        {
            ImGui.Text($"Voices: {audioStats.ActiveVoices}  Streaming: {audioStats.StreamingVoices}");
            ImGui.Text($"Stolen: {audioStats.StolenVoicesThisFrame}  Virtual: {audioStats.VirtualizedVoices}");
            ImGui.Text($"Update: {audioStats.UpdateTimeMs:F2} ms");
        }
    }

    private void DrawAssetIconStats()
    {
        var icons = _app.AssetIcons;
        if (ImGui.CollapsingHeader("Asset Icons"))
        {
            ImGui.Text($"Queue: {icons.QueueLength}  Eligible: {icons.EligibleAssetCount}  Loaded: {icons.LoadedIconCount}");
            ImGui.Text($"Generated: {icons.TotalGenerated}  Failed: {icons.TotalFailed}  Cache Hits: {icons.CacheHits}");
            ImGui.Text($"Last Generation: {icons.LastGenerationMs:F1} ms");
            int totalLoads = icons.TotalGenerated + icons.CacheHits;
            double hitRate = totalLoads > 0 ? icons.CacheHits / (double)totalLoads * 100.0 : 0;
            ImGui.Text($"Cache Hit Rate: {hitRate:F1}%%");
        }
    }

    private void DrawLegend()
    {
        ImGui.Separator();
        var drawList = ImGui.GetWindowDrawList();
        float sz = ImGui.GetTextLineHeight() - 2;

        bool drewAnyCategory = false;
        for (int c = 0; c < CategoryInfo.Length; c++)
        {
            var category = (ProfileCategory)c;
            if (!ShouldShowCategory(category))
                continue;

            if (drewAnyCategory)
                ImGui.SameLine(0, 12);
            var pos = ImGui.GetCursorScreenPos();
            drawList.AddRectFilled(pos + new Vector2(0, 1), pos + new Vector2(sz, sz + 1), CategoryInfo[c].Color, 2f);
            ImGui.Dummy(new Vector2(sz + 2, sz));
            ImGui.SameLine(0, 2);
            ImGui.Text(CategoryInfo[c].Name);
            drewAnyCategory = true;
        }

        // "Other" entry
        if (drewAnyCategory)
            ImGui.SameLine(0, 12);
        var otherPos = ImGui.GetCursorScreenPos();
        drawList.AddRectFilled(otherPos + new Vector2(0, 1), otherPos + new Vector2(sz, sz + 1), ColorOther, 2f);
        ImGui.Dummy(new Vector2(sz + 2, sz));
        ImGui.SameLine(0, 2);
        ImGui.Text("Other");

        // "Idle" / "GPU (est)" entry
        ImGui.SameLine(0, 12);
        bool isUncapped = RenderRuntimeCvars.TargetFps == 0
                          && !Raylib.IsWindowState(ConfigFlags.VSyncHint);
        var idlePos = ImGui.GetCursorScreenPos();
        drawList.AddRectFilled(idlePos + new Vector2(0, 1), idlePos + new Vector2(sz, sz + 1), ColorIdle, 2f);
        ImGui.Dummy(new Vector2(sz + 2, sz));
        ImGui.SameLine(0, 2);
        ImGui.Text(isUncapped ? "GPU (est)" : "Idle");
    }

    private double GetDisplayedTotalMs(in FrameSnapshot snap)
    {
        if (!_ignoreEditor)
            return snap.TotalFrameMs;

        return Math.Max(0, snap.TotalFrameMs - snap.GetCategoryMs(ProfileCategory.Editor));
    }

    private double GetDisplayedCategoryMs(in FrameSnapshot snap, ProfileCategory category)
    {
        if (_ignoreEditor && category == ProfileCategory.Editor)
            return 0;

        return snap.GetCategoryMs(category);
    }

    private double GetDisplayedOtherMs(in FrameSnapshot snap)
    {
        return snap.OtherMs;
    }

    private bool ShouldShowCategory(ProfileCategory category)
    {
        return !_ignoreEditor || category != ProfileCategory.Editor;
    }
}
