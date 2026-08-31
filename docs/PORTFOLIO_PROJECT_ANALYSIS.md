# MY LITTLE GRAND PRIX — repository-grounded portfolio analysis

> Audit date: 2026-08-31
>
> Internal repository name: `F1_XR_Visualizer`
>
> Public working title supplied by the project owner: **MY LITTLE GRAND PRIX (MLGP)**
>
> Verification method: static source inspection, serialized scene/prefab inspection, package and build-setting inspection. This audit did **not** run Unity Play Mode, automated tests, the companion backend, an Android build, or a Quest device session.

## 1. Purpose and evidence policy

This document is the source of truth for public portfolio copy. It reconciles three audits—replay/data, MR spatial architecture, and platform/deployment—against the actual default runtime path.

Evidence is prioritized in this order:

1. A live call site on the default runtime path.
2. A component serialized and enabled in the default scene or prefab.
3. Active package and platform configuration.
4. Tests or development notes, labeled as such and never treated as executed evidence.
5. Uncalled code, alternate scenes, and design notes, classified as experimental, disconnected, abandoned, or planned.

Earlier notes do not override contradictory source, scene, prefab, or configuration evidence.

### Status vocabulary

| Status | Meaning in this audit |
| --- | --- |
| **Verified implementation** | Code exists and its call site, serialized scene/prefab connection, or active setting was confirmed. It does not imply a successful device session. |
| **Test-session observation** | A dated development note records a result from one test session. It is not a general benchmark. |
| **Experimental** | Implemented in an alternate path, curated scenario, or explicitly experimental mode, but not established as the default experience. |
| **Disconnected / abandoned** | Code exists but is not consumed by the production path, or a duplicate/obsolete helper has no call site. |
| **Planned** | Required work or validation is not represented by an active, verified implementation. |

## 2. Reconciled default runtime

The safest high-level description is:

> A Unity XR client that consumes externally prepared historical F1 replay datasets, reconstructs multiple drivers on one source-time timeline, and presents selected events as a room-scale mixed-reality diorama. The default room flow uses AR planes to select two different walls and a Hero focus, then applies a reversible rigid presentation transform to an event-local two-car stage.

The default runtime path is:

```text
BootstrapSpace
  └─ HomeSpace
      └─ SessionSpace (default replay target)
          ├─ VRDroneSpace (additive for SessionSpace* hosts)
          ├─ AutoReplayStarter → ApiClient → manifest/chunks
          ├─ ReplayPlayer → shared ReplayTimeline → ReplayCarSet
          ├─ EventPopoutReplay → event-local path/progress/stage
          └─ Room Showcase
              ├─ WallDiscovery
              ├─ ShowcaseLayout + RoomShowcaseSetupController
              └─ ShowcaseVehiclePathMapper
                  └─ default RoomDiorama: Hero-centered rigid placement
```

Primary runtime evidence:

- Default scene selection: `Assets/F1_XR_Visualizer/02_Scripts/RestAPI/SessionSelect/UI/ReplayLoadUI.cs:24,146`.
- Bootstrap/additive loading: `Assets/F1_XR_Visualizer/02_Scripts/Bootstrap/BootstrapLoader.cs:12-16,67-88,120-139`.
- Default Room Showcase serialization and mode: `Assets/F1_XR_Visualizer/01_Scenes/SessionSpace.unity:755-962`.
- Replay dataset bootstrap: `Assets/F1_XR_Visualizer/02_Scripts/RestAPI/Replay/Startup/AutoReplayStarter.cs:36-57,97-202`.
- Event component configuration: `Assets/F1_XR_Visualizer/02_Scripts/RestAPI/Replay/Playback/ReplayPlayer.cs:205-208`.

### Build Settings caveat

