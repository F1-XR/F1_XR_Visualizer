# Pit-lane next step

## Current replacement point

- PitStopShowcasePresentation.CreateEnvironmentModules creates PitTrackModule under PitStopTeamBox.
- When a matching PitEnvironmentProfile.pitTrackPrefab exists, it is instantiated as PitTrack.
- When the profile or prefab is absent, CreateFallbackPitTrack creates the current dark PitTrackBase and PitLaneSurface.
- The project currently contains no PitEnvironmentProfile asset, so the fallback remains the active path.

The future cropped pit-lane prefab should replace only the optional pitTrackPrefab input. Do not replace PitStopTeamBox, PitTrackModule, the vehicle, or the fallback implementation.

## Coordinate and origin contract

- Prefab local origin: ground-level center of the serviced pit box.
- Local +Y: up.
- Local +Z: vehicle travel and departure direction.
- Local -Z: approach direction.
- Local X: across the pit lane.
- PitStopTeamBox is positioned at the replay focus plus the measured vehicle ground offset. The track prefab is then positioned by pitTrackLocalPosition, pitTrackLocalEulerAngles, and pitTrackLocalScale.
- Export the cropped mesh with this convention where possible. Use profile rotation/offset only to correct a source asset's pivot or axis, not to move replay or vehicle data.

## Approximate required extent

Let L be the presentation vehicle length supplied to PitStopShowcasePresentation.

- Current fallback base: approximately 2.2L wide, 15L long, and 0.05L thick.
- Current lane surface: approximately 1.62L wide and 14.7L long.
- The pit-box reference is at local Z = 0.
- The useful crop should cover roughly -7.5L through +7.5L on Z, with enough X extent for the service apron, lane markings, crew, and a small pit-wall/building edge.

These ratios are safer than assuming real-world metres because the presentation enforces a minimum local vehicle length and may be rescaled for MR.

## Placement plan

1. Crop one pit-box/service-apron segment plus the necessary approach and departure lane.
2. Put the serviced box center at the prefab origin and its driving surface at Y = 0.
3. Align the lane centerline to local Z and validate that the car approaches from negative Z.
4. Keep the primary surface close to PitTrackModule origin; use pitTrackLocalPosition only for small pivot corrections.
5. Keep visual pit buildings or walls in pitBuildingPrefab when they need separate placement. Do not bake them into the vehicle or choreography hierarchy.
6. Create a circuit-specific PitEnvironmentProfile and assign it to EventPopoutReplay.pitEnvironmentProfiles.
7. Preserve the dark fallback by leaving CreateFallbackPitTrack unchanged. A missing profile, unmatched circuit, or null pitTrackPrefab must continue to select it.

## What remains vehicle-local

- Vehicle root and replay motion.
- All four tyres, hubs, brakes, and suspension.
- FL service anchors and wheel-gun targets.
- Loose-tyre ownership and hide/show state.
- Pit crew and the 3.20 s choreography.

The track is presentation environment data only. It must not become a parent of the vehicle or write offsets back into replay data.

## Unnecessary source content

A full circuit is not required. Exclude grandstands, distant buildings, unused track sectors, race-grid assets, terrain outside the pit crop, spectator props, and geometry that cannot appear from Hero 45 degrees, Service Side, or Elevated Overview views.

## Quest performance concerns

- Prefer a few static renderers grouped by compatible material and region.
- Limit material count, texture resolution, and texture memory before reducing visible lane quality.
- Avoid transparent overlays and unnecessary decals when an opaque surface or packed texture is sufficient.
- Do not include colliders, rigidbodies, animated objects, or per-frame scripts in the track prefab unless a later requirement proves they are needed.
- Author renderer settings for presentation use: no unnecessary realtime shadows, probes, or motion vectors.
- Keep enough culling granularity that the whole long crop is not forced visible from every close-up view.
- Plan optional LODs for walls, gantries, and distant props; the driving surface itself should remain stable.
- Validate draw calls, active renderers, triangles, texture memory, and close-range visual quality in Editor before the first Quest performance pass.

No scene, profile, or environment asset is changed by this document.
