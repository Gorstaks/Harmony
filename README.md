# Harmony

**Background healing tone generator for Windows.**

Plays low-volume therapeutic frequencies alongside your normal audio. No interception, no virtual cables, no audio modification - just adds a gentle tone layer.

## Presets

| Preset | Frequencies | Purpose |
|--------|-------------|---------|
| Schumann Resonance | 7.83 Hz binaural | Earth's natural frequency. Grounding, calm. |
| Deep Relaxation | 4 Hz theta binaural | Meditation, near-sleep state. |
| Focus & Concentration | 14 Hz beta binaural | Alertness, productivity. |
| 528 Hz Healing | 528 Hz + 264 Hz | Solfeggio "miracle tone" - restoration. |
| 432 Hz Natural Tuning | 432 Hz + 216 Hz | Harmonious alternative to 440Hz standard. |
| Sleep Induction | 2 Hz delta binaural | Deep dreamless sleep. |
| Anti-Anxiety | 10 Hz alpha + 396 Hz | Alpha state + liberation from fear. |
| Body Healing | 174 Hz + 285 Hz | Pain relief + tissue repair solfeggio. |

## How It Works

- Generates pure sine wave tones at therapeutic frequencies
- Binaural beats: slightly different frequency in each ear, brain perceives the difference as a pulsing tone that entrains brainwaves
- Plays at low volume alongside whatever else you're listening to
- Minimizes to system tray

## Usage

1. Run `Harmony.exe`
2. Select a preset
3. Adjust volume (10-20% recommended)
4. Click **Play**
5. Use headphones for binaural beat presets (L/R difference must reach each ear separately)

## Build

```
build.bat
```

Output: `publish\Harmony.exe` (self-contained, no .NET install needed)

## Notes

- Binaural beats require headphones to work - speakers blend L/R channels
- Keep volume low - these are meant to be felt, not consciously heard
- Safe to leave running all day
- No network access, no telemetry
