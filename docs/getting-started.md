# Getting Started

Use this guide when you want the fastest path from "I have the engine" to "I can launch a project and see it run."

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows for `.bat` helper scripts, or use the equivalent `dotnet` commands directly

## Choose Your Starting Path

Use a release if you want to try the editor quickly. Use source if you want the latest code, example projects, or to work on the engine itself.

### Release Build

1. Download a packaged release from [GitHub Releases](https://github.com/frinky04/frinky-engine/releases).
2. Extract it to a normal writable folder.
3. Launch `FrinkyEngine.Editor.exe`.
4. Create or open a project in the editor.

### Source Build

Clone the repository with submodules:

```bash
git clone --recurse-submodules https://github.com/frinky04/FrinkyEngine.git
cd FrinkyEngine
```

If you already cloned without submodules:

```bash
git submodule update --init --recursive
```

Build the solution:

```powershell
.\build.bat
```

```bash
dotnet build FrinkyEngine.sln -c Release
```

Launch the editor:

```powershell
.\launch-editor.bat
```

```bash
dotnet run --project src/FrinkyEngine.Editor
```

## First Success

The goal here is simple: prove the editor, project loading, and play loop work on your machine.

### Fastest path from source

Open the included example project:

```powershell
.\launch-editor.bat ExampleGames\PrefabTestingGround\PrefabTestingGround.fproject
```

```bash
dotnet run --project src/FrinkyEngine.Editor -- ExampleGames/PrefabTestingGround/PrefabTestingGround.fproject
```

Then:

1. Wait for the project to finish loading.
2. Press `F5` to enter Play mode.
3. Confirm the scene runs from the main camera.
4. Press `F5` again to stop.

### Fastest path from a release build

1. Launch the editor.
2. Create a project with `File -> New Project`.
3. Save it to a normal writable folder.
4. Open the default scene or create one.
5. Press `F5` to enter Play mode.
6. Confirm the project runs and returns to the editor when you stop.

## Create a New Game Project from Source

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

This generates:

```text
MyGame/
  MyGame.fproject
  MyGame.csproj
  Assets/
    Scenes/MainScene.fscene
    Scripts/
      RotatorComponent.cs
  .gitignore
```

The `dotnet new` template `.csproj` includes a placeholder comment for `FrinkyEngine.Core`. Add a `ProjectReference` to your local `src/FrinkyEngine.Core/FrinkyEngine.Core.csproj` if you are using the template path from source. The editor's `File -> New Project` flow generates a project file that already points at the running editor's engine DLL instead.

After creating the project:

1. Open the `.fproject` in the editor.
2. Build scripts with `File -> Build Scripts` or `Ctrl+B`.
3. Press `F5` to confirm the project runs.

## What Success Looks Like

You are in a good state when all of these are true:

- The editor opens your project without path errors.
- `Ctrl+B` completes without script build errors.
- `F5` enters Play mode and renders the scene.
- The runtime starts in dev mode:

```bash
dotnet run --project src/FrinkyEngine.Runtime -- path/to/MyGame.fproject
```

## Quick Editor Tour

| Panel | Description |
|-------|-------------|
| **Viewport** | 3D scene rendering with transform gizmos |
| **Hierarchy** | Entity tree with drag-and-drop reordering and parenting |
| **Inspector** | Component editing with attribute-driven reflection |
| **Assets** | File browser for models, prefabs, scenes, and scripts |
| **Console** | Log stream viewer |
| **Performance** | Frame time and rendering statistics |

## Next Steps

- [Editor Guide](editor-guide.md) to learn the normal authoring loop.
- [Scripting](scripting.md) to add your first gameplay component.
- [Components](components.md) to choose common built-in setups.
- [Prefabs](prefabs.md) to build reusable entities.
