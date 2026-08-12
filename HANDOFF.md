# F1_XR_Visualizer — MR↔VR 전환 / Shell Fracture 작업 인수인계

작성일 2026-08-11. 이 문서만 읽고 바로 이어서 작업 가능하도록 작성.

---

## 0. 환경

| 항목 | 값 |
|---|---|
| 프로젝트 루트 | `C:\F1_XR_Visualizer` |
| 브랜치 | `develop` |
| Unity | 6000.4.11f1 |
| URP | 17.4.0 |
| OpenXR | 1.16.1 |
| com.unity.xr.meta-openxr | 2.5.1 |
| AR Foundation | 6.5.0 |
| XR Interaction Toolkit | 3.4.1 |
| 대상 기기 | Meta Quest 3 |
| 작업 씬 | `Assets/F1_XR_Visualizer/01_Scenes/SessionSpace 1.unity` |

---

## 1. 절대 지키는 제약 (사용자가 반복해서 못박은 것)

**건드리면 안 되는 것**
- OpenXR / Unity OpenXR Meta / XR Interaction Toolkit / XR Origin — 제거·교체 금지
- `OVRCameraRig` 추가 금지 (Meta SDK 리그로 갈아타지 말 것)
- 패키지 업/다운그레이드 금지, Meta XR Simulator 금지
- 기존 시스템 수정 금지: `Experience`, `ExperienceModeManager`, `PassthroughTransitionController`, `Room Shell`, `RoomSurfaceProvider`, `RoomShellProxyGenerator`, `ReplayPlayer`, Gear 시스템, Vehicle Selection
  - (STEP 1·2가 기기 검증 끝난 상태라 회귀 금지 목적)
- 원본 `ARPlane` / 앵커 오브젝트 파괴·수정 금지. **프록시만** 다룬다

**구현 제약**
- 외부 파괴(destruction) 에셋 금지
- 파편에 `Rigidbody` 금지, `Collider` 금지
- 매 프레임 메시 재생성 금지, 매 프레임 Voronoi 금지, `Update`에서 GC alloc 금지
- 파편 재질은 절대 파편별 `Material` 인스턴스 만들지 말 것 → `sharedMaterial` + `MaterialPropertyBlock`
- VR→MR 스냅샷 실패 시 `[VR2MR][FATAL VISUAL]` 에러 로그 + 파괴 시작 거부. **회색 폴백 절대 금지**
- 새 STEP 들어가기 전에 항상 **먼저 보고**하고 승인 받은 뒤 구현

**작업 습관 (과거에 사고 난 것)**
- 씬 수정하면 즉시 저장 (에디터 강제종료로 날린 적 있음)
- Meta XR Project Setup Tool이 에디터 재시작마다 `com.meta.openxr.featureset.metaxr` 를 자동 활성화하고 `ProjectSettings.asset` 을 덮어씀. **XR 회귀 조사 시 `git status` 부터 확인**
- 기기 테스트는 `[ContextMenu]` 로 못 함 (헤드셋 쓰면 인스펙터 접근 불가) → 월드스페이스 디버그 패널 사용

---

## 2. STEP 진행 현황

| STEP | 내용 | 상태 |
|---|---|---|
| 1 | MR↔VR Passthrough 전환 상태머신 | **완료, 기기 검증됨** |
| 2 | 실제 방 Wall/Floor/Ceiling 감지 + Proxy Surface 생성 | **완료, 기기 검증됨** |
| 3 | Eggshell Shell Fracture (달걀껍질 파괴 연출) | **진행 중** |