`ProjectSettings/EditorBuildSettings.asset:8-28` enables `BootstrapSpace`, `HomeSpace`, `SessionSpace`, `SessionSpace0804`, `VRDroneSpace`, `SessionSpace 1`, and `DrivingTest`. The referenced `SessionSpace0804.unity` file is absent. `SessionSpace_fitin.unity` exists but is not in Build Settings. Build inclusion alone is not evidence that each scene is a finished product path.

## 3. Public claim ledger

### 3.1 Replay and data pipeline

| Public-safe claim | Status | Runtime evidence | Boundary |
| --- | --- | --- | --- |
| The Unity client consumes catalog, dataset manifest, and time-chunk endpoints from a companion REST service. | Verified implementation | `Assets/F1_XR_Visualizer/02_Scripts/RestAPI/Api/ApiClient.cs:11-73`; `AutoReplayStarter.cs:36-57,97-202`; `SessionSpace.unity:3491-3519` | The repository contains no backend or OpenF1 fetcher. Do not claim that Unity calls OpenF1 directly. |
| Manifest polling waits for ready chunks, and the chunk loader fetches current or requested time ranges into driver-indexed in-memory stores. | Verified implementation | `Replay/Playback/ReplayManifestPoller.cs:18-74`; `Replay/Playback/ReplayChunkLoader.cs:11-17,47-70,98-269`; `Replay/Playback/ReplayPlayer.cs:223-296,662-717` | This is not persistent or offline caching. |
| Location data keeps a normalized source copy after time sorting and duplicate-timestamp removal, then derives a separate playback copy with additional direction-glitch cleanup and motion stabilization. | Verified implementation | `Replay/Samples/LocationReplaySamples.cs:14-19,21-128,172-247`; `Replay/Samples/LocationMotionStabilizer.cs:16-112,214-317` | This is presentation processing, not preservation of byte-identical server order or validation of physical telemetry truth. |
| One source-time timeline samples the loaded drivers consistently and supports pause, speed, seek, and optional red-flag downtime compression. | Verified implementation | `Replay/Playback/ReplayPlayer.cs:88-118,176-209,340-380,507-635`; `Replay/Playback/ReplayTimeline.cs:38-327`; `Replay/Car/ReplayCarSet.cs:390-543` | It is local historical replay, not live or network-synchronized playback. |
| Curated fixtures and server events merge by `eventId`, with the manifest event winning a duplicate. | Verified implementation | `Replay/Event/ReplayEventMerger.cs:9-28`; `Replay/Playback/ReplayPlayer.cs:956-965`; `Replay/Event/EventPopoutReplay.cs:4475-4505` | Event sources are mixed; do not call every event automatically detected. |

The data contract includes driver metadata, race-control flags, replay events, chunk descriptors, location/telemetry, position, and tire samples: `Assets/F1_XR_Visualizer/02_Scripts/RestAPI/Api/ApiModels.cs:53-202`. A DTO proves the client contract, not the upstream provenance or accuracy of every field.

### 3.2 Room understanding and layout

| Public-safe claim | Status | Runtime evidence | Boundary |
| --- | --- | --- | --- |
| AR plane semantics, vertical fallback, and minimum size filter likely walls; when a floor polygon containing the camera is available, automatic setup further restricts candidates to that boundary. | Verified implementation | `Replay/Room/WallDiscovery.cs:298-343,410-413,848-881,1015-1239`; `RoomShowcaseSetupController.cs:606-681`; `SessionSpace.unity:792-891` | Without a usable containing boundary or automatic pair, setup falls back to manual selection of distinct Entry and Exit walls. This is not a full semantic room mesh. |
| A removed plane can be reacquired by exact ID or a short heuristic match using normal, distance, overlap, size, and ambiguity checks. | Verified implementation | `WallDiscovery.cs:1368-1489,1696-1935,2040-2318`; the default grace period is serialized in `SessionSpace.unity:803-825` | Reacquisition can time out or reject ambiguous candidates. |
| The layout requires two different walls and defines Entry, Exit, and a user-view or automatically placed Hero focus. | Verified implementation | `Replay/Room/ShowcaseLayout.cs:88-104,334-441,477-514,594-724`; `RoomShowcaseSetupController.cs:75-82,176-310,606-777` | The standard setup is not a single-wall layout. |
| After confirmation, the wall frames are frozen and AR plane/raycast managers and plane visualizers are suspended; reset restores them. | Verified implementation | `ShowcaseLayout.cs:477-514`; `RoomShowcaseSetupController.cs:1173-1255` | No percentage performance improvement was measured. |

