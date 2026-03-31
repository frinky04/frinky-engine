---
title: Legacy UI Roadmap
---

# UI Roadmap (Legacy Immediate-Mode)

> The active game-facing UI direction is now [CanvasUI](../CANVASUI_ROADMAP.md). This page tracks the older ImGui-wrapper roadmap, which still matters for editor-facing UI.

## Summary

This roadmap defines the phased plan for FrinkyEngine's immediate-mode wrapper UI system. It focuses on a wrapper-first API powered by ImGui, with runtime and editor parity where practical.

## Status

- State: Legacy roadmap
- Current baseline: Foundation work is implemented
- Scope: Immediate-mode wrapper API and editor-adjacent UI work
- Related docs: [Game UI](../ui.md), [CanvasUI Roadmap](../CANVASUI_ROADMAP.md)

## Current Baseline

The engine now includes:
- `FrinkyEngine.Core.UI` wrapper API for game scripts (`UI.Draw((ctx) => { ... })`)
- A strict no-raw-ImGui boundary for gameplay code
- Core HUD widgets:
  - `Text`, `Button`, `Checkbox`, `SliderFloat`, `ProgressBar`, `Image`
  - `Panel`, `Horizontal`, `Vertical`, `Spacer`, `SameLine`, `Separator`
- Dynamic font pixel sizing via ImGui's sized font push path
- Runtime integration (overlay rendering after world/post-process rendering)
- Editor Play/Simulate viewport integration (same game UI rendered into the viewport texture)
- Input capture exposure (`UI.InputCapture`) so gameplay input can avoid conflicting with UI interaction

## Design Principles

- Keep gameplay API stable and wrapper-only.
- Stay immediate-mode for low mental overhead and iteration speed.
- Preserve runtime/editor behavior parity where possible.
- Ship in slices with testable acceptance criteria.
- Prepare architecture for world-space UI without forcing it into v1.

## Roadmap Phases

| Phase | Status | Outcome |
|-------|--------|---------|
| v1 | Implemented | Wrapper API, core HUD widgets, dynamic font sizing, runtime/editor integration |
| v2 | Planned | Form controls, focus helpers, input-consumption policies |
| v3 | Planned | Styling layer, asset-aware helpers, diagnostics |
| v4 | Planned | World-space UI support |

## Phase Details

### Phase v2: Input and form controls

### Goals
- Expand from HUD/basic controls into practical menu/settings UI.
- Improve keyboard/gamepad interaction quality.

### Scope
- Add wrappers for:
  - text input
  - dropdown/combobox
  - list/select controls
- Add focus/navigation helpers:
  - default focus and tab order helpers
  - explicit focus APIs for scripted menu flow
- Add optional input consumption policies:
  - UI-only
  - gameplay-only
  - mixed mode with per-source gating

### Acceptance Criteria
- Common pause/settings menu can be built entirely with wrapper API.
- Keyboard and gamepad navigation works without raw ImGui calls.
- Input capture behavior is deterministic across runtime and editor play mode.

### Phase v3: Styling, assets, and diagnostics

### Goals
- Improve visual consistency and make style control easier for teams.
- Add practical diagnostics for UI performance and interaction debugging.

### Scope
- Theme/style layer:
  - named size/style tokens
  - project-level defaults with local overrides
- Asset-aware UI wrappers:
  - stronger texture/image handling
  - icon-friendly helpers
- Diagnostics:
  - UI frame timings
  - widget count / draw stats
  - optional debug overlay

### Acceptance Criteria
- Teams can define and apply shared UI look rules from one place.
- Large HUD/menu screens remain debuggable and measurable.

### Phase v4: World-space UI

### Goals
- Enable 3D-attached UI (diegetic panels, world labels, interaction prompts).
- Reuse existing wrapper API where possible.

### Scope
- Introduce world-space canvas concepts (entity/component driven).
- Add projection/transform path from world-space anchors to UI layout/render.
- Add world-space hit testing and interaction routing.
- Add depth behavior options (always on top vs depth-tested variants).

### Acceptance Criteria
- A world-anchored UI panel can be rendered and interacted with in a scene.
- Screen-space and world-space UIs can coexist in the same frame.

## Cross-Phase Workstreams
- Documentation and examples:
  - keep README and examples aligned with each phase
- Compatibility/versioning:
  - additive API growth first
  - deprecate old wrappers before removal
- Reliability:
  - maintain runtime/editor parity checks
  - keep simple smoke scenes for regression validation

## Suggested Execution Order
1. Finish v2 form/input controls.
2. Add v3 style system and diagnostics.
3. Implement v4 world-space UI.

## Exit Criteria for "Production Ready UI"
- Core gameplay UIs can be built without raw ImGui calls.
- Dynamic text sizing and common controls are stable.
- Runtime/editor play parity is dependable.
- Input capture is predictable and script-friendly.
- World-space UI path is implemented and documented.

