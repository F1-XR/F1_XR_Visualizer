# Unity Asset and Code Rules

Read this file only when the task edits C# code, scenes, prefabs, serialized data, packages, or platform settings.

## Project Layout

- Main project assets are under `Assets/F1_XR_Visualizer`.
- Put RestAPI code under `02_Scripts/RestAPI` by role: `Api`, `Replay`, `UI`, or `Utility`.
- Match namespaces to folders, such as `F1XR.RestAPI.Replay`.

## Code Style

- Prefer short, direct names that reveal purpose.
- Avoid vague `Manager`, `Handler`, or `Controller` suffixes unless the role is genuinely broad.
- Keep methods focused and prefer readable Unity C# over clever abstractions.
- Avoid decorative comments, unnecessary wrappers, excessive defensive code, and verbose exception handling.
- Do not add dependencies without a concrete need.

## Unity Asset Safety

- Do not rename existing classes, public methods, serialized fields, scenes, prefabs, GameObjects, or assets without explicit approval.
- Preserve Inspector references, prefab connections, and overrides.
- Avoid manual scene or prefab YAML edits unless explicitly required and justified.
- Do not move assets or modify `.meta` files unnecessarily.
- Do not edit generated Unity folders or third-party code.

## Packages and Platform

- Do not change Unity, XR, Meta, URP, OpenXR, AR, or other package versions unless explicitly requested.
- Verify compatibility only for APIs used by the requested change.
- Preserve the current Meta Quest target, URP pipeline, and XR stereo compatibility.