### 3.3 Event-local MR presentation

| Public-safe claim | Status | Runtime evidence | Boundary |
| --- | --- | --- | --- |
| An active general event builds an event-local source path and per-driver longitudinal progress, then binds the first two distinct drivers to a room presentation stage. | Verified implementation | `Replay/Event/EventPopoutReplay.cs:1654-1835,2006-2135`; `Replay/Room/ShowcaseVehiclePathMapper.cs:1506-1787,6302-6327` | The room mapper is a two-car path. `EventPopoutReplay` can stage up to four drivers, but that is a different limit. |
| The default `RoomDiorama` preserves the event-local source-track shape and applies one position/yaw/uniform-scale transform to `EventReplayStage`, centered on the Hero pose. | Verified implementation | `ShowcaseVehiclePathMapper.cs:5698-5934,6083-6153`; `EventPopoutReplay.cs:762-820`; mode serialized in `SessionSpace.unity:927-935` | It does not guarantee that cars cross the selected Entry and Exit walls. |
| Authoritative replay roots remain separate from presentation: `EventReplayStage` owns the room placement transform, while each `VisualMotionRoot` owns vehicle-specific presentation motion/effect/scale; both are restored on release. | Verified implementation | `Replay/Car/ReplayCarView.cs:26-158`; `EventPopoutReplay.cs:762-820`; `ShowcaseVehiclePathMapper.cs:6142-6148,6390-6420,6838-6868,7010-7045` | Room placement is a presentation reconstruction, not a change to authoritative source data. |
| A free TrackExit portal and a pit single-wall portal have active call paths. | Verified implementation | `ShowcaseVehiclePathMapper.cs:1906-1934,3534-3680`; `ShowcasePortalPresentation.cs:314-524`; `PitWallShowcasePresenter.cs:318-331` | The connected dual-wall portal overload is not called. |

### 3.4 Track placement and platform configuration

| Public-safe claim | Status | Runtime evidence | Boundary |
| --- | --- | --- | --- |
| The default track-placement mode lets the user place the track on a detected non-floor horizontal surface using an XR ray, with attached-anchor preference and a standalone fallback. | Verified implementation | `SessionSpace.unity:2065-2189`; `Replay/Track/Placement/ARPlanePlacementController.cs:49-88,372-490,627-675,753-827`; `TrackRevealPlacer.cs:10-15,523-579` | Automatic table fitting exists as an alternative, but it is not the default mode. |
| The project is configured for Android/Meta Quest through OpenXR and includes passthrough camera, planes, anchors, raycasts, mesh, boundaries, and composition-layer features. | Verified configuration | `Assets/XR/Settings/OpenXRPackageSettings.asset:319-321,504-506,527-529,619-621,642-667,986-988,1378-1381,1526-1529`; `Assets/XR/XRGeneralSettingsPerBuildTarget.asset:30-50` | Configuration is not evidence of a successful Quest build or session. Automatic loading/running are serialized off, so device initialization needs validation. |
| The Android target is ARM64/IL2CPP with minimum SDK 32, target SDK 34, and a Mobile URP asset. | Verified configuration | `ProjectSettings/ProjectSettings.asset:177-182,268,549-551,853-865`; `ProjectSettings/QualitySettings.asset:10-62,120` | These are build settings, not performance results. |

