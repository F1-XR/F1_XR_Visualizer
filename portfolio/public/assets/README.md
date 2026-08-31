# Portfolio media replacement guide

Real project captures were not present in the audited repository, so the site intentionally renders labeled media slots instead of fabricated screenshots. Add only captures that can be publicly shared and that match the behavior described on the page.

Adding a file alone does not silently replace a slot. After review, update `portfolio/index.html` with an explicit `<video>` or `<img>` element, real alternative text, width/height, and—when needed—captions. This keeps a missing or unreviewed asset from becoming an accidental public claim.

## Expected files

| Filename | Recommended format | Recommended dimensions | Content |
| --- | --- | --- | --- |
| `demo-main.mp4` | H.264 MP4, muted, no private audio | 1920×1080, under 15 MB | One continuous Quest 3 capture: room setup → event selection → Hero-centered diorama replay |
| `demo-poster.webp` | WebP | 1920×1080, under 350 KB | Poster frame for `demo-main.mp4` |
| `room-detection.webp` | WebP | 1600×1000, under 300 KB | Entry/Exit wall selection or room-understanding state |
| `path-generation.webp` | WebP | 1600×1000, under 300 KB | Entry–Hero–Exit setup preview, visibly labeled as not yet connected to the vehicle mapper |
| `vehicle-replay.webp` | WebP | 1600×1000, under 300 KB | Two replay cars on the Hero-centered, rigidly transformed event-local source track |
| `overtake-replay.webp` | WebP | 1600×1000, under 300 KB | A two-car presentation reconstruction with the order change visible |
| `architecture.webp` | WebP | 1600×1000, under 250 KB | Optional export of the verified architecture diagram; the live page currently uses accessible inline SVG |
| `og-image.webp` | WebP | 1200×630, under 300 KB | Social preview graphic; do not present generated art as a product screenshot |

## Capture checklist

- Remove IP addresses, personal paths, account names, API keys, room photos, and bystanders.
- Do not show F1 logos, broadcast footage, team marks, or other assets unless publication rights are confirmed.
- Keep debug overlays only when they directly explain an engineering decision.
- Avoid performance claims unless the capture comes with a reproducible Quest build, test scene, device settings, and measurement method.
- Add concise, factual alternative text in `src/main.js` when replacing a slot.
