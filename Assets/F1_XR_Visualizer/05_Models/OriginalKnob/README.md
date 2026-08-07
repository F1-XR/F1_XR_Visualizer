# OrIginalKnob — 기본형 3D 로터리 노브

Meta Quest 3 컨트롤러로 잡고 회전시키는 재사용 가능한 월드 스페이스 로터리 노브.
특정 기능(볼륨/배속/재생 위치 등)에 종속되지 않으며, 회전값을 **이벤트로만** 외부에 제공한다.

- Unity 6.x / URP / OpenXR / XR Interaction Toolkit
- 컨트롤러 기반 근거리 직접 조작 (손 추적·Pinch·Poke·Ray·Thumbstick 미사용)
- 네임스페이스: `F1XR.OriginalKnob`

---

## 파일 구성

```
Assets/F1_XR_Visualizer/OriginalKnob/
├─ Scripts/
│  ├─ RotaryKnobController.cs        # 회전 상태·누적각·이벤트 (핵심 허브)
│  ├─ ControllerRotaryInput.cs       # XR 입력 → 프레임별 Signed Angle 계산
│  ├─ RotaryRingVisualController.cs  # 발광 링 4개 Arc 시각 피드백
│  └─ ControllerHapticController.cs  # Hover/Grip/회전 Tick 햅틱
├─ Shaders/
│  └─ RotaryRingArc.shader           # URP Additive 원호 셰이더 (프로퍼티 구동)
├─ Editor/
│  └─ OriginalKnobPrefabBuilder.cs   # 프리팹 생성 빌더 (재실행 가능)
├─ Prefabs/OrIginalKnob.prefab
├─ Materials/  (Panel / Knob / Marker / Ring)
└─ Meshes/OrIginalKnob_Panel.asset   # 절차 생성된 라운드 사각 패널 메시
```

메뉴: `Tools ▸ F1 XR ▸ OriginalKnob`
- **Build Prefab** — 프리팹 에셋만 재생성
- **Build & Place In Active Scene** — 활성 씬에 `OrIginalKnob` 인스턴스로 배치

현재 `UI Test` 씬에 배치되어 있음. 위치 `(0, 1.2, 0.55)`, 회전 `(0, 180, 0)`(정면이 사용자 쪽을 향함).

---

## 계층 구조

```
OrIginalKnob (RotaryKnobController / ControllerRotaryInput /
              RotaryRingVisualController / ControllerHapticController)
├─ PanelBody           (PanelMesh + BoxCollider)
├─ RingRoot            (BaseRing / MainGlowArc / TrailArc01 / TrailArc02)
└─ KnobPivot           (XRSimpleInteractable — 이것만 회전)
   ├─ KnobMesh
   ├─ KnobCollider     (CapsuleCollider, XRI가 런타임에 자동 등록)
   └─ RotationMarker   (DotLarge / DotMedium / DotSmall)
```

- 회전은 **`KnobPivot.localRotation`(로컬 Z축)만** 변경한다. 위치·스케일은 절대 변하지 않는다.
- 발광 링과 패널은 노브와 함께 회전하지 않는다. 마커만 노브와 함께 돈다.

### 발광 링 구조 (C자형)
- **BaseRing**: 360° 매우 어두운 회색 홈. 노브의 원형 궤적을 희미하게 보여줌.
- **MainGlowArc**: 밝은 흰색 C자 주 원호(기본 ~210°, `mainArcLength`로 조절). 항상 표시.
- **TrailArc01 / TrailArc02**: 반지름·두께·투명도가 다른 추가 동심 원호(2번째·3번째 링).
- 원호는 SDF 셰이더(`F1XR/RotaryRingArc`)로 그려 끝이 **Round Cap**. 속성: `_StartAngle`,
  `_RotationOffset`, `_ArcLength`, `_Radius`, `_RadiusOffset`, `_LineWidth`, `_Intensity`, `_Alpha`.
- 링 Quad는 패널(Y 180° 회전)의 앞면을 보도록 Y 180° 회전되어 있음(UV 미러링 보정).

#### 회전 동작 (핵심) — "회전으로 그리는 링"
- **Idle/미조작 시 비활성**: 잡지 않으면 밝은 링이 꺼져 있음(`activeAmount`=0). 머티리얼 기본
  `_Intensity`도 0이라 MPB가 없을 때(에디트 모드/첫 프레임)도 안 보임. 옅은 홈(BaseRing)만 희미하게.
- **회전량에 비례해 그려짐**: 잡고 돌리면 원호가 0°에서 시작해 회전량만큼 길어지며, **한 바퀴에
  완전한 원**이 됨. 고정된 반원이 도는 게 아니라 선이 채워지듯 그려짐.
- **바퀴 수로 링 누적**:
  - 0~1바퀴: 1번째 링이 0→360° 채워짐
  - 1바퀴 완료: 2번째 링(다른 반지름)이 그 위에서 다시 0→360° 그려지기 시작
  - 2바퀴 완료: 3번째 링 시작
  - 되감으면 반대로 줄어듦
- **놓으면 Fade Out**(`Release Time`), 다시 잡으면 현재 누적 바퀴 수까지 다시 그림.
- `Draw Start Angle`(그리기 시작 각, 기본 90°=위), `Turns Per Ring`(링 하나당 바퀴 수),
  `Invert Draw`(그리는 방향 반전)로 조절. 방향이 노브와 반대로 느껴지면 `Invert Draw` 체크.