The exact package baseline is Unity `6000.4.11f1`, URP `17.4.0`, OpenXR `1.16.1`, Meta OpenXR `2.5.1`, XR Interaction Toolkit `3.4.1`, and XR Hands `1.8.0`. AR Foundation `6.5.0` is resolved transitively through the XR dependency graph, not declared as a direct dependency. See `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json:24-39`, and `Packages/packages-lock.json:348-365,436-447`.

## 4. Reconciled conflicts and corrections

| Initially plausible interpretation | Source-of-truth resolution | Portfolio treatment |
| --- | --- | --- |
| `ShowcasePathPreview` maps the actual vehicle path through Entry → Hero → Exit. | It resamples an active source path to 61 points and gates setup, but `ShowcaseVehiclePathMapper` only assigns the component and never consumes the preview path. A fallback can be valid even when `GeometrySafetyPassed` is false. | Classify as disconnected prototype; do not present it as the authoritative vehicle mapper. |
| Cars always pass through the two selected walls. | The default RoomDiorama validates without selected-wall miss/angle constraints and applies a Hero-centered rigid transform. | State “Hero-centered room-scale diorama.” Do not claim wall traversal. |
| Connected dual-wall portals are active because the scene flag and methods exist. | `ShowcasePortalPresentation.Configure(stage, layout, twoVehicles)` and the connected-wall planning helpers have no production call site. | Exclude. Mention only active TrackExit and pit single-wall portal paths. |
| Room-shell fracture is the main MR→VR transition. | The provider/proxy/fracture stack is serialized only in `SessionSpace 1`; the default `SessionSpace` uses the additive coordinator fallback when the optional shell is absent. | Experimental alternate-scene work only. |
| Maximum three detailed cars is a main-scene optimization. | The detail budget exists, but car LOD is disabled in `SessionSpace` and enabled only in `SessionSpace 1`. | Do not use as a main-experience metric. |
| The Unity client directly integrates OpenF1. | The Unity repository only calls the companion REST dataset API. No OpenF1 fetcher is present. | Say “externally prepared historical F1 datasets.” Verify backend provenance before using “OpenF1-derived.” |
| Hand tracking and environment occlusion are active because packages/code exist. | Hand Tracking, Hand Aim, Hand Interaction Profile, and environment occlusion are disabled in current OpenXR settings. | Do not claim support. |
| Quest performance is validated. | Profiler markers and mobile render settings exist, but no stored Quest profiler result or reproducible benchmark is present. | No FPS, latency, memory, success-rate, or accuracy claims. |

## 5. Feature classification

### Verified implementation

- Companion REST client, manifest polling, ready-chunk loading, and in-memory driver sample stores.
- Source-sample preservation plus separate processed playback samples.
- Shared source-time multi-driver replay with pause, speed, seek, and red-flag compression.
- Fixture/manifest event merging with deterministic precedence.
- Wall filtering, two-wall Entry/Exit selection, Hero focus, short plane reacquisition, and frozen room frames.
- General event-local replay stage, first-two-driver room binding, Hero-centered RoomDiorama transform, and reversible cleanup.
- Logical replay vs `VisualMotionRoot` presentation separation.
- User-directed placement on detected non-floor horizontal surfaces, with anchor fallback.
- Active TrackExit and pit single-wall portal call paths.
- Quest/OpenXR target configuration, XRI controller interaction, permission flow, and mobile rendering configuration.

### Experimental or curated

- Overtake showcase: searches a supplied event window for an order transition and reconstructs lateral separation within a presentation corridor. It is not a universal detector (`ShowcaseOvertakeBattle.cs`, `OvertakeMotion.cs`).
- Pit-stop sequence: Approach/Brake/Service/Release/Exit phases, with confidence-gated reconstruction when official duration is unavailable (`PitStopSequence.cs`).
- Collision forensics: one curated Suzuka fixture in the current working tree, explicitly separating observed source evidence from reconstructed post-impact presentation (`CollisionTrajectoryForensics.cs`, `9496.json`). These files are currently modified and require review before publication.
- Life-size drive-by and trackside immersive modes, named experimental and disabled in the default scene.
- Room-surface proxy/fracture MR→VR flow in the alternate `SessionSpace 1` scene.
- Car detail-budget LOD in the alternate scene.
- Automatic table-fit placement mode.

