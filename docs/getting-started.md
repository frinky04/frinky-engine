# Getting Started

This guide walks you through installing FrinkyEngine, creating a game project, and running it.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows for `.bat` helper scripts (or use `dotnet` commands directly on any platform)

## Download a Release or Build from Source

If you want to try the editor or runtime without building the repository yourself, download a packaged release from [GitHub Releases](https://github.com/frinky04/frinky-engine/releases).

If you want the latest source, example projects, or engine code for local development, clone the repository instead.

## Clone the Repository

Example projects are stored as git submodules. Clone with `--recurse-submodules` to include them:

```bash
git clone --recurse-submodules https://github.com/frinky04/FrinkyEngine.git
```

If you already cloned without submodules:

```bash
git submodule update --init --recursive
```

## Build

```powershell
.\build.bat
```

Or with dotnet directly:

```bash
dotnet build FrinkyEngine.sln -c Release
```

## Launch the Editor

```powershell
.\launch-editor.bat
```

```bash
dotnet run --project src/FrinkyEngine.Editor
```

To open a specific project:

```powershell
.\launch-editor.bat ExampleGames\PrefabTestingGround\PrefabTestingGround.fproject
```

```bash
dotnet run --project src/FrinkyEngine.Editor -- ExampleGames/PrefabTestingGround/PrefabTestingGround.fproject
```

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

This generates:

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

## Quick Editor Tour

| Panel | Description |
|-------|-------------|
| **Viewport** | 3D scene rendering with transform gizmos (translate, rotate, scale) |
| **Hierarchy** | Entity tree with drag-and-drop reordering and parenting |
| **Inspector** | Component editing with attribute-driven reflection |
| **Assets** | File browser with drag-and-drop for models, prefabs, and scenes |
| **Console** | Log stream viewer |
| **Performance** | Frame time and rendering statistics |

For a full editor walkthrough, see the [Editor Guide](editor-guide.md).

## Build and Run Your Game

1. Build game scripts from the editor: `File -> Build Scripts` (or `Ctrl+B`)
2. Enter play mode: press `F5` or `Play` in the toolbar
3. Run standalone: `dotnet run --project src/FrinkyEngine.Runtime -- path/to/MyGame.fproject`

## What's Next

- [Editor Guide](editor-guide.md) — master the editor tools and shortcuts
- [Scripting](scripting.md) — write custom components
- [Components Reference](components.md) — explore built-in components
- [Physics](physics.md) — set up rigidbodies and character controllers