### STEP 3 목표 스펙 (사용자 원문 요약)
- Voronoi 불규칙 파편 (격자 아님), 20~40개
- 파괴 시작점 1개 → 인접 파편으로 균열 전파 (BFS)
- 단계 분리: **Crack → Loose → Detach**
- 중력 기반 이동: Wall = 제자리에서 아래로 떨어짐 / Floor = 가라앉음 / **Ceiling = 떨어지지 않고 제자리에서 페이드**
- 파편이 **사용자 쪽으로 날아오면 안 됨** (과거 버그)
- 먼지 파티클 (사용자 쪽으로 분사 금지)
- 나타날 다음 공간은 이미 뒤에 존재해야 함. Passthrough 페이드가 파괴와 동기
- 양방향 규칙: **"나가는 쪽 공간이 부서진다"**
  - MR→VR: 파편이 **MR 마스크** 역할 (파편 남은 곳=실제 방 보임, 떨어진 곳=VR)
  - VR→MR: 파편이 **나가는 VR 화면 스냅샷**을 입고 있음

---

## 3. 파일별 역할

경로 기준: `Assets/F1_XR_Visualizer/02_Scripts/Experience/`

### STEP 1
**`PassthroughTransitionController.cs`**
- Passthrough 가시성 = **XR 카메라 background 알파** 로만 제어. `Passthrough Layer` GameObject 를 토글
- `PassthroughLayerData` 는 빈 클래스, `ColorScaleBiasExtension` 도 못 씀 — 파일 주석에 이유 기록됨
- `ARCameraManager` 는 절대 안 건드림 (사용자 지시)
```csharp
public enum PassthroughState { MR, TransitionToVR, VR, TransitionToMR }
public void EnterVR(float duration);
public void EnterMR(float duration);
public void ApplyVRImmediate();
public void ApplyMRImmediate();

void ApplyHideAmount(float amount) {
    passthroughLayer.SetActive(amount < 1f);
    xrCamera.clearFlags = CameraClearFlags.SolidColor;
    xrCamera.backgroundColor = new Color(r, g, b, amount);
}
```

**`ExperienceModeManager.cs`**
- 상태머신. `StopCoroutine` 절대 안 씀 — 단일 루프가 `targetMode` 를 향해 수렴하고 항상 `ApplyMRState()` / `ApplyVRState()` 로 끝남 (전환 중 역방향 호출해도 상태 안 깨짐)
- 파괴 연출 훅:
```csharp
public Func<IEnumerator> BreakSequence;    // MR→VR
public Func<IEnumerator> RebuildSequence;  // VR→MR
```
- `TransitionToVRRoutine`: `SetVREnvironmentActive(true)` → `BreakSequence()` → `passthrough.EnterVR` → `SetMRVisualsActive(false)`
- `TransitionToMRRoutine`: `SetMRVisualsActive(true)` → `RebuildSequence()` → `passthrough.EnterMR` → `SetVREnvironmentActive(false)`
- 차량 검증 규칙 — **0 = 미선택** (프로젝트 자체 관례. -1 아님):
```csharp
int candidate = player.SelectedDriverNumber;
if (candidate <= 0) { warn; return false; }
if (!player.TryGetCarTransform(candidate, out _)) { warn(hasDataset, isTrackPlaced); return false; }
```
- `mrVisualRoots` 5개: Track Placement, Room Showcase Setup Panel, Leaderboard Panel2, Playback Control Panel2, RaceControlFlagPresenter_TEST
  - **`Room Showcase` 는 의도적으로 제외**. 넣으면 토글 시 내부 상태머신이 Review 로 강등됨

**`ExperienceModeDebugTrigger.cs`**
- `TestSelectVehicle`(준비상태 게이트), `TestEnterVRGame`, `TestReturnMR`, `LogCurrentState`, `LogExistingCars`, `GetReadiness()`, `IsVehicleReadyForVR()`, `LastSelectResult`
- **`TestClearVehicleSelection` 은 완전 삭제됨** — 멀쩡한 선택을 계속 지워서 사고 냄. 되살리지 말 것
- `player.SelectedDriverChanged` 구독해서 변경마다 스택트레이스 로그

**`ExperienceDebugPanel.cs`**
- 런타임 월드스페이스 캔버스. 헤드셋 착용 상태에서 버튼 누르려고 만든 것
- 버튼 17개. 파괴적 `! Clear Proxies` 는 마지막 줄에 격리
- `TrackedDeviceGraphicRaycaster` 만 사용
- 배경 `Image.raycastTarget = false` **필수** — true 면 컨트롤러 레이를 전부 삼켜서 3D 상호작용이 죽음 (실제로 터졌던 버그)

