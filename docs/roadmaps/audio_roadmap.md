---
title: Audio Roadmap
---

# Audio Roadmap

## Summary

This roadmap covers the planned post-v1 evolution of the FrinkyEngine audio system, including authored sound assets, spatial context, mixer controls, diagnostics, and backend extensibility.

## Status

- State: Planned
- Current baseline: v1 audio system is implemented
- Scope: Runtime audio authoring, playback, diagnostics, and backend strategy
- Related docs: [Audio](../audio.md)

## Current Baseline

The current system provides:
- UE-style static gameplay helpers (`PlaySound2D`, `PlaySoundAtLocation`, `SpawnSoundAttached`)
- `AudioSourceComponent` and `AudioListenerComponent`
- 2D and 3D playback with distance attenuation and stereo pan
- Mixer bus controls (`Master`, `Music`, `Sfx`, `Ui`, `Voice`, `Ambient`)
- Project settings and export manifest support for audio configuration
- Editor inspector support, performance stats, and audio asset recognition

The next phases focus on authoring depth, runtime scalability, and advanced spatial features.

## Design Principles

- Keep gameplay API stable. New features should be additive.
- Keep editor and runtime behavior consistent.
- Avoid hard-locking into one backend. Preserve backend abstraction boundaries.
- Ship in small slices with measurable acceptance criteria.
- Prefer deterministic behavior over hidden automation.

## Roadmap Phases

| Phase | Status | Outcome |
|-------|--------|---------|
| v2 | Planned | Authored `SoundAsset` workflow, concurrency, bus ducking, better editor tooling |
| v3 | Planned | Occlusion, reverb zones, Doppler, spatial debugging |
| v4 | Planned | Mixer snapshots, runtime profiling, advanced bus control |
| v5 | Planned | Backend capability model, plugin path, import and cook improvements |

## Phase Details

### Phase v2: Authoring and voice management

### Goals
- Move from "play a file" toward reusable authored sound behaviors.
- Add deterministic voice governance under load.
- Improve designer workflow inside the editor.

### Scope
1. `SoundAsset` format (`.fsound`)
- Introduce a serialized audio asset that references one or more source clips.
- Support basic playback graph features:
  - Random select (weighted)
  - Sequential select
  - Pitch variance range
  - Volume variance range
  - Start time variance range
- Keep format JSON for easy diffs and versioning.

2. Concurrency groups
- Add project-level concurrency definitions:
  - Max voices per group
  - Resolution policy (`StopOldest`, `StopLowestPriority`, `RejectNew`)
- Allow `AudioSourceComponent` and static API calls to specify a concurrency group.

3. Bus ducking rules
- Add optional bus ducking in project settings:
  - Trigger bus
  - Target bus
  - Duck amount
  - Attack and release times
- Use this primarily for `Voice` over `Music`.

4. Editor improvements
- Add `SoundAsset` inspector.
- Add one-click preview for `SoundAsset`.
- Display current concurrency group usage in performance panel.

### Public API Additions
- `AudioHandle PlaySound(string soundAssetPath, in AudioPlayParams? playParams = null)`
- `AudioPlayParams.ConcurrencyGroup` (string or ID)
- `Audio.GetConcurrencyStats(...)`

### Data and Serialization
- New asset type: `AssetType.SoundAsset`
- New extension mapping: `.fsound`
- Add `SoundAsset` metadata to export pipeline

### Tests and Acceptance Criteria
- Under a stress scene, concurrency rules enforce limits exactly.
- `SoundAsset` random and sequence selection is deterministic when seeded.
- Bus ducking attack and release curves are frame-rate independent.
- Exported game reproduces same `SoundAsset` behavior as editor Play mode.

### Risks
- Complex authoring can create hard-to-debug layering effects.
- Concurrency rules can surprise users unless inspector feedback is clear.

### Phase v3: Spatial context and environment audio

### Goals
- Improve positional realism in practical game scenes.
- Integrate environment awareness without overloading CPU.

### Scope
1. Occlusion and obstruction
- Add optional per-source occlusion checks:
  - Single ray (v3 baseline)
  - Configurable check interval
  - Low-pass and volume attenuation when blocked
- Integrate with existing physics system.

