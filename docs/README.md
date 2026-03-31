# FrinkyEngine Documentation

Welcome to the FrinkyEngine documentation. This is the reference hub for game developers building projects with FrinkyEngine.

## Getting Started

New to FrinkyEngine? Start here:

1. [Getting Started](getting-started.md) — install prerequisites, create a project, and run it
2. [Editor Guide](editor-guide.md) — learn the editor panels, shortcuts, and workflows
3. [Scripting](scripting.md) — write custom components and gameplay code

## Guides

| Guide | Description |
|-------|-------------|
| [Getting Started](getting-started.md) | Prerequisites, project creation, first steps |
| [Editor Guide](editor-guide.md) | Panels, gizmos, play/simulate modes, keyboard shortcuts |
| [Scripting](scripting.md) | Custom components, lifecycle, game assemblies, auto-serialization |
| [Components Reference](components.md) | All built-in components with key properties |
| [Physics](physics.md) | Rigidbodies, colliders, character controller, crouching |
| [Audio](audio.md) | 2D/3D playback, components, mixer buses, attenuation |
| [Rendering & Post-Processing](rendering.md) | Lighting, materials, camera setup, skeletal animation, inverse kinematics, bloom/fog/SSAO |
| [Game UI](ui.md) | CanvasUI retained-mode panels, flexbox layout, and legacy immediate-mode wrapper |
| [Prefabs & Entity References](prefabs.md) | Prefab workflow, overrides, entity cross-linking |
| [Exporting & Packaging](exporting.md) | Export pipeline, runtime modes, packaging scripts |
| [Project Settings](project-settings.md) | `.fproject`, `project_settings.json`, editor settings |
| [Troubleshooting](troubleshooting.md) | Common issues and solutions |
| [API Reference](api/index.md) | Auto-generated from XML comments |

## API Reference

Auto-generated API documentation from XML comments is available in the [`api/`](api/index.md) folder. Regenerate it with:

```bash
.\generate-api-docs.bat
```

## Testing

Run the automated test suites from the repository root with:

```bash
dotnet test FrinkyEngine.sln
```

## Roadmaps

- [CanvasUI Roadmap](CANVASUI_ROADMAP.md) — retained-mode game UI (active plan)
- [Audio Roadmap](roadmaps/audio_roadmap.md)
- [UI Roadmap (Legacy)](roadmaps/ui_roadmap.md) — ImGui wrapper (editor-focused)
- [Asset Icon Implementation Guide](roadmaps/asset_icon_implementation_guide.md)