### STEP 2 — `Experience/Room/`
**`RoomSurfaceProvider.cs`**
- `ARPlane.classifications` 로 분류
- `OnEnable` 에서 `requestedDetectionMode |= Horizontal | Vertical`, `OnDisable` 에서 복원 (복원은 `planeManager.subsystem is { running: true }` 가드 — 없으면 플레이 종료 시 네이티브 크래시)
- Wall 판정 = `(WallFace | InvisibleWallFace)` **AND** `alignment == Vertical` (`requireVerticalWalls` 시)
- `disableWallPlaneColliders` 로 벽 plane 콜라이더 비활성
- 로그: plane 마다 `[RoomShell][Plane]` 1회 + 요약 `[RoomShell][Planes] total=... walls=... (invisible=..., rejectedByAlignment=...)`

**`RoomShellProxyGenerator.cs`**
- `ARPlane.boundary` → `ARPlaneMeshGenerator.TryGenerateMesh` (AR Foundation 자체 ear-clipping) 로 프록시 메시 생성
- API: `BuildRoomProxies()`, `ClearRoomProxies()`, `SetSurfaceOffset(float)`, `ApplySurfaceOffset()`, `OnValidate()`, `RebuildNextFrame()`(coalesce)
- **`ClearRoomProxies` 는 `sharedMesh` 를 명시적으로 Destroy** — GameObject 파괴해도 Mesh 는 안 죽음 (메시 누수 사고 있었음)
- `ApplyPose` 는 매번 소스 plane 에서 재계산 (드리프트 방지):
```csharp
proxy.GameObject.transform.SetPositionAndRotation(
    plane.position + proxy.InwardNormal * surfaceOffset, plane.rotation);
```
- 주의: 인스펙터는 프로퍼티 setter 를 안 거치고 백킹 필드에 직접 씀 → `surfaceOffset` 은 반드시 `SetSurfaceOffset()` 경유

**`RoomShellProxyDebug.cs`** ← **마지막 작업 지점**
- 감지 확인용 시각화 **전용**. 파괴 로직과 무관 (grep 으로 `RoomShellFractureController` 가 이 클래스를 참조 안 하는 것 확인됨)
- 현재 상태:
```csharp
[SerializeField] Color wallColor    = new(0.20f, 0.55f, 1f,    0.25f);
[SerializeField] Color floorColor   = new(0.20f, 1f,    0.45f, 0.25f);
[SerializeField] Color ceilingColor = new(1f,    0.55f, 0.15f, 0.25f);

[SerializeField, Range(0f, 1f)] float debugAlpha = 0.1f;   // 방금 0.2 → 0.1 로 낮춤
```
- `CreateDebugMaterial` 이 RGB 는 위 색에서 가져오고 **알파는 `debugAlpha` 로 덮어씀**
- 재질 설정: `Universal Render Pipeline/Unlit` (없으면 `Sprites/Default` 폴백), `_Surface=1`(Transparent), `_Blend=0`(Alpha), `_SrcBlend=SrcAlpha`, `_DstBlend=OneMinusSrcAlpha`, `_ZWrite=0`, `_Cull=Off`, 키워드 `_SURFACE_TYPE_TRANSPARENT`, `renderQueue=RenderQueue.Transparent(3000)`
- Play 중 실시간 변경: `OnValidate()` → `RefreshMaterialAlpha()` → `SetMaterialAlpha()` (`Application.isPlaying` 가드)

