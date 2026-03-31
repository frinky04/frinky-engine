# Physics

Physics is powered by [BepuPhysics 2](https://github.com/bepu/bepuphysics2).

## Rigidbodies

Add a `RigidbodyComponent` to give an entity physics behavior. Three motion types are available:

| Motion Type | Description |
|-------------|-------------|
| **Dynamic** | Fully simulated, affected by forces and collisions |
| **Kinematic** | Moves via transform, pushes dynamic bodies but is not affected by forces |
| **Static** | Immovable, used for terrain and walls |

Collider-only entities are also supported as true static collidables. If an entity has an enabled collider but no `RigidbodyComponent`, physics registers it as a static collider or trigger.

### Kinematic Stability

- Contact-driving velocity is derived from consecutive kinematic target poses (continuity-aware), not from arbitrary pose snaps
- Kinematic linear and angular contact velocities are safety-clamped to avoid extreme one-frame impulses
- Large discontinuities (e.g. sudden large rotation jumps) are treated as teleport-style corrections for that step, with kinematic velocity suppressed

## Scene Gravity

Scene gravity comes from `Scene.PhysicsSettings.Gravity`. Runtime gravity changes apply immediately to the active simulation, including changes made through the `physics_gravity` console cvar.

## Colliders

| Collider | Key Properties |
|----------|---------------|
| `BoxColliderComponent` | `Size` (1,1,1), `Center`, `IsTrigger` |
| `SphereColliderComponent` | `Radius` (0.5), `Center`, `IsTrigger` |
| `CapsuleColliderComponent` | `Radius` (0.5), `Length` (1.0), `Center`, `IsTrigger` |
| `MeshColliderComponent` | `MeshPath`, `UseMeshRendererWhenEmpty`, `Center`, `IsTrigger` |

All colliders support:
- **Center** offset from the entity origin
- **IsTrigger** mode for overlap detection without physical response

`MeshColliderComponent` builds a triangle-mesh collider from either its own `MeshPath` override or, when `UseMeshRendererWhenEmpty` is enabled, the sibling `MeshRendererComponent` model. Mesh colliders support collider-only statics, `RigidbodyComponent` statics, and kinematic rigidbodies. Dynamic rigidbodies and skinned/bone-driven model sources are rejected.

## Triggers

Set `IsTrigger = true` on any collider to make it a trigger volume. Trigger colliders detect overlaps but do not produce a physical response — objects pass through them.

### Trigger Callbacks

Override the trigger callbacks in your component to respond to overlaps:

```csharp
public class PickupZone : Component
{
    public override void OnTriggerEnter(Entity other)
    {
        // Called once when another entity first overlaps this trigger
        FrinkyLog.Info($"{other.Name} entered the zone!");
    }

    public override void OnTriggerStay(Entity other)
    {
        // Called each frame while the overlap persists
    }

    public override void OnTriggerExit(Entity other)
    {
        // Called once when the overlap ends
        FrinkyLog.Info($"{other.Name} left the zone!");
    }
}
```

Both entities in the overlap receive callbacks. At least one of the two colliders must have `IsTrigger` enabled. Trigger colliders can be collider-only statics or can use a `RigidbodyComponent` with Static, Kinematic, or Dynamic motion.

## Raycasting

Cast rays into the physics world to detect colliders:

```csharp
using FrinkyEngine.Core.Physics;

// Closest hit
if (Physics.Raycast(origin, direction, 100f, out var hit))
{
    FrinkyLog.Info($"Hit {hit.Entity.Name} at {hit.Point}, distance {hit.Distance}");
    // hit.Normal is the surface normal at the impact point
}

// All hits
var hits = Physics.RaycastAll(origin, direction, 100f);
foreach (var h in hits)
{
    FrinkyLog.Info($"Hit {h.Entity.Name} at distance {h.Distance}");
}
```

### Point-to-Point Raycasting

Cast between two world-space positions instead of specifying direction and distance:

```csharp
if (Physics.Raycast(pointA, pointB, out var hit))
{
    FrinkyLog.Info($"Something between A and B: {hit.Entity.Name}");
}

var hits = Physics.RaycastAll(pointA, pointB);
```

### RaycastParams

Use `RaycastParams` to filter raycast results:

```csharp
var rayParams = new RaycastParams
{
    IncludeTriggers = true,                       // include trigger colliders (skipped by default)
    IgnoredEntities = new HashSet<Entity> { Entity } // skip specific entities
};

if (Physics.Raycast(origin, direction, 100f, out var hit, rayParams))
{
    // hit.Entity will never be this entity
}
```

#### Ignoring an Entity Tree

`IgnoreEntityTree` collects the full hierarchy (root and all descendants) of a given entity into the ignore set. This is useful for ignoring the caster and all of its children/parents:

```csharp
var rayParams = new RaycastParams();
rayParams.IgnoreEntityTree(Entity); // ignores root parent + entire subtree

if (Physics.Raycast(origin, direction, 100f, out var hit, rayParams))
{
    // won't hit any entity in the same hierarchy tree
}
```

`RaycastHit` fields:

| Field | Type | Description |
|-------|------|-------------|
| `Entity` | `Entity` | The entity whose collider was hit |
| `Point` | `Vector3` | World-space impact point |
| `Normal` | `Vector3` | Surface normal at impact |
| `Distance` | `float` | Distance from ray origin to hit |

## Shape Casts

Sweep a shape along a direction to detect colliders. Like a raycast but with volume. All shape casts accept the same `RaycastParams` filtering as raycasts.

### Sphere Cast

```csharp
using FrinkyEngine.Core.Physics;

if (Physics.SphereCast(origin, 0.5f, direction, 20f, out var hit))
{
    FrinkyLog.Info($"Sphere hit {hit.Entity.Name} at {hit.Point}, distance {hit.Distance}");
}

// All hits
var hits = Physics.SphereCastAll(origin, 0.5f, direction, 20f);
```

### Box Cast

```csharp
var halfExtents = new Vector3(1f, 0.5f, 1f);
if (Physics.BoxCast(origin, halfExtents, Quaternion.Identity, direction, 20f, out var hit))
{
    FrinkyLog.Info($"Box hit {hit.Entity.Name}");
}
```

### Capsule Cast

```csharp
if (Physics.CapsuleCast(origin, 0.5f, 1f, Quaternion.Identity, direction, 20f, out var hit))
{
    FrinkyLog.Info($"Capsule hit {hit.Entity.Name}");
}
```

`ShapeCastHit` fields:

| Field | Type | Description |
|-------|------|-------------|
| `Entity` | `Entity` | The entity whose collider was hit |
| `Point` | `Vector3` | World-space impact point |
| `Normal` | `Vector3` | Surface normal at impact |
| `Distance` | `float` | Distance from sweep origin to hit |
| `StartedOverlapped` | `bool` | `true` when the cast began already overlapping the collider |

When `StartedOverlapped` is `true`, `Distance` is `0`, `Point` is the sweep origin, and `Normal` is `Vector3.Zero` because BEPU does not provide a resolved separating normal for initial overlaps.

## Overlap Queries

Test for all colliders overlapping a shape at a given position. Returns a list of entities. Accepts `RaycastParams` for filtering.

```csharp
using FrinkyEngine.Core.Physics;

// Find all entities within a sphere
List<Entity> nearby = Physics.OverlapSphere(center, 5f);

// Find all entities within a box
List<Entity> inBox = Physics.OverlapBox(center, new Vector3(2f, 1f, 2f), Quaternion.Identity);

// Find all entities within a capsule
List<Entity> inCapsule = Physics.OverlapCapsule(center, 0.5f, 2f, Quaternion.Identity);
```

## Collision Callbacks

Override collision callbacks in your component to respond to physics collisions between non-trigger colliders:

```csharp
public class DamageReceiver : Component
{
    public override void OnCollisionEnter(CollisionInfo info)
    {
        // Called once when a collision first begins
        FrinkyLog.Info($"Hit by {info.Other.Name} at {info.ContactPoint}");
    }

    public override void OnCollisionStay(CollisionInfo info)
    {
        // Called each frame while the collision persists
    }

    public override void OnCollisionExit(CollisionInfo info)
    {
        // Called once when the collision ends
    }
}
```

Both entities in the collision receive callbacks. These fire for non-trigger collisions only — trigger overlaps use `OnTriggerEnter/Stay/Exit` instead.

`CollisionInfo` fields:

| Field | Type | Description |
|-------|------|-------------|
| `Other` | `Entity` | The other entity in the collision |
| `ContactPoint` | `Vector3` | World-space contact point |
| `Normal` | `Vector3` | Contact normal pointing toward this entity |
| `PenetrationDepth` | `float` | Penetration depth of the collision contact |

## Character Controller

A dynamic character controller backed by BEPU support constraints. Minimum setup on one entity:

1. `RigidbodyComponent` with `MotionType = Dynamic`
2. `CapsuleColliderComponent` (must be the first enabled collider)
3. `CharacterControllerComponent`

### Script-Side Input Methods

| Method | Style | Description |
|--------|-------|-------------|
| `AddMovementInput(direction)` | Unreal-style | Add world-space movement input |
| `Jump()` | Unreal-style | Request a jump |
| `SetMoveInput(Vector2)` | Direct | Set planar input directly |
| `MoveAndSlide(desiredVelocity, requestJump)` | Godot-style | Convenience all-in-one |

Or use `SimplePlayerInputComponent` for built-in WASD + mouse look with configurable keys.

### Key Properties

| Property | Default | Description |
|----------|---------|-------------|
| `MoveSpeed` | 4 | Movement speed |
| `JumpVelocity` | 6 | Jump impulse |
| `MaxSlopeDegrees` | 45 | Maximum walkable slope angle |
| `CrouchHeightScale` | 0.5 | Capsule height multiplier when crouching |
| `CrouchSpeedScale` | 0.5 | Speed multiplier when crouching |

## Crouching

The character controller has built-in crouch support:

- `Crouch()` / `Stand()` / `SetCrouching(bool)` — control crouch state from scripts
- Crouching shrinks the capsule height by `CrouchHeightScale` (default 50%) and reduces move speed by `CrouchSpeedScale` (default 50%)
- The entity position is adjusted to keep feet on the ground
- Velocity is preserved through the physics body rebuild that occurs during capsule resizing
- `Stand()` can fail when blocked by solid geometry overhead; in that case the controller stays crouched

`SimplePlayerInputComponent` provides automatic crouch handling with Left Ctrl (configurable via `CrouchKey`), including camera height blending:

| Property | Default | Description |
|----------|---------|-------------|
| `CrouchKey` | LeftControl | Key to hold for crouching |
| `AdjustCameraOnCrouch` | true | Blend camera height on crouch |
| `CrouchCameraYOffset` | -0.8 | Camera offset when crouched |
| `CameraOffsetLerpSpeed` | 10.0 | Camera blend speed (units/sec) |

## Quick-Add Physics Shortcuts

The editor provides quick-add shortcuts for common physics setups, accessible from:

- **Right-click an entity** in the Hierarchy panel -> **Add Physics** submenu
- **Inspector panel** -> **Quick Add Physics** section (below Add Component)

Three presets are available:

| Preset | What it adds |
|--------|-------------|
| **Static** | Collider only (no rigidbody) — registered by the engine as a static collidable for floors, walls, and static geometry |
| **Dynamic** | Collider + Rigidbody (Dynamic) — for objects affected by gravity and forces |
| **Kinematic** | Collider + Rigidbody (Kinematic) — for objects moved by code that push dynamic bodies |

The collider shape is auto-detected from the entity's primitive component, except that **Static** and **Kinematic** shortcuts prefer `MeshColliderComponent` when the entity has a `MeshRendererComponent`:

| Primitive | Collider | Auto-sized to |
|-----------|----------|--------------|
| Cube | Box Collider | Cube width/height/depth |
| Sphere | Sphere Collider | Sphere radius |
| Cylinder | Capsule Collider | Cylinder radius and height |
| Plane | Box Collider | Plane width/depth with thin height |
| None/Other | Box Collider | Default unit size |

For `MeshRendererComponent` entities:

- **Static** and **Kinematic** quick-add create `MeshColliderComponent`
- **Dynamic** quick-add still falls back to the primitive/default collider path, because dynamic mesh colliders are not supported

Existing colliders and rigidbodies are preserved — the shortcuts skip adding duplicates.

## Physics Hitbox Preview

Press `F8` in the editor to toggle a wireframe overlay of all collider shapes.
