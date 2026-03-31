# FrinkyEngine

FrinkyEngine is a C# 3D game engine with a desktop editor, standalone runtime, JSON-based scenes and prefabs, hot-reloadable gameplay assemblies, and a packaged export pipeline.

## Highlights

- ImGui-based editor with viewport, hierarchy, inspector, asset browser, console, and performance tools
- Standalone runtime that runs from `.fproject` in development or `.fasset` in exported builds
- ECS-style gameplay model with custom components and hot-reloadable game assemblies
- Forward+ tiled lighting, post-processing, skeletal animation, and inverse kinematics
- BEPU physics with rigidbodies, colliders, triggers, and a built-in character controller
- Audio system with 2D and 3D playback, buses, listener/source components, and attenuation
- Prefab workflow with override tracking and entity-reference remapping
- `dotnet new` template pack for creating game projects

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows for the included `.bat` helper scripts, or use the equivalent `dotnet` commands directly

## Quick Start

### Option 1: Download a release

If you just want to try the editor, download a packaged build from [GitHub Releases](https://github.com/frinky04/frinky-engine/releases).

### Option 2: Build from source

Clone with submodules:

```bash
git clone --recurse-submodules https://github.com/frinky04/FrinkyEngine.git
cd FrinkyEngine
```

If you already cloned without submodules:

```bash
git submodule update --init --recursive
```

Build:

```powershell
.\build.bat
```

```bash
dotnet build FrinkyEngine.sln -c Release
```

Run the editor:

```powershell
.\launch-editor.bat
```

```bash
dotnet run --project src/FrinkyEngine.Editor
```

Open the example project:

```powershell
.\launch-editor.bat ExampleGames\PrefabTestingGround\PrefabTestingGround.fproject
```

```bash
dotnet run --project src/FrinkyEngine.Editor -- ExampleGames/PrefabTestingGround/PrefabTestingGround.fproject
```

## Common Commands

| Goal | Command |
|------|---------|
| Build the solution | `dotnet build FrinkyEngine.sln -c Release` |
| Run the editor | `dotnet run --project src/FrinkyEngine.Editor` |
| Run the runtime in dev mode | `dotnet run --project src/FrinkyEngine.Runtime -- path/to/Game.fproject` |
| Install the template | `dotnet new install ./templates/FrinkyEngine.Templates --force` |
| Publish the editor | `dotnet publish src/FrinkyEngine.Editor/FrinkyEngine.Editor.csproj -c Release -r win-x64 --self-contained false -o artifacts/release/editor/win-x64` |
| Publish the runtime | `dotnet publish src/FrinkyEngine.Runtime/FrinkyEngine.Runtime.csproj -c Release -r win-x64 -p:FrinkyExport=true --self-contained false -o artifacts/release/runtime/win-x64` |
| Package a game | `.\package-game.bat path\to\Game.fproject [outDir] [rid]` |
| Build the docs site | `npm run docs:build` |

## Create a Game Project

Install the template:

```bash
dotnet new install ./templates/FrinkyEngine.Templates --force
```

Create a project:

```bash
dotnet new frinky-game -n MyGame
```

The template creates a `.fproject`, `.csproj`, starter scene, and starter script under `Assets/Scripts/`.

## Documentation

- Docs site: [frinky04.github.io/frinky-engine](https://frinky04.github.io/frinky-engine/)
- Docs source: [docs/index.md](docs/index.md)
- Local docs dev server:

```bash
npm install
npm run docs:dev
```

Recommended starting pages:

- [Getting Started](https://frinky04.github.io/frinky-engine/getting-started)
- [Editor Guide](https://frinky04.github.io/frinky-engine/editor-guide)
- [Scripting](https://frinky04.github.io/frinky-engine/scripting)
- [Physics](https://frinky04.github.io/frinky-engine/physics)
- [Rendering & Post-Processing](https://frinky04.github.io/frinky-engine/rendering)
- [Exporting & Packaging](https://frinky04.github.io/frinky-engine/exporting)

## Repository Layout

```text
FrinkyEngine/
  src/
    FrinkyEngine.Core/        shared engine code
    FrinkyEngine.Editor/      desktop editor
    FrinkyEngine.Runtime/     standalone runtime
  templates/
    FrinkyEngine.Templates/   dotnet new template pack
  docs/                       hand-written documentation
  Shaders/                    GLSL shaders copied to output
  EditorAssets/               editor fonts and icons
  ExampleGames/               git submodule example projects
  artifacts/                  publish and build outputs
```

## Example Projects

Example projects are stored as git submodules.

- `ExampleGames/PrefabTestingGround/` demonstrates prefabs, entity references, and post-processing
- `ExampleGames/PhysicsStressTest/` exercises rigidbodies, colliders, and physics-heavy scenes

## Dependencies

| Project | Package | Version |
|---------|---------|---------|
| Core | `BepuPhysics` | 2.4.0 |
| Core | `Raylib-cs` | 7.0.2 |
| Editor | `NativeFileDialogSharp` | 0.5.0 |
| Editor | `Hexa.NET.ImGui` | 2.2.9 |
| Editor | `Hexa.NET.ImGui.Widgets` | 1.2.15 |
| Runtime | `Raylib-cs` | 7.0.2 |

Global project defaults: .NET 8, C# 12, nullable enabled, `AllowUnsafeBlocks=true`.

## License

Unlicensed. All rights reserved.
