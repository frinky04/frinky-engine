using System.Numerics;
using System.Linq;
using FrinkyEngine.Core.Animation.IK;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Rendering;
using Raylib_cs;

namespace FrinkyEngine.Core.Components;

/// <summary>
/// Holds a list of <see cref="IKSolver"/> instances that are applied after animation sampling.
/// </summary>
[ComponentCategory("Animation")]
[ComponentDisplayName("Inverse Kinematics")]
public class InverseKinematicsComponent : Component
{
    private BoneHierarchy? _hierarchy;
    private int _lastModelVersion;
    private int _lastSolverCount = -1;

    /// <summary>
    /// Ordered list of IK solvers to apply.
    /// </summary>
    [InspectorOnChanged(nameof(OnSolversChanged))]
    public List<IKSolver> Solvers { get; set; } = new();

    internal bool HasEnabledSolvers => Solvers.Any(static s => s.Enabled && s.Weight > 0f && s.IsConfigured);

    internal bool HasRunnableSolvers(Model model)
    {
        var hierarchy = EnsureHierarchy(model);
        if (hierarchy == null || hierarchy.BoneCount == 0)
            return false;

        foreach (var solver in Solvers)
        {
            if (solver.Enabled && solver.Weight > 0f && solver.CanSolve(hierarchy))
                return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override void LateUpdate(float dt)
    {
        // Eagerly populate hierarchy so inspector bone dropdowns work
        // without waiting for the first render-time ApplyIK call.
        EnsureHierarchyFromMeshRenderer();

        if (NeedsSolverHierarchyRefresh())
            RefreshSolverHierarchyRefs();
    }

    /// <summary>
    /// Applies all enabled solvers to the given bone-local transforms.
    /// Called by <see cref="SkinnedMeshAnimatorComponent"/> after animation sampling.
    /// </summary>
    /// <param name="localTransforms">Per-bone local transforms to modify in place.</param>
    /// <param name="model">The Raylib model (for hierarchy lookup).</param>
    /// <param name="entityWorldMatrix">Entity world transform.</param>
    /// <param name="worldMatrices">Scratch buffer for FK world matrices (caller-owned, avoids per-frame alloc).</param>
    internal void ApplyIK(
        (Vector3 translation, Quaternion rotation, Vector3 scale)[] localTransforms,
        Model model,
        Matrix4x4 entityWorldMatrix,
        Matrix4x4[] worldMatrices)
    {
        var hierarchy = EnsureHierarchy(model);
        if (hierarchy == null || hierarchy.BoneCount == 0)
            return;
        if (!HasEnabledSolvers)
            return;

        // Compute FK once; solvers read and may mutate worldMatrices, so recompute between solvers
        IKMath.ForwardKinematics(hierarchy.ParentIndices, localTransforms, entityWorldMatrix, worldMatrices);

        foreach (var solver in Solvers)
        {
            if (solver.Enabled && solver.Weight > 0f && solver.CanSolve(hierarchy))
            {
                solver.Solve(localTransforms, hierarchy, entityWorldMatrix, worldMatrices);
                // Recompute FK after each solver so the next solver sees updated world positions
                IKMath.ForwardKinematics(hierarchy.ParentIndices, localTransforms, entityWorldMatrix, worldMatrices);
            }
        }
    }

    /// <summary>
    /// Ensures the bone hierarchy cache is up to date with the current model.
    /// </summary>
    internal BoneHierarchy? EnsureHierarchy(Model model)
    {
        var meshRenderer = Entity.GetComponent<MeshRendererComponent>();
        if (meshRenderer == null)
            return _hierarchy;

        int version = meshRenderer.ModelVersion;
        if (_hierarchy == null || version != _lastModelVersion)
        {
            _hierarchy = model.BoneCount > 0 ? new BoneHierarchy(model) : null;
            _lastModelVersion = version;

            // Push hierarchy ref to all solvers for dropdown support
            foreach (var solver in Solvers)
                solver.Hierarchy = _hierarchy;
        }

        return _hierarchy;
    }

    /// <summary>
    /// Refreshes hierarchy references on solvers when the solver list changes.
    /// </summary>
    internal void RefreshSolverHierarchyRefs()
    {
        foreach (var solver in Solvers)
            solver.Hierarchy = _hierarchy;
        _lastSolverCount = Solvers.Count;
    }

    private bool NeedsSolverHierarchyRefresh()
    {
        if (_lastSolverCount != Solvers.Count)
            return true;

        foreach (var solver in Solvers)
        {
            if (!ReferenceEquals(solver.Hierarchy, _hierarchy))
                return true;
        }

        return false;
    }

    private void OnSolversChanged()
    {
        EnsureHierarchyFromMeshRenderer();
        RefreshSolverHierarchyRefs();
    }

    private void EnsureHierarchyFromMeshRenderer()
    {
        var meshRenderer = Entity.GetComponent<MeshRendererComponent>();
        var queries = RenderGeometryQueries.Current;
        if (meshRenderer != null && queries != null && queries.TryGetSharedModel(meshRenderer, out var model))
            EnsureHierarchy(model);
    }
}
