# Game UI

FrinkyEngine currently exposes two UI paths for game code:

- **CanvasUI** (`FrinkyEngine.Core.CanvasUI`) is the recommended retained-mode UI system for new work.
- **Immediate-mode UI** (`FrinkyEngine.Core.UI`) is the older ImGui wrapper and is best treated as legacy gameplay UI.

## Use This When

Choose **CanvasUI** when you want:

- persistent HUDs and menus
- panel trees and reusable UI widgets
- layout driven by flexbox-style rules
- styling with classes and CSS-like selectors

Use the immediate-mode wrapper only if you already have gameplay UI built on it or you need that style of API for a quick internal tool.

## Minimal CanvasUI Example

```csharp
using Raylib_cs;
using FrinkyEngine.Core.CanvasUI;
using FrinkyEngine.Core.CanvasUI.Panels;
using FrinkyEngine.Core.CanvasUI.Styles;
using FrinkyEngine.Core.ECS;

public class HudComponent : Component
{
    private Label _healthLabel = null!;

    public override void Start()
    {
        var hud = CanvasUI.RootPanel.AddChild<Panel>(p =>
        {
            p.Style.FlexDirection = FlexDirection.Row;
            p.Style.Padding = new Edges(12);
            p.Style.Gap = 8;
        });

        _healthLabel = hud.AddChild<Label>(l =>
        {
            l.Text = "Health: 100";
            l.Style.FontSize = 20f;
            l.Style.Color = new Color(0, 255, 0, 255);
        });

        hud.AddChild<Button>(b =>
        {
            b.Text = "Menu";
            b.OnClick += _ => OpenMenu();
        });
    }

    public override void Update(float dt)
    {
        _healthLabel.Text = $"Health: {GetHealth()}";
    }

    private int GetHealth() => 100;
    private void OpenMenu() { }
}
```

What success looks like:

- the label appears on screen
- the button is clickable
- the label text updates at runtime

## Common CanvasUI Workflow

1. Create panels from `CanvasUI.RootPanel`.
2. Use `Panel` as the base container type.
3. Add built-in child panels such as `Label`, `Button`, `Checkbox`, `Slider`, `ProgressBar`, `TextEntry`, `ScrollPanel`, and `Image`.
4. Set simple layout inline first.
5. Move repeated styling into CSS-like rules once the structure works.

## Styling

CanvasUI supports CSS-like styling through style sheets and selector matching.

Typical workflow:

1. Give panels classes with `AddClass()`.
2. Load style sheets with `CanvasUI.LoadStyleSheet(css)`.
3. Keep one-off overrides inline on the panel itself.

Example:

```css
Panel.hud {
  flex-direction: row;
  gap: 8px;
  padding: 12px;
}

Button {
  font-size: 18px;
}
```

## Common Built-In Panels

| Panel | Use it for |
|-------|------------|
| `Panel` | layout containers |
| `Label` | text output |
| `Button` | click actions |
| `Checkbox` | boolean toggles |
| `Slider` | normalized or mapped value selection |
| `ProgressBar` | health, stamina, loading, cooldown |
| `TextEntry` | single-line text input |
| `ScrollPanel` | scrollable content |
| `Image` | textures and render textures |

## Immediate-Mode UI

The older `FrinkyEngine.Core.UI` wrapper still exists, but it is no longer the recommended path for new gameplay UI.

Use it when:

- you already have existing gameplay UI built on it
- you need a quick immediate-mode overlay

Avoid using it as the default choice for new game HUDs or menus.

## See Also

- [Scripting](scripting.md) for general gameplay component structure
- [Editor Guide](editor-guide.md) for creating Canvas assets
- [CANVASUI_ROADMAP.md](CANVASUI_ROADMAP.md) if you specifically want the long-term direction for CanvasUI