### STEP 3 — `Experience/Fracture/`
**`VoronoiShatter.cs`** (static 유틸)
- `BuildCells` — half-plane clipping (Sutherland–Hodgman). 셀이 항상 볼록 → fan triangulation 가능
- `GenerateSeeds` — jittered grid + 시작점 편향 + 최소간격 rejection (얇은 조각 제거)
- `BuildAdjacency` — 공유 코너 2개 = 공유 엣지
- `NearestCell`, `Centroid`, `GetBounds`
- 검증 완료: 30셀이 경계를 정확히 타일링 (면적 1.08 = 1.2×0.9), 코너 4~10, 크기비 23.4배, 이웃 min2/max8/avg4.9/고립0
- **제약: 경계 폴리곤이 볼록해야 함.** 오목한 ARPlane 은 셀이 오목부 밖으로 삐져나감 (미해결)

**`ShellFractureRig.cs`** (MonoBehaviour 아님, 순수 클래스)
```csharp
public enum ShellVisualMode { DebugGray, MRMask, VRSnapshot }

public bool Build(IReadOnlyList<Vector2> boundary, Vector2 fractureOrigin, Transform parent,
    Material sharedMaterial, Settings rigSettings, string rootName = "ShellFragments",
    ShellVisualMode mode = ShellVisualMode.DebugGray)
```
- `Settings.Default`: fragmentCount 30, originBias 0.8, crackWidthMillimetres 0.4, liftDistance 0.015, liftDuration 0.22, fallDistance 0.6, lateralDistance 0.04, settleDistance 0.008, breakDuration 0.7, `breakCurve = GravityCurve()`, fallsUnderGravity true, fadeStartFraction 0.55, propagationStep 0.09, holdDuration 0.12, delayJitter 0.05, rotationRange (10,10,8), endScale 0.95
- 낙하 방향은 **월드 기준 아래**:
```csharp
localFall = Root.InverseTransformDirection(Vector3.down).normalized;
```
- 3단계:
```csharp
if (time <= 0f) return;                                    // Phase 0: 온전
float detachStart = settings.liftDuration + settings.holdDuration;
if (time < detachStart) { /* Phase 1-2: 균열+헐거움, 살짝 들림만, alpha 1 */ }
// Phase 3: 분리
if (settings.fallsUnderGravity) {
    piece.localPosition = anim.LiftPosition
        + anim.FallDirection    * (settings.fallDistance    * fallen)
        + anim.LateralDirection * (settings.lateralDistance * progress)
        + Vector3.forward       * (settings.settleDistance  * progress);
} else { piece.localPosition = anim.LiftPosition; }        // 천장은 안 떨어짐
```
- `SetAlpha(int, float)` — **MRMask 모드는 알파로 페이드 불가** (마스크가 검정으로 안 어두워짐) → 렌더러 on/off 토글. DebugGray/VRSnapshot 은 `MaterialPropertyBlock` 으로 알파 구동
- `BakeSnapshotUVs(Camera)` — 파편별로 world → `camera.WorldToViewportPoint` → `mesh.SetUVs(0, uvs)`

**`RoomShellFractureController.cs`** (`Room Shell` 오브젝트에 붙음)
- `OnEnable` 에서 매니저 훅 연결
- 프록시 폴리곤을 메시 버텍스 `(x, z)` 에서 읽음 — 제너레이터 수정 없이
- `_FractureSpace` 스페이서 삽입: `Quaternion.FromToRotation(Vector3.forward, Vector3.up)`
```csharp
public IEnumerator PlayBreakSequence()   => RunBreak(towardVR: true);
public IEnumerator PlayRebuildSequence() => RunBreak(towardVR: false);

IEnumerator RunBreak(bool towardVR) {
    string tag = towardVR ? "MR2VR" : "VR2MR";
    ClearRigs(); EnsureProxiesExist();
    ShellVisualMode mode = forceDebugGray ? ShellVisualMode.DebugGray
        : (towardVR ? ShellVisualMode.MRMask : ShellVisualMode.VRSnapshot);
    if (mode == ShellVisualMode.VRSnapshot && !CaptureVRSnapshot()) {
        Debug.LogError("[VR2MR][FATAL VISUAL] Snapshot unavailable; not starting the fracture. ...");
        yield break;                                   // 회색 폴백 금지
    }
    Material material = MaterialFor(mode);
    if (!BuildRigs(mode, material)) { warn; yield break; }
    if (mode == ShellVisualMode.VRSnapshot) foreach (rig) rig.BakeSnapshotUVs(cam);
    SetProxyRenderersVisible(false);
    if (revealDuringBreak && passthrough != null)
        (towardVR ? passthrough.EnterVR : passthrough.EnterMR)(total * revealFinishFraction);
    // ... step loop, [crack] / [detach] 로그
    if (!towardVR) { ClearRigs(); SetProxyRenderersVisible(true); }
}
```
- 표면별 프로파일: Wall fall 1.4m / Ceiling `fallsUnderGravity=false`, fadeStartFraction 0, liftDistance 0.003 / Floor fall 0.35m, settle 0
- `CaptureVRSnapshot()` — 카메라를 재사용 `RenderTexture`(`VRTransitionRT`) 에 1회 렌더

