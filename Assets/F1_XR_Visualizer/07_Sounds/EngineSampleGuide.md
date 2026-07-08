# F1 Engine Sample Guide

SampleLoop mode expects exterior mono loops recorded without baked Doppler, camera movement, cockpit resonance, or trackside pass-by distance changes.

## Minimum Slots

| Slot | Base RPM | Type | Notes |
| --- | ---: | --- | --- |
| idle | 5000 | Loop | Stable exterior idle, seamless loop. |
| high_on | 15000 | Loop | Exterior full throttle, high RPM, no shift, seamless loop. |
| high_off | 15000 | Loop | Exterior closed throttle, engine braking, no downshift, seamless loop. |
| mid_off | 11000 | Loop | Exterior closed throttle, medium RPM, seamless loop. |
| downshift_01 | n/a | One-shot | Short sequential gearbox downshift with RPM flare. |
| downshift_02 | n/a | One-shot | Alternate downshift so repeats do not sound identical. |

## Optional Expansion

| Slot | Base RPM | Type |
| --- | ---: | --- |
| low_on | 7000 | Loop |
| mid_on | 11000 | Loop |
| very_high_on | 18000 | Loop |
| low_off | 7000 | Loop |
| very_high_off | 18000 | Loop |
| upshift_01 | n/a | One-shot |
| upshift_02 | n/a | One-shot |
| gearbox_whine | n/a | Loop |
| rev_limiter | n/a | One-shot or loop |
| overrun_crackle | n/a | One-shot |

## Import Settings

- Load Type: Decompress On Load
- Compression Format: PCM or ADPCM
- Sample Rate Setting: Preserve Sample Rate
- Force To Mono: On for exterior 3D point sources
- Preload Audio Data: On

## Inspector Wiring

1. Select the scene object with `ChunkReplayPlayer`.
2. Open `Engine Sound`.
3. Set `Mode` to `SampleLoop`.
4. Assign clips either in `Sample Loop Layers` or the compatible legacy clip slots.
5. Use these starting base RPM values:
   - idle: `5000`
   - low: `7000`
   - mid: `11000`
   - high: `15000`
   - very high: `18000`
6. Keep `Generate Fallback Clips` disabled when judging real samples.

## Validation Checklist

- Throttle increase fades from off-load toward on-load.
- Throttle release fades from on-load toward off-load.
- Falling RPM moves from high off-load toward mid off-load.
- Gear decrease plays one downshift one-shot.
- Gear increase plays one upshift one-shot when clips are assigned.
- Procedural mode mutes SampleLoop sources.
- SampleLoop mode mutes procedural audio.
- Empty AudioClip slots run without exceptions.
