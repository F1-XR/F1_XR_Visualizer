# Suzuka pit-lane integration

## Completed shared state (2026-08-25)

The pit-stop presentation now uses the Stage2 optimized Ferrari as the
persistent Ferrari entry in `SessionSpace_fitin`. The previous Lowpoly Ferrari
remains in the project as a fallback asset. No vehicle path, stop point, crew
anchor, Root Motion policy, tyre ownership logic, or FL choreography timing was
changed.

Only Suzuka is used for this environment. Bahrain assets and alignment values
were not used or modified.

## Real Suzuka source and extraction

Source prefab:

`Assets/F1_XR_Visualizer/03_Prefabs/Tracks/SuzukaCircuit.prefab`

The useful pit geometry is not a separate GameObject. It is contained in the
monolithic `suzuka_2001` mesh referenced from
`Assets/F1_XR_Visualizer/05_Models/Tracks/SuzukaCircuit.glb`.

The derived mesh keeps triangles whose centroids fall inside these source-mesh
bounds:

- source X: `-451` through `-405`
- source Y / pit travel axis: `349` through `581`
- source center used for normalization: `(-420.69, 464.78, -27.77)`
- normalization scale: `72 / 232 = 0.3103448`

Axis conversion into the pit presentation contract is:

- local X = `(source X + 420.69) * 0.3103448`
- local Y = `-(source Z + 27.77) * 0.3103448`
- local Z = `(source Y - 464.78) * 0.3103448`

This gives +Y up, +Z departure, and -Z approach without modifying the source
mesh. UVs, normals, tangents, vertex colors, and original material references
are retained. The extraction contains these source submesh/material regions:

- `RDLTC` (38)
- `GRLTA` (98)
- `RDLTB` (99)
- `RDLTD` (100)
- `GURDRC` (101)
- `RDLTA` (203)
- `ROADD` (234)
- `ROAD02` (236)
- `RDCP01` (238)
- `ROAD05` (239)
- `PITROAD` (240)
- `ROAD07` (241)
- `ROAD01` (242)
- `line4` (278)
- `046` (292)
- `001` (293)

Grandstands, buildings, ferris wheel, distant circuit sectors, garage objects,
and the rest of the full circuit are excluded.

Derived assets:

- `Assets/F1_XR_Visualizer/03_Prefabs/Tracks/PitLane/SuzukaPitLaneExtracted.prefab`
- `Assets/F1_XR_Visualizer/03_Prefabs/Tracks/PitLane/SuzukaPitLaneExtractedMesh.asset`
- `Assets/F1_XR_Visualizer/09_Data/Resources/PitEnvironments/SuzukaPitEnvironmentProfile.asset`

The previous `SuzukaPitLaneGround.prefab` remains available as the lightweight
placeholder fallback. The generated black `EventRoad` fallback is also
unchanged and remains active when a matching profile/prefab is unavailable.

## Dimensions and runtime cost

The derived mesh bounds are approximately:

- local width: `18.30 m`
- local height variation: `5.54 m`
- local length: `73.12 m`
- runtime presentation width: approximately `29.8 m`
- runtime presentation length: approximately `118.9 m`

Asset cost:

- renderers: `1`
- active renderers: `1`
- vertices: `8,826`
- triangles: `5,846`
- materials: `16`
- submeshes: `16`
- environment draw-call contribution: at most `16` ordinary submesh draws

The Unity rendering-stats resource returned zeroed frame counters while the
validation timeline was paused, so a reliable total-frame SetPass count was
not available. A Quest-device frame capture remains the authoritative next
performance check.

No extra environment optimization pass was needed: the result is already one
renderer and omits the unused circuit, while retaining the visible Suzuka
surface and marking detail.

## Stage2 Ferrari alignment

Profile values:

- `pitTrackLocalPosition = (-0.00581513, -0.00000101, 0)`
- `pitTrackLocalEulerAngles = (0, 0, 0)`
- `pitTrackLocalScale = (0.00176216, 0.00176216, 0.00176216)`

Alignment used the stopped Stage2 vehicle's rendered bounds and the actual
Suzuka pit trajectory, transformed into `PitTrack` local coordinates. The
selected service apron is source X approximately `-410`, represented by local
X `3.30 m` in the derived mesh.

At the stopped service pose:

- Stage2 pivot: approximately `(3.30, 0.00, 0.00)` in `PitTrack` local space
- Stage2 rendered visual center: approximately `(3.30, 3.71, 0.00)`
- vehicle forward: `(0, 0, 1)` in `PitTrack` local space
- tyre/rendered minimum Y: `0.0004 m`
- extracted surface Y at the service center: `0.0004 m`
- FL crew roots: Y `0`

The environment was moved around the existing vehicle system. The vehicle
motion path, stop point, crew anchors, and choreography were not moved.

## Validation performed

Scene: `Assets/F1_XR_Visualizer/01_Scenes/SessionSpace_fitin.unity`

Cached source data:
`2024_suzuka_race_9496_c2_o2_m45`

The cache's usable pit event has no authoritative stop duration
(`pitStopDuration = -1`). For bounded Editor validation only, the existing
event was cloned in Play Mode with a temporary `3.20 s` service duration, and
its actual driver-27 Suzuka pit trajectory was rendered with the Stage2 Ferrari
prefab. This temporary mapping and event clone were not saved to assets.

Results:

- persistent Ferrari entry resolves to
  `F1_Ferrari_Original_PitReady_Optimized_Stage2.prefab`
- mounted Stage2 runtime renderers: `93`
- approach at local `(3.30, 0.08, -2.42)`, forward `(0, 0, 1)`
- stopped vehicle centered on the selected service apron
- FL Gunner, Wheel Off, Wheel On, loose-old tyre, loose-new tyre, and
  Ferrari-owned `FL_Tire` states were present
- mounted `FL_Tire`: 8 renderers
- hub-empty interval: the 8 mounted tyre renderers are hidden from about
  `1.30 s` through `2.25 s`
- replacement tyre mounted again at about `2.35 s`
- final tightening/contact checks passed at `2.70 s` and `3.00 s`
- completion remained `3.20 s`
- departure entered `Exit`, stayed on +Z, and remained within the extracted
  road at local Z `5.47 m` during the checked departure frame
- generated `EventRoad` renderer was disabled only while the matching real
  Suzuka profile was active
- no Ferrari, Suzuka, pit-environment, missing-reference, or null-reference
  runtime error was observed

The existing home Editor warnings about XR subsystems and stale Visual
Scripting node options remain unrelated to this work.

Local validation captures are under `Temp/CodexSuzukaValidation` and are not
shared assets. They include approach, Wheel Off, Wheel On, departure, top, and
multi-angle views.

## Visual comparison

The old placeholder was one textured quad with 4 vertices and 2 triangles.
The extracted version visibly adds the real Suzuka asphalt variation, pit-road
surface transitions, yellow lane lines, white edge lines, and repeated service
apron markings. It is coherent from service-side, top, and bird's-eye views and
provides substantially more arrival/departure depth than the placeholder.

## Remaining work

The next milestone is a Quest-device performance and readability pass for this
single Suzuka/Stage2/FL configuration. Measure real SetPass/draw calls and GPU
time on device before considering any material atlas or further crop. Do not
start other circuits, buildings, or additional crew as part of that check.
