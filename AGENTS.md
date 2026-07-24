# AGENTS.md

## Project Overview

* This repository is a Unity XR project for visualizing F1 replay data.
* The main stack is Unity, C#, XR Interaction Toolkit, uGUI, and REST API data loading.
* The main project files live under `Assets/F1_XR_Visualizer`.
* RestAPI replay code is under `Assets/F1_XR_Visualizer/02_Scripts/RestAPI`.

## Setup Commands

```bash
# Open the project with Unity Editor.
```

## Build Commands

```bash
# Use Unity Editor build settings for the target platform.
```

## Test Commands

```bash
# Check Unity Console after changes.
# Run Play Mode in the relevant scene when behavior changes.
```

If automated tests are not available, validate by confirming Unity compiles, checking the Console, and testing the affected scene manually.

## Project Structure

```text
Assets/F1_XR_Visualizer/
├─ 01_Scenes/        Unity scenes
├─ 02_Scripts/       Runtime and editor scripts
├─ 03_Prefabs/       Prefabs
├─ 04_Images/        Images
├─ 05_Models/        Models
├─ 08_Materials/     Materials
└─ 09_Data/          Data assets
```

RestAPI code should stay role-based:

```text
Assets/F1_XR_Visualizer/02_Scripts/RestAPI/
├─ Api/              API clients and DTOs
├─ Replay/           Playback, samples, and car visualization
├─ UI/               Replay UI and canvas controls
└─ Utility/          Small pure helpers
```

Use namespaces that match folders, for example:

```csharp
namespace F1XR.RestAPI.Replay
```

## Coding Standards

* Prefer clean, short, direct names.
* Names should explain purpose at a glance.
* Avoid vague suffixes like `Manager`, `Handler`, or `Controller` unless the role is truly broad.
* Keep methods small and focused.
* Prefer readable Unity C# over clever abstractions.
* Avoid excessive defensive code, wrappers, and verbose exception handling unless there is a real failure case.
* Do not add decorative comments or long explanations in code.
* Follow nearby style and keep changes narrow.
* Do not rewrite unrelated code.
* Do not introduce dependencies unless necessary.

## Refactor Style

* Split by clear roles, not by abstract patterns.
* UI scripts should only handle UI state and input.
* Replay scripts should handle playback flow, samples, and visual updates.
* API scripts should only call the server and parse DTOs.
* Utility scripts should stay small and pure.
* Temporary bootstrap scripts may stay rough if they are planned to be removed soon.

For the RestAPI replay area, prefer names like:

```text
ReplayUI
ReplayBar
ReplaySamples
CarReplayView
ReplayPlayer
ApiClient
CoordinateUtil
```

`ChunkReplayPlayer` is a legacy serialized class label in `SessionSpace`; the active script is `ReplayPlayer`.

## Feature Development Guardrails

### Confirmed Active Architecture

* Feature work is currently scoped to `Assets/F1_XR_Visualizer/01_Scenes/SessionSpace.unity`.
* The active Open flow is the runtime-created button in `ReplayUI`, through `OpenTestEvent()` and `EventPopoutReplay.OpenTestOvertake()`.
* The active replay system is `ReplayPlayer`; do not create a parallel replay manager.
* The table presentation uses `TrackVisualizer`, with runtime cars under `TrackVisualizer/Visual/Cars`.
* The enlarged event replay is positioned by `EventPopoutReplay` and contained by the runtime `EventReplayStage` root.
* Treat `EventReplayStage` as the future presentation transform boundary unless a later phase explicitly changes that decision.
* The confirmed project versions are Unity `6000.4.11f1`, Meta OpenXR `2.5.1`, URP `17.4.0`, OpenXR `1.16.1`, and AR Foundation `6.5.0`. MRUK is not installed.

### Project Architecture Rules

* Inspect and reuse the confirmed active architecture before creating new systems.
* Do not create a parallel replay manager.
* Do not create a second OpenF1 pipeline.
* Do not create duplicate overtake-event processing.
* Do not create separate table, floor, or wall replay implementations when one presentation layer can be reused.
* Extend the confirmed active replay system rather than similarly named legacy or experimental systems.
* Treat classes identified as legacy, duplicate, experimental, or inactive during the Phase 0 audit as unavailable unless explicitly requested.

### Scope Control Rules

* Implement only the explicitly requested phase.
* Never continue into the next phase automatically.
* Do not broaden the task to include cleanup, refactoring, polishing, or optimization outside the requested scope.
* If a task requires substantially broader changes than expected, stop and report the reason before implementing.
* Prefer the smallest reversible change.
* Do not modify more files than necessary.
* Do not perform unrelated formatting changes.

### Unity Asset Safety Rules

* Do not rename existing classes, public methods, serialized fields, scenes, prefabs, GameObjects, or assets without explicit approval.
* Preserve all existing Inspector references.
* Preserve prefab connections and overrides.
* Avoid editing scene or prefab YAML manually unless explicitly required and justified.
* Do not regenerate or modify `.meta` files unnecessarily.
* Do not move assets unless explicitly requested.
* Do not modify generated Unity folders or files.

### Package and Platform Rules

