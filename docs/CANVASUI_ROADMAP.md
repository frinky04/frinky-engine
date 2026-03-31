---
title: CanvasUI Roadmap
---

# CanvasUI Roadmap

## Summary

CanvasUI is the active roadmap for FrinkyEngine's game-facing UI. It replaces the immediate-mode `FrinkyEngine.Core.UI` gameplay layer with a retained-mode panel system inspired by web UI patterns, while the editor continues to use ImGui.

This is a clean break. There is no planned backward-compatibility layer for gameplay UI.

## Status

- State: Active roadmap
- Current milestone: Foundation and CSS styling are implemented
- Scope: Runtime and game-facing UI
- Related docs: [Game UI](ui.md), [Legacy UI Roadmap](roadmaps/ui_roadmap.md)

## Current Baseline

FrinkyEngine currently has:

- A retained-mode panel tree under `src/FrinkyEngine.Core/CanvasUI/`
- Yoga-backed flexbox layout
- Basic rendering, clipping, input, and pseudo-class support
- Runtime integration in `FrinkyEngine.Runtime`
- CSS parsing and style resolution
- Core widgets including labels, buttons, text entry, sliders, checkboxes, progress bars, scroll panels, and images

## Design Goals

- Keep the gameplay UI API retained-mode and composition-friendly
- Use familiar layout and styling concepts such as panels, classes, and flexbox
- Preserve a clear split between gameplay UI and editor UI
- Add features in testable slices instead of one large rewrite

## Roadmap Phases

| Phase | Status | Outcome |
|-------|--------|---------|
| Phase 1 | Implemented | Panel tree, Yoga layout, rendering, input, runtime wiring |
| Phase 2 | Implemented in part | CSS styling engine, renderer refactor, more built-in widgets |
| Phase 3 | Planned | Markup, data binding, hot reload, asset integration |
| Phase 4 | Planned | Transitions, transforms, batching, gamepad navigation |
| Phase 5 | Planned | Migration of overlays and retirement of old gameplay UI |

## Phase Details

### Phase 1: Foundation

Phase 1 established the retained-mode UI core: layout, rendering, input, and engine integration.

#### Namespace and project structure

Created `src/FrinkyEngine.Core/CanvasUI/` with:

```
CanvasUI/
  CanvasUI.cs              — Static entry point (Initialize/Update/Shutdown)
  Panel.cs                 — Base retained-mode panel class
  RootPanel.cs             — Tree root, owns layout/render/input subsystems
  PseudoClassFlags.cs      — [Flags] enum: Hover, Active, Focus, Disabled, Intro, Outro
  Box.cs                   — Computed layout rect struct (X, Y, Width, Height)
  Panels/
    Label.cs               — Text display
    Button.cs              — Clickable, raises OnClick
  Styles/
    StyleSheet.cs           — Per-panel inline style property bag
    ComputedStyle.cs        — Flat resolved style struct
    StyleEnums.cs           — FlexDirection, AlignItems, JustifyContent, Overflow, Display, PositionMode
    Length.cs               — Pixels / Percent / Auto
    Edges.cs                — Top/Right/Bottom/Left shorthand
    StyleResolver.cs        — Resolves inline styles into ComputedStyle
  Layout/
    YogaLayoutEngine.cs     — Syncs styles → Yoga nodes, runs CalculateLayout, reads results
  Rendering/
    CanvasRenderer.cs       — Depth-first tree traversal, draws via Raylib
    DrawCommands.cs         — Filled rect, rounded rect, text, textured quad
    FontManager.cs          — TTF font loading/caching via Raylib.LoadFontEx
    ScissorStack.cs         — Nested clipping rect stack (Rlgl.Scissor)
  Input/
    InputManager.cs         — Hit testing, hover/focus tracking, event dispatch
  Events/
    MouseEvent.cs           — ScreenPos, LocalPos, Button, Target, Handled
    FocusEvent.cs
    KeyboardEvent.cs
```

#### Yoga dependency

Added `IceReaper.YogaSharp` (1.18.0.3) NuGet package to `FrinkyEngine.Core.csproj`. Wraps Facebook's Yoga C library (used by React Native).

#### Panel base class

Every UI element is a `Panel`. Key API:
- `Id`, `Classes` (list of string class names)
- `Parent`, `Children` tree links
- `Style` (inline StyleSheet), `ComputedStyle` (resolved)
- `PseudoClasses` flags (set by input manager)
- `YogaNode` handle (internal)
- Events: `OnClick`, `OnMouseOver`, `OnMouseOut`, `OnMouseDown`, `OnMouseUp`, `OnFocus`, `OnBlur`
- Lifecycle: `OnCreated()`, `OnDeleted()`, `Tick()` (per-frame)
- Child management: `AddChild<T>()`, `RemoveChild()`, `DeleteChildren()`

#### Rendering backend

Uses Raylib drawing API (`DrawRectangleRounded`, `DrawTextEx`, etc.) with Rlgl scissor clipping. Render pipeline:
1. Disable depth test
2. Depth-first traversal of panel tree
3. Per panel: draw background rect → draw border → draw content (text/image) → recurse children
4. Scissor clipping for `Overflow.Hidden`

