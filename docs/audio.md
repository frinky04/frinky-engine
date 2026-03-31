# Audio

Use this guide when you want to play UI sounds, music, ambient loops, or spatialized world audio.

## Use This When

Use the audio system when you need:

- one-shot sound playback from code
- persistent sound emitters attached to entities
- 3D positional audio
- bus-based volume control for music, SFX, UI, voice, and ambient audio

## Minimal One-Shot Example

```csharp
Audio.PlaySound2D("Sounds/click.wav");
```

Use this for UI sounds, menu feedback, or simple non-spatial one-shots.

## Minimal 3D Example

```csharp
Audio.PlaySoundAtLocation("Sounds/explosion.wav", worldPosition);
```

Use this when the sound should come from a world position instead of the listener.

## Persistent Audio Sources

Attach `AudioSourceComponent` to an entity when the sound should belong to that entity.

Important properties:

| Property | Use it for |
|----------|------------|
| `SoundPath` | choose the source asset |
| `PlayOnStart` | start automatically with the scene |
| `Spatialized` | switch between 2D and 3D playback |
| `Looping` | keep the sound running |
| `Bus` | route to the correct mixer bus |
| `Volume` / `Pitch` | local playback adjustment |
| `AttenuationSettings` | 3D falloff behavior |

## 3D Audio Listener

Add `AudioListenerComponent` to the entity that should act as the listener.

If no explicit listener exists, the main camera is used as a fallback listener.

## Mixer Buses

Use buses to control categories of audio:

| Bus | Typical Use |
|-----|-------------|
| `Master` | overall output |
| `Music` | background music |
| `Sfx` | gameplay sound effects |
| `Ui` | menu and interface sounds |
| `Voice` | dialogue |
| `Ambient` | environmental audio |

Runtime volume control example:

```csharp
Audio.SetBusVolume(AudioBusId.Music, 0.5f);
Audio.SetBusMuted(AudioBusId.Sfx, true);
```

## AudioPlayParams

Use `AudioPlayParams` when the default one-shot behavior is not enough.

Most useful options:

- `Bus`
- `Volume`
- `Pitch`
- `Looping`
- `AttenuationOverride`

Example:

```csharp
Audio.PlaySound2D("Sounds/music.wav", new AudioPlayParams
{
    Bus = AudioBusId.Music,
    Volume = 0.8f,
    Looping = true
});
```

## Notes

- Missing audio assets fail safely with warnings instead of crashing the runtime.
- 3D sounds need either an `AudioListenerComponent` or a main camera fallback.
- Bus defaults and voice limits are configured in `project_settings.json`.

## See Also

- [Project Settings](project-settings.md) for runtime audio defaults
- [Choosing Components](components.md) for audio source and listener setup