* Do not upgrade or downgrade Unity.
* Do not upgrade or downgrade Meta XR, MRUK, URP, XR, OpenXR, or other packages unless explicitly requested.
* Use only APIs confirmed to exist in the installed package versions.
* Do not copy examples from newer SDK versions without verifying compatibility.
* Preserve the current Meta Quest target and URP render pipeline.
* Keep XR stereo compatibility in mind for future visual features.

### Replay-System Rules

* Keep logical replay data separate from presentation-only spatial transforms.
* OpenF1 positions, timing, event order, and logical car state must remain authoritative.
* Room placement, portal placement, cinematic offsets, scale, and path mapping must not be written back into logical replay data.
* Do not allow presentation offsets to accumulate after restart, seeking, reuse, or cleanup.
* Reuse existing replay timing rather than creating an unrelated timer where practical.
* Preserve the current table replay until an explicitly requested phase changes its presentation.
* Existing normal replay behavior must remain available as a fallback until explicitly removed.

### MR Placement Rules

* Use actual scanned MRUK or Meta scene data rather than fixed world positions for walls and tables.
* Showcase layout may use manual selection and tunable offsets, but it must remain relative to real detected anchors.
* Do not implement fully automatic arbitrary-room placement until explicitly requested.
* Do not search the entire room every frame.
* Subscribe and unsubscribe from room lifecycle events safely.

### Data and Backend Rules

* Do not modify the Python or FastAPI backend unless explicitly requested.
* Do not modify OpenF1 API calls, cached files, replay DTOs, schemas, JSON formats, or dataset formats unless explicitly requested.
* Do not invent telemetry that does not exist.
* Clearly treat lateral overtake movement as presentation reconstruction when it is not real lane-level telemetry.

### Phase Validation Rules

After every implementation phase, report:

* files changed
* why each file changed
* hierarchy or serialized-reference changes
* compilation or test commands run
* compilation errors and warnings
* manual Unity Editor checks
* manual Quest headset checks
* known limitations
* postponed work
* confirmation that the next phase was not started

If validation cannot be completed, state exactly what remains unverified.

## User Preference

* The user prefers concise Korean guidance.
* The user values code that is easy to understand from names alone.
* The user dislikes long, vague, or ornamental method names.
* The user prefers minimal safe changes over large rewrites.
* The user is sensitive to token usage.
* When the user asks for guidance, provide exact file names and paste-ready changes instead of editing files.
* Only edit files directly when the user clearly says to do it.
* If editing directly, keep the scope tight and say exactly what changed.
* If a command or file read would use many tokens, explain the cheaper option first.

## Notion Document Instructions

When a task requires checking Notion documents:

* Always check the Notion document's `AI 작업 지침` before answering or making changes.
* Start the response by explicitly stating which `AI 작업 지침` was checked.
* Follow the checked `AI 작업 지침` when summarizing, planning, or implementing work from the Notion document.
* If the `AI 작업 지침` cannot be found, state that clearly before continuing.

## Testing and Validation

Before finishing a task:

* Check Unity compile errors or warnings when possible.
* Run the relevant scene manually when behavior changes.
* State exactly what was validated.
* If validation was not possible, say so clearly.

## Unity Feedback Requests

When asking the user to verify behavior in Unity, always give a short, exact checklist instead of vague "try it" instructions.

Include only the items relevant to the change, but prefer these checks:

* Scene to open, for example `RestAPI_DriverDetail`.
* Whether Play Mode must be restarted or Inspector changes can be checked live.
* Unity Console errors or warnings to look for.
* Specific GameObject path to inspect in Hierarchy.
* Specific component and field names to check in Inspector.
* Expected runtime values, for example `AudioSource.volume > 0`, `isPlaying = true`, or nonzero telemetry logs.
* User action sequence, for example place track, start replay, pause, toggle setting, move listener.
* What result confirms success and what result means the change failed.
* Ask for a screenshot only when it would materially help diagnose the next step.

For engine audio work, include checks such as:

* `RestAPI Replay Runtime > ChunkReplayPlayer > Engine Sound` settings.
* Runtime car object path like `TrackVisualizer/Visual/Cars/Car_*/Audio/HighOn`.
* `HighOn` and `HighOff` `AudioSource` clip, volume, pitch, loop, mute, and spatial blend.
* Console logs beginning with `[EngineSound]`.
* Whether `Red Bull Only`, `maxActiveCars`, distance, playback state, and track placement are gating audio.
* Whether audible cars are limited as expected instead of all cars playing full audio.

## Security Rules

* Never commit secrets, API keys, tokens, passwords, or private credentials.
* Do not print sensitive values in logs.
* Do not weaken authentication, authorization, validation, or error handling.
* Treat generated code as untrusted until reviewed.

## Boundaries

Do not modify the following unless the task explicitly requires it:

* Lock files
* Generated files
* Vendor or third-party code
* Migration files
* Production deployment settings
* Public API contracts
* Existing data formats

## Git and Pull Request Rules

* Keep changes focused on the user's request.
* Avoid unrelated formatting changes.
* Do not delete files unless necessary.
* Summarize what changed and why.
* Mention any follow-up steps the maintainer must perform manually.

## Agent Behavior

* Inspect relevant files before editing.
* Prefer the minimal safe fix.
* Ask for clarification only when required to avoid a wrong change.
* If assumptions are made, state them.
* If a command fails, report the error and what it likely means.
* Do not claim validation was performed unless it actually was.
