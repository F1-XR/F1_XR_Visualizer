# MY LITTLE GRAND PRIX

**MY LITTLE GRAND PRIX (MLGP)** is the public working title for `F1_XR_Visualizer`, a Unity XR technical study that works with the companion `F1_XR_Server`.

The client consumes externally prepared historical F1 replay datasets, reconstructs loaded drivers on one source-time timeline, and presents selected events as a room-scale mixed-reality diorama. The current default presentation preserves an event-local source-track shape and applies a reversible transform around a Hero focus in the user's room.

> **Project status:** in development. The repository audit confirms code, default-scene wiring, and active configuration; it does not yet confirm a reproducible Quest device session, performance benchmark, passed test run, or direct OpenF1 integration from the Unity client.

## Portfolio case study

- [Repository-grounded project analysis](docs/PORTFOLIO_PROJECT_ANALYSIS.md)
- [Portfolio site specification](docs/PORTFOLIO_SITE_SPEC.md)
- [Public claim verification pass 2](docs/PORTFOLIO_CLAIM_VERIFICATION.md)
- [Main portfolio integration guide](docs/MAIN_PORTFOLIO_INTEGRATION.md)
- [Isolated portfolio site](portfolio/README.md)
- Personal portfolio case-study URL: [yundonggeurami.github.io/f1](https://yundonggeurami.github.io/f1/)

The site intentionally shows labeled media slots until real, rights-cleared Quest captures are supplied. See [the media replacement guide](portfolio/public/assets/README.md).

## Verified architecture

```text
F1_XR_Server dataset API
  → manifest polling and ready chunk loading
  → source + processed driver sample stores
  → one shared ReplayTimeline
  → synchronized multi-driver replay
  → EventPopoutReplay event-local stage
  → first two event drivers in a Hero-centered RoomDiorama

AR planes
  → wall filtering and conservative plane reacquisition
  → distinct Entry / Exit walls + Hero focus
  → frozen room frames
  → reversible room presentation transform
```

Important implementation boundary: the current `ShowcasePathPreview` is a setup preview/gate and is not consumed by the default vehicle mapper. The default RoomDiorama does not guarantee that vehicles cross the selected Entry and Exit walls. Connected dual-wall portals and room-shell fracture are not default production paths.

## Technology

- Unity `6000.4.11f1`
- C# and Universal Render Pipeline `17.4.0`
- OpenXR `1.16.1`
- Meta OpenXR `2.5.1`
- XR Interaction Toolkit `3.4.1`
- AR Foundation `6.5.0` through the resolved XR dependency graph
- Android ARM64 / IL2CPP
- REST / JSON companion-service integration

The project is configured to target Meta Quest devices including Quest 3. Hand tracking and environment occlusion are disabled in the current OpenXR configuration and are not claimed as active features.

## Related project

- `F1_XR_Server`: companion service that prepares and serves catalog, dataset manifest, and replay chunks
- `F1_XR_Visualizer`: this Unity XR client

The Unity repository does not contain an OpenF1 fetcher. Confirm and document the companion backend's data provenance before describing datasets as OpenF1-derived.

## Unity run order

1. Start a compatible `F1_XR_Server` instance.
2. Configure the API base URL for the target runtime. The repository default `http://127.0.0.1:8000` is a local development value; on a Quest headset it does not point to the development PC.
3. Open the project with Unity `6000.4.11f1`.
4. Start from the configured bootstrap flow and load `SessionSpace` for the default replay path.
5. Complete permission, passthrough, room-plane, input, and recovery validation on the intended device before calling the build verified.

For the service contract, see [F1_XR_Server Integration Guide](docs/F1_XR_Server.md).

## Portfolio site development

Requirements: Node.js 22.12 or newer.

```bash
cd portfolio
npm install
npm run dev
```

Production verification:

```bash
cd portfolio
npm ci
npm run build
npm run verify
```

This Unity repository does not deploy GitHub Pages. The live case-study source is integrated into the separate personal repository `YunDonggeurami/Yundonggeurami.github.io` under `f1/`. That repository's existing Pages workflow builds the root portfolio and the F1 child site, merges them into one artifact, and publishes `/f1/` without touching the team repository's Pages.

## Validation status

This portfolio implementation was validated independently from the Unity runtime. The Unity source, scenes, prefabs, packages, and project settings were audited read-only for public claims. Unity Play Mode, Editor tests, Android build, backend execution, and Quest device tests were not run as part of the portfolio work.

The repository currently includes Editor tests for selected pit and portal presentation helpers, but no test result is claimed until they are actually executed and recorded.

## Project structure

```text
F1_XR_Visualizer/
├─ Assets/F1_XR_Visualizer/  Main Unity assets, scenes, scripts, prefabs, and data
├─ Packages/                 Unity package manifest and lock file
├─ ProjectSettings/          Unity and platform settings
├─ docs/                     Integration and portfolio audit documentation
├─ portfolio/                Isolated static case-study site
└─ .github/workflows/        GitHub Pages deployment
```

## Publication note

Before publishing captures or the repository broadly, verify personal contribution ownership, remove or template tracked local paths, review Unity Cloud/telemetry identifiers, replace the development API configuration, and confirm licenses for F1/team marks, vehicle art, music, aerial imagery, and data sources.

MLGP is an independent technical study and is not affiliated with Formula 1, its teams, Meta, Unity, or any data provider.
