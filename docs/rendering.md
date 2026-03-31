# Rendering & Post-Processing

Use this guide when you want to make a scene visible, light it, animate skinned models, or add post-processing.

## Use This When

Come here for:

- camera setup
- lights
- models and materials
- skeletal animation and IK
- post-processing

## Minimal Scene Setup

For a basic lit 3D scene, you usually need:

1. a `CameraComponent` with `IsMain = true`
2. one or more visible entities such as primitives or `MeshRendererComponent`
3. at least one `LightComponent`

If the scene is black, check the camera, light, and shader output paths first.

## Cameras

Add a `CameraComponent` to the entity you want to render from.

Most important properties:

| Property | Use it for |
|----------|------------|
| `IsMain` | choose the active scene camera |
| `FieldOfView` | perspective camera FOV |
| `Projection` | perspective or orthographic |
| `NearPlane` / `FarPlane` | clip distances |
| `ClearColor` | background color |

## Lights

FrinkyEngine uses forward+ tiled lighting.

Available light types:

| Type | Use it for |
|------|------------|
| `Directional` | sunlight-style scene lighting |
| `Point` | local lights such as lamps or torches |
| `Skylight` | ambient fill lighting |

Forward+ tuning values live in `project_settings.json`:

- `forwardPlusTileSize`
- `forwardPlusMaxLights`
- `forwardPlusMaxLightsPerTile`

Most projects should leave these at defaults unless they have a specific scaling problem.

## Models and Materials

Supported model formats:

- OBJ
- GLTF
- GLB

Drag a model from the asset browser into the scene to create an entity with `MeshRendererComponent`.

Available material types:

| Type | Use it for |
|------|------------|
| `SolidColor` | flat-color shading |
| `Textured` | standard UV-mapped textures |
| `TriplanarTexture` | world or local axis projection |

## Skeletal Animation

For a skinned animated model:

1. import a GLTF or GLB with animation clips
2. add `MeshRendererComponent`
3. add `SkinnedMeshAnimatorComponent`

The animator can play embedded clips or clips loaded from additional animation source files.

### Important animator properties

| Property | Use it for |
|----------|------------|
| `ClipIndex` | choose the active clip |
| `PlayAutomatically` | start playback on load |
| `Playing` | pause or resume |
| `Loop` | looping playback |
| `PlaybackSpeed` | speed multiplier |
| `AnimationSources` | additional animation clip files |
| `UseEmbeddedAnimations` | include clips from the mesh file itself |

Press `F10` to preview bones in the editor.

## Inverse Kinematics

Add `InverseKinematicsComponent` to the same entity as the skinned animator when you need IK after animation sampling.

Built-in solvers:

- `TwoBoneIKSolver` for limbs
- `FABRIKSolver` for arbitrary-length chains
- `LookAtIKSolver` for head, eye, or turret aiming

Use this when imported animation is close, but you need runtime target control.

## Post-Processing

Add a `PostProcessStackComponent` to the camera entity.

Typical setup:

1. select the camera
2. add `PostProcessStackComponent`
3. add effects in the inspector
4. order them from top to bottom

Built-in effects:

- Bloom
- Fog
- Ambient Occlusion (SSAO)
- Palette
- Dither

Use `r_postprocess 0` or `1` in the console to toggle post-processing at runtime.

## Notes

- Skinned animators require a sibling `MeshRendererComponent`.
- Active skinned animation disables automatic instancing for that entity.
- Post-processing is only active when a post-process stack exists and is enabled.

## See Also

- [Project Settings](project-settings.md) for runtime rendering defaults
- [Editor Guide](editor-guide.md) for bone preview and scene workflow
