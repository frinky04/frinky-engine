# Choosing Components

Use this page when you know what you want to build, but you are not sure which built-in components belong on the entity.

## Common Setups

### A player or character you can move

Start with:

- `RigidbodyComponent` with `MotionType = Dynamic`
- `CapsuleColliderComponent`
- `CharacterControllerComponent`

Add `SimplePlayerInputComponent` if you want the built-in input-driven movement setup.

Important:

- the capsule must be the first enabled collider on the entity for the built-in character controller path

Use this for:

- walking characters
- jump and crouch gameplay
- controllable prototypes

See [Physics](physics.md) for the full movement workflow.

### A static trigger volume

Start with:

- `BoxColliderComponent`, `SphereColliderComponent`, or `CapsuleColliderComponent`
- `IsTrigger = true`

You usually do not need a rigidbody for a simple trigger volume.

Use this for:

- pickups
- detection areas
- scripted zone events

### A visible 3D model

Start with:

- `MeshRendererComponent`

Add:

- `LightComponent` elsewhere in the scene so the model is visible
- collider and rigidbody components if the model should participate in physics

Use this for:

- imported props
- environment art
- visible gameplay objects

### An animated character or prop

Start with:

- `MeshRendererComponent`
- `SkinnedMeshAnimatorComponent`

Optionally add:

- `InverseKinematicsComponent` for IK solvers

Use this for:

- skinned characters
- animated creatures
- animated props with imported clips

See [Rendering & Post-Processing](rendering.md) for the setup flow.

### A camera

Start with:

- `CameraComponent`

Set `IsMain = true` on the camera you want the scene to render from.

Optionally add:

- `PostProcessStackComponent` for bloom, fog, SSAO, palette, or dither effects

### A light source

Start with:

- `LightComponent`

Choose the light type based on the job:

- `Directional` for sunlight-style lighting
- `Point` for local lights
- `Skylight` for ambient base illumination

### A sound emitter

Start with:

- `AudioSourceComponent`

Set:

- `Spatialized = false` for UI or global 2D sounds
- `Spatialized = true` for in-world 3D sounds

Add an `AudioListenerComponent` to the listener entity if you want an explicit 3D audio listener.

See [Audio](audio.md) for usage patterns.

### A reusable gameplay object

Use a prefab instead of assembling the same entity tree repeatedly.

Typical prefab contents:

- visual components
- physics components
- gameplay script components
- child entities and references

See [Prefabs & Entity References](prefabs.md).

## Quick Reference

| Goal | Components |
|------|------------|
| Move a character | `RigidbodyComponent`, `CapsuleColliderComponent`, `CharacterControllerComponent` (`CapsuleColliderComponent` must be the first enabled collider) |
| Detect overlaps | Any collider with `IsTrigger = true` |
| Show a model | `MeshRendererComponent` |
| Play imported animation | `MeshRendererComponent`, `SkinnedMeshAnimatorComponent` |
| Render the scene | `CameraComponent` |
| Add lighting | `LightComponent` |
| Post-processing | `CameraComponent`, `PostProcessStackComponent` |
| Persistent sound source | `AudioSourceComponent` |
| Explicit 3D listener | `AudioListenerComponent` |

## When to Use Topic Guides Instead

- Go to [Physics](physics.md) when setup order and motion type matter.
- Go to [Rendering](rendering.md) when cameras, lighting, materials, or animation matter.
- Go to [Audio](audio.md) when you need routing, buses, or 3D sound.
- Go to [Scripting](scripting.md) when you need custom gameplay behavior.
