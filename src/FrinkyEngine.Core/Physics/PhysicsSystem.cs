using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using BepuPhysics.Trees;
using BepuUtilities.Memory;
using FrinkyEngine.Core.Components;
using FrinkyEngine.Core.ECS;
using FrinkyEngine.Core.Physics.Characters;
using FrinkyEngine.Core.Rendering;

namespace FrinkyEngine.Core.Physics;

internal sealed class PhysicsSystem : IDisposable
{
    private sealed class PhysicsBodyState
    {
        public required Entity Entity;
        public RigidbodyComponent? Rigidbody;
        public required ColliderComponent Collider;
        public required TypedIndex ShapeIndex;
        public required BodyMotionType MotionType;
        public bool IsImplicitStatic;
        public BodyHandle? BodyHandle;
        public StaticHandle? StaticHandle;
        public int ConfigurationHash;
        public Vector3 LockedPosition;
        public Quaternion LockReferenceOrientation;
        public byte RotationLockMask;
        public Vector3 AuthoritativeLocalPosition;
        public Quaternion AuthoritativeLocalRotation;
        public Vector3 LastPublishedVisualLocalPosition;
        public Quaternion LastPublishedVisualLocalRotation;
        public bool HasPublishedVisualPose;
        public RigidPose PreviousSimulationPose;
        public RigidPose CurrentSimulationPose;
        public bool HasSimulationPoseHistory;
        public bool SuppressInterpolationForFrame;
        public RigidPose PreviousKinematicTargetPose;
        public bool HasPreviousKinematicTargetPose;
    }

    private readonly Scene.Scene _scene;
    private readonly BufferPool _bufferPool = new();
    private readonly PhysicsMaterialTable _materialTable = new();
    private readonly Dictionary<Guid, PhysicsBodyState> _bodyStates = new();
    private readonly Dictionary<Guid, CharacterControllerRuntimeState> _characterStates = new();
    private readonly HashSet<Guid> _warnedNoCollider = new();
    private readonly HashSet<Guid> _warnedParented = new();
    private readonly HashSet<Guid> _warnedImplicitStaticParented = new();
    private readonly HashSet<Guid> _warnedMultipleColliders = new();
    private readonly HashSet<Guid> _warnedMultipleRigidbodies = new();
    private readonly HashSet<Guid> _warnedCharacterMissingRigidbody = new();
    private readonly HashSet<Guid> _warnedCharacterMissingCapsule = new();
    private readonly HashSet<Guid> _warnedCharacterWrongMotionType = new();
    private readonly HashSet<Guid> _warnedCharacterParented = new();
    private readonly HashSet<Guid> _warnedCharacterNonCapsuleBody = new();
    private readonly HashSet<Guid> _warnedKinematicDiscontinuity = new();
    private readonly CharacterControllerBridge _characterBridge = new();

    // Handle-to-entity reverse mapping
    private readonly Dictionary<int, Guid> _bodyHandleToEntityId = new();
    private readonly Dictionary<int, Guid> _staticHandleToEntityId = new();

    // Trigger tracking
    private readonly HashSet<int> _triggerBodyHandles = new();
    private readonly HashSet<int> _triggerStaticHandles = new();
    private readonly ConcurrentBag<(CollidableReference A, CollidableReference B)> _narrowPhaseTriggerPairs = new();
    private HashSet<(Guid, Guid)> _previousTriggerPairs = new();
    private HashSet<(Guid, Guid)> _currentTriggerPairs = new();

    // Collision tracking
    private readonly ConcurrentBag<CollisionPairData> _narrowPhaseCollisionPairs = new();
    private HashSet<(Guid, Guid)> _previousCollisionPairs = new();
    private HashSet<(Guid, Guid)> _currentCollisionPairs = new();
    private readonly Dictionary<(Guid, Guid), CollisionContactData> _collisionContactData = new();
    private static FieldInfo? _poseIntegratorCallbacksField;

    private Simulation? _simulation;
    private CharacterControllers? _characterControllers;
    private float _accumulator;
    private int _lastSubstepCount;
    private double _lastStepTimeMs;
    private const byte RotationLockXMask = 1 << 0;
    private const byte RotationLockYMask = 1 << 1;
    private const byte RotationLockZMask = 1 << 2;
    private const float MaxKinematicLinearSpeed = 50f;
    private const float MaxKinematicAngularSpeed = 20f;
    private const float MaxKinematicAngularStepDegrees = 120f;
    private const float KinematicDiscontinuityLinearSpeedMultiplier = 2f;

    public PhysicsSystem(Scene.Scene scene)
    {
        _scene = scene;
    }

    public bool IsInitialized => _simulation != null;

    public void Initialize()
    {
        if (_simulation != null)
            return;

        _scene.PhysicsSettings.Normalize();
        var projSettings = PhysicsProjectSettings.Current;
        projSettings.Normalize();

        _characterControllers = new CharacterControllers(_bufferPool);
        var narrowPhaseCallbacks = new PhysicsNarrowPhaseCallbacks(
            new SpringSettings(projSettings.ContactSpringFrequency, projSettings.ContactDampingRatio),
            projSettings.MaximumRecoveryVelocity,
            projSettings.DefaultFriction,
            projSettings.DefaultRestitution,
            _materialTable,
            _characterControllers,
            _triggerBodyHandles,
            _triggerStaticHandles,
            _narrowPhaseTriggerPairs,
            _narrowPhaseCollisionPairs);
        var poseCallbacks = new PhysicsPoseIntegratorCallbacks(_scene.PhysicsSettings.Gravity);
        var solveDescription = new SolveDescription(projSettings.SolverVelocityIterations, projSettings.SolverSubsteps);

        _simulation = Simulation.Create(_bufferPool, narrowPhaseCallbacks, poseCallbacks, solveDescription);
        _accumulator = 0f;

        ReconcileParticipants();
    }

    public void Step(float dt)
    {
        if (_simulation == null)
            return;

        if (!float.IsFinite(dt) || dt <= 0f)
            return;

        var sw = Stopwatch.StartNew();

        ReconcileParticipants();
        SyncPoseIntegratorGravity();

        var projSettings = PhysicsProjectSettings.Current;
        projSettings.Normalize();
        var fixedDt = projSettings.FixedTimestep;
        var maxSubsteps = projSettings.MaxSubstepsPerFrame;

        _accumulator += dt;
        int steps = 0;
        _characterBridge.CaptureFrameInput(_characterStates.Values);

        while (_accumulator >= fixedDt && steps < maxSubsteps)
        {
            PushSceneTransformsToPhysics(fixedDt);
            ApplyPendingForces(fixedDt);
            ApplyCharacterGoalsForStep(fixedDt, allowJump: steps == 0);
            _simulation.Timestep(fixedDt);
            AccumulateTriggerPairs();
            AccumulateCollisionPairs();
            ApplyPostStepBodyModifiers(fixedDt);
            SyncDynamicTransformsFromPhysics();
            SyncCharacterRuntimeState();

            _accumulator -= fixedDt;
            steps++;
        }

        if (steps > 0)
        {
            _characterBridge.ConsumeFrameInput(_characterStates.Values);
            ProcessTriggerEvents();
            ProcessCollisionEvents();
        }

        if (steps >= maxSubsteps && _accumulator >= fixedDt)
            _accumulator = 0f;

        sw.Stop();
        _lastSubstepCount = steps;
        _lastStepTimeMs = sw.Elapsed.TotalMilliseconds;
    }

    public PhysicsFrameStats GetFrameStats()
    {
        if (_simulation == null)
            return default;

        int dynamic = 0, kinematic = 0, staticCount = 0;
        foreach (var state in _bodyStates.Values)
        {
            switch (state.MotionType)
            {
                case BodyMotionType.Dynamic: dynamic++; break;
                case BodyMotionType.Kinematic: kinematic++; break;
                case BodyMotionType.Static: staticCount++; break;
            }
        }

        return new PhysicsFrameStats(
            Valid: true,
            DynamicBodies: dynamic,
            KinematicBodies: kinematic,
            StaticBodies: staticCount,
            SubstepsThisFrame: _lastSubstepCount,
            StepTimeMs: _lastStepTimeMs,
            ActiveCharacterControllers: _characterStates.Count);
    }

