# Project Settings

Use this page when you need to change how a project is located, launched, or built.

## The Two Files That Matter

- `.fproject` tells the editor and runtime where your assets, startup scene, and game assembly live.
- `project_settings.json` controls runtime defaults and build metadata.

## `.fproject`

Typical file:

```json
{
  "projectName": "MyGame",
  "defaultScene": "Scenes/MainScene.fscene",
  "assetsPath": "Assets",
  "gameAssembly": "bin/Debug/net8.0/MyGame.dll",
  "gameProject": "MyGame.csproj"
}
```

### Change the startup scene

Set `defaultScene` to the scene you want to open at startup.

Important rule:

- `defaultScene` is relative to `assetsPath`, not the repository root

### Fix script build or hot-reload path issues

Check these fields first:

- `gameProject` should point to the gameplay `.csproj`
- `gameAssembly` should point to the DLL that project actually outputs

The editor script build uses `Debug` by default, so the usual path is under `bin/Debug`.

### `.fproject` fields

| Field | Description |
|-------|-------------|
| `projectName` | display name for the project |
| `defaultScene` | startup scene path, relative to `assetsPath` |
| `assetsPath` | root folder for project assets |
| `gameAssembly` | compiled gameplay DLL |
| `gameProject` | gameplay `.csproj` used for builds |

## `project_settings.json`

Typical file:

```json
{
  "project": {
    "version": "0.1.0",
    "author": "",
    "company": "",
    "description": ""
  },
  "runtime": {
    "targetFps": 120,
    "vSync": true,
    "windowTitle": "MyGame",
    "windowWidth": 1280,
    "windowHeight": 720,
    "resizable": true,
    "fullscreen": false,
    "startMaximized": false,
    "startupSceneOverride": "",
    "forwardPlusTileSize": 16,
    "forwardPlusMaxLights": 256,
    "forwardPlusMaxLightsPerTile": 64,
    "physicsFixedTimestep": 0.016666668,
    "physicsMaxSubstepsPerFrame": 4,
    "physicsSolverVelocityIterations": 8,
    "physicsSolverSubsteps": 1,
    "physicsContactSpringFrequency": 30.0,
    "physicsContactDampingRatio": 1.0,
    "physicsMaximumRecoveryVelocity": 2.0,
    "physicsDefaultFriction": 0.8,
    "physicsDefaultRestitution": 0.0,
    "physicsInterpolationEnabled": true,
    "audioMasterVolume": 1.0,
    "audioMusicVolume": 1.0,
    "audioSfxVolume": 1.0,
    "audioUiVolume": 1.0,
    "audioVoiceVolume": 1.0,
    "audioAmbientVolume": 1.0,
    "audioMaxVoices": 128,
    "audioDopplerScale": 1.0,
    "audioEnableVoiceStealing": true,
    "screenPercentage": 100
  },
  "build": {
    "outputName": "MyGame",
    "buildVersion": "0.1.0"
  }
}
```

## Common Changes

### Change the game window defaults

Edit:

- `windowTitle`
- `windowWidth`
- `windowHeight`
- `resizable`
- `fullscreen`
- `startMaximized`

### Override the startup scene at runtime

Use `startupSceneOverride` when you want to force a different scene without changing the main `.fproject` startup scene.

### Tune rendering defaults

Most projects only touch these if they have a specific need:

- `forwardPlusTileSize`
- `forwardPlusMaxLights`
- `forwardPlusMaxLightsPerTile`
- `screenPercentage`

### Tune physics defaults

These control the built-in runtime simulation settings:

- `physicsFixedTimestep`
- `physicsMaxSubstepsPerFrame`
- `physicsSolverVelocityIterations`
- `physicsSolverSubsteps`
- `physicsContactSpringFrequency`
- `physicsContactDampingRatio`
- `physicsMaximumRecoveryVelocity`
- `physicsDefaultFriction`
- `physicsDefaultRestitution`
- `physicsInterpolationEnabled`

### Tune audio defaults

Use these to set the initial mix and runtime limits:

- `audioMasterVolume`
- `audioMusicVolume`
- `audioSfxVolume`
- `audioUiVolume`
- `audioVoiceVolume`
- `audioAmbientVolume`
- `audioMaxVoices`
- `audioDopplerScale`
- `audioEnableVoiceStealing`

### Control build output naming

Use:

- `outputName` for the exported executable and archive base name
- `buildVersion` for the embedded build version

## Field Reference

### Project section

| Field | Description |
|-------|-------------|
| `version` | project version string |
| `author` | author name |
| `company` | company name |
| `description` | project description |

### Runtime section

| Field | Default | Description |
|-------|---------|-------------|
| `targetFps` | 120 | target frame rate (`0` for uncapped) |
| `vSync` | true | enable vertical sync |
| `windowTitle` | project name | initial window title |
| `windowWidth` | 1280 | initial window width |
| `windowHeight` | 720 | initial window height |
| `resizable` | true | allow window resizing |
| `fullscreen` | false | start in fullscreen |
| `startMaximized` | false | start maximized |
| `startupSceneOverride` | `""` | override startup scene |
| `forwardPlusTileSize` | 16 | lighting tile size in pixels |
| `forwardPlusMaxLights` | 256 | maximum total lights |
| `forwardPlusMaxLightsPerTile` | 64 | maximum lights per tile |
| `physicsFixedTimestep` | `1/60` | physics fixed timestep |
| `physicsMaxSubstepsPerFrame` | 4 | maximum physics catch-up steps per frame |
| `physicsSolverVelocityIterations` | 8 | solver velocity iterations |
| `physicsSolverSubsteps` | 1 | solver substeps |
| `physicsContactSpringFrequency` | 30.0 | contact spring frequency |
| `physicsContactDampingRatio` | 1.0 | contact damping ratio |
| `physicsMaximumRecoveryVelocity` | 2.0 | max penetration recovery velocity |
| `physicsDefaultFriction` | 0.8 | default friction |
| `physicsDefaultRestitution` | 0.0 | default restitution |
| `physicsInterpolationEnabled` | true | rigidbody interpolation toggle |
| `audioMasterVolume` | 1.0 | master bus volume |
| `audioMusicVolume` | 1.0 | music bus volume |
| `audioSfxVolume` | 1.0 | SFX bus volume |
| `audioUiVolume` | 1.0 | UI bus volume |
| `audioVoiceVolume` | 1.0 | voice bus volume |
| `audioAmbientVolume` | 1.0 | ambient bus volume |
| `audioMaxVoices` | 128 | maximum concurrent voices |
| `audioDopplerScale` | 1.0 | doppler effect scale |
| `audioEnableVoiceStealing` | true | allow voice stealing at max voices |
| `screenPercentage` | 100 | internal render scale percentage |

### Build section

| Field | Default | Description |
|-------|---------|-------------|
| `outputName` | project name | exported executable and archive name |
| `buildVersion` | `"0.1.0"` | embedded build version |

## Editor Settings

Per-project editor files are stored under `.frinky/`.

- `.frinky/editor_settings.json` stores editor preferences.
- `.frinky/keybinds.json` stores keybind overrides.

## See Also

- [Exporting & Packaging](exporting.md) for shipping builds
- [Getting Started](getting-started.md) for project creation and runtime launch