### Disconnected or abandoned

- `ShowcasePathPreview` as a vehicle-control path: connected to setup visualization/gating but not consumed by the vehicle mapper.
- Connected dual-wall portal planning/configuration: methods and flags exist without a production call site.
- Duplicate `ReplayEventFixtures.Merge` helper: no call site; the production path uses `ReplayEventMerger`.

### Planned or not yet validated

- Quest 3 build, permission, passthrough, plane-detection, input, comfort, and recovery verification on a documented device setup.
- Reproducible Quest frame-time, memory, thermal, battery, and draw-call profiling.
- Production network/API configuration; the client default is `http://127.0.0.1:8000` and cleartext development traffic is allowed.
- Activating the session picker in Build Settings and removing the stale missing-scene entry.
- Connecting one validated Entry/Hero/Exit path representation to the actual mapper, or changing the public UX to match the existing Hero-centered diorama.
- Real portfolio media and a rights-cleared social preview.
- Confirming whether the companion backend dataset is OpenF1-derived, and documenting transformations and licenses.

### Test-session observations kept out of headline copy

`PIT_LANE_NEXT_STEP.md` records one Editor observation for pit-lane geometry, renderer/vertex/triangle/material counts, and a pit-stop visual check. The same document says total SetPass confidence and Quest capture remain incomplete. These values are not used as general public performance evidence.

The repository has six Editor test files and 32 `[Test]`/`[TestCase]` declarations, but this audit did not execute them and found no stored result. Public copy must not say that tests passed.

## 6. Portfolio story

### One-sentence project definition

**MY LITTLE GRAND PRIX turns an externally prepared historical F1 replay dataset into a synchronized multi-driver Unity replay, then re-stages selected events as a room-scale MR diorama without mutating the source replay state.**

### Why mixed reality

The room is an input to presentation, not a decorative background. Plane semantics narrow wall candidates, and an available floor boundary containing the camera further constrains automatic setup; otherwise the flow falls back to manual Entry/Exit selection. The Hero pose gives the experience a repeatable focus, the event stage is scaled and oriented around it, and the stage/vehicle presentation split lets the result be removed without corrupting replay state.

### Three defensible engineering challenges

1. **Maintain a coherent race clock while data arrives in chunks.** Poll manifest state, load only needed ready ranges, keep a normalized source copy plus a processed playback copy, and sample multiple drivers against one timeline.
2. **Stabilize a layout built from changing AR planes.** Apply a containing-floor boundary when available, fall back to manual wall selection when automatic setup is unavailable, reacquire replaced plane IDs conservatively, freeze confirmed frames, and restore tracking on reset.
3. **Change spatial presentation without changing replay truth.** Build an event-local stage, bind two drivers, place `EventReplayStage` around the Hero pose, apply per-vehicle presentation through `VisualMotionRoot`, and restore transforms and playback state when the showcase closes.

### Entry / Hero / Exit design decision

- **Entry** and **Exit** identify two different physical walls and establish a presentation corridor and orientation context.
- **Hero** is a stored spatial pose and viewing focus used as the default RoomDiorama placement reference; it is not an `ARAnchor` claim.
- The current implementation does not make the cars traverse Entry and Exit. That mismatch is documented as a priority improvement rather than hidden in the portfolio.

## 7. Claims excluded from the public site