    private void SyncPoseIntegratorGravity()
    {
        if (_simulation == null)
            return;

        _scene.PhysicsSettings.Normalize();
        var poseIntegrator = _simulation.PoseIntegrator;
        _poseIntegratorCallbacksField ??= poseIntegrator.GetType().GetField("Callbacks", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (_poseIntegratorCallbacksField?.GetValue(poseIntegrator) is PhysicsPoseIntegratorCallbacks callbacks)
        {
            callbacks.Gravity = _scene.PhysicsSettings.Gravity;
            _poseIntegratorCallbacksField.SetValue(poseIntegrator, callbacks);
        }
    }

    public void OnComponentStateChanged()
    {
        // Reconciliation runs each frame; this hook exists so components can signal immediate intent.
    }

    public void OnEntityRemoved(Entity entity)
    {
        if (_simulation == null)
            return;

        if (_bodyStates.TryGetValue(entity.Id, out var state))
        {
            RemoveBodyState(state, entityRemoved: true);
            _bodyStates.Remove(entity.Id);
        }

        RemoveCharacterStateIfPresent(entity.Id);

        _warnedNoCollider.Remove(entity.Id);
        _warnedParented.Remove(entity.Id);
        _warnedMultipleColliders.Remove(entity.Id);
        _warnedMultipleRigidbodies.Remove(entity.Id);
        _warnedCharacterMissingRigidbody.Remove(entity.Id);
        _warnedCharacterMissingCapsule.Remove(entity.Id);
        _warnedCharacterWrongMotionType.Remove(entity.Id);
        _warnedCharacterParented.Remove(entity.Id);
        _warnedCharacterNonCapsuleBody.Remove(entity.Id);
        _warnedKinematicDiscontinuity.Remove(entity.Id);
    }

    public bool TryGetLinearVelocity(RigidbodyComponent rigidbody, out Vector3 velocity)
    {
        velocity = Vector3.Zero;
        if (_simulation == null)
            return false;

        if (!_bodyStates.TryGetValue(rigidbody.Entity.Id, out var state))
            return false;
        if (state.BodyHandle is not BodyHandle handle)
            return false;
        if (!_simulation.Bodies.BodyExists(handle))
            return false;

        var body = _simulation.Bodies.GetBodyReference(handle);
        velocity = body.Velocity.Linear;
        return true;
    }

    public void SetLinearVelocity(RigidbodyComponent rigidbody, Vector3 velocity)
    {
        if (_simulation == null)
            return;

        if (!_bodyStates.TryGetValue(rigidbody.Entity.Id, out var state))
            return;
        if (state.BodyHandle is not BodyHandle handle)
            return;
        if (!_simulation.Bodies.BodyExists(handle))
            return;

        var body = _simulation.Bodies.GetBodyReference(handle);
        body.Velocity.Linear = velocity;
        body.Awake = true;
    }

    public void TeleportBody(RigidbodyComponent rigidbody, Vector3 position, Quaternion rotation, bool resetVelocity)
    {
        var transform = rigidbody.Entity.Transform;
        var normalizedRotation = NormalizeOrIdentity(rotation);
        transform.LocalPosition = position;
        transform.LocalRotation = normalizedRotation;

        if (_simulation == null)
        {
            if (resetVelocity)
                rigidbody.InitialLinearVelocity = Vector3.Zero;
            return;
        }

        if (!_bodyStates.TryGetValue(rigidbody.Entity.Id, out var state))
        {
            if (resetVelocity)
                rigidbody.InitialLinearVelocity = Vector3.Zero;
            return;
        }

        state.AuthoritativeLocalPosition = position;
        state.AuthoritativeLocalRotation = normalizedRotation;
        state.LastPublishedVisualLocalPosition = position;
        state.LastPublishedVisualLocalRotation = normalizedRotation;
        state.HasPublishedVisualPose = true;

        if (state.MotionType == BodyMotionType.Static)
        {
            if (state.StaticHandle is StaticHandle staticHandle && _simulation.Statics.StaticExists(staticHandle))
            {
                var staticRef = _simulation.Statics.GetStaticReference(staticHandle);
                staticRef.Pose = BuildBodyPose(position, normalizedRotation, transform.LocalScale, state.Collider);
                staticRef.UpdateBounds();
            }

            return;
        }

        if (state.BodyHandle is not BodyHandle bodyHandle || !_simulation.Bodies.BodyExists(bodyHandle))
            return;

        var hasCharacterController = state.Entity.GetComponent<CharacterControllerComponent>() is { Enabled: true };
        var targetPose = hasCharacterController && state.Collider is CapsuleColliderComponent capsule
            ? BuildCharacterBodyPose(position, transform.LocalScale, capsule)
            : BuildBodyPose(position, normalizedRotation, transform.LocalScale, state.Collider);

        var body = _simulation.Bodies.GetBodyReference(bodyHandle);
        body.Pose = targetPose;
        if (state.MotionType == BodyMotionType.Kinematic)
        {
            // Teleports should not inject one-frame kinematic velocities into contacts.
            body.Velocity.Linear = Vector3.Zero;
            body.Velocity.Angular = Vector3.Zero;
            if (resetVelocity)
                rigidbody.InitialLinearVelocity = Vector3.Zero;
            state.PreviousKinematicTargetPose = targetPose;
            state.HasPreviousKinematicTargetPose = true;
        }
        else if (resetVelocity)
        {
            body.Velocity.Linear = Vector3.Zero;
            body.Velocity.Angular = Vector3.Zero;
            rigidbody.InitialLinearVelocity = Vector3.Zero;
        }

        body.Awake = true;
        RefreshDynamicLockAnchors(state, targetPose);
        SnapSimulationPoseHistory(state, targetPose, suppressInterpolation: true);
    }

    public void PublishInterpolatedVisualPoses()
    {
        if (_simulation == null)
            return;

        var projSettings = PhysicsProjectSettings.Current;
        projSettings.Normalize();

        var fixedDt = projSettings.FixedTimestep;
        if (!float.IsFinite(fixedDt) || fixedDt <= 0f)
            return;

        var alpha = Math.Clamp(_accumulator / fixedDt, 0f, 1f);

        foreach (var state in _bodyStates.Values)
        {
            if (state.MotionType != BodyMotionType.Dynamic)
            {
                state.HasPublishedVisualPose = false;
                state.SuppressInterpolationForFrame = false;
                continue;
            }

            var transform = state.Entity.Transform;
            var currentTransformPosition = transform.LocalPosition;
            var currentTransformRotation = NormalizeOrIdentity(transform.LocalRotation);
            var hasCharacterController = state.Entity.GetComponent<CharacterControllerComponent>() is { Enabled: true };

            if (hasCharacterController)
                state.AuthoritativeLocalRotation = currentTransformRotation;

            var positionExternallyEdited = state.HasPublishedVisualPose
                ? !ApproximatelyEqual(currentTransformPosition, state.LastPublishedVisualLocalPosition)
                : !ApproximatelyEqual(currentTransformPosition, state.AuthoritativeLocalPosition);
            var rotationExternallyEdited = !hasCharacterController && (state.HasPublishedVisualPose
                ? !ApproximatelyEqual(currentTransformRotation, state.LastPublishedVisualLocalRotation)
                : !ApproximatelyEqual(currentTransformRotation, state.AuthoritativeLocalRotation));
            if (positionExternallyEdited || rotationExternallyEdited)
            {
                state.SuppressInterpolationForFrame = true;
                continue;
            }

            var authoritativePosition = state.AuthoritativeLocalPosition;
            var authoritativeRotation = state.AuthoritativeLocalRotation;

            if (!ShouldInterpolateBody(state, projSettings) || !state.HasSimulationPoseHistory)
            {
                transform.LocalPosition = authoritativePosition;
                transform.LocalRotation = authoritativeRotation;
                state.LastPublishedVisualLocalPosition = authoritativePosition;
                state.LastPublishedVisualLocalRotation = authoritativeRotation;
                state.HasPublishedVisualPose = true;
                state.SuppressInterpolationForFrame = false;
                continue;
            }

            var previousPose = state.PreviousSimulationPose;
            var currentPose = state.CurrentSimulationPose;
            var useCurrentPose = state.SuppressInterpolationForFrame;
            var visualPose = useCurrentPose
                ? currentPose
                : InterpolatePose(previousPose, currentPose, alpha);

            var visualPosition = visualPose.Position - ComputeWorldCenterOffset(state.Collider, transform.LocalScale, visualPose.Orientation);
            var visualRotation = hasCharacterController
                ? authoritativeRotation
                : NormalizeOrIdentity(visualPose.Orientation);

            transform.LocalPosition = visualPosition;
            transform.LocalRotation = visualRotation;
            state.LastPublishedVisualLocalPosition = visualPosition;
            state.LastPublishedVisualLocalRotation = visualRotation;
            state.HasPublishedVisualPose = true;
            state.SuppressInterpolationForFrame = false;
        }
    }

    private void ReconcileParticipants()
    {
        if (_simulation == null)
            return;

        var seenEntityIds = new HashSet<Guid>();
        var entitiesWithAnyRigidbody = new HashSet<Guid>();
        var rigidbodies = _scene.GetComponents<RigidbodyComponent>();

        foreach (var rigidbody in rigidbodies)
        {
            entitiesWithAnyRigidbody.Add(rigidbody.Entity.Id);

            if (!rigidbody.Enabled || !rigidbody.Entity.Active)
                continue;
            if (rigidbody.Entity.Scene != _scene)
                continue;

            var entity = rigidbody.Entity;
            if (!seenEntityIds.Add(entity.Id))
            {
                WarnOnce(_warnedMultipleRigidbodies, entity.Id, $"Entity '{entity.Name}' has multiple RigidbodyComponent instances. Only the first one is used.");
                continue;
            }

            if (entity.Transform.Parent != null)
            {
                RemoveStateIfPresent(entity.Id);
                WarnOnce(_warnedParented, entity.Id, $"Rigidbody on '{entity.Name}' is ignored because parented rigidbodies are not supported.");
                continue;
            }
            _warnedParented.Remove(entity.Id);
            _warnedImplicitStaticParented.Remove(entity.Id);

            if (!TryGetPrimaryCollider(entity, out var collider, out var hasMultipleColliders))
            {
                RemoveStateIfPresent(entity.Id);
                WarnOnce(_warnedNoCollider, entity.Id, $"Rigidbody on '{entity.Name}' has no enabled collider and will be ignored.");
                continue;
            }
            _warnedNoCollider.Remove(entity.Id);

            if (hasMultipleColliders)
            {
                WarnOnce(_warnedMultipleColliders, entity.Id, $"Entity '{entity.Name}' has multiple collider components. Only the first enabled collider is used.");
            }
            else
            {
                _warnedMultipleColliders.Remove(entity.Id);
            }

            ReconcileBodyState(entity, rigidbody, collider, rigidbody.MotionType, configurationHash: ComputeConfigurationHash(entity, rigidbody, collider, isImplicitStatic: false), isImplicitStatic: false);
        }

        var processedImplicitStaticIds = new HashSet<Guid>();
        var colliders = _scene.GetComponents<ColliderComponent>();
        foreach (var collider in colliders)
        {
            if (!collider.Enabled || !collider.Entity.Active)
                continue;
            if (collider.Entity.Scene != _scene)
                continue;

            var entity = collider.Entity;
            if (!processedImplicitStaticIds.Add(entity.Id))
                continue;
            if (entitiesWithAnyRigidbody.Contains(entity.Id))
                continue;
            if (!seenEntityIds.Add(entity.Id))
                continue;

            if (entity.Transform.Parent != null)
            {
                RemoveStateIfPresent(entity.Id);
                WarnOnce(_warnedImplicitStaticParented, entity.Id, $"Collider-only static '{entity.Name}' is ignored because parented physics participants are not supported.");
                continue;
            }
            _warnedImplicitStaticParented.Remove(entity.Id);
            _warnedParented.Remove(entity.Id);

            if (!TryGetPrimaryCollider(entity, out var primaryCollider, out var hasMultipleColliders))
            {
                RemoveStateIfPresent(entity.Id);
                continue;
            }

            if (hasMultipleColliders)
            {
                WarnOnce(_warnedMultipleColliders, entity.Id, $"Entity '{entity.Name}' has multiple collider components. Only the first enabled collider is used.");
            }
            else
            {
                _warnedMultipleColliders.Remove(entity.Id);
            }

            ReconcileBodyState(entity, rigidbody: null, primaryCollider, BodyMotionType.Static, configurationHash: ComputeConfigurationHash(entity, null, primaryCollider, isImplicitStatic: true), isImplicitStatic: true);
        }

        var staleIds = _bodyStates.Keys.Where(id => !seenEntityIds.Contains(id)).ToList();
        foreach (var staleId in staleIds)
        {
            if (_bodyStates.TryGetValue(staleId, out var state))
                RemoveBodyState(state);
            _bodyStates.Remove(staleId);
        }

        ReconcileCharacterControllers();
    }

    private void ReconcileBodyState(
        Entity entity,
        RigidbodyComponent? rigidbody,
        ColliderComponent collider,
        BodyMotionType motionType,
        int configurationHash,
        bool isImplicitStatic)
    {
        if (_simulation == null)
            return;

        if (_bodyStates.TryGetValue(entity.Id, out var existing))
        {
            if (existing.ConfigurationHash == configurationHash &&
                ReferenceEquals(existing.Rigidbody, rigidbody) &&
                ReferenceEquals(existing.Collider, collider) &&
                existing.MotionType == motionType &&
                existing.IsImplicitStatic == isImplicitStatic)
            {
                return;
            }

            Vector3 preservedLinearVelocity = Vector3.Zero;
            Vector3 preservedAngularVelocity = Vector3.Zero;
            bool shouldPreserveVelocity = false;

            if (existing.MotionType != BodyMotionType.Static &&
                existing.BodyHandle is BodyHandle bodyHandle &&
                _simulation.Bodies.BodyExists(bodyHandle))
            {
                var body = _simulation.Bodies.GetBodyReference(bodyHandle);
                preservedLinearVelocity = body.Velocity.Linear;
                preservedAngularVelocity = body.Velocity.Angular;
                shouldPreserveVelocity = true;
            }

            RemoveBodyState(existing);
            _bodyStates.Remove(entity.Id);

            var rebuiltState = CreateBodyState(entity, rigidbody, collider, motionType, configurationHash, isImplicitStatic);
            if (rebuiltState == null)
                return;

            _bodyStates[entity.Id] = rebuiltState;

            if (shouldPreserveVelocity &&
                rebuiltState.BodyHandle is BodyHandle newBodyHandle &&
                _simulation.Bodies.BodyExists(newBodyHandle))
            {
                var newBody = _simulation.Bodies.GetBodyReference(newBodyHandle);
                newBody.Velocity.Linear = preservedLinearVelocity;
                newBody.Velocity.Angular = preservedAngularVelocity;
                newBody.Awake = true;
            }

            return;
        }

        var newState = CreateBodyState(entity, rigidbody, collider, motionType, configurationHash, isImplicitStatic);
        if (newState != null)
            _bodyStates[entity.Id] = newState;
    }

    private void ReconcileCharacterControllers()
    {
        if (_simulation == null || _characterControllers == null)
            return;

        var seenEntityIds = new HashSet<Guid>();
        var controllers = _scene.GetComponents<CharacterControllerComponent>();

        foreach (var controller in controllers)
        {
            if (!controller.Enabled || !controller.Entity.Active)
                continue;
            if (controller.Entity.Scene != _scene)
                continue;

            var entity = controller.Entity;
            if (!seenEntityIds.Add(entity.Id))
                continue;

            if (entity.Transform.Parent != null)
            {
                RemoveCharacterStateIfPresent(entity.Id);
                WarnOnce(_warnedCharacterParented, entity.Id, $"Character controller on '{entity.Name}' is ignored because parented rigidbodies are not supported.");
                continue;
            }
            _warnedCharacterParented.Remove(entity.Id);

            var rigidbody = entity.GetComponent<RigidbodyComponent>();
            if (rigidbody == null || !rigidbody.Enabled)
            {
                RemoveCharacterStateIfPresent(entity.Id);
                WarnOnce(_warnedCharacterMissingRigidbody, entity.Id, $"Character controller on '{entity.Name}' requires an enabled RigidbodyComponent.");
                continue;
            }
            _warnedCharacterMissingRigidbody.Remove(entity.Id);

            var capsule = entity.GetComponent<CapsuleColliderComponent>();
            if (capsule == null || !capsule.Enabled)
            {
                RemoveCharacterStateIfPresent(entity.Id);
                WarnOnce(_warnedCharacterMissingCapsule, entity.Id, $"Character controller on '{entity.Name}' requires an enabled CapsuleColliderComponent.");
                continue;
            }
            _warnedCharacterMissingCapsule.Remove(entity.Id);

            if (rigidbody.MotionType != BodyMotionType.Dynamic)
            {
                RemoveCharacterStateIfPresent(entity.Id);
                WarnOnce(_warnedCharacterWrongMotionType, entity.Id, $"Character controller on '{entity.Name}' requires Rigidbody MotionType = Dynamic.");
                continue;
            }
            _warnedCharacterWrongMotionType.Remove(entity.Id);

            if (!_bodyStates.TryGetValue(entity.Id, out var bodyState) ||
                bodyState.BodyHandle is not BodyHandle bodyHandle ||
                !_simulation.Bodies.BodyExists(bodyHandle))
            {
                RemoveCharacterStateIfPresent(entity.Id);
                continue;
            }

            if (bodyState.Collider is not CapsuleColliderComponent primaryCapsule || !ReferenceEquals(primaryCapsule, capsule))
            {
                RemoveCharacterStateIfPresent(entity.Id);
                WarnOnce(_warnedCharacterNonCapsuleBody, entity.Id, $"Character controller on '{entity.Name}' requires the primary enabled collider to be the capsule.");
                continue;
            }
            _warnedCharacterNonCapsuleBody.Remove(entity.Id);

            EnsureCharacterState(entity, rigidbody, primaryCapsule, controller, bodyHandle);
        }

        var staleIds = _characterStates.Keys.Where(id => !seenEntityIds.Contains(id)).ToList();
        foreach (var staleId in staleIds)
            RemoveCharacterStateIfPresent(staleId);
    }

    private void EnsureCharacterState(
        Entity entity,
        RigidbodyComponent rigidbody,
        CapsuleColliderComponent capsule,
        CharacterControllerComponent controller,
        BodyHandle bodyHandle)
    {
        if (_characterControllers == null)
            return;

        if (_characterStates.TryGetValue(entity.Id, out var existing))
        {
            if (existing.BodyHandle.Value != bodyHandle.Value)
            {
                RemoveCharacterStateIfPresent(entity.Id);
            }
            else
            {
                existing.Entity = entity;
                existing.Rigidbody = rigidbody;
                existing.Capsule = capsule;
                existing.Controller = controller;
                return;
            }
        }

        ref var character = ref _characterControllers.AllocateCharacter(bodyHandle);
        character.BodyHandle = bodyHandle;
        character.LocalUp = Vector3.UnitY;
        character.ViewDirection = entity.Transform.Forward;
        character.TargetVelocity = Vector2.Zero;

        _characterStates[entity.Id] = new CharacterControllerRuntimeState
        {
            Entity = entity,
            Rigidbody = rigidbody,
            Capsule = capsule,
            Controller = controller,
            BodyHandle = bodyHandle,
            FrameInput = default
        };
    }

    private void RemoveCharacterStateIfPresent(Guid entityId)
    {
        if (!_characterStates.TryGetValue(entityId, out var state))
            return;

        if (_characterControllers != null)
            _characterControllers.RemoveCharacterByBodyHandle(state.BodyHandle);

        state.Controller.SetRuntimeState(false, Vector3.Zero);
        _characterStates.Remove(entityId);
    }

    private void RemoveCharacterStateByBodyHandle(BodyHandle bodyHandle)
    {
        if (_characterStates.Count == 0)
            return;

        Guid? matchedEntityId = null;
        foreach (var pair in _characterStates)
        {
            if (pair.Value.BodyHandle.Value != bodyHandle.Value)
                continue;

            matchedEntityId = pair.Key;
            break;
        }

        if (matchedEntityId.HasValue)
            RemoveCharacterStateIfPresent(matchedEntityId.Value);
    }

    private void ApplyCharacterGoalsForStep(float stepDt, bool allowJump)
    {
        if (_simulation == null || _characterControllers == null || _characterStates.Count == 0)
            return;

        _characterBridge.ApplyGoalsForStep(_simulation, _characterControllers, _characterStates.Values, stepDt, allowJump);
    }

    private void SyncCharacterRuntimeState()
    {
        if (_simulation == null || _characterControllers == null || _characterStates.Count == 0)
            return;

        _characterBridge.SyncRuntimeState(_simulation, _characterControllers, _characterStates.Values);
    }

    private PhysicsBodyState? CreateBodyState(
        Entity entity,
        RigidbodyComponent? rigidbody,
        ColliderComponent collider,
        BodyMotionType motionType,
        int configurationHash,
        bool isImplicitStatic)
    {
        if (_simulation == null)
            return null;

        var transform = entity.Transform;
        var authoritativePosition = transform.LocalPosition;
        var authoritativeRotation = NormalizeOrIdentity(transform.LocalRotation);
        var mass = MathF.Max(0.0001f, rigidbody?.Mass ?? 1f);
        var shapeResult = CreateShape(collider, transform.LocalScale, mass);
        var hasCharacterController = motionType == BodyMotionType.Dynamic && entity.GetComponent<CharacterControllerComponent>() is { Enabled: true };
        var pose = hasCharacterController && collider is CapsuleColliderComponent capsuleCollider
            ? BuildCharacterBodyPose(authoritativePosition, transform.LocalScale, capsuleCollider)
            : BuildBodyPose(authoritativePosition, authoritativeRotation, transform.LocalScale, collider);
        var continuity = rigidbody?.ContinuousDetection == true
            ? ContinuousDetection.Continuous()
            : ContinuousDetection.Discrete;
        var collidable = new CollidableDescription(shapeResult.ShapeIndex, 0.1f, continuity);
        var material = new PhysicsMaterial(collider.Friction, collider.Restitution);

        BodyHandle? bodyHandle = null;
        StaticHandle? staticHandle = null;

        switch (motionType)
        {
            case BodyMotionType.Dynamic:
            {
                var dynamicInertia = shapeResult.DynamicInertia;
                if (hasCharacterController)
                {
                    dynamicInertia.InverseInertiaTensor = default;
                }

                var velocity = new BodyVelocity { Linear = rigidbody?.InitialLinearVelocity ?? Vector3.Zero };
                var activity = new BodyActivityDescription(0.01f);
                var description = BodyDescription.CreateDynamic(pose, velocity, dynamicInertia, collidable, activity);
                bodyHandle = _simulation.Bodies.Add(description);
                _materialTable.Set(bodyHandle.Value, material);
                _bodyHandleToEntityId[bodyHandle.Value.Value] = entity.Id;
                if (collider.IsTrigger)
                    _triggerBodyHandles.Add(bodyHandle.Value.Value);
                break;
            }
            case BodyMotionType.Kinematic:
            {
                var velocity = new BodyVelocity { Linear = rigidbody?.InitialLinearVelocity ?? Vector3.Zero };
                var activity = new BodyActivityDescription(0.01f);
                var description = BodyDescription.CreateKinematic(pose, velocity, collidable, activity);
                bodyHandle = _simulation.Bodies.Add(description);
                _materialTable.Set(bodyHandle.Value, material);
                _bodyHandleToEntityId[bodyHandle.Value.Value] = entity.Id;
                if (collider.IsTrigger)
                    _triggerBodyHandles.Add(bodyHandle.Value.Value);
                break;
            }
            case BodyMotionType.Static:
            {
                var description = new StaticDescription(pose, shapeResult.ShapeIndex, continuity);
                staticHandle = _simulation.Statics.Add(description);
                _materialTable.Set(staticHandle.Value, material);
                _staticHandleToEntityId[staticHandle.Value.Value] = entity.Id;
                if (collider.IsTrigger)
                    _triggerStaticHandles.Add(staticHandle.Value.Value);
                break;
            }
            default:
                return null;
        }

        return new PhysicsBodyState
        {
            Entity = entity,
            Rigidbody = rigidbody,
            Collider = collider,
            ShapeIndex = shapeResult.ShapeIndex,
            MotionType = motionType,
            IsImplicitStatic = isImplicitStatic,
            BodyHandle = bodyHandle,
            StaticHandle = staticHandle,
            ConfigurationHash = configurationHash,
            LockedPosition = pose.Position,
            LockReferenceOrientation = NormalizeOrIdentity(pose.Orientation),
            RotationLockMask = GetRotationLockMask(rigidbody),
            AuthoritativeLocalPosition = authoritativePosition,
            AuthoritativeLocalRotation = authoritativeRotation,
            LastPublishedVisualLocalPosition = authoritativePosition,
            LastPublishedVisualLocalRotation = authoritativeRotation,
            HasPublishedVisualPose = true,
            PreviousSimulationPose = pose,
            CurrentSimulationPose = pose,
            HasSimulationPoseHistory = true,
            PreviousKinematicTargetPose = pose,
            HasPreviousKinematicTargetPose = motionType == BodyMotionType.Kinematic
        };
    }

    private void RemoveStateIfPresent(Guid entityId)
    {
        if (_bodyStates.TryGetValue(entityId, out var state))
        {
            RemoveBodyState(state);
            _bodyStates.Remove(entityId);
        }
    }

    private void RemoveBodyState(PhysicsBodyState state, bool entityRemoved = false, bool dispatchExitCallbacks = true)
    {
        if (_simulation == null)
            return;

        if (dispatchExitCallbacks)
            FlushInteractionExitsForEntity(state.Entity.Id, entityRemoved);

        if (state.BodyHandle is BodyHandle bodyHandle)
        {
            RemoveCharacterStateByBodyHandle(bodyHandle);
            _materialTable.Remove(bodyHandle);
            _bodyHandleToEntityId.Remove(bodyHandle.Value);
            _triggerBodyHandles.Remove(bodyHandle.Value);
            if (_simulation.Bodies.BodyExists(bodyHandle))
                _simulation.Bodies.Remove(bodyHandle);
        }

        if (state.StaticHandle is StaticHandle staticHandle)
        {
            _materialTable.Remove(staticHandle);
            _staticHandleToEntityId.Remove(staticHandle.Value);
            _triggerStaticHandles.Remove(staticHandle.Value);
            if (_simulation.Statics.StaticExists(staticHandle))
                _simulation.Statics.Remove(staticHandle);
        }

        _simulation.Shapes.Remove(state.ShapeIndex);
    }

    private void PushSceneTransformsToPhysics(float stepDt)
    {
        if (_simulation == null)
            return;

        foreach (var state in _bodyStates.Values)
        {
            var transform = state.Entity.Transform;
            var currentTransformPosition = transform.LocalPosition;
            var currentTransformRotation = NormalizeOrIdentity(transform.LocalRotation);
            var hasCharacterController = state.Entity.GetComponent<CharacterControllerComponent>() is { Enabled: true };

            if (state.MotionType == BodyMotionType.Static)
            {
                if (state.StaticHandle is not StaticHandle staticHandle || !_simulation.Statics.StaticExists(staticHandle))
                    continue;

                state.AuthoritativeLocalPosition = currentTransformPosition;
                state.AuthoritativeLocalRotation = currentTransformRotation;
                var targetPose = BuildBodyPose(state.AuthoritativeLocalPosition, state.AuthoritativeLocalRotation, transform.LocalScale, state.Collider);
                var staticRef = _simulation.Statics.GetStaticReference(staticHandle);
                staticRef.Pose = targetPose;
                staticRef.UpdateBounds();
                continue;
            }

            if (state.BodyHandle is not BodyHandle bodyHandle || !_simulation.Bodies.BodyExists(bodyHandle))
                continue;

            var body = _simulation.Bodies.GetBodyReference(bodyHandle);
            if (state.MotionType == BodyMotionType.Kinematic)
            {
                state.AuthoritativeLocalPosition = currentTransformPosition;
                state.AuthoritativeLocalRotation = currentTransformRotation;
                var targetPose = BuildBodyPose(state.AuthoritativeLocalPosition, state.AuthoritativeLocalRotation, transform.LocalScale, state.Collider);
                var previousTargetPose = state.HasPreviousKinematicTargetPose
                    ? state.PreviousKinematicTargetPose
                    : body.Pose;

                var previousOrientation = NormalizeOrIdentity(previousTargetPose.Orientation);
                var targetOrientation = EnsureSameHemisphere(previousOrientation, targetPose.Orientation);
                targetPose = new RigidPose(targetPose.Position, targetOrientation);

                var linearVelocity = (targetPose.Position - previousTargetPose.Position) / stepDt;
                var angularVelocity = ComputeAngularVelocity(previousOrientation, targetOrientation, stepDt);
                var linearSpeed = linearVelocity.Length();
                var angularStep = ComputeAngularStepRadians(previousOrientation, targetOrientation);

                var maxAngularStepRadians = MaxKinematicAngularStepDegrees * (MathF.PI / 180f);
                var maxDiscontinuityLinearSpeed = MaxKinematicLinearSpeed * KinematicDiscontinuityLinearSpeedMultiplier;
                var hasDiscontinuity = linearSpeed > maxDiscontinuityLinearSpeed ||
                                       angularStep > maxAngularStepRadians;

                if (hasDiscontinuity)
                {
                    body.Velocity.Linear = Vector3.Zero;
                    body.Velocity.Angular = Vector3.Zero;
                    WarnOnce(
                        _warnedKinematicDiscontinuity,
                        state.Entity.Id,
                        $"Kinematic body '{state.Entity.Name}' had a discontinuous target step. Velocities were suppressed for stability.");
                }
                else
                {
                    body.Velocity.Linear = ClampMagnitude(linearVelocity, MaxKinematicLinearSpeed);
                    body.Velocity.Angular = ClampMagnitude(angularVelocity, MaxKinematicAngularSpeed);
                }

                body.Pose = targetPose;
                body.Awake = true;
                state.PreviousKinematicTargetPose = targetPose;
                state.HasPreviousKinematicTargetPose = true;
                continue;
            }

            var positionExternallyEdited = state.HasPublishedVisualPose
                ? !ApproximatelyEqual(currentTransformPosition, state.LastPublishedVisualLocalPosition)
                : !ApproximatelyEqual(currentTransformPosition, state.AuthoritativeLocalPosition);
            var rotationExternallyEdited = !hasCharacterController && (state.HasPublishedVisualPose
                ? !ApproximatelyEqual(currentTransformRotation, state.LastPublishedVisualLocalRotation)
                : !ApproximatelyEqual(currentTransformRotation, state.AuthoritativeLocalRotation));

            if (hasCharacterController)
            {
                state.AuthoritativeLocalRotation = currentTransformRotation;

                if (positionExternallyEdited)
                {
                    var currentPose = state.Collider is CapsuleColliderComponent capsule
                        ? BuildCharacterBodyPose(currentTransformPosition, transform.LocalScale, capsule)
                        : body.Pose;
                    body.Pose = currentPose;
                    body.Awake = true;

                    state.AuthoritativeLocalPosition = currentTransformPosition;
                    state.LastPublishedVisualLocalPosition = currentTransformPosition;
                    state.LastPublishedVisualLocalRotation = currentTransformRotation;
                    state.HasPublishedVisualPose = true;
                    RefreshDynamicLockAnchors(state, currentPose);
                    SnapSimulationPoseHistory(state, currentPose, suppressInterpolation: true);
                }

                continue;
            }

            if (positionExternallyEdited || rotationExternallyEdited)
            {
                state.AuthoritativeLocalPosition = currentTransformPosition;
                state.AuthoritativeLocalRotation = currentTransformRotation;
                var targetPose = BuildBodyPose(state.AuthoritativeLocalPosition, state.AuthoritativeLocalRotation, transform.LocalScale, state.Collider);
                body.Pose = targetPose;
                body.Awake = true;
                state.LastPublishedVisualLocalPosition = currentTransformPosition;
                state.LastPublishedVisualLocalRotation = currentTransformRotation;
                state.HasPublishedVisualPose = true;
                RefreshDynamicLockAnchors(state, targetPose);
                SnapSimulationPoseHistory(state, targetPose, suppressInterpolation: true);
            }
        }
    }

    private void ApplyPendingForces(float stepDt)
    {
        if (_simulation == null)
            return;

        foreach (var state in _bodyStates.Values)
        {
            if (state.MotionType != BodyMotionType.Dynamic || state.Rigidbody == null)
                continue;
            if (state.BodyHandle is not BodyHandle bodyHandle || !_simulation.Bodies.BodyExists(bodyHandle))
                continue;

            state.Rigidbody.ConsumePendingForces(out var force, out var impulse);
            if (force == Vector3.Zero && impulse == Vector3.Zero)
                continue;

            var combinedImpulse = impulse + force * stepDt;
            if (combinedImpulse == Vector3.Zero)
                continue;

            var body = _simulation.Bodies.GetBodyReference(bodyHandle);
            body.ApplyLinearImpulse(combinedImpulse);
            body.Awake = true;
        }
    }

    private void ApplyPostStepBodyModifiers(float stepDt)
    {
        if (_simulation == null)
            return;

        foreach (var state in _bodyStates.Values)
        {
            if (state.MotionType != BodyMotionType.Dynamic || state.Rigidbody == null)
                continue;
            if (state.BodyHandle is not BodyHandle bodyHandle || !_simulation.Bodies.BodyExists(bodyHandle))
                continue;

            var body = _simulation.Bodies.GetBodyReference(bodyHandle);
            var linear = body.Velocity.Linear;
            var angular = body.Velocity.Angular;

            var linearDampingFactor = MathF.Pow(Math.Clamp(1f - state.Rigidbody.LinearDamping, 0f, 1f), stepDt);
            var angularDampingFactor = MathF.Pow(Math.Clamp(1f - state.Rigidbody.AngularDamping, 0f, 1f), stepDt);
            linear *= linearDampingFactor;
            angular *= angularDampingFactor;

            var pose = body.Pose;
            var position = pose.Position;
            var lockedPosition = state.LockedPosition;

            if (state.Rigidbody.LockPositionX) { position.X = lockedPosition.X; linear.X = 0f; } else { lockedPosition.X = position.X; }
            if (state.Rigidbody.LockPositionY) { position.Y = lockedPosition.Y; linear.Y = 0f; } else { lockedPosition.Y = position.Y; }
            if (state.Rigidbody.LockPositionZ) { position.Z = lockedPosition.Z; linear.Z = 0f; } else { lockedPosition.Z = position.Z; }
            state.LockedPosition = lockedPosition;

            var rotationLockMask = GetRotationLockMask(state.Rigidbody);
            if (rotationLockMask == 0)
            {
                state.LockReferenceOrientation = NormalizeOrIdentity(pose.Orientation);
                state.RotationLockMask = 0;
            }
            else
            {
                if (state.RotationLockMask != rotationLockMask)
                    state.LockReferenceOrientation = NormalizeOrIdentity(pose.Orientation);

                ApplyWorldRotationLocksStrict(ref pose, ref angular, state, rotationLockMask);
                state.RotationLockMask = rotationLockMask;
            }

            pose.Position = position;
            pose.Orientation = NormalizeOrIdentity(pose.Orientation);
            body.Pose = pose;
            body.Velocity.Linear = linear;
            body.Velocity.Angular = angular;
        }
    }

    private void SyncDynamicTransformsFromPhysics()
    {
        if (_simulation == null)
            return;

        foreach (var state in _bodyStates.Values)
        {
            if (state.MotionType != BodyMotionType.Dynamic)
                continue;
            if (state.BodyHandle is not BodyHandle bodyHandle || !_simulation.Bodies.BodyExists(bodyHandle))
                continue;

            var body = _simulation.Bodies.GetBodyReference(bodyHandle);
            var pose = body.Pose;
            var transform = state.Entity.Transform;
            var hasCharacterController = state.Entity.GetComponent<CharacterControllerComponent>() is { Enabled: true };

            var offset = ComputeWorldCenterOffset(state.Collider, transform.LocalScale, pose.Orientation);
            var authoritativePosition = pose.Position - offset;
            var authoritativeRotation = state.AuthoritativeLocalRotation;
            if (!hasCharacterController)
                authoritativeRotation = NormalizeOrIdentity(pose.Orientation);

            state.AuthoritativeLocalPosition = authoritativePosition;
            state.AuthoritativeLocalRotation = authoritativeRotation;
            CaptureSimulationPoseAfterStep(state, pose);
        }
    }

    private ShapeCreationResult CreateShape(ColliderComponent collider, Vector3 localScale, float mass)
    {
        if (_simulation == null)
            throw new InvalidOperationException("Physics simulation is not initialized.");

        var absScale = new Vector3(MathF.Abs(localScale.X), MathF.Abs(localScale.Y), MathF.Abs(localScale.Z));
        absScale.X = MathF.Max(absScale.X, 0.0001f);
        absScale.Y = MathF.Max(absScale.Y, 0.0001f);
        absScale.Z = MathF.Max(absScale.Z, 0.0001f);

        switch (collider)
        {
            case BoxColliderComponent box:
            {
                var dimensions = box.Size * absScale;
                dimensions.X = MathF.Max(0.001f, dimensions.X);
                dimensions.Y = MathF.Max(0.001f, dimensions.Y);
                dimensions.Z = MathF.Max(0.001f, dimensions.Z);
                var shape = new Box(dimensions.X, dimensions.Y, dimensions.Z);
                return new ShapeCreationResult
                {
                    // TODO: If collider resizes become hot, replace per-participant ownership with a ref-counted shared shape pool.
                    ShapeIndex = _simulation.Shapes.Add(shape),
                    DynamicInertia = shape.ComputeInertia(mass)
                };
            }
            case SphereColliderComponent sphere:
            {
                var radiusScale = MathF.Max(absScale.X, MathF.Max(absScale.Y, absScale.Z));
                var radius = MathF.Max(0.001f, sphere.Radius * radiusScale);
                var shape = new Sphere(radius);
                return new ShapeCreationResult
                {
                    ShapeIndex = _simulation.Shapes.Add(shape),
                    DynamicInertia = shape.ComputeInertia(mass)
                };
            }
            case CapsuleColliderComponent capsule:
            {
                var shape = CreateScaledCapsule(capsule, absScale, capsule.Length);
                return new ShapeCreationResult
                {
                    ShapeIndex = _simulation.Shapes.Add(shape),
                    DynamicInertia = shape.ComputeInertia(mass)
                };
            }
            default:
                throw new NotSupportedException($"Collider type '{collider.GetType().Name}' is not supported.");
        }
    }

    private static RigidPose BuildBodyPose(TransformComponent transform, ColliderComponent collider)
    {
        return BuildBodyPose(transform.LocalPosition, transform.LocalRotation, transform.LocalScale, collider);
    }

    private static RigidPose BuildBodyPose(Vector3 position, Quaternion rotation, Vector3 localScale, ColliderComponent collider)
    {
        var orientation = NormalizeOrIdentity(rotation);
        var offset = ComputeWorldCenterOffset(collider, localScale, orientation);
        return new RigidPose(position + offset, orientation);
    }

    private static RigidPose BuildCharacterBodyPose(Vector3 position, Vector3 localScale, CapsuleColliderComponent capsule)
    {
        var orientation = Quaternion.Identity;
        var offset = ComputeWorldCenterOffset(capsule, localScale, orientation);
        return new RigidPose(position + offset, orientation);
    }

    private static Vector3 ComputeWorldCenterOffset(ColliderComponent collider, Vector3 localScale, Quaternion orientation)
    {
        var scaledCenter = new Vector3(
            collider.Center.X * localScale.X,
            collider.Center.Y * localScale.Y,
            collider.Center.Z * localScale.Z);
        return Vector3.Transform(scaledCenter, orientation);
    }

    private static Capsule CreateScaledCapsule(CapsuleColliderComponent capsule, Vector3 absScale, float lengthOverride)
    {
        var radiusScale = MathF.Max(absScale.X, absScale.Z);
        var radius = MathF.Max(0.001f, capsule.Radius * radiusScale);
        var length = MathF.Max(0.001f, lengthOverride * absScale.Y);
        return new Capsule(radius, length);
    }

    private static int ComputeConfigurationHash(Entity entity, RigidbodyComponent? rigidbody, ColliderComponent collider, bool isImplicitStatic)
    {
        var hash = new HashCode();
        hash.Add(isImplicitStatic);
        if (rigidbody != null)
        {
            hash.Add(rigidbody.SettingsVersion);
            hash.Add((int)rigidbody.MotionType);
        }
        hash.Add(collider.SettingsVersion);
        hash.Add(collider.GetType());
        hash.Add(collider.IsTrigger);
        hash.Add(entity.Transform.LocalScale.X);
        hash.Add(entity.Transform.LocalScale.Y);
        hash.Add(entity.Transform.LocalScale.Z);
        hash.Add(!isImplicitStatic && entity.GetComponent<CharacterControllerComponent>() is { Enabled: true });
        return hash.ToHashCode();
    }

    private static byte GetRotationLockMask(RigidbodyComponent? rigidbody)
    {
        if (rigidbody == null)
            return 0;

        byte mask = 0;
        if (rigidbody.LockRotationX)
            mask |= RotationLockXMask;
        if (rigidbody.LockRotationY)
            mask |= RotationLockYMask;
        if (rigidbody.LockRotationZ)
            mask |= RotationLockZMask;
        return mask;
    }

    private static void ApplyWorldRotationLocksStrict(ref RigidPose pose, ref Vector3 angularVelocity, PhysicsBodyState state, byte lockMask)
    {
        if ((lockMask & RotationLockXMask) != 0)
            angularVelocity.X = 0f;
        if ((lockMask & RotationLockYMask) != 0)
            angularVelocity.Y = 0f;
        if ((lockMask & RotationLockZMask) != 0)
            angularVelocity.Z = 0f;

        pose.Orientation = FilterRotationDeltaByMask(state.LockReferenceOrientation, pose.Orientation, lockMask);
    }

    private static Quaternion FilterRotationDeltaByMask(Quaternion reference, Quaternion current, byte lockMask)
    {
        var normalizedReference = NormalizeOrIdentity(reference);
        var normalizedCurrent = NormalizeOrIdentity(current);

        var delta = NormalizeOrIdentity(normalizedCurrent * Quaternion.Conjugate(normalizedReference));
        var rotationVector = QuaternionToRotationVector(delta);
        if ((lockMask & RotationLockXMask) != 0)
            rotationVector.X = 0f;
        if ((lockMask & RotationLockYMask) != 0)
            rotationVector.Y = 0f;
        if ((lockMask & RotationLockZMask) != 0)
            rotationVector.Z = 0f;

        var filteredDelta = RotationVectorToQuaternion(rotationVector);
        return NormalizeOrIdentity(filteredDelta * normalizedReference);
    }

    private static Vector3 QuaternionToRotationVector(Quaternion rotation)
    {
        var normalized = NormalizeOrIdentity(rotation);
        if (normalized.W < 0f)
            normalized = new Quaternion(-normalized.X, -normalized.Y, -normalized.Z, -normalized.W);

        var w = Math.Clamp(normalized.W, -1f, 1f);
        var angle = 2f * MathF.Acos(w);
        if (angle < 1e-6f)
            return Vector3.Zero;

        var sinHalf = MathF.Sqrt(MathF.Max(0f, 1f - w * w));
        if (sinHalf < 1e-6f)
            return Vector3.Zero;

        var axis = new Vector3(normalized.X, normalized.Y, normalized.Z) / sinHalf;
        return axis * angle;
    }

    private static Quaternion RotationVectorToQuaternion(Vector3 rotationVector)
    {
        var angle = rotationVector.Length();
        if (angle < 1e-6f)
            return Quaternion.Identity;

        var axis = rotationVector / angle;
        var halfAngle = angle * 0.5f;
        var sinHalf = MathF.Sin(halfAngle);
        var cosHalf = MathF.Cos(halfAngle);
        var rotation = new Quaternion(axis.X * sinHalf, axis.Y * sinHalf, axis.Z * sinHalf, cosHalf);
        return NormalizeOrIdentity(rotation);
    }

    private static bool IsFinite(Quaternion rotation)
    {
        return float.IsFinite(rotation.X) &&
               float.IsFinite(rotation.Y) &&
               float.IsFinite(rotation.Z) &&
               float.IsFinite(rotation.W);
    }

    private static Quaternion NormalizeOrIdentity(Quaternion rotation)
    {
        if (!IsFinite(rotation))
            return Quaternion.Identity;

        var lengthSquared = rotation.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= 1e-12f)
            return Quaternion.Identity;

        return Quaternion.Normalize(rotation);
    }