**`ShellPassthroughMask.shader`** — `F1XR/ShellPassthroughMask`
- `ZWrite On`, `ZTest LEqual`, `Cull Off`, `Blend SrcAlpha OneMinusSrcAlpha`, Queue `Geometry-1`
- 프래그먼트 출력 `half4(0,0,0,_Alpha)` — 색은 무의미, **아이버퍼 알파만** 중요 (Meta 컴포지터가 SourceAlpha 로 합성)
- single-pass instanced stereo 매크로 포함

**`ShellSnapshot.shader`** — `F1XR/ShellSnapshot`
- `ZWrite Off`, `Cull Off`, Transparent queue
- 베이크된 스크린 UV 로 `_SnapshotTex` 샘플, 알파 `_Alpha`
- stereo 매크로 포함

**두 셰이더 모두 반드시 유지해야 하는 stereo 매크로 세트**
`#pragma multi_compile_instancing`, `UNITY_VERTEX_INPUT_INSTANCE_ID`, `UNITY_VERTEX_OUTPUT_STEREO`, `UNITY_SETUP_INSTANCE_ID`, `UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO`, `UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX`
(빠지면 양쪽 눈 렌더링이 어긋남 — 실제로 발생했던 버그)

**`ShellDustEmitter.cs`** — 공유 ParticleSystem 1개, world simulation, gravityModifier 0.7, 파편 분리 시 수동 `Emit`
**`FractureMaterial.cs`** — URP Lit 투명 회색 생성 (DebugGray 전용)
**`ShellFracturePrototype.cs`** — 독립 테스트 스캐폴딩. `SurfaceKind`(Wall/Ceiling/Floor) + `IncomingBackdrop` 쿼드

### 씬 구성 (`SessionSpace 1.unity`, 루트 20개)
추가된 루트:
- `Experience` — PassthroughTransitionController, ExperienceModeManager, ExperienceModeDebugTrigger, ExperienceDebugPanel
- `Room Shell` — RoomSurfaceProvider, RoomShellProxyGenerator, RoomShellProxyDebug, RoomShellFractureController
- `VR Game Environment` — 비활성, 자식 10개
- `Shell Fracture Prototype` — 위치 (-1.30, 1.45, 1.60)

---

## 4. 마지막 작업: Debug Material 투명도 (미완결)

### 사용자 요청
> 벽/천장/바닥 감지 확인용 Debug Material 이 너무 불투명해서 실제 MR 공간 확인을 방해한다. 기능 로직은 건드리지 말고 오직 `RoomShellProxyDebug` 쪽 시각화 Material 만 수정해라. 기본 알파 0.15~0.25, 인스펙터 노출, Play 중 실시간 변경, Wall/Floor/Ceiling 색 구분 유지, Transparent + 올바른 URP 알파 블렌딩, ZWrite/RenderQueue 로 앞뒤 뒤집힘 없는지 확인. Collider/ARPlane/Proxy 생성 로직 영향 금지, **Fracture 재질 영향 금지**.

### 한 것
`RoomShellProxyDebug.cs` 에 `debugAlpha` 필드 + `RefreshMaterialAlpha()` + `SetMaterialAlpha()` + `OnValidate()` 추가. `CreateDebugMaterial` 을 static → 인스턴스 메서드로 변경하고 알파를 `debugAlpha` 로 덮도록 수정.