#### Engine integration

In `src/FrinkyEngine.Runtime/Program.cs`, within `RunGameLoop`:
- After `UI.Initialize()`: `CanvasUI.Initialize()`
- In the UI profile scope: `CanvasUI.Update(dt, screenW, screenH)`
- At shutdown: `CanvasUI.Shutdown()`

#### Usage example

```csharp
var hud = CanvasUI.RootPanel.AddChild<Panel>(p => {
    p.Style.FlexDirection = FlexDirection.Row;
    p.Style.Padding = new Edges(12);
});
hud.AddChild<Label>(l => { l.Text = "Health: 100"; });
hud.AddChild<Button>(b => { b.Text = "Menu"; b.OnClick += _ => OpenMenu(); });
```

### Phase 2: CSS styling and more widgets

#### CSS styling and renderer refactor

**Renderer refactor**: Content rendering moved from type-checks in `CanvasRenderer` to virtual `Panel.RenderContent()` overrides in `Label` and `Button`. Prepares the architecture for new panel types without touching the renderer.

**CSS engine**: Hand-written tokenizer, parser, and selector matcher in `Styles/Css/`:

```
Styles/Css/
  CssToken.cs          — Token type enum + struct with line/col tracking
  CssTokenizer.cs      — Tokenizes CSS strings, skips /* */ comments
  CssSelector.cs       — Selector chain (type, class, pseudo-class, combinators)
  CssSpecificity.cs    — (Ids, Classes, Types) ordering
  CssStyleRule.cs      — Selector + StyleSheet declarations
  CssParser.cs         — Full CSS parser: tokenize → selectors → declaration blocks
  CssPropertyMap.cs    — Maps CSS property strings to StyleSheet setters, handles shorthands
  CssColorNames.cs     — Named colors + #hex + rgb()/rgba() parsing
  CssSelectorMatcher.cs — Matches panels against selectors (type, class, pseudo, descendant, child)
```

**Supported selectors**: type (`Label`), class (`.foo`), pseudo (`:hover`, `:active`, `:focus`, `:disabled`), descendant (space), child (`>`), universal (`*`), comma-separated groups.

**Supported properties**: All layout and visual properties from Phase 1, plus shorthand `padding`, `margin`, `border`.

**Style cascade**: `StyleResolver` collects matching rules per panel, sorts by specificity, applies low→high, then applies inline `panel.Style` last (highest priority).

**Public API**: `CanvasUI.LoadStyleSheet(css)`, `CanvasUI.ClearStyleSheets()`.

#### More built-in panels

| Panel | Purpose |
|-------|---------|
| `TextEntry` | Single-line text input with cursor, selection, keyboard handling |
| `Checkbox` | Toggle with `:checked` pseudo-class |
| `Slider` | Range input (horizontal drag) |
| `ProgressBar` | Visual fill bar |
| `ScrollPanel` | Scrollable container, mouse wheel, scroll bar |
| `Image` | Displays a Texture2D/RenderTexture |

#### Border rendering

Rounded corners via tessellated triangle fans at each corner.

### Phase 3: Markup and data binding

#### XML markup format (`.canvas` files)

```xml
<Panel class="hud-root">
    <Label text="{Health}" class="health-text" />
    <Button text="Menu" onclick="OpenMenu" />
</Panel>
```

Parsed at runtime into Panel trees. **Not** Razor — avoids heavy `Microsoft.AspNetCore.Razor.Language` dependency.

#### Data binding

`{PropertyName}` syntax bound to a context object implementing `INotifyPropertyChanged`.

#### Hot reload

File watcher on `.canvas` and `.css` files — rebuilds panel tree and re-applies styles during development.

#### Asset integration

Markup/CSS loaded through `AssetManager` for both dev and exported builds.

### Phase 4: Polish and advanced features

- **SDF font rendering** — Raylib's built-in SDF support for resolution-independent text
- **Transitions** — CSS-like `transition: opacity 0.3s ease`, `:intro`/`:outro` pseudo-classes for enter/exit animations
- **Transforms** — `translate`, `rotate`, `scale` applied as matrix before render
- **Box shadows** — Blurred rectangle behind panel
- **Gamepad navigation** — D-pad focus traversal
- **Dirty flags** — Skip layout/style recalc when nothing changed
- **Draw batching** — Minimize Rlgl state changes

### Phase 5: Migration

1. Port `EngineOverlays` (stats + dev console) to CanvasUI panels
2. Port `DebugDraw.PrintString` to CanvasUI
3. Move old `FrinkyEngine.Core.UI` to editor-only (or delete entirely)
4. Remove `Hexa.NET.ImGui` dependency from `FrinkyEngine.Core.csproj`

## Key Files

| File | Change |
|------|--------|
| `src/FrinkyEngine.Core/FrinkyEngine.Core.csproj` | Added IceReaper.YogaSharp NuGet package |
| `src/FrinkyEngine.Runtime/Program.cs` | Wired CanvasUI.Initialize/Update/Shutdown into game loop |
| `src/FrinkyEngine.Core/CanvasUI/**` | All new files (listed above) |

