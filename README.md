# FrinkyEngine

A lightweight 3D game engine built with C#, Raylib, and ImGui; featuring an entity-component system, physics, scene/prefab serialization, and post-processing pipeline. Agentically/vibe coded.

## Features

- **Editor** with viewport, hierarchy, inspector, asset browser, console, and performance panels
- **Runtime** that runs from `.fproject` (dev) or `.fasset` (exported) files
- **Entity-Component** architecture with lifecycle hooks and hot-reloadable game assemblies
- **Forward+ Tiled Lighting** supporting hundreds of lights with directional, point, and skylight types
- **Post-Processing** pipeline with bloom, fog, and SSAO
- **BEPU Physics** with rigidbodies, colliders, and a character controller
- **Audio System** with 2D/3D playback, listener/source components, attenuation, and mixer buses
- **Game UI Wrapper API** (`FrinkyEngine.Core.UI`) for immediate-mode HUD/menu UI without raw ImGui calls
- **Prefab System** with `.fprefab` files, override tracking, and drag-and-drop instantiation
- **Entity References** for cross-entity linking that survive serialization and prefab instantiation
- **Scene Serialization** to human-readable `.fscene` JSON
- **Export Pipeline** that packages games into a single `.exe` + `.fasset` archive

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows for `.bat` helper scripts (or use `dotnet` commands directly on any platform)

## Quick Start

> **Note:** Example projects are stored as git submodules. Clone with `--recurse-submodules` to include them:
>
> ```bash
> git clone --recurse-submodules https://github.com/frinky04/FrinkyEngine.git
> ```
>
> If you already cloned without submodules, run:
>
> ```bash
> git submodule update --init --recursive
> ```

1. **Build the solution.**

```powershell
.\build.bat
```
```bash
dotnet build FrinkyEngine.sln -c Release
```

2. **Launch the editor.**

```powershell
.\launch-editor.bat
```
```bash
dotnet run --project src/FrinkyEngine.Editor
```

3. **Open a project.**

```powershell
.\launch-editor.bat ExampleGames\PrefabTestingGround\PrefabTestingGround.fproject
```
```bash
dotnet run --project src/FrinkyEngine.Editor -- ExampleGames/PrefabTestingGround/PrefabTestingGround.fproject
```

4. **Build game scripts** from the editor (`Scripts -> Build Scripts`) or manually:

```bash
dotnet build path/to/MyGame.csproj --configuration Debug
```

5. **Run the runtime.**

```powershell
.\launch-runtime.bat ExampleGames\PrefabTestingGround\PrefabTestingGround.fproject
```
```bash
dotnet run --project src/FrinkyEngine.Runtime -- path/to/MyGame.fproject
```

## Command Cheat Sheet

| Goal | Windows script | Dotnet equivalent |
|---|---|---|
| Build all projects | `.\build.bat [debug]` | `dotnet build FrinkyEngine.sln -c Release` |
| Run editor | `.\launch-editor.bat [Game.fproject]` | `dotnet run --project src/FrinkyEngine.Editor -- [Game.fproject]` |
| Run runtime (dev mode) | `.\launch-runtime.bat Game.fproject` | `dotnet run --project src/FrinkyEngine.Runtime -- Game.fproject` |
| Install template | `.\install-template.bat` | `dotnet new install .\templates\FrinkyEngine.Templates --force` |
| Publish editor | `.\publish-editor.bat [rid] [outDir]` | `dotnet publish src/FrinkyEngine.Editor/FrinkyEngine.Editor.csproj -c Release -r win-x64 --self-contained false -o artifacts/release/editor/win-x64` |
| Publish runtime | `.\publish-runtime.bat [rid] [outDir]` | `dotnet publish src/FrinkyEngine.Runtime/FrinkyEngine.Runtime.csproj -c Release -r win-x64 -p:FrinkyExport=true --self-contained false -o artifacts/release/runtime/win-x64` |
| Package a game | `.\package-game.bat path\to\Game.fproject [outDir] [rid]` | Run build + publish + copy steps manually |
| Local release zips | `.\release-local.bat vX.Y.Z` | Run restore/build/publish/pack/zip steps manually |

## Create a New Game Project

Install the template:

```powershell
.\install-template.bat
```
```bash
dotnet new install ./templates/FrinkyEngine.Templates --force
```

Create a project:

```bash
dotnet new frinky-game -n MyGame
```

Template output:

```text
MyGame/
  MyGame.fproject
  MyGame.csproj
  Assets/
    Scenes/MainScene.fscene
    Scripts/
      RotatorComponent.cs
      CharacterControllerExample.cs
  .gitignore
```