2. Reverb zones
- Add `AudioReverbZoneComponent`:
  - Shape (box/sphere)
  - Reverb preset
  - Send level
  - Blend distance
- Listener position drives active zone blending.

3. Listener velocity and Doppler
- Use transform deltas for listener/source velocity estimates.
- Respect `AudioDopplerScale` from project settings.

4. Debug visualization
- Gizmos for attenuation radius, occlusion ray, and reverb zone bounds.
- Optional per-source debug labels in viewport.

### Public API Additions
- `AudioSourceComponent.OcclusionEnabled`
- `AudioSourceComponent.OcclusionIntervalMs`
- `AudioSourceComponent.ReverbSend`
- `Audio.GetSpatialDebugStats()`

### Tests and Acceptance Criteria
- Occluded source transitions are smooth with no hard pops.
- Reverb zone transitions blend without sudden gain jumps.
- Doppler effect remains stable for high-speed motion.
- Performance overhead is measurable and configurable in settings.

### Risks
- Occlusion raycasts can become expensive in dense scenes.
- Reverb and occlusion interactions can over-attenuate if not normalized.

### Phase v4: Mixer, snapshots, and runtime control

### Goals
- Enable robust runtime mixing workflows for gameplay states.
- Improve tooling for live balancing and diagnostics.

### Scope
1. Mixer snapshots
- Add snapshot assets (`.fmixsnap`) that define target bus states.
- Support snapshot blend transitions with duration and easing.

2. Advanced bus graph control
- Per-bus LPF/HPF controls.
- Optional compressor/limiter at master bus.
- Safe defaults with opt-in advanced controls.

3. Runtime profiling and capture
- Add audio profiler panel with:
  - Voice list
  - Bus meter values
  - Occlusion and reverb contributions
  - Stream IO counters
- Add optional CSV dump for session analysis.

4. Scripting ergonomics
- Snapshot API:
  - `Audio.PushSnapshot(name, blendSeconds)`
  - `Audio.PopSnapshot(name, blendSeconds)`
- One-shot helper overloads for common gameplay usage.

### Tests and Acceptance Criteria
- Snapshot blends are deterministic and reversible.
- Master limiter prevents clipping under synthetic stress mix.
- Profiler counters match runtime state within one frame.

### Risks
- Advanced DSP controls can complicate support burden.
- Incorrect limiter defaults can alter mix character unexpectedly.

### Phase v5: Platform and extensibility strategy

### Goals
- Prepare for scale, portability, and optional higher-fidelity backends.

### Scope
1. Backend capability model
- Add capability flags to backend abstraction:
  - Streaming
  - Pan
  - Filters
  - HRTF
  - Reverb sends
- Engine features should degrade gracefully when unsupported.

2. Optional backend plugins
- Keep Raylib backend as default.
- Define path for additional backends without changing gameplay API.

3. Import and cook pipeline
- Optional pre-processing for distribution:
  - Loudness normalization metadata
  - Format conversion rules
  - Streaming chunk hints

### Tests and Acceptance Criteria
- Same gameplay scene behaves consistently across supported backends.
- Missing backend capability logs explicit warnings and uses fallback logic.

## Cross-Phase Workstreams

### Reliability
- Add unit and integration test project for audio-specific logic.
- Build synthetic stress scenes for CI regression checks.

### Migration and Versioning
- Include version field in future `.fsound` and `.fmixsnap` formats.
- Add migration utilities for older asset schema versions.

### Documentation
- Keep README aligned each phase.
- Add dedicated user docs for:
  - Authoring guides
  - Mixer and snapshot usage
  - Performance tuning

## Suggested Execution Order
1. Finish v2 `SoundAsset` + concurrency first (highest user impact).
2. Implement v3 occlusion and reverb zones with strict performance budgets.
3. Add v4 snapshots and profiler for live balancing workflows.
4. Execute v5 backend capability work once feature set stabilizes.

## Exit Criteria for "Production Ready Audio"
- Stable authored asset pipeline (`.fsound`, optional snapshots).
- Clear concurrency and priority behavior under stress.
- Environment features (occlusion/reverb) with controllable cost.
- Solid diagnostics in editor and runtime.
- Export parity with editor behavior.
