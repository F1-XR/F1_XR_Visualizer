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
ChunkReplayPlayer
ApiClient
CoordinateUtil
```

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

## Testing and Validation

Before finishing a task:

* Check Unity compile errors or warnings when possible.
* Run the relevant scene manually when behavior changes.
* State exactly what was validated.
* If validation was not possible, say so clearly.

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
