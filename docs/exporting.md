# Exporting & Packaging

Use this guide when you want to run the game outside the editor or prepare a build for distribution.

## Use This When

Come here for:

- running the runtime in dev mode
- creating a packaged exported build
- validating the files needed to ship the game

## Runtime Modes

### Dev Mode (`.fproject`)

Use this while iterating on a project from source or local build outputs.

```bash
dotnet run --project src/FrinkyEngine.Runtime -- path/to/MyGame.fproject
```

This mode:

- loads the `.fproject`
- resolves `assetsPath`, `defaultScene`, and `gameAssembly`
- applies runtime settings from `project_settings.json`

### Exported Mode (`.fasset`)

Use this for packaged distribution.

The runtime looks for a `.fasset` file next to the executable, extracts it to a temp folder, loads assets and shaders, then runs the packaged startup scene.

## Export from the Editor

Use `File -> Export Game...` or `Ctrl+Shift+E`.

The export flow:

1. builds game scripts in `Release`
2. publishes the runtime
3. packs assets, shaders, manifest, and game assembly into `.fasset`
4. outputs `<OutputName>.exe` and `<OutputName>.fasset`

`OutputName` and `BuildVersion` come from `project_settings.json`.

## Prepare an Export Safely

Before exporting:

1. build scripts successfully
2. confirm the correct startup scene opens in the editor
3. confirm `project_settings.json` has the output name you want
4. run the project once in play mode

## Validate the Exported Build

After exporting, check all of these:

- the executable exists
- a `.fasset` archive exists beside it
- the exported runtime starts
- the startup scene loads
- assets and shaders render correctly
- audio plays

If the runtime fails immediately, first make sure there is a `.fasset` file beside the executable. The exporter writes matching names by convention, but the runtime looks for the first `.fasset` next to the executable rather than enforcing a name match.

## Scripted Packaging

### `package-game.bat`

```powershell
.\package-game.bat path\to\Game.fproject [outDir] [rid]
```

Use this when you want a scripted packaging path outside the editor.

What it currently copies:

- the runtime publish output
- the `.fproject`
- the game assembly
- the `Assets/` folder
- the `Scenes/` folder if one exists

It does not currently copy `project_settings.json`, so packaged dev-mode runs from this script can fall back to default runtime settings.

### `release-local.bat`

```powershell
.\release-local.bat v0.1.0
```

Use this when you want local release artifacts and versioned zip outputs from the repository itself.

## Runtime Overlay Controls

Available in both standalone runtime and editor play/simulate mode:

- **F3** cycles stats overlay modes
- **\`** toggles the developer console

Common console use:

- `help` to list commands and CVars
- `r_postprocess 0` or `1` to toggle post-processing

## See Also

- [Project Settings](project-settings.md) for build and runtime configuration
- [Getting Started](getting-started.md) for dev-mode runtime launch
