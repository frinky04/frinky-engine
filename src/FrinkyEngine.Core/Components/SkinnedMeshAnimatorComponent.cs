using System.Buffers;
using System.Numerics;
using System.Linq;
using FrinkyEngine.Core.Animation.IK;
using FrinkyEngine.Core.Assets;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Rendering;
using Raylib_cs;

namespace FrinkyEngine.Core.Components;

/// <summary>
/// Plays skeletal animation clips for a sibling <see cref="MeshRendererComponent"/> using GPU skinning.
/// Supports frame interpolation, multiple animation sources, and can preview in editor viewport rendering.
/// </summary>
[ComponentCategory("Rendering")]
[ComponentDisplayName("Skinned Mesh Animator")]
public sealed unsafe class SkinnedMeshAnimatorComponent : Component
{
    private const float DefaultAnimationFps = 60f;

    private MeshRendererComponent? _meshRenderer;
    private int _lastModelVersion;
    private unsafe ModelAnimation* _animations;
    private int _animationCount;

    // Multi-source aggregated animation entries
    private readonly List<AggregatedClip> _aggregatedClips = new();
    private int _lastSourcesHash;
    private int _lastAssetGeneration;

    /// <summary>
    /// Tracks a single animation clip loaded from a specific source file.
    /// </summary>
    private readonly struct AggregatedClip
    {
        public readonly string SourcePath;
        public readonly int SourceLocalIndex;
        public readonly string ClipName;
        public readonly ModelAnimation* Pointer;
        public readonly bool IsValid;

        public AggregatedClip(string sourcePath, int sourceLocalIndex, string clipName, ModelAnimation* pointer, bool isValid)
        {
            SourcePath = sourcePath;
            SourceLocalIndex = sourceLocalIndex;
            ClipName = clipName;
            Pointer = pointer;
            IsValid = isValid;
        }
    }

    private int _clipIndex;
    private float _playheadFrames;
    private double _lastSampleTime = -1d;
    private bool _playbackInitialized;
    private ulong _lastPreparedRenderToken;

    private Matrix4x4[][]? _bindPose;
    private Matrix4x4[][]? _poseFrameA;
    private Matrix4x4[][]? _poseFrameB;
    private Matrix4x4[][]? _poseLerped;
    private bool _hasSkinnedMeshes;
    private SkinPaletteHandle _currentSkinPaletteHandle;

    // Current model-space bone transforms, updated each PrepareForRender for bone preview.
    private (Vector3 t, Quaternion r, Vector3 s)[]? _currentModelPose;

    // IK pose buffers (allocated on demand when IK component is present)
    private (Vector3 t, Quaternion r, Vector3 s)[]? _ikModelPoseA;
    private (Vector3 t, Quaternion r, Vector3 s)[]? _ikModelPoseB;
    private (Vector3 t, Quaternion r, Vector3 s)[]? _ikModelPose;
    private (Vector3 t, Quaternion r, Vector3 s)[]? _ikLocalPoseA;
    private (Vector3 t, Quaternion r, Vector3 s)[]? _ikLocalPoseB;
    private (Vector3 t, Quaternion r, Vector3 s)[]? _ikLocalPose;
    private int[]? _ikParentIndices;
    private Matrix4x4[]? _ikWorldMatrices;

    /// <summary>
    /// Additional .glb files to load animation clips from. When empty, animations are loaded
    /// from the mesh file only. When populated, animations are loaded from all listed sources
    /// and merged into the available clip list. Each source is validated against the model's
    /// skeleton at load time.
    /// </summary>
    [AssetFilter(AssetType.Model)]
    [InspectorOnChanged(nameof(OnAnimationSourcesChanged))]
    public List<AssetReference> AnimationSources { get; set; } = new();

    /// <summary>
    /// Whether to load animation clips embedded in the mesh file.
    /// When <c>true</c> (default), clips from the mesh file are included in the available clip list.
    /// When <c>false</c>, only clips from <see cref="AnimationSources"/> are used.
    /// </summary>
    public bool UseEmbeddedAnimations { get; set; } = true;

    /// <summary>
    /// Whether playback starts automatically once a valid clip is available.
    /// </summary>
    public bool PlayAutomatically { get; set; } = true;

    /// <summary>
    /// Whether playback loops when reaching the end of the selected clip.
    /// </summary>
    public bool Loop { get; set; } = true;

    /// <summary>
    /// Whether playback advances over time.
    /// </summary>
    public bool Playing { get; set; } = true;

    /// <summary>
    /// Playback speed multiplier where 1.0 is normal speed.
    /// </summary>
    [InspectorRange(0f, 4f, 0.01f)]
    public float PlaybackSpeed { get; set; } = 1f;

    /// <summary>
    /// Animation sample rate in frames per second.
    /// </summary>
    [InspectorRange(1f, 120f, 1f)]
    public float AnimationFps { get; set; } = DefaultAnimationFps;

    /// <summary>
    /// Selected animation clip index.
    /// </summary>
    [InspectorDropdown(nameof(GetActionNames))]
    [InspectorOnChanged(nameof(OnClipIndexChanged))]
    public int ClipIndex
    {
        get => _clipIndex;
        set => _clipIndex = Math.Max(0, value);
    }

    /// <summary>
    /// Name of the currently selected animation action.
    /// </summary>
    [InspectorReadOnly]
    public string ActionName => GetAnimationName(ResolveClipIndex());

