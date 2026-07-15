# XR Essential UX Audit

대상: Unity 6.4.11f1, Meta Quest 3, `SessionSpace`

## Phase 0 감사 결과

| 우선순위 | 문제 | 기존 동작 / 사용자 문제 | 원인 | 적용 또는 권장 조치 | 영향 범위 | 기기 테스트 |
|---|---|---|---|---|---|---|
| P0 | 리플레이 UI가 보이지 않음 | `SessionSpace`의 재생·순위 UI를 사용할 수 없음 | 루트 World Space Canvas가 비활성 | Canvas 활성화, 배치 상태/Confirm/Cancel/Edit/Undo/Reset UI 추가 | `SessionSpace`, `ReplayUI*` | 필요 |
| P0 | 손으로 배치 확정 불가 | 컨트롤러 트리거만 `TrackRevealPlacer`를 확정하고 손 핀치는 별도 비활성 배치 경로에 머묾 | 두 배치 컴포넌트 사이에 손 입력 전달이 없음 | `PlacementRequested` 이벤트로 손 핀치를 실제 preview 확정 경로에 연결 | `ARPlanePlacementController`, `TrackRevealPlacer` | 필요 |
| P0 | 방 데이터가 없으면 배치 불가 | Scene 권한 거부 또는 plane 미검출 시 계속 실패 | AR plane raycast만 허용 | AR hit 실패 시 구성 가능한 world-floor ray fallback 사용 | `ARPlanePlacementController`, `SessionSpace` | 필요 |
| P0 | 트랙이 표면에서 뜰 수 있음 | prefab pivot에 따라 고정 Y offset이 실제 바닥과 불일치 | renderer bounds를 고려하지 않는 magic offset | renderer 최저점을 표면에 맞추고 `surfaceOffset`만 Inspector에서 조절 | `TrackRevealPreview`, `TrackRevealPlacement` | 필요 |
| P0 | 차량 선택이 트랙 이동으로 이어질 수 있음 | TrackVisualizer grab/scale이 항상 활성 | Normal/Edit 상태 분리 없음 | 기본 Normal에서 grab/scale 비활성, Edit에서만 활성; 경계 색으로 상태 표시 | `TrackEditState`, `TrackVisualizer` 런타임 인스턴스 | 필요 |
| P0 | 3D 차량 선택과 UI가 분리됨 | 순위 행으로만 선택 가능하고 차량 hover target이 없음 | 차량 XRI interactable 및 공통 selection event 부재 | 확대 trigger collider, hover ring, 선택 pulse, `SelectedDriverChanged`로 UI 동기화 | `ReplayCarInteractable`, `ReplayCarView*`, `ReplayPlayer`, `ReplayUI*` | 필요 |
| P0 | 손/컨트롤러 interactor 동시 활성 가능 | 동일 대상에 중복 pointer/select 가능 | `HandInputModeSwitcher`가 `SessionSpace`에 없고 null 자동 복구도 없음 | 씬에 switcher 추가, hand tracking 상태에 따라 direct hand/controller interactor 상호 배타화 | `HandInputModeSwitcher`, `SessionSpace` | 필요 |
| P0 | 선택 리플레이와 자동 리플레이가 경쟁 | 선택된 manifest 로드와 기본 dataset 생성이 같은 씬에서 동시에 시작 가능 | `ReplaySceneStart`와 `AutoReplayStarter` 모두 활성 | pending/loaded dataset이 있으면 auto start 생략 | `AutoReplayStarter`, `ReplayPlayer` | 서버 연동 테스트 필요 |
| P0 | dataset reload 후 stale selection | 삭제된 driver 번호가 presentation/audio에 남을 수 있음 | `ReplayCarSet.Clear`가 selection을 초기화하지 않음 | reload 전에 중앙 selection을 0으로 초기화하고 UI에도 전파 | `ReplayCarSet`, `ReplayPlayer`, `ReplayUI*` | 필요 |
| P1 | 마지막 배치 복구 없음 | 앱 재시작 시 다시 배치해야 함 | anchor persistence 계층 없음 | position/rotation/scale PlayerPrefs fallback 추가; Reset에서 삭제 | `TrackRevealPlacer` | 필요 |
| P1 | spatial anchor ID 복원 없음 | tracking origin이 달라지면 저장 위치가 실제 표면과 어긋날 수 있음 | Meta anchor 저장/resolve 미구현 | Meta OpenXR 2.5 API로 anchor ID 저장/timeout resolve 추가 권장 | 배치 계층 | 필요 |
| P1 | 환경 depth 기능은 켜졌지만 씬 연결 미확인 | 실제 물체가 트랙을 가리는지 보장 안 됨 | Android OpenXR Occlusion feature는 활성이나 `AROcclusionManager`가 씬에 없음 | Quest에서 provider 확인 후 카메라에 manager 연결; 미지원 시 현 상태 유지 | OpenXR settings, Main Camera | 필요 |
| P1 | 트랙 contact shadow 없음 | 선택 차량 ring 외에는 물리 표면 접촉감이 약함 | 저비용 shadow receiver/blob 없음 | Quest GPU 측정 후 트랙용 단일 blob/receiver 권장 | Track prefab/material | 필요 |
| P1 | 오류 안내 범위가 좁음 | placement surface는 안내하지만 API/passthrough/depth 실패는 UI에 표시되지 않음 | 오류 상태가 대부분 Console log에만 존재 | 공통 짧은 status banner 권장 | API, passthrough, depth startup | 필요 |
| P1 | UI 매 프레임 할당 | standings refresh가 매 프레임 `HashSet` 생성 | 지역 컬렉션 생성 | 캐시된 `HashSet` 재사용 | `ReplayUI`, `ReplayUIStandings` | Profiler 확인 |
| P2 | onboarding 완료 저장 없음 | 배치 안내가 항상 남음 | onboarding preference 부재 | 첫 성공 후 힌트 축소 및 Reset Guidance 옵션 권장 | `ReplayUIPlacement` | 선택 |