The template `.csproj` includes a placeholder comment for `FrinkyEngine.Core`. Add a `ProjectReference` to your local `src/FrinkyEngine.Core/FrinkyEngine.Core.csproj`.

## Editor Overview

### Panels

| Panel | Description |
|---|---|
| **Viewport** | 3D scene rendering with transform gizmos (translate, rotate, scale) |
| **Hierarchy** | Entity tree with drag-and-drop reordering and parenting |
| **Inspector** | Component editing with attribute-driven reflection and targeted custom drawers for complex UIs |
| **Assets** | File browser with drag-and-drop, inline rename (F2) with automatic reference updating, and tag management |
| **Console** | Log stream viewer |
| **Performance** | Frame time and rendering statistics, including an `Ignore Editor` toggle for runtime-estimate CPU views |

### Play and Simulate Modes

- **Play** (`F5`) — runs gameplay from the scene's main camera, locks scene editing
- **Simulate** (`Alt+F5`) — runs gameplay while keeping the editor camera and tools available
- The editor snapshots the scene before entering either mode and restores it on stop

## Documentation

Full engine documentation for game developers is available at [frinky04.github.io/FrinkyEngine](https://frinky04.github.io/FrinkyEngine/) and in the [`docs/`](docs/index.md) folder. The docs site is powered by VitePress:

```bash
npm install
npm run docs:dev
```

Core guides:

- [Getting Started](https://frinky04.github.io/FrinkyEngine/getting-started)
- [Editor Guide](https://frinky04.github.io/FrinkyEngine/editor-guide)
- [Scripting](https://frinky04.github.io/FrinkyEngine/scripting)
- [Components Reference](https://frinky04.github.io/FrinkyEngine/components)
- [Physics](https://frinky04.github.io/FrinkyEngine/physics)
- [Audio](https://frinky04.github.io/FrinkyEngine/audio)
- [Rendering & Post-Processing](https://frinky04.github.io/FrinkyEngine/rendering)
- [Game UI](https://frinky04.github.io/FrinkyEngine/ui)
- [Prefabs & Entity References](https://frinky04.github.io/FrinkyEngine/prefabs)
- [Exporting & Packaging](https://frinky04.github.io/FrinkyEngine/exporting)
- [Project Settings](https://frinky04.github.io/FrinkyEngine/project-settings)
- [API Reference](https://frinky04.github.io/FrinkyEngine/api/index) (auto-generated)

## Repository Layout

```text
FrinkyEngine/
  src/
    FrinkyEngine.Core/        # Engine library: ECS, rendering, physics, serialization
    FrinkyEngine.Editor/      # Dear ImGui editor application
    FrinkyEngine.Runtime/     # Standalone game player
  templates/
    FrinkyEngine.Templates/   # dotnet new template pack
  docs/                       # Game developer documentation
  Shaders/                    # GLSL shaders (copied to output)
  EditorAssets/               # Editor fonts and icons
  ExampleGames/
    PrefabTestingGround/      # [submodule] Example project with prefab usage
    PhysicsStressTest/        # [submodule] Example project with physics stress testing
  artifacts/                  # Publish and build outputs
  *.bat                       # Build, launch, publish, and packaging scripts
  FrinkyEngine.sln
  Directory.Build.props       # Global build settings (.NET 8, C# 12, unsafe)
```

## Example Games

Example projects are stored as [git submodules](https://git-scm.com/book/en/v2/Git-Tools-Submodules). See [Quick Start](#quick-start) for clone instructions.

### PrefabTestingGround

A test project demonstrating the prefab system, entity references, and post-processing. Located at `ExampleGames/PrefabTestingGround/`.

Open it in the editor:

```bash
dotnet run --project src/FrinkyEngine.Editor -- ExampleGames/PrefabTestingGround/PrefabTestingGround.fproject
```

### PhysicsStressTest

A test project for physics stress testing with rigidbodies and colliders. Located at `ExampleGames/PhysicsStressTest/`.

Open it in the editor:

```bash
dotnet run --project src/FrinkyEngine.Editor -- ExampleGames/PhysicsStressTest/PhysicsStressTest.fproject
```

## Dependencies

| Project | Package | Version |
|---|---|---|
| Core | `BepuPhysics` | 2.4.0 |
| Core | `Raylib-cs` | 7.0.2 |
| Editor | `NativeFileDialogSharp` | 0.5.0 |
| Editor | `Hexa.NET.ImGui` | 2.2.9 |
| Editor | `Hexa.NET.ImGui.Widgets` | 1.2.15 |
| Runtime | `Raylib-cs` | 7.0.2 |

Global: .NET 8, C# 12, nullable enabled, `AllowUnsafeBlocks=true`.

## License

Unlicensed. All rights reserved.
