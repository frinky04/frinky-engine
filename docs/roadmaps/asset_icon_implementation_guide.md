---
title: Asset Icon Roadmap
---

# Asset Icon Roadmap

## Summary

This roadmap tracks the staged rollout for generated asset icons for textures, models, and prefabs, plus the longer-term goal of making preview rendering reusable for scripting and tooling scenarios such as inventory icons.

## Status

- State: In progress
- Current milestone: Editor icon generation and most editor UX integration are implemented
- Scope: Asset-browser previews, icon generation pipeline, future reusable preview APIs
- Related docs: [Scripting](../scripting.md)

## Current Baseline

The current implementation already includes:

- An editor-side icon service with a throttled work queue
- Generated previews for textures, meshes and models, and prefabs
- Off-screen preview rendering through temporary preview scenes
- Disk caching under `.frinky/asset-icons/`
- Cache invalidation keyed by asset changes
- Asset Browser and `AssetReference` inspector integration

## Design Goals

- Keep icon generation cheap enough to run alongside editor interaction
- Reuse the same preview pipeline for more than one editor surface
- Expand toward scripting and tooling use cases without coupling them to editor panels
- Keep cache behavior deterministic and debuggable

## Roadmap Phases

| Phase | Status | Outcome |
|-------|--------|---------|
| Phase 1 | Implemented | Core icon generation, caching, invalidation, preview rendering |
| Phase 2 | Mostly implemented | Asset Browser integration, inspector integration, status UI, perf counters |
| Phase 3 | Planned | Reusable preview API, pluggable providers, runtime-safe controls, scripting support |

## Phase Details

### Phase 1: Editor icon generation pipeline

- [x] Add an editor-side icon service with a background-style queue that processes a small amount of work each frame.
- [x] Support generated icon rendering for textures.
- [x] Support generated icon rendering for meshes and models.
- [x] Support generated icon rendering for prefabs.
- [x] Render previews off-screen through a temporary preview scene, then dispose of the preview resources.
- [x] Cache generated icon PNGs under `.frinky/asset-icons/`.
- [x] Add a cache manifest keyed by asset path with a content hash to skip unchanged assets.
- [x] Wire icon invalidation and requeueing into asset refresh and file-change flow.

### Phase 2: Editor UX integration and reliability

- [x] Use generated icons in the Asset Browser with fallback to static type icons.
- [x] Use generated icons in `AssetReference` inspector dropdown entries.
- [x] Add an on-demand `Regenerate Icon` context action for supported asset types.
- [x] Add generation status indicators for queued, generating, and failed states.
- [x] Add perf counters such as queue length, milliseconds per icon, and cache hit rate.
- [ ] Add retry and backoff behavior for transient generation failures.
- [x] Add prefab preview support for non-model prefab renderables such as primitives and custom renderables.

### Phase 3: Generic runtime and scripting extensibility

- [ ] Extract preview rendering into a reusable API surface that does not depend on editor panels.
- [ ] Introduce pluggable icon providers such as `IIconPreviewProvider`.
- [ ] Allow script-side registration of custom icon builders for gameplay items and inventory definitions.
- [ ] Add runtime-safe output controls for size, camera framing preset, and transparent or solid backgrounds.
- [ ] Document scripting-facing usage patterns in [Scripting](../scripting.md).
- [ ] Add an example for generating inventory icons from scripted item definitions at build time or in tooling mode.

## Risks

- Preview rendering can become too expensive if queue throttling or cache invalidation regresses.
- Reusable preview APIs can accidentally pull editor-only dependencies into scripting workflows.
- Asset-specific preview logic can become fragmented unless provider registration stays centralized.

## Exit Criteria

- Generated icons are dependable across the main editor workflows.
- Failures are visible, recoverable, and inexpensive to retry.
- Preview rendering is reusable outside the Asset Browser.
- Scripting and tooling scenarios can generate icons without depending on editor UI code.