## 확인된 구조

- 시작 씬은 Build Settings의 `HomeSpace`; 해당 흐름은 `SessionSpace`로 이동한다.
- `SessionSpace`에는 선택 manifest를 소비하는 `ReplaySceneStart`와 직접 진입용 `AutoReplayStarter`가 함께 있다.
- 실제 트랙 배치는 `ARPlanePlacementController`가 hit/input source를 제공하고 `TrackRevealPlacer`가 preview와 최종 TrackVisualizer를 소유한다. 직접 배치 입력은 씬에서 `handlePlacementInput = false`다.
- MRUK 패키지는 확인되지 않았다. AR Foundation + Meta OpenXR plane/anchor/raycast를 사용한다.
- XRI 3.4.1, XR Hands 1.7.3, Input System 1.19.0을 함께 사용한다. 활성 EventSystem은 `SessionSpace`에 하나이며 `XRUIInputModule` 하나가 연결되어 있다.
- Passthrough composition layer와 Meta OpenXR Camera/Occlusion/Plane/Anchor/Raycast 기능은 활성 상태다. 환경 depth manager의 실제 씬 연결은 없다.
- replay는 `waitForTrackPlacementBeforeStart = true`라 확정 전 자동 재생되지 않는다.

## 변경 파일

| 파일 | 이유와 주요 동작 | 회귀 위험 |
|---|---|---|
| `Assets/F1_XR_Visualizer/01_Scenes/SessionSpace.unity` | Canvas 활성, floor 허용/fallback 값, input switcher, persistence 설정 | UI 위치와 hand/controller 전환은 Quest 확인 필요 |
| `.../Interaction/Input/HandInputModeSwitcher.cs` | null-safe 자동 reference 탐색 및 interactor 상호 배타화 | prefab naming이 바뀌면 자동 탐색 실패 가능 |
| `.../Interaction/World/ScaleController.cs` | 정확한 undo snapshot을 위한 scale 시작 이벤트 | 기존 scale 동작 자체는 변경 없음 |
| `.../Replay/Track/Placement/ARPlanePlacementController.cs` | 손 배치 요청 이벤트, floor 허용, no-room fallback | floor height는 tracking origin에 의존 |
| `.../Replay/Track/Build/TrackRevealPlacer.cs` | placement/edit 상태, UI API, transform 저장/복원 | world transform 복원은 spatial anchor가 아님 |
| `.../Replay/Track/Build/TrackRevealPreview.cs` | bounds grounding 및 preview smoothing | 비표준 pivot/기울어진 plane은 기기 확인 필요 |
| `.../Replay/Track/Build/TrackRevealPlacement.cs` | 확정/복원 공통 edit 설정, reset 가능 | reveal 중 빠른 reset 동작 확인 필요 |
| `.../Replay/Track/Build/TrackEditState.cs` | Normal/Edit 분리, 경계 표시, move/rotate/scale undo | 동적 LineRenderer 폭은 scale에 따라 체감 차이 가능 |
| `.../Replay/Car/ReplayCarInteractable.cs` | 확대 ray target, hover/select XRI 피드백 | 기존 car collider와 interaction layer 확인 필요 |
| `.../Replay/Car/ReplayCarSet.cs` | car interaction 연결, reload selection 초기화 | runtime component 추가 비용은 차량당 1회 |
| `.../Replay/Car/ReplayCarView.cs` | hover 상태에서도 feedback update | 선택된 한 대 외 hover 한 대만 추가 update |
| `.../Replay/Car/ReplayCarView.Effects.cs` | hover ring과 selected pulse 구분 | 투명 material 외관은 Quest 확인 필요 |
| `.../Replay/Car/ReplayCarView.Label.cs` | hover 동안 label 유지 | label clutter가 잠깐 늘 수 있음 |
| `.../Replay/Playback/ReplayPlayer.cs` | 중앙 selection event, placement UI API, dataset 상태 | 외부 코드가 같은 driver를 재선택해 강제 refresh하던 경우 영향 가능 |
| `.../Replay/Startup/AutoReplayStarter.cs` | 선택 dataset과 default dataset 중복 로드 방지 | 직접 씬 진입 auto start는 유지 |
| `.../UI/Replay/ReplayUI.cs` | selection 구독, play 중복 호출 debounce, cached set | 0.15초 이내 의도적 연속 click은 무시 |
| `.../UI/Replay/ReplayUIDriverDetail.cs` | world/UI selection 양방향 동기화 | 없음 |
| `.../UI/Replay/ReplayUIStandings.cs` | per-frame HashSet 할당 제거 | 없음 |
| `.../UI/Replay/ReplayUIPlacement.cs` | contextual status와 Confirm/Cancel/Edit/Undo/Reset | panel 배치는 Quest 시야에서 확인 필요 |

