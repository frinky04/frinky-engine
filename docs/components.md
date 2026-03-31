# Components Reference

All built-in components and their key properties. For information on writing custom components, see [Scripting](scripting.md).

## Rendering

| Component | Key Properties |
|-----------|---------------|
| `CameraComponent` | `FieldOfView` (60), `NearPlane` (0.1), `FarPlane` (1000), `Projection` (Perspective/Orthographic), `ClearColor`, `IsMain` |
| `LightComponent` | `LightType` (Directional/Point/Skylight), `LightColor`, `Intensity` (1.0), `Range` (10.0) |
| `MeshRendererComponent` | `ModelPath`, `MaterialSlots` (list of `Material`), `Tint`, `EditorOnly` |
| `SkinnedMeshAnimatorComponent` | `AnimationSources` (list of .glb asset paths for multi-source animation loading), `UseEmbeddedAnimations` (true), `PlayAutomatically` (true), `Loop` (true), `PlaybackSpeed` (1.0), `ClipIndex` (0), `Playing` (true), `LoopFrameTrim` (1, advanced) |
| `InverseKinematicsComponent` | `Solvers` (list of IK solver objects, processed in order) |
| `PostProcessStackComponent` | `PostProcessingEnabled` (true), `Effects` (list of effects) |

### Animation Export Tips

- **Disable "Sampling Animations"** in Blender's glTF export settings. Enabling it bakes in-between keyframes that can make interpolation appear to step or stutter.
- **Start animations at frame 0.** The engine expects the first keyframe at the beginning of the clip.
- **Looping animations automatically trim the duplicate seam frame.** glTF exports include a duplicate of the first frame at the end for loop continuity; the engine skips it by default (`LoopFrameTrim = 1`). If you still see a hitch at the loop boundary, try increasing `LoopFrameTrim` in the Advanced section of the inspector.

## Primitives

| Component | Key Properties |
|-----------|---------------|
| `CubePrimitive` | `Width` (1), `Height` (1), `Depth` (1), `Material` |
| `SpherePrimitive` | `Radius` (0.5), `Rings` (16), `Slices` (16), `Material` |
| `PlanePrimitive` | `Width` (10), `Depth` (10), `ResolutionX` (1), `ResolutionZ` (1), `Material` |
| `CylinderPrimitive` | `Radius` (0.5), `Height` (2), `Slices` (16), `Material` |

## Physics

| Component | Key Properties |
|-----------|---------------|
| `RigidbodyComponent` | `MotionType` (Dynamic/Kinematic/Static), `Mass` (1.0), `LinearDamping` (0.03), `AngularDamping` (0.03), `ContinuousDetection`, axis locks |
| `BoxColliderComponent` | `Size` (1,1,1), `Center`, `IsTrigger` |
| `SphereColliderComponent` | `Radius` (0.5), `Center`, `IsTrigger` |
| `CapsuleColliderComponent` | `Radius` (0.5), `Length` (1.0), `Center`, `IsTrigger` |
| `MeshColliderComponent` | `MeshPath`, `UseMeshRendererWhenEmpty` (true), `Center`, `IsTrigger` |
| `CharacterControllerComponent` | `MoveSpeed` (4), `JumpVelocity` (6), `MaxSlopeDegrees` (45), `CrouchHeightScale` (0.5), `CrouchSpeedScale` (0.5), air control settings |

## Input

| Component | Key Properties |
|-----------|---------------|
| `SimplePlayerInputComponent` | Movement keys, mouse look, `CrouchKey` (LeftControl), `AdjustCameraOnCrouch`, `CameraEntity` (EntityReference), `UseCharacterController` |

## Audio

| Component | Key Properties |
|-----------|---------------|
| `AudioSourceComponent` | `SoundPath`, `PlayOnStart`, `Spatialized`, `Looping`, `Bus`, `Volume`, `Pitch`, `Attenuation` |
| `AudioListenerComponent` | `IsPrimary`, `MasterVolumeScale` |

## Materials

Both `MeshRendererComponent` (via `MaterialSlots` list) and primitive components (via a single `Material` property) use the `Material` class. Three material types are available:

| Type | Description |
|------|-------------|
| `SolidColor` | Flat color (default) |
| `Textured` | Albedo texture mapped with UV coordinates |
| `TriplanarTexture` | Texture projected along world/local axes, configurable scale and blend sharpness |