    /// <summary>
    /// Number of animation actions loaded for the current model.
    /// </summary>
    [InspectorReadOnly]
    public int ActionCount => UseMultiSource ? _aggregatedClips.Count : _animationCount;

    /// <summary>
    /// Frame count of the currently selected animation clip.
    /// </summary>
    [InspectorReadOnly]
    public int FrameCount
    {
        get
        {
            int clip = ResolveClipIndex();
            if (clip < 0) return 0;
            return ResolveAnimation(clip).FrameCount;
        }
    }

    /// <summary>
    /// Number of leading frames to skip in a looping animation to avoid dwelling on
    /// the duplicate seam pose. Most glTF exports include one duplicate frame at the
    /// start/end boundary; some exporters add more. Only applies when <see cref="Loop"/>
    /// is enabled.
    /// </summary>
    [InspectorHeader("Advanced")]
    [InspectorRange(0f, 10f, 1f)]
    public int LoopFrameTrim { get; set; } = 1;

    /// <summary>
    /// Resets playback time to clip start.
    /// </summary>
    [InspectorButton("Restart")]
    public void Restart()
    {
        ResetPlayhead();
        Playing = true;
    }

    /// <summary>
    /// Stops playback and applies bind pose.
    /// </summary>
    [InspectorButton("Stop")]
    public void StopAndResetPose()
    {
        Playing = false;
        ResetPlayhead();
        if (TryGetAnimationModel(out var model, out _))
            ApplyBindPose(model);
    }