## Unity 설정 확인

- Scene: `Assets/F1_XR_Visualizer/01_Scenes/SessionSpace.unity`
- XR Origin의 `ARPlaneManager`, `ARRaycastManager`, `ARAnchorManager`, `QuestScenePermissionRequester` 참조가 존재한다.
- `TrackRevealPlacer.placementController`, `placementPrefab`, `anchorManager`가 씬 YAML에서 연결되어 있다.
- World Canvas에 `TrackedDeviceGraphicRaycaster`, EventSystem에 `XRUIInputModule`이 하나씩 있다.
- OpenXR Android에서 Hand Tracking, Meta Hand Tracking Aim, Touch Plus, Meta Camera, Planes, Anchors, Raycasts, Occlusion 기능이 활성화되어 있다.
- `com.oculus.permission.USE_SCENE`는 런타임 요청한다. 거부 시 AR manager가 꺼지고 world-floor fallback을 사용한다.
- 새 package, layer, interaction layer는 추가하지 않았다.
- Inspector에서 실제로 확인하지 못한 항목: Android manifest 최종 permission merge, Quest provider의 environment depth 지원 상태.

## Quest 테스트 체크리스트

- [ ] `SessionSpace`를 새로 시작한다. 배치 안내 panel과 ghost track이 보이고 replay는 시작하지 않는다.
- [ ] table을 가리키고 controller trigger로 확정한다. track 최저점이 surface에 닿고 reveal 후 replay가 시작한다.
- [ ] 앱 데이터를 지운 뒤 hand tracking만 켠다. pinch 한 번으로 정확히 한 번 확정된다.
- [ ] Scene 권한을 거부한다. 바닥을 향하면 fallback preview/Confirm이 가능하고 반복 error log가 없다.
- [ ] controller만 켜고 UI hover/press, replay bar, speed dropdown을 조작한다.
- [ ] hands → controllers, controllers → hands 순서로 바꾸며 동일 버튼이 두 번 실행되지 않는지 본다.
- [ ] 3D car를 ray로 hover한다. 얇은 ring/label이 나타나고 select 후 pulse와 상세/순위 선택이 같은 driver를 가리킨다.
- [ ] seek, dataset reload, scene reload 후 삭제된 차량의 선택/detail/audio가 남지 않는다.
- [ ] `Edit` 전에는 track grab이 안 되고 car 선택은 된다.
- [ ] `Edit` 후 cyan boundary가 보인다. move, Y rotation, two-hand/two-controller scale이 되고 조작 중 orange로 바뀐다.
- [ ] `Undo`가 직전 move/rotation/scale 전 transform으로 한 번 복귀한다.
- [ ] `Done` 후 track manipulation이 다시 막히고 car/replay UI가 동작한다.
- [ ] `Reset`은 dataset을 다시 받지 않고 preview 상태로 돌아간다.
- [ ] 앱 재시작 후 저장 transform이 복원된다. tracking origin이 달라졌을 때 어긋남 정도를 기록한다.
- [ ] passthrough permission 거부 시 앱이 crash하지 않고 virtual content/UI가 유지되는지 본다.
- [ ] environment depth 지원/미지원 각각에서 트랙 전체가 잘못 가려지지 않는지 본다.
- [ ] Development Build + Profiler로 CPU/GPU frame time, GC Alloc, UI rebuild, active audio/particle 수를 5분간 측정한다.