### 사용자 반응
> 그대로다 이자식아 (스크린샷: Game view 가 진한 남색 벽 + 초록 바닥으로 꽉 참)

### 조사 결과 — **알파는 정상 동작 중이었다**
런타임 덤프:
```
Wall    RoomShellWallDebug    URP/Unlit  baseColor=(0.200,0.550,1.000,0.200) queue=3000 surface=1 zwrite=0
Floor   RoomShellFloorDebug   URP/Unlit  baseColor=(0.200,1.000,0.450,0.200) queue=3000 surface=1 zwrite=0
Ceiling RoomShellCeilingDebug URP/Unlit  baseColor=(1.000,0.550,0.150,0.200) queue=3000 surface=1 zwrite=0
Main Camera: clear=SolidColor bg=RGBA(0.015, 0.020, 0.040, 0.000)
```
동일 씬의 `ShellFractureSurface`(알파 0.55)는 화면에서 트랙이 비쳐 보임 → 이 씬 알파 블렌딩 자체는 정상.

**진짜 원인 2가지:**
1. **에디터 Game view 에는 passthrough 가 없다.** 카메라 background 알파 0 = 기기에서만 실제 방이 합성됨. 에디터에선 벽 프록시 뒤가 거의 검정(`0.015,0.020,0.040`). 그래서 알파를 0.2 로 하든 0.02 로 하든 "비쳐 보일 대상"이 없음. 검정 위 파랑 20% + linear→sRGB 감마 = 스크린샷의 그 남색. **에디터에서 이 항목을 검증하는 건 불가능하다.**
2. **레이어 누적.** `_Cull=Off` + 사용자가 방 안에 서 있음 = 앞벽 앞/뒷면 + 반대편 벽 앞/뒷면까지 최대 5겹 겹쳐 그림. 0.2 가 여러 겹이면 체감 0.5~0.7.

### 미완결 상태 (Codex 가 이어받을 지점)
- [x] `RoomShellProxyDebug.cs` 의 `debugAlpha` 기본값 `0.1f` — **파일·HEAD 모두 반영 완료**
- [ ] 런타임 재질에 0.1 을 즉시 반영하는 명령 — 사용자가 도구 실행을 중단시킴. **미적용** (다음 Play 진입 시 새 재질이 0.1 로 생성되므로 실질 영향 없음)
- [ ] 누적 문제 해결 여부 결정. `_Cull=Off` → `Cull Back` 으로 바꾸면 겹침이 절반으로 줄지만, ARPlane winding 방향이 보장 안 돼서 **일부 면이 통째로 사라질 위험** 있음. 사용자 판단 필요 (아직 답 못 받음)
- [ ] Quest 3 에서 슬라이더 0.05~0.15 돌려보고 최종값 확정

### 리포지토리 상태 (확인 완료)
```
git status --short   →  ?? HANDOFF.md   (그 외 clean)
git show HEAD:.../RoomShellProxyDebug.cs  →  debugAlpha = 0.1f
```
작업 중 `EditorSceneManager.SaveScene()` 을 Play Mode 중에 호출한 적이 있어 씬 파일 오염을 의심했으나, **`SessionSpace 1.unity` diff 없음 — 오염 없음.** 여기까지의 코드 변경은 전부 커밋된 상태.

---

## 5. 해결 완료된 이슈 (재발 방지용)

