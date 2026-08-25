# Pit-lane next step

## 2026-08-25 Suzuka implementation result

The active circuit source is
`Assets/F1_XR_Visualizer/03_Prefabs/Tracks/SuzukaCircuit.prefab`.
The Bahrain source is not used by this pit environment.

A performance-focused derivative was baked from the Suzuka pit straight
instead of duplicating the full circuit:

- Prefab: `Assets/F1_XR_Visualizer/03_Prefabs/Tracks/PitLane/SuzukaPitLaneGround.prefab`
- Profile: `Assets/F1_XR_Visualizer/09_Data/Resources/PitEnvironments/SuzukaPitEnvironmentProfile.asset`
- Source crop: the Suzuka main/pit straight, centered on the marked pit road
- Authored extent: 14 m wide x 72 m long
- Prefab cost: 1 renderer, 4 vertices, 2 triangles
- Texture: 512 x 2,048, mipmapped and compressed
- Runtime profile circuit key: `Suzuka`
- Runtime profile scale: `0.00176216`

`EventPopoutReplay.ResolvePitEnvironmentProfile` loads the profile from
`Resources/PitEnvironments` only when no profiles were assigned explicitly.
An Inspector-assigned list remains authoritative. When a matching profile
with a pit-track prefab is active, the generated dark `EventRoad` renderer is
hidden so it cannot cover the circuit surface. An unmatched or missing
profile still uses the previous generated road and dark pit fallback.

### Validation performed

`SessionSpace_fitin` was run with the cached Suzuka dataset
`2024_suzuka_race_9496_c2_o2_m45`.

- Matching `Suzuka` profile and `SuzukaPitLaneGround` were instantiated.
- Runtime ground extent was approximately 104.4 m long x 20.3 m wide.
- Generated `EventRoad` rendering was disabled only for the matched profile.
- Arrival, static Service, and departure frames remained aligned to the pit
  road without moving the vehicle, crew root, choreography anchors, or timing.
- Service remained at 1.600 s and pit-stop completion at 3.200 s.
- No related `EventPopoutReplay`, `NullReferenceException`, or
  `MissingReferenceException` error was observed.
- The pre-existing Visual Scripting node-option cache exception remains
  unrelated to this change.

Local validation captures are under `Temp/CodexPitValidation`:

- `SuzukaPit_Runtime_ArrivalMotion_Final.png`
- `SuzukaPit_Runtime_Service_Final.png`
- `SuzukaPit_Runtime_Service_Top_Final.png`
- `SuzukaPit_Runtime_Departure_Final.png`

## Current replacement point

- PitStopShowcasePresentation.CreateEnvironmentModules creates PitTrackModule under PitStopTeamBox.
- When a matching PitEnvironmentProfile.pitTrackPrefab exists, it is instantiated as PitTrack.
- When the profile or prefab is absent, CreateFallbackPitTrack creates the current dark PitTrackBase and PitLaneSurface.
- The project contains one Suzuka profile. Other circuits still use the fallback.

The cropped Suzuka pit-lane prefab replaces only the optional pitTrackPrefab input. It does not replace PitStopTeamBox, PitTrackModule, the vehicle, or the fallback implementation.

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
6. Keep the circuit-specific profile in `Resources/PitEnvironments`, or assign an explicit profile list when Inspector ownership is preferred.
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