## 남은 위험과 제한

- Quest 기기가 없어 hand aim pose, trigger/pinch 중복, UI 실제 크기·거리, surface grounding, environment depth, 성능을 검증하지 못했다.
- 저장 복원은 PlayerPrefs world transform fallback이다. Meta spatial anchor identifier를 저장하거나 resolve하지 않으므로 tracking origin 변화에 강하지 않다.
- Meta OpenXR 2.5의 anchor/environment-depth provider 동작은 OS와 package patch version에 따라 달라질 수 있다.
- `AROcclusionManager`, track contact shadow, onboarding completion preference, API/passthrough/depth 통합 error banner는 남아 있다.
- `PlanarReflection`은 현재 Editor log에서 SRP `stereoTargetEye` warning을 반복한다. 이번 XR interaction 변경 범위 밖이라 수정하지 않았지만 Quest frame-time 전에 비활성 또는 URP 호환 경로 전환이 필요하다.

## 수행한 검증

- `dotnet build Assembly-CSharp.csproj --no-restore`: 오류 0, 기존 warning 11.
- `git diff --check`: whitespace error 없음. Windows line-ending 안내만 존재.
- 씬에 추가한 component file ID와 새 meta GUID의 중복 여부를 정적으로 확인했다.
- Unity Play Mode 및 Quest standalone 실기 검증은 수행하지 못했다.
