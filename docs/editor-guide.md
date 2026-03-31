# Editor Guide

The FrinkyEngine editor is an ImGui-based desktop application for building and testing scenes.

## Panels

| Panel | Description |
|-------|-------------|
| **Viewport** | 3D scene rendering with transform gizmos (translate, rotate, scale) |
| **Hierarchy** | Entity tree with drag-and-drop reordering and parenting |
| **Inspector** | Component editing with attribute-driven reflection (engine and script components use the same inspector pipeline) |
| **Assets** | File browser with drag-and-drop for models, prefabs, and scenes |
| **Console** | Log stream viewer with severity filtering, text search, timestamps toggle, and entry counts |
| **Performance** | Frame time and rendering statistics, including an `Ignore Editor` toggle for runtime-estimate CPU views. Collapsible **Asset Icons** section shows queue depth, cache hit rate, and generation timing. |

## Transform Gizmos

- **1** — Translate mode
- **2** — Rotate mode
- **3** — Scale mode
- **X** — Toggle world/local space

## Inspector Gizmos

Some components expose draggable gizmo handles in the viewport (for example, IK target and pole-target positions). Click a gizmo sphere to select it, then use the translate handle to reposition it. The entity transform gizmo is hidden while an inspector gizmo is active; click empty space to deselect.

## Entity Management

- **Create**: right-click in the hierarchy or use the `Entity` menu
- **Rename**: select an entity and press `F2`
- **Delete**: select an entity and press `Delete`
- **Duplicate**: `Ctrl+D`
- **Reparent**: drag entities in the hierarchy to reorder or nest them
- **Select all**: `Ctrl+A`
- **Deselect**: `Escape`

## Play and Simulate Modes

- **Play** (`F5`) — runs gameplay from the scene's main camera, locks scene editing
- **Simulate** (`Alt+F5`) — runs gameplay while keeping the editor camera and tools available
- The editor snapshots the scene before entering either mode and restores it on stop
- **Shift+F1** — toggle cursor lock during play mode

## Asset Browser

- Browse project files with filtering and search
- Drag-and-drop models (`.obj`, `.gltf`, `.glb`), prefabs (`.fprefab`), and scenes (`.fscene`) into the viewport or hierarchy
- Tags and type filters for quick asset lookup
- Canvas UI files (`.canvas`) are classified as **Canvas** assets with their own icon/filter
- Right-click an asset for context actions: open, copy path, rename, delete, tag management, and **Regenerate Icon** (textures, models, prefabs)
- Small status dots appear on asset thumbnails during icon generation: gray (queued), blue (generating), red (failed)
- Click the settings cog (⚙) to access:
  - **Refresh** — manually refresh the asset database
  - **Grid/List View** — toggle between view modes
  - **Icon Size** — scale asset icons (0.5x–3.0x)
  - **Hide Unrecognised Assets** — filter out files with unknown types when browsing all assets
  - **Tag Manager** — create, edit, and delete asset tags

## Creating Assets

Use `Create` in the top menu bar to create assets:

- `Create -> Create Script...`
- `Create -> Create Canvas...`

Both entries open the same asset-creation modal and create files directly under `Assets/` (folder picker is not used).

## Building Scripts

Build game assemblies from the editor with `File -> Build Scripts` (`Ctrl+B`). The editor hot-reloads the assembly without restarting.

## Quick-Add Physics

Right-click an entity in the Hierarchy and select **Add Physics** to quickly add collider and rigidbody components. Three presets are available: **Static Body** (collider only, registered as a static collidable), **Dynamic Body** (collider + dynamic rigidbody), and **Kinematic Body** (collider + kinematic rigidbody). The collider shape and size are auto-detected from the entity's primitive component (Cube, Sphere, Cylinder, or Plane). The same shortcuts are available in the Inspector under **Quick Add Physics**.

## Physics Hitbox Preview

Press `F8` to toggle a wireframe overlay showing collider shapes in the viewport.

## Collider Edit Mode

Press `F9` (or use **View -> Collider Edit Mode**) to enter collider edit mode. While active:

- All collider wireframes in the scene are displayed.
- The selected entity's collider can be resized and repositioned using an ImGuizmo handle (Scale + Translate).
- Box colliders: drag the scale handles to adjust `Size`, or translate to move the `Center` offset.
- Sphere colliders: drag any scale handle to uniformly adjust `Radius`, or translate to move `Center`.
- Capsule colliders: drag scale handles to adjust `Radius` (X/Z) and `Length` (Y), or translate to move `Center`.
- The normal entity transform gizmo is replaced by the collider gizmo while this mode is active.
- Changes are tracked by the undo system.

## Bone Preview

Press `F10` to toggle a bone preview overlay in the viewport. When enabled, bone joints are drawn as small wireframe spheres and parent-child connections are drawn as lines for all entities with a Skinned Mesh Animator component. The inspector also shows a collapsible bone hierarchy tree under the Skinned Mesh Animator component header.

## Stats Overlay and Developer Console

- **F3** — cycle stats overlay modes: None → FPS + MS → Advanced Stats → Most Verbose Stats
- **\`** (Grave) — toggle the developer console
  - `help` lists registered commands and cvars
  - `Tab` cycles suggestions, `Enter` accepts and executes
  - `Up/Down` navigates command history

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
| Delete | Delete Entity |
| Ctrl+D | Duplicate Entity |
| F2 | Rename Entity |
| Ctrl+A | Select All |
| Escape | Deselect |

### Build and Runtime

| Shortcut | Action |
|----------|--------|
| Ctrl+B | Build Scripts |
| F5 | Play / Stop |
| Alt+F5 | Simulate / Stop |
| Shift+F1 | Toggle Play Mode Cursor Lock |

### Gizmos

| Shortcut | Action |
|----------|--------|
| 1 | Translate Mode |
| 2 | Rotate Mode |
| 3 | Scale Mode |
| X | Toggle World/Local Space |

### View

| Shortcut | Action |
|----------|--------|
| G | Toggle Game View |
| F | Frame Selected (focus camera on selection) |
| F3 | Cycle Stats Overlay |
| F8 | Toggle Physics Hitbox Preview |
| F9 | Toggle Collider Edit Mode |
| F10 | Toggle Bone Preview |

### Navigation

| Shortcut | Action |
|----------|--------|
| Ctrl+F | Focus Hierarchy Search |
| Right Arrow | Expand Selection (Hierarchy) |
| Left Arrow | Collapse Selection (Hierarchy) |

### Project and Tools

| Shortcut | Action |
|----------|--------|
| Ctrl+Shift+O | Open Assets Folder |
| Ctrl+Shift+V | Open Project in VS Code |
| Ctrl+Shift+E | Export Game |

### Prefabs

| Shortcut | Action |
|----------|--------|
| Ctrl+Shift+M | Create Prefab from Selection |
| Ctrl+Alt+P | Apply Prefab |
| Ctrl+Alt+R | Revert Prefab |
| Ctrl+Shift+U | Make Unique Prefab |
| Ctrl+Alt+K | Unpack Prefab |

### Camera Controls (Viewport)

| Input | Action |
|-------|--------|
| Right Mouse Button (hold) | Free Look |
| W/A/S/D (while right-mouse held) | Move Camera |
| Left Shift (while right-mouse held) | 2.5x Camera Speed |
| Mouse Scroll | Zoom (2x speed) |

All keybinds are customizable per-project in `.frinky/keybinds.json`.