### 깊이 / 마커 / Ray Line
- 노브는 패널보다 앞으로 크게 돌출(`PanelKnobGap`≈0.024m), 링은 패널 표면 가까이(`RingZ`) 배치 →
  노브와 링이 다른 평면으로 보이고, 노브 외곽과 링 사이에 검은 간격이 생김.
- 회전 마커는 작은 점 3개(큰→중→작)를 원주 **접선 방향** 짧은 스트릭으로 배치. `RotationMarker` 아래에서
  위치/크기 조절.
- **KnobRayLineHider**: Select 중 잡은 컨트롤러의 Ray Line(Line Visual + LineRenderer)을 숨겨 노브·링을
  가리지 않게 함. 해제 시 원상 복구. 런타임에만 동작하며 공유 리그 에셋은 건드리지 않음.

---

## 회전값 이벤트 (향후 기능 연결 지점)

`RotaryKnobController`가 C# 이벤트와 Inspector UnityEvent를 모두 제공한다.

```csharp
// 코드 구독 예 (볼륨/배속 Adapter에서)
knob.RotationStarted += OnStart;
knob.RotationChanged += (deltaAngle, totalAngle) => { /* ... */ };
knob.RotationEnded   += OnEnd;
```

제공 값:
| 프로퍼티 | 의미 |
|---|---|
| `TotalAngle` | 누적 회전각 (무한, 클램프 없음) — 보고 규약 |
| `DeltaAngle` | 이번 프레임 변화량 |
| `AngularVelocity` | 회전 속도 (deg/s, 부호 = 방향) |
| `Direction` | -1 / 0 / +1 (시각 방향) |
| `DisplayAngle` | 0~360 순환 표시각 |
| `NormalizedValue` | 한 바퀴 내 0~1 |

Inspector 옵션:
- **Invert Rotation** — 컨트롤러 움직임과 노브 회전 방향 반전
- **Clockwise Is Positive** — 시계 방향을 양수로 보고할지 (시각에는 영향 없음, 보고 규약만)

> 이번 단계에서는 어떤 기능에도 연결하지 않았다. 이후 볼륨·배속·재생위치는 각각 별도 Adapter가 위 이벤트를 구독하는 방식으로 붙인다.

---

## 실기 테스트 체크리스트 (Quest 3)

현재 리그(`XR Origin (VR)`)의 **NearFarInteractor 근거리 상호작용**으로 동작한다.

1. 컨트롤러를 노브 가까이 → Hover (링 밝기 소폭 증가, 약한 햅틱 1회)
2. **Grip** 으로 Select (링 활성, 클릭 햅틱)
3. 노브 중심 주변으로 컨트롤러를 원주 방향으로 이동 → 노브 회전
4. 시계/반시계, 여러 바퀴 연속 회전, 잡는 순간 점프 없음 확인
5. Grip 해제 → 마지막 각도 유지, 링 잔상 Fade Out

> **Grip 버튼 바인딩**은 리그의 Interactor(Select 액션)에서 결정된다. Grip이 Select로
> 매핑돼 있는지 `XRI Default Input Actions`에서 확인할 것.

### 튜닝 포인트
- 회전 안정성: `ControllerRotaryInput`의 `minInteractRadius`, `angleDeadzone`, `maxDeltaPerFrame`
- 링 반응: `RotaryRingVisualController`의 `minVisualSpeed`/`maxVisualSpeed`, 각 Arc의 Intensity/Length
- 햅틱: `ControllerHapticController`의 `tickDegrees`, 각 amplitude/duration, `minPulseInterval`
- 링 셰이더(공유 머티리얼 `OrIginalKnob_Ring`): `_Radius`, `_Thickness`, `_Softness`

---

## (보류) 원거리 Ray 선택 차단 — 나중에 적용할 방법

현재 리그는 근거리+원거리 통합 `NearFarInteractor`라, 노브가 **원거리 Ray로도 잡힐 수 있다.**
스펙의 "Ray Interactor가 의도치 않게 조작하지 않음"을 충족하려면 아래를 적용한다.
(**이 씬에서만** 적용하려면 프리팹 오버라이드로, 씬 안 `XR Origin (VR)` 인스턴스에만 변경한다.)

1. **인터랙션 레이어 추가**: `Edit ▸ Project Settings ▸ XR Interaction Toolkit`에서
   `RotaryKnob` 인터랙션 레이어를 추가.
2. **노브를 전용 레이어로**: `KnobPivot`의 `XRSimpleInteractable`의 Interaction Layer Mask를
   `RotaryKnob`으로 설정.
3. **NearFar에서 제외**: Left/Right `NearFarInteractor`의 Interaction Layer Mask에서
   `RotaryKnob`을 **해제** → NearFar(및 그 Ray)가 노브를 무시.
4. **근거리 전용 Interactor 추가**: 각 컨트롤러(`Left/Right Controller`)에
   `XR Direct Interactor`(+ 필요한 Sphere/근접 트리거)를 추가하고,
   - Select 입력 = **Grip** 액션 연결
   - Interaction Layer Mask = `RotaryKnob` 만
   → 노브는 오직 이 근거리 전용 Interactor로만, 가까이서 Grip 했을 때만 잡힌다.

결과: 가까이 가서 Grip으로 잡는 건 되고, 멀리서 Ray로 잡는 건 안 됨.
```