## Verification Targets

1. **Build** — `dotnet build FrinkyEngine.sln` compiles with no errors ✅
2. **Runtime smoke test** — Launch runtime with a test scene; a game component creates a Panel with a Label and Button; panels render on screen with correct flexbox layout
3. **Input test** — Hover highlights button (pseudo-class), click fires OnClick event
4. **Coexistence** — Old ImGui UI (EngineOverlays) still works alongside CanvasUI in the same frame

## Supported CSS Properties

| Property | Values | Default | Notes |
|----------|--------|---------|-------|
| `background-color` | `#hex`, `rgb()`, `rgba()`, named colors | `transparent` | |
| `color` | `#hex`, `rgb()`, `rgba()`, named colors | `white` | Text/foreground color |
| `border-color` | `#hex`, `rgb()`, `rgba()`, named colors | `transparent` | |
| `border-width` | `<length>` | `0` | |
| `border-radius` | `<length>` | `0` | |
| `border` | `<width> <style> <color>` | — | Shorthand; style keyword is ignored |
| `opacity` | `0`–`1` | `1` | |
| `font-size` | `<length>` | `16px` | |
| `text-align` | `left`, `center`, `right` | `left` | Horizontal text alignment |
| `width` | `<length>`, `<percent>`, `auto` | `auto` | |
| `height` | `<length>`, `<percent>`, `auto` | `auto` | |
| `min-width` | `<length>`, `<percent>`, `auto` | `auto` | |
| `min-height` | `<length>`, `<percent>`, `auto` | `auto` | |
| `max-width` | `<length>`, `<percent>`, `auto` | `auto` | |
| `max-height` | `<length>`, `<percent>`, `auto` | `auto` | |
| `flex-direction` | `column`, `column-reverse`, `row`, `row-reverse` | `column` | |
| `justify-content` | `flex-start`, `center`, `flex-end`, `space-between`, `space-around`, `space-evenly` | `flex-start` | |
| `align-items` | `auto`, `flex-start`, `center`, `flex-end`, `stretch`, `baseline` | `stretch` | |
| `align-self` | `auto`, `flex-start`, `center`, `flex-end`, `stretch`, `baseline` | `auto` | |
| `flex-grow` | `<number>` | `0` | |
| `flex-shrink` | `<number>` | `1` | |
| `flex-basis` | `<length>`, `<percent>`, `auto` | `auto` | |
| `gap` | `<length>` | `0` | |
| `display` | `flex`, `none` | `flex` | |
| `position` | `relative`, `absolute` | `relative` | |
| `overflow` | `visible`, `hidden`, `scroll` | `visible` | |
| `top`, `right`, `bottom`, `left` | `<length>`, `<percent>`, `auto` | `auto` | For `position: absolute` |
| `padding` | 1–4 `<length>`/`<percent>` values | `0` | Shorthand (top, right, bottom, left) |
| `padding-top/right/bottom/left` | `<length>`, `<percent>` | `0` | |
| `margin` | 1–4 `<length>`/`<percent>` values | `0` | Shorthand |
| `margin-top/right/bottom/left` | `<length>`, `<percent>` | `0` | |

## Supported Selectors

| Selector | Example | Notes |
|----------|---------|-------|
| Type | `Label` | Matches panel class name |
| Class | `.foo` | Matches `panel.AddClass("foo")` |
| Pseudo-class | `:hover`, `:active`, `:focus`, `:disabled`, `:checked` | |
| Descendant | `Panel Label` | Space combinator |
| Child | `Panel > Label` | Direct child combinator |
| Universal | `*` | Matches any panel |
| Grouping | `Label, Button` | Comma-separated |

## Widget Behavior Notes

### Button
- Sets `text-align: center` as an inline default in `OnCreated` — text is centered unless overridden by CSS.
- `AcceptsFocus = true` — receives `:focus` pseudo-class.
- Text is vertically centered within the content area (inside padding).

### Label
- Text is positioned at top-left of the content area by default (`text-align: left`).
- Respects `text-align` for horizontal positioning.
- Padding is read from Yoga's resolved layout values, so percentage padding works correctly.

### Checkbox
- Renders an inline checkbox box + optional label text.
- Toggles `:checked` pseudo-class on click.
- Fires `OnChanged` event with the new checked state.
- Padding is read from Yoga's resolved layout values.

### TextEntry
- Single-line text input with cursor, selection, and keyboard handling.
- Click-to-place cursor, shift+click/drag for selection.
- Supports Ctrl+A, Ctrl+C, Ctrl+V, Ctrl+X, Home, End, Delete, Backspace.
- Padding is read from Yoga's resolved layout values.

### Slider
- Horizontal drag slider with configurable `Min`, `Max`, `Step`.
- `color` controls the filled track and thumb color.
- Fires `OnChanged` with the mapped value (not normalized).

### ScrollPanel
- Sets `overflow: hidden` as an inline default in `OnCreated`.
- Mouse wheel scrolls content vertically.
- Renders a scrollbar track + thumb when content overflows.

### Image
- Displays a `Texture2D` stretched to fill the panel's content box.
- Set via `image.Texture = myTexture`.