    private static bool TryGetPrimaryCollider(Entity entity, out ColliderComponent collider, out bool hasMultiple)
    {
        collider = null!;
        hasMultiple = false;

        ColliderComponent? first = null;
        int count = 0;

        foreach (var component in entity.Components)
        {
            if (component is not ColliderComponent candidate || !candidate.Enabled)
                continue;

            count++;
            first ??= candidate;
            if (count > 1)
                hasMultiple = true;
        }

        if (first == null)
            return false;

        collider = first;
        return true;
    }

    private static void WarnOnce(HashSet<Guid> warningSet, Guid entityId, string message)
    {
        if (!warningSet.Add(entityId))
            return;

        FrinkyLog.Warning(message);
    }

    private static bool ApproximatelyEqual(Vector3 a, Vector3 b, float epsilon = 1e-4f)
    {
        return MathF.Abs(a.X - b.X) <= epsilon &&
               MathF.Abs(a.Y - b.Y) <= epsilon &&
               MathF.Abs(a.Z - b.Z) <= epsilon;
    }

    private static bool ApproximatelyEqual(Quaternion a, Quaternion b, float epsilon = 1e-4f)
    {
        var dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)));
        return dot >= 1f - epsilon;
    }

    private static Vector3 ComputeAngularVelocity(Quaternion from, Quaternion to, float dt)
    {
        if (dt <= 0f)
            return Vector3.Zero;

        var normalizedFrom = NormalizeOrIdentity(from);
        var normalizedTo = EnsureSameHemisphere(normalizedFrom, to);
        var delta = NormalizeOrIdentity(normalizedTo * Quaternion.Conjugate(normalizedFrom));
        if (delta.W < 0f)
            delta = new Quaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);

        var w = Math.Clamp(delta.W, -1f, 1f);
        var angle = 2f * MathF.Acos(w);
        if (angle < 1e-6f)
            return Vector3.Zero;

        var sinHalf = MathF.Sqrt(MathF.Max(0f, 1f - w * w));
        if (sinHalf < 1e-6f)
            return Vector3.Zero;

        var axis = new Vector3(delta.X, delta.Y, delta.Z) / sinHalf;
        return axis * (angle / dt);
    }

    private static float ComputeAngularStepRadians(Quaternion from, Quaternion to)
    {
        var normalizedFrom = NormalizeOrIdentity(from);
        var normalizedTo = EnsureSameHemisphere(normalizedFrom, to);
        var dot = Math.Clamp(Quaternion.Dot(normalizedFrom, normalizedTo), -1f, 1f);
        var angle = 2f * MathF.Acos(dot);
        if (!float.IsFinite(angle))
            return 0f;
        return angle;
    }

    private static Quaternion EnsureSameHemisphere(Quaternion reference, Quaternion candidate)
    {
        var normalizedReference = NormalizeOrIdentity(reference);
        var normalizedCandidate = NormalizeOrIdentity(candidate);
        if (Quaternion.Dot(normalizedReference, normalizedCandidate) < 0f)
        {
            normalizedCandidate = new Quaternion(
                -normalizedCandidate.X,
                -normalizedCandidate.Y,
                -normalizedCandidate.Z,
                -normalizedCandidate.W);
        }

        return normalizedCandidate;
    }

    private static Vector3 ClampMagnitude(Vector3 vector, float maxLength)
    {
        if (maxLength <= 0f)
            return Vector3.Zero;

        var length = vector.Length();
        if (!float.IsFinite(length) || length <= 1e-6f)
            return Vector3.Zero;
        if (length <= maxLength)
            return vector;

        return vector * (maxLength / length);
    }

    private static bool ShouldInterpolateBody(PhysicsBodyState state, PhysicsProjectSettings settings)
    {
        if (state.Rigidbody == null)
            return false;

        return state.Rigidbody.InterpolationMode switch
        {
            RigidbodyInterpolationMode.None => false,
            RigidbodyInterpolationMode.Interpolate => true,
            _ => settings.InterpolationEnabled
        };
    }

    private static RigidPose InterpolatePose(RigidPose previous, RigidPose current, float alpha)
    {
        var clampedAlpha = Math.Clamp(alpha, 0f, 1f);
        var previousOrientation = NormalizeOrIdentity(previous.Orientation);
        var currentOrientation = NormalizeOrIdentity(current.Orientation);
        if (Quaternion.Dot(previousOrientation, currentOrientation) < 0f)
        {
            currentOrientation = new Quaternion(
                -currentOrientation.X,
                -currentOrientation.Y,
                -currentOrientation.Z,
                -currentOrientation.W);
        }

        return new RigidPose(
            Vector3.Lerp(previous.Position, current.Position, clampedAlpha),
            NormalizeOrIdentity(Quaternion.Slerp(previousOrientation, currentOrientation, clampedAlpha)));
    }

    private static void RefreshDynamicLockAnchors(PhysicsBodyState state, RigidPose pose)
    {
        state.LockedPosition = pose.Position;
        state.RotationLockMask = GetRotationLockMask(state.Rigidbody);
        if (state.RotationLockMask != 0)
            state.LockReferenceOrientation = NormalizeOrIdentity(pose.Orientation);
    }

    private static void CaptureSimulationPoseAfterStep(PhysicsBodyState state, RigidPose pose)
    {
        if (!state.HasSimulationPoseHistory)
        {
            state.PreviousSimulationPose = pose;
            state.CurrentSimulationPose = pose;
            state.HasSimulationPoseHistory = true;
            return;
        }

        state.PreviousSimulationPose = state.CurrentSimulationPose;
        state.CurrentSimulationPose = pose;
    }

    private static void SnapSimulationPoseHistory(PhysicsBodyState state, RigidPose pose, bool suppressInterpolation)
    {
        state.PreviousSimulationPose = pose;
        state.CurrentSimulationPose = pose;
        state.HasSimulationPoseHistory = true;
        if (suppressInterpolation)
            state.SuppressInterpolationForFrame = true;
    }

    // --- Trigger event processing ---

    private Entity? ResolveCollidableToEntity(CollidableReference collidable)
    {
        if (!TryResolveCollidableId(collidable, out var entityId))
            return null;
        return _scene.FindEntityById(entityId);
    }

    private static (Guid, Guid) CanonicalPair(Guid a, Guid b)
    {
        return a.CompareTo(b) <= 0 ? (a, b) : (b, a);
    }

    private bool TryResolveCollidableId(CollidableReference collidable, out Guid entityId)
    {
        if (collidable.Mobility == CollidableMobility.Static)
            return _staticHandleToEntityId.TryGetValue(collidable.StaticHandle.Value, out entityId);
        return _bodyHandleToEntityId.TryGetValue(collidable.BodyHandle.Value, out entityId);
    }

    private void AccumulateTriggerPairs()
    {
        while (_narrowPhaseTriggerPairs.TryTake(out var pair))
        {
            if (!TryResolveCollidableId(pair.A, out var idA))
                continue;
            if (!TryResolveCollidableId(pair.B, out var idB))
                continue;
            if (idA == idB)
                continue;

            _currentTriggerPairs.Add(CanonicalPair(idA, idB));
        }
    }

    private void ProcessTriggerEvents()
    {
        // Enter: in current but not previous
        foreach (var pair in _currentTriggerPairs)
        {
            if (!_previousTriggerPairs.Contains(pair))
                DispatchTriggerCallback(pair.Item1, pair.Item2, TriggerEventType.Enter);
            else
                DispatchTriggerCallback(pair.Item1, pair.Item2, TriggerEventType.Stay);
        }

        // Exit: in previous but not current
        foreach (var pair in _previousTriggerPairs)
        {
            if (!_currentTriggerPairs.Contains(pair))
                DispatchTriggerCallback(pair.Item1, pair.Item2, TriggerEventType.Exit);
        }

        // Swap buffers
        (_previousTriggerPairs, _currentTriggerPairs) = (_currentTriggerPairs, _previousTriggerPairs);
        _currentTriggerPairs.Clear();
    }

    private enum TriggerEventType { Enter, Stay, Exit }

    private void DispatchTriggerCallback(Guid entityIdA, Guid entityIdB, TriggerEventType eventType)
    {
        var entityA = _scene.FindEntityById(entityIdA);
        var entityB = _scene.FindEntityById(entityIdB);
        if (entityA != null && entityB != null)
        {
            DispatchTriggerCallbackToEntity(entityA, entityB, eventType);
            DispatchTriggerCallbackToEntity(entityB, entityA, eventType);
        }
    }

    private static void DispatchTriggerCallbackToEntity(Entity target, Entity other, TriggerEventType eventType)
    {
        foreach (var component in target.Components.ToList())
        {
            if (!component.Enabled)
                continue;
            switch (eventType)
            {
                case TriggerEventType.Enter: Entity.SafeInvokeLifecycle(component, "OnTriggerEnter", () => component.OnTriggerEnter(other)); break;
                case TriggerEventType.Stay: Entity.SafeInvokeLifecycle(component, "OnTriggerStay", () => component.OnTriggerStay(other)); break;
                case TriggerEventType.Exit: Entity.SafeInvokeLifecycle(component, "OnTriggerExit", () => component.OnTriggerExit(other)); break;
            }
        }
    }

    private void FlushInteractionExitsForEntity(Guid entityId, bool entityRemoved)
    {
        var triggerPairsToFlush = _previousTriggerPairs
            .Concat(_currentTriggerPairs)
            .Where(pair => pair.Item1 == entityId || pair.Item2 == entityId)
            .Distinct()
            .ToList();
        foreach (var pair in triggerPairsToFlush)
        {
            _previousTriggerPairs.Remove(pair);
            _currentTriggerPairs.Remove(pair);

            var removedEntity = _scene.FindEntityById(entityId);
            var otherId = pair.Item1 == entityId ? pair.Item2 : pair.Item1;
            var otherEntity = _scene.FindEntityById(otherId);
            if (otherEntity == null)
                continue;

            if (entityRemoved)
            {
                if (removedEntity != null)
                    DispatchTriggerCallbackToEntity(otherEntity, removedEntity, TriggerEventType.Exit);
            }
            else if (removedEntity != null)
            {
                DispatchTriggerCallbackToEntity(removedEntity, otherEntity, TriggerEventType.Exit);
                DispatchTriggerCallbackToEntity(otherEntity, removedEntity, TriggerEventType.Exit);
            }
        }

        var collisionPairsToFlush = _previousCollisionPairs
            .Concat(_currentCollisionPairs)
            .Where(pair => pair.Item1 == entityId || pair.Item2 == entityId)
            .Distinct()
            .ToList();
        foreach (var pair in collisionPairsToFlush)
        {
            _previousCollisionPairs.Remove(pair);
            _currentCollisionPairs.Remove(pair);
            _collisionContactData.TryGetValue(pair, out var contact);
            _collisionContactData.Remove(pair);

            var removedEntity = _scene.FindEntityById(entityId);
            var otherId = pair.Item1 == entityId ? pair.Item2 : pair.Item1;
            var otherEntity = _scene.FindEntityById(otherId);
            if (otherEntity == null)
                continue;

            if (entityRemoved)
            {
                if (removedEntity != null)
                    DispatchCollisionCallbackToEntity(otherEntity, removedEntity, contact, normalPointsTowardTarget: otherId == pair.Item1, CollisionEventType.Exit);
            }
            else if (removedEntity != null)
            {
                DispatchCollisionCallbackToEntity(removedEntity, otherEntity, contact, normalPointsTowardTarget: entityId == pair.Item1, CollisionEventType.Exit);
                DispatchCollisionCallbackToEntity(otherEntity, removedEntity, contact, normalPointsTowardTarget: otherId == pair.Item1, CollisionEventType.Exit);
            }
        }
    }

    // --- Raycast ---

    private struct RaycastFilter
    {
        public bool IncludeTriggers;
        public HashSet<int> TriggerBodyHandles;
        public HashSet<int> TriggerStaticHandles;
        public HashSet<Guid>? IgnoredEntityIds;
        public Dictionary<int, Guid> BodyHandleToEntityId;
        public Dictionary<int, Guid> StaticHandleToEntityId;

        public readonly bool AllowCollidable(CollidableReference collidable)
        {
            bool isStatic = collidable.Mobility == CollidableMobility.Static;
            int handleValue = isStatic ? collidable.StaticHandle.Value : collidable.BodyHandle.Value;

            if (!IncludeTriggers)
            {
                var triggerSet = isStatic ? TriggerStaticHandles : TriggerBodyHandles;
                if (triggerSet.Contains(handleValue))
                    return false;
            }

            if (IgnoredEntityIds != null)
            {
                var handleMap = isStatic ? StaticHandleToEntityId : BodyHandleToEntityId;
                if (handleMap.TryGetValue(handleValue, out var entityId) && IgnoredEntityIds.Contains(entityId))
                    return false;
            }

            return true;
        }
    }

    private struct ClosestHitHandler : IRayHitHandler
    {
        public float ClosestT;
        public Vector3 ClosestNormal;
        public CollidableReference ClosestCollidable;
        public bool HasHit;
        public RaycastFilter Filter;

        public bool AllowTest(CollidableReference collidable) => Filter.AllowCollidable(collidable);
        public bool AllowTest(CollidableReference collidable, int childIndex) => true;

        public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal, CollidableReference collidable, int childIndex)
        {
            if (t < ClosestT || !HasHit)
            {
                ClosestT = t;
                ClosestNormal = normal;
                ClosestCollidable = collidable;
                HasHit = true;
                maximumT = t;
            }
        }
    }

    private struct AllHitsHandler : IRayHitHandler
    {
        public List<(float T, Vector3 Normal, CollidableReference Collidable)> Hits;
        public RaycastFilter Filter;

        public bool AllowTest(CollidableReference collidable) => Filter.AllowCollidable(collidable);
        public bool AllowTest(CollidableReference collidable, int childIndex) => true;

        public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal, CollidableReference collidable, int childIndex)
        {
            Hits.Add((t, normal, collidable));
        }
    }

    internal bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit, RaycastParams raycastParams)
    {
        hit = default;
        if (_simulation == null)
            return false;

        var dirLength = direction.Length();
        if (dirLength < 1e-8f)
            return false;

        var normalizedDir = direction / dirLength;
        var filter = BuildRaycastFilter(raycastParams);
        var handler = new ClosestHitHandler
        {
            ClosestT = float.MaxValue,
            HasHit = false,
            Filter = filter,
        };

        _simulation.RayCast(origin, normalizedDir, maxDistance, ref handler);

        if (!handler.HasHit)
            return false;

        var entity = ResolveCollidableToEntity(handler.ClosestCollidable);
        if (entity == null)
            return false;

        hit = new RaycastHit
        {
            Entity = entity,
            Point = origin + normalizedDir * handler.ClosestT,
            Normal = handler.ClosestNormal,
            Distance = handler.ClosestT,
        };
        return true;
    }

    internal List<RaycastHit> RaycastAll(Vector3 origin, Vector3 direction, float maxDistance, RaycastParams raycastParams)
    {
        var results = new List<RaycastHit>();
        if (_simulation == null)
            return results;

        var dirLength = direction.Length();
        if (dirLength < 1e-8f)
            return results;

        var normalizedDir = direction / dirLength;
        var filter = BuildRaycastFilter(raycastParams);
        var handler = new AllHitsHandler
        {
            Hits = new List<(float, Vector3, CollidableReference)>(),
            Filter = filter,
        };

        _simulation.RayCast(origin, normalizedDir, maxDistance, ref handler);

        foreach (var (t, normal, collidable) in handler.Hits)
        {
            var entity = ResolveCollidableToEntity(collidable);
            if (entity == null)
                continue;

            results.Add(new RaycastHit
            {
                Entity = entity,
                Point = origin + normalizedDir * t,
                Normal = normal,
                Distance = t,
            });
        }

        return results;
    }

    private RaycastFilter BuildRaycastFilter(RaycastParams raycastParams)
    {
        HashSet<Guid>? ignoredEntityIds = null;
        if (raycastParams.IgnoredEntities is { Count: > 0 })
        {
            ignoredEntityIds = new HashSet<Guid>(raycastParams.IgnoredEntities.Count);
            foreach (var entity in raycastParams.IgnoredEntities)
                ignoredEntityIds.Add(entity.Id);
        }

        return new RaycastFilter
        {
            IncludeTriggers = raycastParams.IncludeTriggers,
            TriggerBodyHandles = _triggerBodyHandles,
            TriggerStaticHandles = _triggerStaticHandles,
            IgnoredEntityIds = ignoredEntityIds,
            BodyHandleToEntityId = _bodyHandleToEntityId,
            StaticHandleToEntityId = _staticHandleToEntityId,
        };
    }

    // --- Shape Cast (Sweep) ---

    private struct ClosestSweepHandler : ISweepHitHandler
    {
        public float ClosestT;
        public Vector3 HitLocation;
        public Vector3 HitNormal;
        public CollidableReference HitCollidable;
        public bool HasHit;
        public bool StartedOverlapped;
        public Vector3 OverlapOrigin;
        public RaycastFilter Filter;

        public bool AllowTest(CollidableReference collidable) => Filter.AllowCollidable(collidable);
        public bool AllowTest(CollidableReference collidable, int childIndex) => true;

        public void OnHit(ref float maximumT, float t, in Vector3 hitLocation, in Vector3 hitNormal, CollidableReference collidable)
        {
            if (t < ClosestT || !HasHit)
            {
                ClosestT = t;
                HitLocation = hitLocation;
                HitNormal = hitNormal;
                HitCollidable = collidable;
                HasHit = true;
                maximumT = t;
            }
        }

        public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
        {
            // Overlapping at start — treat as hit at distance 0
            // TODO: If gameplay needs a separating direction here, derive it from a dedicated penetration query instead of returning a zero normal.
            if (!HasHit)
            {
                ClosestT = 0f;
                HitLocation = OverlapOrigin;
                HitNormal = Vector3.Zero;
                HitCollidable = collidable;
                HasHit = true;
                StartedOverlapped = true;
            }
        }
    }

    private struct AllSweepHitsHandler : ISweepHitHandler
    {
        public List<(float T, Vector3 Location, Vector3 Normal, CollidableReference Collidable, bool StartedOverlapped)> Hits;
        public Vector3 OverlapOrigin;
        public RaycastFilter Filter;

        public bool AllowTest(CollidableReference collidable) => Filter.AllowCollidable(collidable);
        public bool AllowTest(CollidableReference collidable, int childIndex) => true;

        public void OnHit(ref float maximumT, float t, in Vector3 hitLocation, in Vector3 hitNormal, CollidableReference collidable)
        {
            Hits.Add((t, hitLocation, hitNormal, collidable, false));
        }

        public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
        {
            Hits.Add((0f, OverlapOrigin, Vector3.Zero, collidable, true));
        }
    }

    internal bool SweepClosest<TShape>(TShape shape, RigidPose pose, Vector3 direction, float maxDistance, out ShapeCastHit hit, RaycastParams raycastParams)
        where TShape : unmanaged, IConvexShape
    {
        hit = default;
        if (_simulation == null)
            return false;

        var dirLength = direction.Length();
        if (dirLength < 1e-8f)
            return false;

        var normalizedDir = direction / dirLength;
        var velocity = new BodyVelocity { Linear = normalizedDir * maxDistance };
        var filter = BuildRaycastFilter(raycastParams);
        var handler = new ClosestSweepHandler
        {
            ClosestT = float.MaxValue,
            HasHit = false,
            StartedOverlapped = false,
            OverlapOrigin = pose.Position,
            Filter = filter,
        };

        _simulation.Sweep(shape, pose, velocity, 1f, _bufferPool, ref handler);

        if (!handler.HasHit)
            return false;

        var entity = ResolveCollidableToEntity(handler.HitCollidable);
        if (entity == null)
            return false;

        hit = new ShapeCastHit
        {
            Entity = entity,
            Point = handler.HitLocation,
            Normal = handler.HitNormal,
            Distance = handler.ClosestT * maxDistance,
            StartedOverlapped = handler.StartedOverlapped,
        };
        return true;
    }

    internal List<ShapeCastHit> SweepAll<TShape>(TShape shape, RigidPose pose, Vector3 direction, float maxDistance, RaycastParams raycastParams)
        where TShape : unmanaged, IConvexShape
    {
        var results = new List<ShapeCastHit>();
        if (_simulation == null)
            return results;

        var dirLength = direction.Length();
        if (dirLength < 1e-8f)
            return results;

        var normalizedDir = direction / dirLength;
        var velocity = new BodyVelocity { Linear = normalizedDir * maxDistance };
        var filter = BuildRaycastFilter(raycastParams);
        var handler = new AllSweepHitsHandler
        {
            Hits = new List<(float, Vector3, Vector3, CollidableReference, bool)>(),
            OverlapOrigin = pose.Position,
            Filter = filter,
        };

        _simulation.Sweep(shape, pose, velocity, 1f, _bufferPool, ref handler);

        foreach (var (t, location, normal, collidable, startedOverlapped) in handler.Hits)
        {
            var entity = ResolveCollidableToEntity(collidable);
            if (entity == null)
                continue;

            results.Add(new ShapeCastHit
            {
                Entity = entity,
                Point = location,
                Normal = normal,
                Distance = t * maxDistance,
                StartedOverlapped = startedOverlapped,
            });
        }

        return results;
    }

    // --- Overlap Queries ---

    private struct OverlapHandler : ISweepHitHandler
    {
        public List<CollidableReference> Overlaps;
        public RaycastFilter Filter;

        public bool AllowTest(CollidableReference collidable) => Filter.AllowCollidable(collidable);
        public bool AllowTest(CollidableReference collidable, int childIndex) => true;

        public void OnHit(ref float maximumT, float t, in Vector3 hitLocation, in Vector3 hitNormal, CollidableReference collidable)
        {
            // Zero-velocity sweep — OnHit should not fire, but collect just in case
        }

        public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
        {
            Overlaps.Add(collidable);
        }
    }

    internal List<Entity> OverlapQuery<TShape>(TShape shape, RigidPose pose, RaycastParams raycastParams)
        where TShape : unmanaged, IConvexShape
    {
        var results = new List<Entity>();
        if (_simulation == null)
            return results;

        var filter = BuildRaycastFilter(raycastParams);
        var handler = new OverlapHandler
        {
            Overlaps = new List<CollidableReference>(),
            Filter = filter,
        };

        // Sweep with zero velocity — OnHitAtZeroT fires for pre-existing overlaps
        var velocity = new BodyVelocity { Linear = Vector3.Zero };
        _simulation.Sweep(shape, pose, velocity, 1f, _bufferPool, ref handler);

        foreach (var collidable in handler.Overlaps)
        {
            var entity = ResolveCollidableToEntity(collidable);
            if (entity != null)
                results.Add(entity);
        }

        return results;
    }

    internal bool CanCharacterStand(CharacterControllerComponent controller, CapsuleColliderComponent capsule, float standingLength)
    {
        if (_simulation == null)
            return true;

        var transform = controller.Entity.Transform;
        var absScale = new Vector3(MathF.Abs(transform.LocalScale.X), MathF.Abs(transform.LocalScale.Y), MathF.Abs(transform.LocalScale.Z));
        absScale.X = MathF.Max(absScale.X, 0.0001f);
        absScale.Y = MathF.Max(absScale.Y, 0.0001f);
        absScale.Z = MathF.Max(absScale.Z, 0.0001f);

        var raycastParams = new RaycastParams();
        raycastParams.IgnoreEntityTree(controller.Entity);

        var scaledCenter = new Vector3(
            capsule.Center.X * transform.LocalScale.X,
            capsule.Center.Y * transform.LocalScale.Y,
            capsule.Center.Z * transform.LocalScale.Z);
        var scaledRadius = MathF.Max(0.001f, capsule.Radius * MathF.Max(absScale.X, absScale.Z));
        var currentScaledLength = MathF.Max(0.001f, capsule.Length * absScale.Y);
        var standingScaledLength = MathF.Max(0.001f, standingLength * absScale.Y);
        var standHeightDelta = standingScaledLength - currentScaledLength;
        if (standHeightDelta <= 0.001f)
            return true;

        var currentTopCenter = transform.LocalPosition + scaledCenter + Vector3.UnitY * (currentScaledLength * 0.5f);
        var upwardTravel = controller.Supported ? standHeightDelta : standHeightDelta * 0.5f;
        if (upwardTravel <= 0.001f)
            return true;

        var shape = new Sphere(scaledRadius);
        var pose = new RigidPose(currentTopCenter);

        // TODO: If gameplay collision filtering grows beyond triggers, expand the uncrouch headroom query to honor those filters too.
        return !SweepClosest(shape, pose, Vector3.UnitY, upwardTravel, out _, raycastParams);
    }

    // --- Collision event processing ---

    private readonly record struct CollisionContactData(Vector3 WorldContactPoint, Vector3 Normal, float Depth);

    private void AccumulateCollisionPairs()
    {
        while (_narrowPhaseCollisionPairs.TryTake(out var pair))
        {
            if (!TryResolveCollidableId(pair.A, out var idA))
                continue;
            if (!TryResolveCollidableId(pair.B, out var idB))
                continue;
            if (idA == idB)
                continue;

            // Compute world-space contact point: offset is relative to collidable A's position
            var worldContactPoint = pair.ContactOffset + GetCollidablePosition(pair.A);

            // Manifold normal points from B toward A.
            // Canonical pair stores (min GUID, max GUID). If idA ended up as Item2
            // (i.e. idA > idB), the canonical order is swapped relative to the manifold,
            // so we negate the normal to keep it pointing from Item2 toward Item1.
            var canonical = CanonicalPair(idA, idB);
            bool swapped = canonical.Item1 != idA;
            var normal = swapped ? -pair.Normal : pair.Normal;

            _currentCollisionPairs.Add(canonical);
            _collisionContactData[canonical] = new CollisionContactData(worldContactPoint, normal, pair.Depth);
        }
    }

    private Vector3 GetCollidablePosition(CollidableReference collidable)
    {
        if (_simulation == null)
            return Vector3.Zero;

        if (collidable.Mobility == CollidableMobility.Static)
        {
            if (_simulation.Statics.StaticExists(collidable.StaticHandle))
                return _simulation.Statics.GetStaticReference(collidable.StaticHandle).Pose.Position;
        }
        else
        {
            if (_simulation.Bodies.BodyExists(collidable.BodyHandle))
                return _simulation.Bodies.GetBodyReference(collidable.BodyHandle).Pose.Position;
        }

        return Vector3.Zero;
    }

    private void ProcessCollisionEvents()
    {
        // Enter: in current but not previous
        foreach (var pair in _currentCollisionPairs)
        {
            _collisionContactData.TryGetValue(pair, out var contact);
            if (!_previousCollisionPairs.Contains(pair))
                DispatchCollisionCallback(pair.Item1, pair.Item2, contact, CollisionEventType.Enter);
            else
                DispatchCollisionCallback(pair.Item1, pair.Item2, contact, CollisionEventType.Stay);
        }

        // Exit: in previous but not current
        foreach (var pair in _previousCollisionPairs)
        {
            if (!_currentCollisionPairs.Contains(pair))
                DispatchCollisionCallback(pair.Item1, pair.Item2, default, CollisionEventType.Exit);
        }

        // Swap buffers
        (_previousCollisionPairs, _currentCollisionPairs) = (_currentCollisionPairs, _previousCollisionPairs);
        _currentCollisionPairs.Clear();
        _collisionContactData.Clear();
    }

    private enum CollisionEventType { Enter, Stay, Exit }

    private void DispatchCollisionCallback(Guid entityIdA, Guid entityIdB, CollisionContactData contact, CollisionEventType eventType)
    {
        var entityA = _scene.FindEntityById(entityIdA);
        var entityB = _scene.FindEntityById(entityIdB);
        if (entityA != null && entityB != null)
        {
            DispatchCollisionCallbackToEntity(entityA, entityB, contact, normalPointsTowardTarget: true, eventType);
            DispatchCollisionCallbackToEntity(entityB, entityA, contact, normalPointsTowardTarget: false, eventType);
        }
    }

    private static void DispatchCollisionCallbackToEntity(Entity target, Entity other, CollisionContactData contact, bool normalPointsTowardTarget, CollisionEventType eventType)
    {
        var info = new CollisionInfo
        {
            Other = other,
            ContactPoint = contact.WorldContactPoint,
            Normal = normalPointsTowardTarget ? contact.Normal : -contact.Normal,
            PenetrationDepth = contact.Depth,
        };

        foreach (var component in target.Components.ToList())
        {
            if (!component.Enabled)
                continue;
            switch (eventType)
            {
                case CollisionEventType.Enter: Entity.SafeInvokeLifecycle(component, "OnCollisionEnter", () => component.OnCollisionEnter(info)); break;
                case CollisionEventType.Stay: Entity.SafeInvokeLifecycle(component, "OnCollisionStay", () => component.OnCollisionStay(info)); break;
                case CollisionEventType.Exit: Entity.SafeInvokeLifecycle(component, "OnCollisionExit", () => component.OnCollisionExit(info)); break;
            }
        }
    }

    private readonly struct ShapeCreationResult
    {
        public required TypedIndex ShapeIndex { get; init; }
        public required BodyInertia DynamicInertia { get; init; }
    }

    public void Dispose()
    {
        if (_simulation == null)
            return;

        foreach (var state in _bodyStates.Values.ToList())
            RemoveBodyState(state, dispatchExitCallbacks: false);

        _bodyStates.Clear();
        _characterStates.Clear();
        _materialTable.Clear();
        _bodyHandleToEntityId.Clear();
        _staticHandleToEntityId.Clear();
        _triggerBodyHandles.Clear();
        _triggerStaticHandles.Clear();
        _previousTriggerPairs.Clear();
        _currentTriggerPairs.Clear();
        _previousCollisionPairs.Clear();
        _currentCollisionPairs.Clear();
        _collisionContactData.Clear();
        _warnedNoCollider.Clear();
        _warnedParented.Clear();
        _warnedImplicitStaticParented.Clear();
        _warnedMultipleColliders.Clear();
        _warnedMultipleRigidbodies.Clear();
        _warnedCharacterMissingRigidbody.Clear();
        _warnedCharacterMissingCapsule.Clear();
        _warnedCharacterWrongMotionType.Clear();
        _warnedCharacterParented.Clear();
        _warnedCharacterNonCapsuleBody.Clear();
        _warnedKinematicDiscontinuity.Clear();

        _characterControllers?.Dispose();
        _characterControllers = null;

        _simulation.Dispose();
        _simulation = null;
        _bufferPool.Clear();
        _accumulator = 0f;
    }
}
