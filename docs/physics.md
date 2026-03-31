# Physics

Physics is powered by [BepuPhysics 2](https://github.com/bepu/bepuphysics2).

## Use This When

Use the physics system when you need:

- rigidbody simulation
- trigger volumes
- collisions and collision callbacks
- a built-in character controller
- raycasts or shape queries

## Common Setups

### Static world geometry

Use:

- a collider on the entity
- optionally a `RigidbodyComponent` with `MotionType = Static`

Collider-only entities are supported as true static collidables and triggers.

### Dynamic physics object

Use:

- `RigidbodyComponent` with `MotionType = Dynamic`
- an appropriate collider

This is the normal setup for movable simulated props.

### Kinematic mover

Use:

- `RigidbodyComponent` with `MotionType = Kinematic`
- an appropriate collider

Use this for scripted moving platforms or objects that should push dynamic bodies without being force-driven themselves.

### Character controller

Use:

- `RigidbodyComponent` with `MotionType = Dynamic`
- `CapsuleColliderComponent`
- `CharacterControllerComponent`

Add `SimplePlayerInputComponent` if you want the built-in input-driven movement path.

Important:

- the `CapsuleColliderComponent` must be the first enabled collider on the entity

## Minimal Trigger Example

```csharp
using FrinkyEngine.Core.ECS;

public class PickupZone : Component
{
    public override void OnTriggerEnter(Entity other)
    {
        FrinkyLog.Info($"{other.Name} entered the zone");
    }
}
```

To make this work:

1. Add a collider to the entity.
2. Set `IsTrigger = true`.
3. Make sure another collider can overlap it.

## Rigidbodies

| Motion Type | Use it for |
|-------------|------------|
| **Dynamic** | fully simulated objects |
| **Kinematic** | scripted movers that still affect dynamic bodies |
| **Static** | immovable collision surfaces |

### Scene gravity

Scene gravity comes from `Scene.PhysicsSettings.Gravity`. Runtime gravity changes apply immediately, including changes made through the `physics_gravity` console CVar.

## Colliders

| Collider | Use it for |
|----------|------------|
| `BoxColliderComponent` | boxes, walls, rectangular triggers |
| `SphereColliderComponent` | radial triggers and simple round objects |
| `CapsuleColliderComponent` | characters and capsule-like actors |
| `MeshColliderComponent` | static or kinematic triangle-mesh collision |

All colliders support:

- `Center` offset from the entity origin
- `IsTrigger` for overlap-only behavior

`MeshColliderComponent` builds a triangle mesh from `MeshPath` or, if `UseMeshRendererWhenEmpty` is enabled, from the sibling `MeshRendererComponent`.

## Raycasting

Use raycasts to check what is under a cursor, in front of an entity, or between two points.

```csharp
using FrinkyEngine.Core.Physics;

if (Physics.Raycast(origin, direction, 100f, out var hit))
{
    FrinkyLog.Info($"Hit {hit.Entity.Name} at {hit.Point}");
}
```

Use `RaycastAll` when you need every hit instead of the closest one.

## Editor Workflow

The editor includes physics helpers:

- **Add Physics** shortcuts in the hierarchy and inspector
- `F8` collider wireframe preview
- `F9` collider edit mode for resizing and repositioning collider shapes

## Notes

- Dynamic mesh colliders are not supported.
- `MeshColliderComponent` is best for static or kinematic collision.
- The built-in character controller expects the capsule collider setup described above, with the capsule as the first enabled collider.

## See Also

- [Choosing Components](components.md) for setup selection
- [Editor Guide](editor-guide.md) for collider editing and quick-add flow
