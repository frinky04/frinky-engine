# Scripting

Use this guide when you want to add custom gameplay behavior in C#.

## Use This When

Write a custom component when you need:

- gameplay logic that runs every frame
- collision or trigger callbacks
- serialized gameplay properties visible in the inspector
- reusable behavior that can be attached to entities

## Your First Component

Create a component that rotates its entity:

```csharp
using System.Numerics;
using FrinkyEngine.Core.ECS;

public class RotatorComponent : Component
{
    public float Speed { get; set; } = 90f;

    public override void Update(float dt)
    {
        Transform.LocalRotation *= Quaternion.CreateFromAxisAngle(
            Vector3.UnitY,
            Speed * (MathF.PI / 180f) * dt);
    }
}
```

Then:

1. Add the script to your game project.
2. Build scripts with `File -> Build Scripts` or `Ctrl+B`.
3. Add the component in the editor.
4. Press `F5` and confirm the entity rotates.

## Common Workflow

### 1. Build your game assembly

- Game code lives in a separate project referenced by the `.fproject`.
- Build it from the editor with `Ctrl+B`.
- The editor hot-reloads the assembly after a successful build.

### 2. Attach components to entities

Your custom type appears in the **Add Component** menu after a successful build if it is:

- `public`
- derived from `Component`

### 3. Expose editable properties

Public read/write properties are serialized automatically and appear in the inspector.

Typical examples:

- numbers such as speed, damage, or cooldown
- booleans for feature toggles
- enums for behavior modes
- `AssetReference` and `EntityReference`

## Lifecycle

The most important callbacks are:

| Method | Use it for |
|--------|------------|
| `Awake` | one-time setup when the component is created |
| `Start` | scene-start initialization before first `Update` |
| `Update(float dt)` | per-frame gameplay logic |
| `LateUpdate(float dt)` | work that should happen after normal updates |
| `OnEnable` / `OnDisable` | activation changes |
| `OnDestroy` | cleanup |
| `OnTriggerEnter/Stay/Exit` | trigger overlaps |
| `OnCollisionEnter/Stay/Exit` | non-trigger physics collisions |

## Constructor Rule

Custom components loaded from scenes and prefabs must have a public parameterless constructor.

Do not put required runtime setup in constructor parameters. Use `Awake` or `Start` instead.

## Serialization

Supported property shapes include:

| Type | Notes |
|------|-------|
| `float`, `int`, `bool`, `string` | primitive types |
| `Vector2`, `Vector3`, `Quaternion` | `System.Numerics` types |
| `Color` | Raylib color |
| enums | serialized by name |
| `EntityReference` | links to entities in the scene |
| `AssetReference` | links to project assets |
| `FObject` subclasses | polymorphic serialized objects |
| `List<T>` where `T : FObject` | serialized lists of polymorphic objects |

## Inspector Attributes

Use inspector attributes when the default property rendering is not enough.

Most useful ones:

| Attribute | Use it for |
|-----------|------------|
| `[InspectorLabel("Name")]` | friendlier inspector labels |
| `[InspectorRange(min, max, speed)]` | clamped numeric editing |
| `[InspectorTooltip("Text")]` | inline help |
| `[InspectorSection("Title")]` | grouping related fields |
| `[InspectorReadOnly]` | visible but not editable values |
| `[InspectorHidden]` | serialized values that should not draw |
| `[AssetFilter(AssetType)]` | narrowing an `AssetReference` picker |
| `[ComponentCategory("Path")]` | organizing the Add Component menu |
| `[ComponentDisplayName("Name")]` | custom component name in the editor |

## Two Useful Patterns

### Referencing another entity

```csharp
public EntityReference Target { get; set; }
```

Use this when a component should track or talk to another entity. See [Prefabs](prefabs.md) for remapping behavior.

### Referencing an asset

```csharp
[AssetFilter(AssetType.Prefab)]
public AssetReference ProjectilePrefab { get; set; }
```

Use this when designers should choose a prefab, sound, model, or other asset from the inspector.

## If Something Is Not Showing Up

Check these first:

- build scripts succeeded
- the component is `public`
- the type derives from `Component`
- the `.fproject` points to the correct game project and game assembly

## See Also

- [Editor Guide](editor-guide.md) for the build-and-play loop
- [Prefabs](prefabs.md) for spawning and references
- [Physics](physics.md) for trigger and collision usage