    /// <summary>
    /// Plays the animation clip with the given name. Searches across all loaded sources.
    /// Returns <c>true</c> if the clip was found and playback started; <c>false</c> otherwise.
    /// </summary>
    /// <param name="clipName">The name of the animation clip to play.</param>
    public bool PlayAnimation(string clipName)
    {
        EnsureMeshRendererReference();
        if (_meshRenderer != null)
            EnsureAnimationState();

        if (UseMultiSource)
        {
            for (int i = 0; i < _aggregatedClips.Count; i++)
            {
                if (string.Equals(_aggregatedClips[i].ClipName, clipName, StringComparison.OrdinalIgnoreCase))
                {
                    ClipIndex = i + 1; // +1 because index 0 = "(none)"
                    Playing = true;
                    return true;
                }
            }
        }
        else
        {
            for (int i = 0; i < _animationCount; i++)
            {
                var name = GetAnimationName(i);
                if (string.Equals(name, clipName, StringComparison.OrdinalIgnoreCase))
                {
                    ClipIndex = i + 1;
                    Playing = true;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the names of all available animation clips across all loaded sources.
    /// </summary>
    public string[] GetAnimationNames()
    {
        EnsureMeshRendererReference();
        if (_meshRenderer != null)
            EnsureAnimationState();

        if (UseMultiSource)
            return _aggregatedClips.Select(c => c.ClipName).ToArray();

        var names = new string[_animationCount];
        for (int i = 0; i < _animationCount; i++)
            names[i] = GetAnimationName(i);
        return names;
    }

    /// <summary>
    /// Returns <c>true</c> if an animation clip with the given name is available.
    /// </summary>
    /// <param name="clipName">The clip name to search for.</param>
    public bool HasAnimation(string clipName)
    {
        EnsureMeshRendererReference();
        if (_meshRenderer != null)
            EnsureAnimationState();

        if (UseMultiSource)
            return _aggregatedClips.Any(c => string.Equals(c.ClipName, clipName, StringComparison.OrdinalIgnoreCase));

        for (int i = 0; i < _animationCount; i++)
        {
            if (string.Equals(GetAnimationName(i), clipName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Adds a new animation source at runtime. The path should be an asset-relative path
    /// to a .glb file containing animations compatible with this model's skeleton.
    /// </summary>
    /// <param name="path">Asset-relative path to the animation source file.</param>
    public void AddAnimationSource(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        AnimationSources.Add(new AssetReference(path));
        InvalidateAnimationSources();
    }

    /// <summary>
    /// Removes an animation source at runtime by path.
    /// Returns <c>true</c> if the source was found and removed.
    /// </summary>
    /// <param name="path">Asset-relative path of the source to remove.</param>
    public bool RemoveAnimationSource(string path)
    {
        for (int i = AnimationSources.Count - 1; i >= 0; i--)
        {
            if (string.Equals(AnimationSources[i].Path, path, StringComparison.OrdinalIgnoreCase))
            {
                AnimationSources.RemoveAt(i);
                InvalidateAnimationSources();
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc />
    public override void Start()
    {
        ResetPlayhead();
        _playbackInitialized = false;
    }

    /// <inheritdoc />
    public override void Awake()
    {
        EnsureMeshRendererReference();
    }

    /// <inheritdoc />
    public override void OnDestroy()
    {
    }

    internal void PrepareForRender(ulong renderToken)
    {
        _currentSkinPaletteHandle = default;

        if (!Enabled)
            return;

        EnsureMeshRendererReference();
        if (_meshRenderer == null)
            return;

        if (!TryGetAnimationModel(out var model, out var skinPaletteHandle))
            return;

        if (!EnsureAnimationState(model))
            return;

        if (_lastPreparedRenderToken == renderToken)
        {
            _currentSkinPaletteHandle = skinPaletteHandle;
            return;
        }

        _lastPreparedRenderToken = renderToken;
        _currentSkinPaletteHandle = skinPaletteHandle;

        if (!RenderRuntimeCvars.AnimationEnabled)
        {
            // Prevent a large dt jump when animation is re-enabled.
            _lastSampleTime = -1d;
            ApplyBindPose(model);
            CaptureCurrentModelPose(model);
            return;
        }

        var ikComponent = Entity.GetComponent<InverseKinematicsComponent>();
        bool hasActiveIk = ikComponent != null && model.BoneCount > 0 && ikComponent.HasRunnableSolvers(model);
        var activeIk = hasActiveIk ? ikComponent : null;

        int clip = ResolveClipIndex();

        // "(none)" selected — use bind pose, optionally with IK
        if (clip < 0)
        {
            if (activeIk != null)
            {
                EnsureIkPoseBuffers(model);
                CopyBindPoseToModel(model, _ikModelPose!);
                ConvertModelPoseToLocal(_ikModelPose!, _ikLocalPose!, _ikParentIndices!);
                activeIk.ApplyIK(_ikLocalPose!, model, Entity.Transform.WorldMatrix, _ikWorldMatrices!);
                ConvertLocalPoseToModel(_ikLocalPose!, _ikModelPose!, _ikParentIndices!);
                ComputeSkinningMatrices(model, _ikModelPose!);
                CaptureCurrentModelPose(_ikModelPose!);
            }
            else
            {
                ApplyBindPose(model);
                CaptureCurrentModelPose(model);
            }
            return;
        }

        if (!Playing)
        {
            // Pose is already applied; keep _currentModelPose from last update.
            return;
        }

        var now = Raylib.GetTime();
        if (_lastSampleTime < 0d)
        {
            _lastSampleTime = now;
        }

        float dt = (float)Math.Max(0d, now - _lastSampleTime);
        _lastSampleTime = now;

        unsafe
        {
            var animation = ResolveAnimation(clip);
            if (animation.FrameCount <= 0 || model.MeshCount <= 0 || !Raylib.IsModelAnimationValid(model, animation))
                return;

            var speed = float.IsFinite(PlaybackSpeed) ? Math.Max(0f, PlaybackSpeed) : 1f;
            _playheadFrames += dt * speed * AnimationFps;

            int frameCount = Math.Max(1, animation.FrameCount);
            // Looping animations from Raylib's glTF resampler have a
            // duplicate seam frame (frame 0 == frame N). LoopFrameTrim
            // skips the first N frames so the wrap jumps from the last
            // unique frame back to frame trim, avoiding a visible dwell
            // on the duplicate start pose.
            int trim = Loop ? Math.Clamp(LoopFrameTrim, 0, frameCount - 1) : 0;
            int loopLength = Math.Max(1, frameCount - trim);
            if (Loop)
            {
                _playheadFrames %= loopLength;
                if (_playheadFrames < 0f)
                    _playheadFrames += loopLength;
            }
            else
            {
                float maxFrame = Math.Max(0f, frameCount - 1);
                if (_playheadFrames >= maxFrame)
                {
                    _playheadFrames = maxFrame;
                    Playing = false;
                }
            }

            int localFrame = (int)MathF.Floor(_playheadFrames);
            float alpha = Math.Clamp(_playheadFrames - localFrame, 0f, 1f);
            int frameA = localFrame + trim;
            int frameB = Loop
                ? (localFrame + 1) % loopLength + trim
                : Math.Min(frameA + 1, frameCount - 1);

            // IK path: sample as local transforms, apply IK, then compute skinning matrices
            if (activeIk != null)
            {
                EnsureIkPoseBuffers(model);
                SampleModelPose(animation, frameA, _ikModelPoseA!);
                ConvertModelPoseToLocal(_ikModelPoseA!, _ikLocalPoseA!, _ikParentIndices!);

                if (alpha > 0f && frameA != frameB)
                {
                    SampleModelPose(animation, frameB, _ikModelPoseB!);
                    ConvertModelPoseToLocal(_ikModelPoseB!, _ikLocalPoseB!, _ikParentIndices!);
                    LerpTransformPose(_ikLocalPoseA!, _ikLocalPoseB!, _ikLocalPose!, alpha);
                }
                else
                {
                    Array.Copy(_ikLocalPoseA!, _ikLocalPose!, _ikLocalPoseA!.Length);
                }

                activeIk.ApplyIK(_ikLocalPose!, model, Entity.Transform.WorldMatrix, _ikWorldMatrices!);
                ConvertLocalPoseToModel(_ikLocalPose!, _ikModelPose!, _ikParentIndices!);
                ComputeSkinningMatrices(model, _ikModelPose!);
                CaptureCurrentModelPose(_ikModelPose!);
                return;
            }

            // Fast path: no IK — use existing matrix-based sampling
            SampleFramePose(model, animation, frameA, _poseFrameA!);
            if (alpha <= 0f || frameA == frameB)
            {
                ApplyPose(model, _poseFrameA!);
                CaptureInterpolatedModelPose(model, animation, frameA, frameA, 0f);
                return;
            }

            SampleFramePose(model, animation, frameB, _poseFrameB!);
            LerpPose(_poseFrameA!, _poseFrameB!, _poseLerped!, alpha);
            ApplyPose(model, _poseLerped!);
            CaptureInterpolatedModelPose(model, animation, frameA, frameB, alpha);
        }
    }

    internal bool UsesSkinning => _hasSkinnedMeshes;
    internal SkinPaletteHandle CurrentSkinPaletteHandle => _currentSkinPaletteHandle;

    /// <summary>
    /// Returns the current model-space bone transforms after animation has been applied.
    /// Each element contains the translation, rotation and scale for that bone index.
    /// Returns an empty span when no animation state is available.
    /// </summary>
    public ReadOnlySpan<(Vector3 t, Quaternion r, Vector3 s)> CurrentModelPose =>
        _currentModelPose is not null ? _currentModelPose.AsSpan() : ReadOnlySpan<(Vector3, Quaternion, Vector3)>.Empty;

    private bool UseMultiSource => AnimationSources.Count > 0 || !UseEmbeddedAnimations;

    private bool EnsureAnimationState()
    {
        if (!TryGetAnimationModel(out var model, out _))
            return false;

        return EnsureAnimationState(model);
    }

    private bool EnsureAnimationState(Model model)
    {
        if (_meshRenderer == null)
            return false;

        bool modelChanged = _meshRenderer.ModelVersion != _lastModelVersion;
        bool sourcesChanged = ComputeSourcesHash() != _lastSourcesHash;
        bool assetsReloaded = AssetManager.Instance.AssetGeneration != _lastAssetGeneration;

        if (modelChanged || sourcesChanged || assetsReloaded)
        {
            ResetAnimationState();
            _lastModelVersion = _meshRenderer.ModelVersion;
            _lastSourcesHash = ComputeSourcesHash();
            _lastAssetGeneration = AssetManager.Instance.AssetGeneration;

            if (UseMultiSource)
            {
                RebuildAggregatedClips(model);
            }
            else
            {
                _animations = AssetManager.Instance.LoadModelAnimations(_meshRenderer.ModelPath.Path, out _animationCount);
            }

            // Clamp clip index to valid range after clip list changes
            int maxIndex = UseMultiSource ? _aggregatedClips.Count : _animationCount;
            if (_clipIndex > maxIndex)
                _clipIndex = maxIndex > 0 ? 1 : 0;

            _playbackInitialized = false;
        }

        if (!PoseShapeMatchesModel(model))
            ResetPoseBuffersOnly();
        CaptureBindPoseIfNeeded(model);
        if (_bindPose == null || _bindPose.Length == 0)
            return false;

        if (!_playbackInitialized)
        {
            // Auto-select the first clip when PlayAutomatically is true and no clip is selected
            int totalClips = UseMultiSource ? _aggregatedClips.Count : _animationCount;
            if (PlayAutomatically && _clipIndex == 0 && totalClips > 0)
                _clipIndex = 1; // index 1 = first real clip (index 0 = "(none)")

            Playing = PlayAutomatically;
            _playbackInitialized = true;
        }

        int clip = ResolveClipIndex();
        if (clip >= 0)
        {
            if (UseMultiSource)
            {
                if (_aggregatedClips[clip].IsValid)
                    return true;
            }
            else
            {
                var animation = _animations[clip];
                if (model.MeshCount > 0 && Raylib.IsModelAnimationValid(model, animation))
                    return true;
            }
        }

        // clip < 0 means "(none)" selected — still valid if IK component is present
        var ikComponent = Entity.GetComponent<InverseKinematicsComponent>();
        if (clip < 0 && ikComponent != null && ikComponent.HasRunnableSolvers(model))
            return true;

        ApplyBindPose(model);
        return false;
    }

    private void RebuildAggregatedClips(Model model)
    {
        _aggregatedClips.Clear();

        if (_meshRenderer == null)
            return;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Load embedded animations from the mesh file first
        if (UseEmbeddedAnimations)
        {
            _animations = AssetManager.Instance.LoadModelAnimations(_meshRenderer.ModelPath.Path, out _animationCount);
            if (_animations != null && _animationCount > 0)
            {
                for (int i = 0; i < _animationCount; i++)
                {
                    var anim = _animations[i];
                    bool valid = model.MeshCount > 0 && Raylib.IsModelAnimationValid(model, anim);
                    var rawName = ExtractClipName(anim, i);

                    usedNames.Add(rawName);
                    _aggregatedClips.Add(new AggregatedClip(
                        _meshRenderer.ModelPath.Path,
                        i,
                        rawName,
                        &_animations[i],
                        valid));
                }
            }
        }

        // Append clips from external animation sources
        foreach (var source in AnimationSources)
        {
            if (source.IsEmpty) continue;

            var anims = AssetManager.Instance.LoadModelAnimations(source.Path, out int count);
            if (anims == null || count == 0) continue;

            var sourceFileName = Path.GetFileNameWithoutExtension(source.Path);

            for (int i = 0; i < count; i++)
            {
                var anim = anims[i];
                bool valid = model.MeshCount > 0 && Raylib.IsModelAnimationValid(model, anim);

                if (!valid)
                {
                    FrinkyLog.Warning(
                        $"Animation source '{source.Path}' clip {i} has incompatible skeleton for model '{_meshRenderer.ModelPath.Path}'");
                }

                var clipName = DeduplicateClipName(ExtractClipName(anim, i), sourceFileName, usedNames);

                _aggregatedClips.Add(new AggregatedClip(
                    source.Path,
                    i,
                    clipName,
                    &anims[i],
                    valid));
            }
        }
    }

    private static unsafe string ExtractClipName(ModelAnimation anim, int index)
    {
        var name = new string(anim.Name, 0, 32).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(name) ? $"Action {index}" : name;
    }

    private static string DeduplicateClipName(string rawName, string sourceFileName, HashSet<string> usedNames)
    {
        if (usedNames.Add(rawName))
            return rawName;

        var prefixed = $"{sourceFileName}/{rawName}";
        if (usedNames.Add(prefixed))
            return prefixed;

        int suffix = 2;
        string candidate;
        do
        {
            candidate = $"{prefixed} ({suffix})";
            suffix++;
        } while (!usedNames.Add(candidate));
        return candidate;
    }

    private int ComputeSourcesHash()
    {
        var hash = new HashCode();
        hash.Add(UseEmbeddedAnimations);
        foreach (var source in AnimationSources)
            hash.Add(source.Path ?? "", StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }

    private void InvalidateAnimationSources()
    {
        _lastSourcesHash = 0;
    }

    private void OnAnimationSourcesChanged()
    {
        InvalidateAnimationSources();
        ResetPlayhead();
    }

    private int ResolveClipIndex()
    {
        // _clipIndex 0 = "(none)" dropdown entry → resolved clip -1
        int resolved = _clipIndex - 1;

        if (UseMultiSource)
        {
            if (_aggregatedClips.Count <= 0)
                return -1;
            if (resolved < 0)
                return -1;
            return Math.Clamp(resolved, 0, _aggregatedClips.Count - 1);
        }

        if (_animations == null || _animationCount <= 0)
            return -1;

        if (resolved < 0)
            return -1;

        return Math.Clamp(resolved, 0, _animationCount - 1);
    }

    private ModelAnimation ResolveAnimation(int clip)
    {
        if (UseMultiSource)
            return *_aggregatedClips[clip].Pointer;
        return _animations[clip];
    }

    private void CaptureBindPoseIfNeeded(Model model)
    {
        if (_bindPose != null)
            return;

        _bindPose = CaptureCurrentPose(model);
        _poseFrameA = ClonePoseShape(_bindPose);
        _poseFrameB = ClonePoseShape(_bindPose);
        _poseLerped = ClonePoseShape(_bindPose);
        _hasSkinnedMeshes = _bindPose.Any(static x => x.Length > 0);
    }

    private static Matrix4x4[][] CaptureCurrentPose(Model model)
    {
        unsafe
        {
            int meshCount = Math.Max(0, model.MeshCount);
            var pose = new Matrix4x4[meshCount][];

            for (int i = 0; i < meshCount; i++)
            {
                var mesh = model.Meshes[i];
                int boneCount = mesh.BoneCount;
                if (boneCount <= 0 || mesh.BoneMatrices == null)
                {
                    pose[i] = Array.Empty<Matrix4x4>();
                    continue;
                }

                var data = new Matrix4x4[boneCount];
                for (int b = 0; b < boneCount; b++)
                    data[b] = mesh.BoneMatrices[b];
                pose[i] = data;
            }

            return pose;
        }
    }

    private bool PoseShapeMatchesModel(Model model)
    {
        if (_bindPose == null)
            return false;
        if (_bindPose.Length != model.MeshCount)
            return false;

        unsafe
        {
            for (int i = 0; i < model.MeshCount; i++)
            {
                int expected = model.Meshes[i].BoneCount;
                if (_bindPose[i].Length != Math.Max(0, expected))
                    return false;
            }
        }

        return true;
    }

    private static Matrix4x4[][] ClonePoseShape(Matrix4x4[][] source)
    {
        var clone = new Matrix4x4[source.Length][];
        for (int i = 0; i < source.Length; i++)
            clone[i] = new Matrix4x4[source[i].Length];
        return clone;
    }

    private unsafe void SampleFramePose(Model model, ModelAnimation animation, int frame, Matrix4x4[][] target)
    {
        if (model.MeshCount <= 0 || animation.BoneCount <= 0 || animation.FrameCount <= 0 || animation.FramePoses == null)
            return;

        Raylib.UpdateModelAnimationBones(model, animation, frame);
        CopyCurrentPoseInto(model, target);
    }

    private static void CopyCurrentPoseInto(Model model, Matrix4x4[][] target)
    {
        unsafe
        {
            for (int i = 0; i < model.MeshCount && i < target.Length; i++)
            {
                var mesh = model.Meshes[i];
                var outArr = target[i];
                if (mesh.BoneMatrices == null || outArr.Length == 0)
                    continue;

                int count = Math.Min(mesh.BoneCount, outArr.Length);
                for (int b = 0; b < count; b++)
                    outArr[b] = mesh.BoneMatrices[b];
            }
        }
    }

    private static void LerpPose(Matrix4x4[][] a, Matrix4x4[][] b, Matrix4x4[][] output, float alpha)
    {
        float beta = 1f - alpha;

        for (int i = 0; i < output.Length; i++)
        {
            int count = output[i].Length;
            for (int m = 0; m < count; m++)
            {
                var ma = a[i][m];
                var mb = b[i][m];
                output[i][m] = new Matrix4x4(
                    ma.M11 * beta + mb.M11 * alpha, ma.M12 * beta + mb.M12 * alpha, ma.M13 * beta + mb.M13 * alpha, ma.M14 * beta + mb.M14 * alpha,
                    ma.M21 * beta + mb.M21 * alpha, ma.M22 * beta + mb.M22 * alpha, ma.M23 * beta + mb.M23 * alpha, ma.M24 * beta + mb.M24 * alpha,
                    ma.M31 * beta + mb.M31 * alpha, ma.M32 * beta + mb.M32 * alpha, ma.M33 * beta + mb.M33 * alpha, ma.M34 * beta + mb.M34 * alpha,
                    ma.M41 * beta + mb.M41 * alpha, ma.M42 * beta + mb.M42 * alpha, ma.M43 * beta + mb.M43 * alpha, ma.M44 * beta + mb.M44 * alpha);
            }
        }
    }

    private void ApplyBindPose(Model model)
    {
        if (_bindPose == null)
            return;

        ApplyPose(model, _bindPose);
    }

    private static void ApplyPose(Model model, Matrix4x4[][] pose)
    {
        unsafe
        {
            for (int i = 0; i < model.MeshCount && i < pose.Length; i++)
            {
                var mesh = model.Meshes[i];
                if (mesh.BoneMatrices == null)
                    continue;

                var data = pose[i];
                int count = Math.Min(mesh.BoneCount, data.Length);
                for (int b = 0; b < count; b++)
                    mesh.BoneMatrices[b] = data[b];
            }
        }
    }

    private unsafe void EnsureIkPoseBuffers(Model model)
    {
        int boneCount = model.BoneCount;
        if (_ikLocalPose != null && _ikLocalPose.Length == boneCount)
            return;

        _ikModelPoseA = new (Vector3, Quaternion, Vector3)[boneCount];
        _ikModelPoseB = new (Vector3, Quaternion, Vector3)[boneCount];
        _ikModelPose = new (Vector3, Quaternion, Vector3)[boneCount];
        _ikLocalPoseA = new (Vector3, Quaternion, Vector3)[boneCount];
        _ikLocalPoseB = new (Vector3, Quaternion, Vector3)[boneCount];
        _ikLocalPose = new (Vector3, Quaternion, Vector3)[boneCount];
        _ikWorldMatrices = new Matrix4x4[boneCount];

        // Cache parent indices (stable for a given model)
        _ikParentIndices = new int[boneCount];
        for (int i = 0; i < boneCount; i++)
            _ikParentIndices[i] = model.Bones[i].Parent;
    }

    private static unsafe void SampleModelPose(
        ModelAnimation animation, int frame,
        (Vector3 t, Quaternion r, Vector3 s)[] target)
    {
        if (animation.BoneCount <= 0 || animation.FrameCount <= 0 || animation.FramePoses == null)
            return;

        int boneCount = Math.Min(animation.BoneCount, target.Length);
        frame = Math.Clamp(frame, 0, Math.Max(0, animation.FrameCount - 1));
        var framePoses = animation.FramePoses[frame];
        for (int i = 0; i < boneCount; i++)
        {
            var t = framePoses[i];
            target[i] = (t.Translation, t.Rotation, t.Scale);
        }
    }

    private static void LerpTransformPose(
        (Vector3 t, Quaternion r, Vector3 s)[] a,
        (Vector3 t, Quaternion r, Vector3 s)[] b,
        (Vector3 t, Quaternion r, Vector3 s)[] output,
        float alpha)
    {
        int count = Math.Min(a.Length, Math.Min(b.Length, output.Length));
        for (int i = 0; i < count; i++)
        {
            output[i] = (
                Vector3.Lerp(a[i].t, b[i].t, alpha),
                Quaternion.Slerp(a[i].r, b[i].r, alpha),
                Vector3.Lerp(a[i].s, b[i].s, alpha));
        }
    }

    private static unsafe void CopyBindPoseToModel(
        Model model,
        (Vector3 t, Quaternion r, Vector3 s)[] target)
    {
        int count = Math.Min(model.BoneCount, target.Length);
        for (int i = 0; i < count; i++)
        {
            var t = model.BindPose[i];
            target[i] = (t.Translation, t.Rotation, t.Scale);
        }
    }

    /// <summary>
    /// Converts per-bone model-space transforms to local-space transforms using parent indices.
    /// Input and output arrays must NOT alias (the forward loop reads parent entries
    /// that may already be overwritten when parent index &gt; child index).
    /// </summary>
    private static void ConvertModelPoseToLocal(
        (Vector3 t, Quaternion r, Vector3 s)[] modelPose,
        (Vector3 t, Quaternion r, Vector3 s)[] localPose,
        int[] parentIndices)
    {
        int count = Math.Min(modelPose.Length, Math.Min(localPose.Length, parentIndices.Length));
        for (int i = 0; i < count; i++)
        {
            int parent = parentIndices[i];
            var model = modelPose[i];
            if (parent < 0 || parent >= count)
            {
                localPose[i] = model;
                continue;
            }

            var parentModel = modelPose[parent];
            var invParentRotation = Raymath.QuaternionInvert(parentModel.r);
            var localRotation = Quaternion.Normalize(Raymath.QuaternionMultiply(invParentRotation, model.r));
            var localScale = ComponentDivide(model.s, parentModel.s);
            var delta = Raymath.Vector3Subtract(model.t, parentModel.t);
            var unrotated = Raymath.Vector3RotateByQuaternion(delta, invParentRotation);
            var localTranslation = ComponentDivide(unrotated, parentModel.s);
            localPose[i] = (localTranslation, localRotation, localScale);
        }
    }

    /// <summary>
    /// Converts per-bone local-space transforms to model-space transforms using parent indices.
    /// Input and output arrays may alias.
    /// </summary>
    private static void ConvertLocalPoseToModel(
        (Vector3 t, Quaternion r, Vector3 s)[] localPose,
        (Vector3 t, Quaternion r, Vector3 s)[] modelPose,
        int[] parentIndices)
    {
        int count = Math.Min(localPose.Length, Math.Min(modelPose.Length, parentIndices.Length));
        if (count <= 0)
            return;

        // Handle arbitrary bone ordering; do not assume parent index < child index.
        var visitState = ArrayPool<byte>.Shared.Rent(count); // 0=unvisited, 1=visiting, 2=done
        Array.Clear(visitState, 0, count);
        for (int i = 0; i < count; i++)
            ComputeModel(i);
        ArrayPool<byte>.Shared.Return(visitState);

        void ComputeModel(int boneIndex)
        {
            if (visitState[boneIndex] == 2)
                return;
            if (visitState[boneIndex] == 1)
            {
                // Cycle guard: fall back to local transform.
                modelPose[boneIndex] = localPose[boneIndex];
                visitState[boneIndex] = 2;
                return;
            }

            visitState[boneIndex] = 1;

            int parent = parentIndices[boneIndex];
            var local = localPose[boneIndex];
            if (parent >= 0 && parent < count)
            {
                ComputeModel(parent);
                var parentModel = modelPose[parent];
                var modelRotation = Quaternion.Normalize(Raymath.QuaternionMultiply(parentModel.r, local.r));
                var modelScale = Raymath.Vector3Multiply(parentModel.s, local.s);
                var scaledTranslation = Raymath.Vector3Multiply(local.t, parentModel.s);
                var modelTranslation = Raymath.Vector3Add(
                    Raymath.Vector3RotateByQuaternion(scaledTranslation, parentModel.r),
                    parentModel.t);
                modelPose[boneIndex] = (modelTranslation, modelRotation, modelScale);
            }
            else
            {
                modelPose[boneIndex] = local;
            }

            visitState[boneIndex] = 2;
        }
    }

    /// <summary>
    /// Replicates Raylib's UpdateModelAnimationBones algorithm:
    /// boneMatrix = inverse(bindMatrix) * targetMatrix (both in model-space).
    /// </summary>
    private unsafe void ComputeSkinningMatrices(
        Model model,
        (Vector3 t, Quaternion r, Vector3 s)[] modelPose)
    {
        int boneCount = model.BoneCount;
        int firstMeshWithBones = -1;
        for (int i = 0; i < model.MeshCount; i++)
        {
            var mesh = model.Meshes[i];
            if (mesh.BoneMatrices != null && mesh.BoneCount > 0)
            {
                firstMeshWithBones = i;
                break;
            }
        }
        if (firstMeshWithBones < 0)
            return;

        var firstMesh = model.Meshes[firstMeshWithBones];
        int firstCount = Math.Min(firstMesh.BoneCount, Math.Min(boneCount, modelPose.Length));
        for (int b = 0; b < firstCount; b++)
        {
            var bind = model.BindPose[b];
            var target = modelPose[b];

            // Match Raylib's UpdateModelAnimationBones implementation exactly:
            // bind = MatrixMultiply(MatrixMultiply(MatrixScale, QuaternionToMatrix), MatrixTranslate)
            var bindMatrix = Raymath.MatrixMultiply(
                Raymath.MatrixMultiply(
                    Raymath.MatrixScale(bind.Scale.X, bind.Scale.Y, bind.Scale.Z),
                    Raymath.QuaternionToMatrix(bind.Rotation)),
                Raymath.MatrixTranslate(bind.Translation.X, bind.Translation.Y, bind.Translation.Z));

            var targetMatrix = Raymath.MatrixMultiply(
                Raymath.MatrixMultiply(
                    Raymath.MatrixScale(target.s.X, target.s.Y, target.s.Z),
                    Raymath.QuaternionToMatrix(target.r)),
                Raymath.MatrixTranslate(target.t.X, target.t.Y, target.t.Z));

            firstMesh.BoneMatrices[b] = Raymath.MatrixMultiply(
                Raymath.MatrixInvert(bindMatrix),
                targetMatrix);
        }

        for (int m = firstMeshWithBones + 1; m < model.MeshCount; m++)
        {
            var mesh = model.Meshes[m];
            if (mesh.BoneMatrices == null || mesh.BoneCount <= 0)
                continue;

            int count = Math.Min(mesh.BoneCount, firstCount);
            for (int b = 0; b < count; b++)
                mesh.BoneMatrices[b] = firstMesh.BoneMatrices[b];
        }
    }

    private unsafe void CaptureCurrentModelPose(Model model)
    {
        int boneCount = model.BoneCount;
        if (boneCount <= 0)
        {
            _currentModelPose = null;
            return;
        }

        if (_currentModelPose == null || _currentModelPose.Length != boneCount)
            _currentModelPose = new (Vector3, Quaternion, Vector3)[boneCount];

        for (int i = 0; i < boneCount; i++)
        {
            var t = model.BindPose[i];
            _currentModelPose[i] = (t.Translation, t.Rotation, t.Scale);
        }
    }

    private void CaptureCurrentModelPose((Vector3 t, Quaternion r, Vector3 s)[] modelPose)
    {
        int count = modelPose.Length;
        if (count <= 0)
        {
            _currentModelPose = null;
            return;
        }

        if (_currentModelPose == null || _currentModelPose.Length != count)
            _currentModelPose = new (Vector3, Quaternion, Vector3)[count];

        Array.Copy(modelPose, _currentModelPose, count);
    }

    private unsafe void CaptureInterpolatedModelPose(Model model, ModelAnimation animation, int frameA, int frameB, float alpha)
    {
        if (animation.FrameCount <= 0 || animation.FramePoses == null)
            return;

        int boneCount = Math.Min(model.BoneCount, animation.BoneCount);
        if (boneCount <= 0)
        {
            _currentModelPose = null;
            return;
        }

        if (_currentModelPose == null || _currentModelPose.Length != boneCount)
            _currentModelPose = new (Vector3, Quaternion, Vector3)[boneCount];

        int clampedA = Math.Clamp(frameA, 0, Math.Max(0, animation.FrameCount - 1));
        int clampedB = Math.Clamp(frameB, 0, Math.Max(0, animation.FrameCount - 1));
        var posesA = animation.FramePoses[clampedA];
        var posesB = animation.FramePoses[clampedB];

        for (int i = 0; i < boneCount; i++)
        {
            var a = posesA[i];
            var b = posesB[i];
            _currentModelPose[i] = (
                Vector3.Lerp(a.Translation, b.Translation, alpha),
                Quaternion.Slerp(a.Rotation, b.Rotation, alpha),
                Vector3.Lerp(a.Scale, b.Scale, alpha));
        }
    }

    private static Vector3 ComponentDivide(Vector3 value, Vector3 divisor)
    {
        return new Vector3(
            divisor.X != 0f ? value.X / divisor.X : 0f,
            divisor.Y != 0f ? value.Y / divisor.Y : 0f,
            divisor.Z != 0f ? value.Z / divisor.Z : 0f);
    }

    private void EnsureMeshRendererReference()
    {
        _meshRenderer ??= Entity.GetComponent<MeshRendererComponent>();
    }

    private void ResetAnimationState()
    {
        _animations = null;
        _animationCount = 0;
        _aggregatedClips.Clear();
        ResetPlayhead();
        ResetPoseBuffersOnly();
    }

    private void ResetPoseBuffersOnly()
    {
        _bindPose = null;
        _poseFrameA = null;
        _poseFrameB = null;
        _poseLerped = null;
        _ikModelPoseA = null;
        _ikModelPoseB = null;
        _ikModelPose = null;
        _ikLocalPoseA = null;
        _ikLocalPoseB = null;
        _ikLocalPose = null;
        _ikParentIndices = null;
        _ikWorldMatrices = null;
        _hasSkinnedMeshes = false;
        _currentModelPose = null;
        _currentSkinPaletteHandle = default;
    }

    private void ResetPlayhead()
    {
        _playheadFrames = 0f;
        _lastSampleTime = -1d;
        _lastPreparedRenderToken = 0;
    }

    private void OnClipIndexChanged() => ResetPlayhead();

    private string GetAnimationName(int index)
    {
        if (UseMultiSource)
        {
            if (index < 0 || index >= _aggregatedClips.Count)
                return "(none)";
            return _aggregatedClips[index].ClipName;
        }

        if (index < 0 || _animations == null || index >= _animationCount)
            return "(none)";

        var name = new string(_animations[index].Name, 0, 32).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(name) ? $"Action {index}" : name;
    }

    private string[] GetActionNames()
    {
        if (UseMultiSource)
        {
            if (_aggregatedClips.Count <= 0)
                return ["(none)"];

            var names = new string[_aggregatedClips.Count + 1];
            names[0] = "(none)";
            for (int i = 0; i < _aggregatedClips.Count; i++)
                names[i + 1] = _aggregatedClips[i].ClipName;
            return names;
        }

        if (_animationCount <= 0 || _animations == null)
            return ["(none)"];

        var result = new string[_animationCount + 1];
        result[0] = "(none)";
        for (int i = 0; i < _animationCount; i++)
            result[i + 1] = GetAnimationName(i);
        return result;
    }

    private bool TryGetAnimationModel(out Model model, out SkinPaletteHandle skinPaletteHandle)
    {
        model = default;
        skinPaletteHandle = default;

        if (_meshRenderer == null)
            return false;

        var queries = RenderGeometryQueries.Current;
        if (queries == null)
            return false;

        return queries.TryGetAnimationModel(_meshRenderer, out model, out skinPaletteHandle);
    }

    /// <summary>
    /// Returns information about all loaded animation clips, grouped by source file.
    /// Each entry contains the source path and the clip names loaded from that source.
    /// </summary>
    public IReadOnlyList<(string SourcePath, IReadOnlyList<string> ClipNames, IReadOnlyList<bool> Valid)> GetAnimationSourceInfo()
    {
        if (!UseMultiSource)
            return Array.Empty<(string, IReadOnlyList<string>, IReadOnlyList<bool>)>();

        var result = new List<(string, IReadOnlyList<string>, IReadOnlyList<bool>)>();
        var grouped = _aggregatedClips
            .GroupBy(c => c.SourcePath, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var clipNames = group.Select(c => c.ClipName).ToList();
            var valid = group.Select(c => c.IsValid).ToList();
            result.Add((group.Key, clipNames, valid));
        }

        return result;
    }
}
