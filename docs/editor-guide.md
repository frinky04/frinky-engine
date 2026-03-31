# Editor Guide

Use this guide when you want to understand the normal editor loop: open a project, place content, inspect entities, run the scene, and save your work.

## Typical Workflow

1. Open or create a project.
2. Open a scene.
3. Add or select entities in the hierarchy.
4. Edit components in the inspector.
5. Drag assets in from the asset browser.
6. Build scripts with `Ctrl+B` when gameplay code changes.
7. Press `F5` to play, inspect the result, then stop and continue editing.
8. Save the scene with `Ctrl+S`.

## Open or Create a Project

- Open an existing project by launching the editor with a `.fproject` path or using the file menu.
- Create a new project with `File -> New Project`.
- For source-built projects, build gameplay scripts before testing them.

## Build and Run Gameplay

### Build scripts

Build game assemblies from the editor with `File -> Build Scripts` or `Ctrl+B`.

Use this after:

- changing gameplay C# code
- adding a new component type
- changing serialized gameplay properties

The editor hot-reloads the game assembly after a successful build.

### Play and simulate

- **Play** (`F5`) runs gameplay from the scene's main camera and locks scene editing.
- **Simulate** (`Alt+F5`) runs gameplay while keeping the editor camera and tools available.
- The editor snapshots the scene before entering either mode and restores it when you stop.
- **Shift+F1** toggles cursor lock during play mode.

## Place and Edit Scene Content

### Entity workflow

- Create entities by right-clicking in the hierarchy background or using the hierarchy's `Create` context menu.
- Rename with `F2`.
- Duplicate with `Ctrl+D`.
- Delete with `Delete`.
- Reparent by dragging entities in the hierarchy.

### Transform workflow

- **1** translate
- **2** rotate
- **3** scale
- **X** toggle world/local transform space

Some components expose their own viewport gizmos. When one of those is active, the normal entity transform gizmo is temporarily hidden.

### Asset workflow

- Drag models (`.obj`, `.gltf`, `.glb`), prefabs (`.fprefab`), and scenes (`.fscene`) from the asset browser into the viewport or hierarchy.
- Use search, tags, and type filters to narrow the asset list.
- Right-click assets for actions such as rename, delete, copy path, tag management, and `Regenerate Icon`.

Status dots on asset thumbnails:

- gray: queued
- blue: generating
- red: failed

## Common Authoring Tasks

### Create a new script or CanvasUI asset

Open a project first, then use the top menu:

- `Create -> Create Script...`
- `Create -> Create Canvas...`

These are created under `Assets/`.

### Add physics quickly

Right-click an entity in the hierarchy and choose **Add Physics**.

Available presets:

- **Static Body** for world geometry and immovable blockers
- **Dynamic Body** for simulated objects
- **Kinematic Body** for scripted movers

For primitive entities, collider size is inferred from the primitive. For mesh renderers, static and kinematic quick-add prefer `MeshColliderComponent` when possible.

### Preview colliders and bones

- `F8` toggles collider wireframe preview.
- `F9` enters collider edit mode for resizing or repositioning colliders in the viewport.
- `F10` shows skeleton joints and parent-child lines for entities with a skinned mesh animator.

## Panels

| Panel | What you use it for |
|-------|----------------------|
| **Viewport** | Navigate the scene, move entities, and inspect overlays |
| **Hierarchy** | Select, rename, duplicate, and reparent entities |
| **Inspector** | Edit engine and gameplay component properties |
| **Assets** | Browse project files and drag assets into the scene |
| **Console** | Read logs, warnings, and script build errors |
| **Performance** | Inspect frame timing, rendering stats, and asset icon generation stats |

## Console and Runtime Overlay

- **F3** cycles stats overlay modes.
- **\`** toggles the developer console.
- `help` lists commands and CVars.
- `Tab` cycles suggestions.
- `Up/Down` navigates command history.

## Keyboard Shortcuts

### File

| Shortcut | Action |
|----------|--------|
| Ctrl+N | New Scene |
| Ctrl+O | Open Scene |
| Ctrl+S | Save Scene |
| Ctrl+Shift+S | Save Scene As |
| Ctrl+Shift+N | New Project |

### Edit

| Shortcut | Action |
|----------|--------|
| Ctrl+Z | Undo |
| Ctrl+Y | Redo |
| Ctrl+D | Duplicate Entity |
| Delete | Delete Entity |
| F2 | Rename Entity |
| Ctrl+A | Select All |
| Escape | Deselect |

### Play and View

| Shortcut | Action |
|----------|--------|
| F5 | Play |
| Alt+F5 | Simulate |
| Shift+F1 | Toggle cursor lock |
| F8 | Toggle collider preview |
| F9 | Toggle collider edit mode |
| F10 | Toggle bone preview |

### Prefabs

| Shortcut | Action |
|----------|--------|
| Ctrl+Shift+M | Create Prefab from Selection |
| Ctrl+Alt+P | Apply Prefab |
| Ctrl+Alt+R | Revert Prefab |
| Ctrl+Shift+U | Make Unique Prefab |
| Ctrl+Alt+K | Unpack Prefab |