- Direct or live OpenF1 integration in the Unity client.
- Live race data, real-time synchronization, or multi-device synchronization.
- Persistent offline caching or standalone Quest data acquisition.
- Automatic detection of every overtake, pit stop, or collision.
- Physics-accurate incidents, exact racing lines, or measured telemetry correction.
- All circuits, all events, all vehicles, or a complete session-selection product flow.
- Guaranteed traversal of selected Entry/Exit walls or an active connected dual-wall portal.
- Full room-mesh reconstruction, furniture recognition, active hand tracking, or active environment occlusion.
- Main-flow room-shell fracture, life-size drive-by, trackside immersive, or main-scene car LOD.
- Quest device validation, passed tests, FPS, latency, memory, thermal, battery, accuracy, or success-rate numbers.
- Production readiness or an official relationship with Formula 1, teams, drivers, OpenF1, Meta, or Unity.

## 8. Contribution and ownership

The Git history contains multiple contributor identities, and this audit did not map commits to a verified role breakdown. The public site therefore includes a visible editable placeholder rather than implying solo ownership.

Before publication, the owner should provide:

- role title and dates;
- team size;
- personally owned systems or decisions;
- collaboration boundaries;
- links to commits, issues, design notes, or recordings that support the statement.

Recommended placeholder wording:

> **Contribution verification required.** This repository has multiple contributors. Replace this panel with the project owner's verified role, owned systems, team size, and dates before using the case study for applications.

## 9. Security, privacy, and rights review

No obvious API key, token, password, private key, or active credential was found in the inspected working tree. This was not a full Git-history secret scan.

Items to resolve before making the repository or captures public:

- Tracked `.mcp.json` contains personal Windows paths such as `C:\Users\Admin\...` and should be removed, templated, and checked in history.
- `ProjectSettings/ProjectSettings.asset:956-961` contains Unity Cloud project/organization identifiers even though cloud services are disabled.
- `Assets/Plugins/Android/AndroidManifest.xml:16` contains an Oculus telemetry project GUID.
- Package identity remains `DefaultCompany`, a template bundle identifier, and version `0.1.0`.
- The client uses a localhost cleartext HTTP development endpoint. On Quest, `127.0.0.1` points to the headset, not the development PC.
- Git history contains contributor email addresses; do not reproduce them on the portfolio page.
- The repository includes F1/team marks, vehicle art, named commercial music files, and aerial imagery without a first-party license inventory found by this audit. Do not copy them to the website until publication rights are confirmed.
- The portfolio should state that MLGP is an independent technical study and is not affiliated with Formula 1, its teams, Meta, Unity, or data providers.

## 10. Media inventory

No shareable product demo video was found. Recorder code targets 1920×1080, 30 fps, H.264, muted output under `Temp`, but no output is present in the repository.

Potential technical images exist—such as `SuzukaTerrain.png`, `SuzukaAerial.png`, and UI icons—but their source/redistribution rights are not established. The aerial image is also too large for direct web use. The portfolio therefore uses explicit placeholders and does not fabricate product screenshots.

Required captures are documented in `portfolio/public/assets/README.md`.

## 11. Three highest-impact project improvements

1. **Unify layout preview and actual vehicle mapping.** Make one validated Entry/Hero/Exit path the single source consumed by setup, preview, and vehicle presentation; reject the unsafe fallback or surface its degraded state. Alternatively, simplify the UX to honestly center on the existing Hero-anchored diorama.
2. **Create one releaseable Quest 3 path.** Remove the missing Build Settings scene, choose the main scene, verify OpenXR initialization and permissions, replace localhost configuration, run the Editor suite, and capture a reproducible on-device profiling/comfort checklist.
3. **Reduce experimental surface area and document provenance.** Remove or feature-gate unwired dual-wall portal code and duplicate helpers, separate curated event reconstructions from automatically supplied events, and document backend/OpenF1 provenance plus media/IP licenses.

## 12. Verification conclusion

The repository supports a strong engineering case study when framed as an in-development Unity MR replay architecture. Its strongest evidence is data-timeline separation, conservative room-plane handling, event-local staging, and reversible logical-versus-visual transforms. The case study must remain explicit that Quest runtime behavior, performance, media, upstream data provenance, contribution ownership, and several ambitious spatial modes are not yet verified.