| # | 증상 | 진짜 원인 / 조치 |
|---|---|---|
| 1 | MRUK 사용 불가 | `MRUK.Awake()` 가 `OVRCameraRig` 를 강제 요구, 없으면 `Debug.LogError`. → AR Foundation `ARPlaneManager` 경로로 전환 |
| 2 | `surfaceOffset` 바꿔도 프록시 안 움직임 | 인스펙터가 프로퍼티 setter 를 안 거치고 백킹 필드에 직접 씀. → `SetSurfaceOffset()`/`ApplySurfaceOffset()`/`OnValidate()` 추가 |
| 3 | 메시 누수 | GameObject 파괴해도 Mesh 는 안 죽음. → `ClearRoomProxies` 가 `sharedMesh` 명시 파괴. (그 뒤 "181개 누수" 측정은 Unity `Destroy()` 지연 때문의 **오탐**. 프레임 지나고 재측정하니 1) |
| 4 | 기기에서 STEP 1 테스트 불가 | `[ContextMenu]` 는 헤드셋 쓰면 접근 불가. → `ExperienceDebugPanel` 제작 |
| 5 | 컨트롤러 입력 전부 죽음 | 디버그 패널 배경 `Image.raycastTarget=true` 가 모든 레이를 삼킴 (UI 히트가 3D 보다 우선) + 불필요한 `GraphicRaycaster`. → 둘 다 제거 |
| 6 | "아까랑 다른데?" (3회) | Meta XR SDK Project Setup Tool 이 `com.meta.openxr.featureset.metaxr` 자동 활성화 + `ProjectSettings.asset` 수정. `git status` 로 발견. Meta XR 피처셋만 비활성화 |
| 7 | `SelectedDriverNumber = 0` | `TestClearVehicleSelection()` 이 호출되고 있었음 (패널 버튼 → 나중엔 인스펙터 ContextMenu). → 메서드·ContextMenu·버튼 전부 삭제 |
| 8 | VR→MR 에서 파괴 안 보임 | `PlayRebuildSequence` 에 파괴 코드가 아예 없었음. → `RunBreak(towardVR)` 로 양방향 통합 |
| 9 | 파편이 카메라로 날아옴 | `breakDirection = normal*w + radial*w` × 0.6m 가 전부 시점 쪽. → 필드 삭제, 월드 아래 중력으로 교체 |
| 10 | 전부 동시에 부서짐 | 지연이 `거리 × propagation` 이라 링 단위로 같이 움직임. → 공유엣지 인접그래프 + BFS |
| 11 | 회색 판이 부서짐 | MRMask/VRSnapshot 모드가 애초에 없었음. → 셰이더 2개 신규 제작 |
| 12 | 양쪽 눈 렌더링 어긋남 | 커스텀 셰이더에 single-pass instanced stereo 매크로 없음. → 6종 매크로 추가 |
| 13 | "테이블이 안잡힌다" | 코드 문제 아님. REST API 서버가 꺼져 있었음 |

---

## 6. 남은 작업 / 미검증

**즉시**
1. 위 §4 의 씬 파일 오염 여부 확인
2. `debugAlpha` 런타임 반영 + 씬 저장 (Play Mode 아닐 때)
3. `Cull Off` 누적 건 사용자 판단 받기

**기기 검증 필요 (에디터에서 불가)**
- MRMask 알파-홀 방식이 실제로 동작하는지. Passthrough 는 Quest 컴포지터에만 존재하고, URP 가 아이버퍼 알파를 제출 버퍼까지 보존해야 함. **회색이 절대 안 나와야 함**
- Debug Material 투명도 최종값

**알려진 한계**
- VR 스냅샷이 **모노**. 양쪽 눈이 같은 텍스처를 샘플 → 파편에 시차 없음. 거슬리면 per-eye 캡처 필요
- `VoronoiShatter` 는 **볼록** 경계만 지원. 오목 ARPlane 은 셀이 삐져나감
- `SessionSpace 1` 이 **Build Settings 에 없음** (BootstrapSpace, HomeSpace, SessionSpace, SessionSpace0803, VRDroneSpace 만 있음). APK 빌드하려면 추가 필요

**STEP 3 스펙 중 아직 미구현**
- 균열선(crack line) 메시 시각화
- 동시 분리 파편 수를 1~3개로 제한
- 전역 passthrough 페이드 대신 **균열 면적 기반** per-pixel reveal
- 전용 VR Transition Shell 지오메트리 (현재는 사용자 선택에 따라 RoomShellProxy 지오메트리 재사용 중)
