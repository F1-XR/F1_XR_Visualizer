# Occluded Input Visual Prototype

## Audited sources

| Input | Visual prefab | Runtime scene path | Renderer source | Mesh / bones | Original material | Existing visibility owner |
| --- | --- | --- | --- | --- | --- | --- |
| Left hand | `Assets/Samples/XR Hands/1.7.3/HandVisualizer/Prefabs/Left Hand Tracking.prefab` | `XR Origin (VR)/Camera Offset/HandVisualizer/Left Hand Tracking` | `Left Hand Tracking/Mesh` `SkinnedMeshRenderer` | `LeftHand.fbx/Mesh`, root `L_Wrist`, 26 bones | `HandsDefaultMaterial` | `HandInputModeSwitcher` sets `HandVisualizer.drawMeshes`; `HandVisualizer` disables its `XRHandMeshController` renderer |
| Right hand | `Assets/Samples/XR Hands/1.7.3/HandVisualizer/Prefabs/Right Hand Tracking.prefab` | `XR Origin (VR)/Camera Offset/HandVisualizer/Right Hand Tracking` | `Right Hand Tracking/Mesh` `SkinnedMeshRenderer` | `RightHand.fbx/Mesh`, root `R_Wrist`, 26 bones | `HandsDefaultMaterial` | Same as left hand |
| Left controller | `Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/Controllers/XR Controller Left.prefab` | `XR Origin (VR)/Camera Offset/Left Controller/XR Controller Left` | Nine `MeshRenderer` parts under `UniversalController` | Parts from `UniversalController.fbx`; no bones | `Controller_Grey`, `Controller_White` | `HandInputModeSwitcher` disables original controller `MeshRenderer` components |
| Right controller | `Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/Controllers/XR Controller Right.prefab` | `XR Origin (VR)/Camera Offset/Right Controller/XR Controller Right` | Nine `MeshRenderer` parts under `UniversalController` | Parts from `UniversalController.fbx`; no bones | `Controller_Grey`, `Controller_White` | Same as left controller |

`HandVisualizer.drawMeshes = false` does not disable every renderer below the hand root. It disables only the renderer assigned to its `XRHandMeshController`, so the project-owned silhouette renderer remains independent. The duplicate hand renderer shares `sharedMesh`, `rootBone`, and `bones`; it does not duplicate tracking, skeleton drivers, interactors, or rays. Controller source parts are combined once into one pose-following renderer per controller.

## Prototype behavior

`OccludedInputVisualController` is attached to `XR Origin (VR)`. Its component and prefab default is `IndicatorsOnly`; `SessionSpace` currently overrides this to `OccludedSoftSilhouette` for device validation. Modes can be changed during Play Mode without recreating tracking objects. `OccludedInputSilhouetteURP` uses `ZTest Greater`, `ZWrite Off`, transparent blending, no shadow pass, no extra camera, and no environment depth. Hover and select states smoothly adjust opacity through `MaterialPropertyBlock`.

The previous `LocalDepthOccluderPrototype` component remains available for comparison but is disabled on the active prefab and `SessionSpace` scene.

## UI limitation

The prototype depends on existing virtual geometry writing scene depth. World-space UI using transparent materials with `ZWrite Off` does not reliably occlude the silhouette. This phase intentionally adds no full-screen pass, extra camera, or UI-specific workaround.

## Quest validation

Use `SessionSpace`, restart Play Mode, and select a mode on `XR Origin (VR) > OccludedInputVisualController`.

1. Confirm `IndicatorsOnly` matches the current ring behavior and shows no white hand/controller mesh.
2. Select `OccludedSoftSilhouette`; move the left hand behind and then in front of an opaque cube and opaque track section.
3. Confirm only the hidden portion is white, both eyes align, and pinch/grab increases opacity briefly.
4. Repeat with the right hand, then both controllers.
5. Check fast and slow motion plus tracking loss/reacquisition.
6. Select `OccludedOutline` and `OccludedSoftSilhouetteWithIndicators` for comparison.
7. Check the Console for shader or script errors and record FPS, App CPU ms, and App GPU ms from the device rather than estimating them.
