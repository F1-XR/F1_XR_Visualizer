using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.Interaction.World
{
    /// <summary>
    /// 기어봉을 4방향으로 기울여 원형 UI 메뉴를 조작하는 컨트롤러입니다. 손잡이를 컨트롤러 위치로
    /// 끌어오지 않고, GS_Knob 의 <see cref="XRSimpleInteractable"/> 로 Hover / Select 만 감지한 뒤
    /// 컨트롤러 Attach 위치를 읽어 GS_Bend(또는 GS_Pivot) 의 <c>localRotation</c> 을 계산합니다.
    ///
    /// 흐름 (명세 9~17):
    ///   * GS_Knob Hover     → UI 열림(Preview)
    ///   * GS_Knob Select    → 조작 활성화, 컨트롤러 Attach 저장
    ///   * 기울임 강도 0~1 을 Neutral(0~0.2) / Hover(0.2~0.7) / Select(0.7~1.0) 로 판정
    ///   * 방향은 abs(x) vs abs(y) 우세 축 + 히스테리시스로 결정 (대각선 튐 방지)
    ///   * Select 구간을 처음 통과할 때만 1회 실행 + 햅틱, 중앙 복귀 전까지 잠금
    ///   * Grip 해제 → 부드럽게 중앙 복귀, UI 기본 배열 복귀
    /// UI 시각화는 전부 <see cref="GearUIController"/> 로 위임합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GearShiftController : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("로컬 축 기준이 되는 트랜스폼. 보통 GearShift 또는 GS_Base.")]
        [SerializeField] Transform gearReference;
        [Tooltip("실제로 기울어지는(회전하는) 트랜스폼. GS_Bend 또는 GS_Pivot.")]
        [SerializeField] Transform bendTransform;
        [Tooltip("GS_Knob 에 붙은 XR Simple Interactable. Hover/Select 만 감지.")]
        [SerializeField] XRSimpleInteractable knobInteractable;

        [Header("UI")]
        [SerializeField] GearUIController uiController;

        [Header("Tilt")]
        [Tooltip("좌우 최대 기울기(도).")]
        [SerializeField, Range(5f, 45f)] float maxHorizontalAngle = 22f;
        [Tooltip("앞뒤 최대 기울기(도).")]
        [SerializeField, Range(5f, 45f)] float maxVerticalAngle = 22f;
        [SerializeField] bool invertHorizontal;
        [SerializeField] bool invertVertical;
        [Tooltip("좌우 입력에 쓰는 gearReference 로컬 축. 기본 X.")]
        [SerializeField] Vector3 horizontalAxis = Vector3.right;
        [Tooltip("앞뒤 입력에 쓰는 gearReference 로컬 축. 모델에 따라 Z 또는 Y. 기본 Z.")]
        [SerializeField] Vector3 verticalAxis = Vector3.forward;
        [Tooltip("잡은 순간을 중립으로 삼아, 손을 이 거리(m)만큼 옆으로 움직이면 최대 기울기에 도달. 직접 grab 감도.")]
        [SerializeField, Min(0.01f)] float handTravelForFullTilt = 0.08f;
        [Tooltip("좌우 틸트를 잠급니다. 켜면 기어봉이 앞뒤 1자로만 움직입니다(예전 방식).")]
        [SerializeField] bool lockHorizontal = false;
        [Tooltip("앞뒤 틸트를 잠급니다. 켜면 좌우로만 움직입니다.")]
        [SerializeField] bool lockVertical = false;

        [Header("Detent (앞뒤 4단)")]
        [Tooltip("켜면 앞뒤 축을 이산 단(gear)으로 나눠 딱딱 걸리게 합니다. 좌우 잠금과 함께 쓰면 예전 4단 기어.")]
        [SerializeField] bool useDetents = false;
        [Tooltip("단(gear) 개수.")]
        [SerializeField, Range(2, 8)] int gearCount = 4;
        [Tooltip("단 사이 앞뒤 각도(도).")]
        [SerializeField, Range(2f, 40f)] float detentStepAngle = 10f;
        [Tooltip("단을 넘어가는 데 필요한 추가 저항(도). 클수록 단이 딱딱하게 버팀.")]
        [SerializeField, Range(0f, 15f)] float detentHysteresis = 4f;
        [Tooltip("단으로 스냅되는 부드러움. 작을수록 딱 걸림.")]
        [SerializeField, Min(0f)] float snapSmoothTime = 0.05f;
        [Tooltip("처음 시작 단(0 = 맨 뒤/앞).")]
        [SerializeField] int initialGear = 0;

        [Header("Zones (기울기 강도 0..1)")]
        [Tooltip("이 값 미만은 Neutral(아무것도 Hover 안 함, 선택 잠금 해제).")]
        [SerializeField, Range(0f, 1f)] float neutralMax = 0.20f;
        [Tooltip("이 값 이상부터 방향 UI 가 Hover 되기 시작.")]
        [SerializeField, Range(0f, 1f)] float hoverThreshold = 0.20f;
        [Tooltip("이 값 이상에서 기능 선택. Hover 임계값과 반드시 다르게.")]
        [SerializeField, Range(0f, 1f)] float selectThreshold = 0.70f;
        [Tooltip("현재 방향을 유지하려는 관성. 다른 축이 이 배수만큼 커져야 방향 전환(대각선 튐 방지).")]
        [SerializeField, Range(1f, 2f)] float directionSwitchBias = 1.25f;

        [Header("Return")]
        [Tooltip("Grip 을 놓았을 때 중앙으로 되돌아오는 부드러움. 클수록 느긋하게 복귀.")]
        [SerializeField, Min(0.01f)] float returnSmoothTime = 0.18f;
        [Tooltip("잡고 있는 동안의 반응 스무딩. 작을수록 손을 즉각 따라감.")]
        [SerializeField, Min(0f)] float heldSmoothTime = 0.03f;

        [Header("Haptics")]
        [SerializeField, Range(0f, 1f)] float selectHapticAmplitude = 0.3f;
        [SerializeField, Min(0f)] float selectHapticDuration = 0.06f;

        /// <summary>기능이 선택될 때 1회 발생. 인수는 선택된 방향.</summary>
        public event Action<GearDirection> DirectionSelected;

        public GearDirection CurrentDirection => hoverDir;
        public bool IsHeld => held;

        /// <summary>
        /// 외부에서 조준 대상을 지정하면(예: 컨트롤러에 기어봉을 띄우는 morph) XR Grab 없이도 이 트랜스폼
        /// 위치를 손 위치로 삼아 틸트 / Hover / Select 를 처리합니다. null 로 되돌리면 해제되고 UI 를 닫습니다.
        /// </summary>
        public Transform ExternalAimTarget
        {
            get => externalAim;
            set
            {
                externalAim = value;
                if (value != null)
                {
                    canSelect = true;
                    if (uiController != null)
                        uiController.Open();
                }
                else if (!held && !IsStillHovered() && uiController != null)
                {
                    uiController.Close();
                }
            }
        }

        /// <summary>Hover 방향이 새 방향으로 바뀔 때 발생(디텐트 클릭용). None 진입 시엔 발생하지 않음.</summary>
        public event Action<GearDirection> HoverDirectionChanged;

        /// <summary>Detent 모드에서 단(gear)이 바뀔 때 발생(딸깍 햅틱용). 인수는 새 단 인덱스.</summary>
        public event Action<int> GearChanged;

        public int CurrentGear => currentGear;

        IXRSelectInteractor heldInteractor;
        Transform attach;
        Transform externalAim;
        Vector3 aimAnchorWorld;
        bool hasAnchor;
        HapticImpulsePlayer haptics;
        Quaternion bendRest;

        bool held;
        bool canSelect = true;
        GearDirection hoverDir = GearDirection.None;

        int currentGear;
        float snappedAngle;
        float grabBaseAngle;   // 잡은 순간의 단 중심 각도 - 여기서부터 손 이동량을 더해 단을 센다

        Vector2 smoothedInput;
        Vector2 inputVelocity;

        void Awake()
        {
            AutoWire();
            if (bendTransform != null)
                bendRest = bendTransform.localRotation;
            currentGear = Mathf.Clamp(initialGear, 0, gearCount - 1);
            snappedAngle = GearAngle(currentGear);
        }

        void AutoWire()
        {
            if (knobInteractable == null)
                knobInteractable = GetComponentInChildren<XRSimpleInteractable>();
            if (gearReference == null)
                gearReference = transform;
        }

        void OnEnable()
        {
            if (knobInteractable == null)
                return;

            knobInteractable.hoverEntered.AddListener(OnHoverEntered);
            knobInteractable.hoverExited.AddListener(OnHoverExited);
            knobInteractable.selectEntered.AddListener(OnSelectEntered);
            knobInteractable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            if (knobInteractable == null)
                return;

            knobInteractable.hoverEntered.RemoveListener(OnHoverEntered);
            knobInteractable.hoverExited.RemoveListener(OnHoverExited);
            knobInteractable.selectEntered.RemoveListener(OnSelectEntered);
            knobInteractable.selectExited.RemoveListener(OnSelectExited);
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (uiController != null)
                uiController.Open();
        }

        void OnHoverExited(HoverExitEventArgs args)
        {
            // 아직 잡고 있거나 다른 인터랙터가 Hover 중이면 유지, 완전히 벗어났을 때만 닫음.
            if (!held && !IsStillHovered() && uiController != null)
                uiController.Close();
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            heldInteractor = args.interactorObject;
            attach = heldInteractor.GetAttachTransform(knobInteractable);
            haptics = (heldInteractor as Component) != null
                ? (heldInteractor as Component).GetComponentInParent<HapticImpulsePlayer>()
                : null;
            held = true;
            canSelect = true;
            hasAnchor = false; // 잡은 순간 위치를 다음 프레임에 중립 기준으로 캡처
            Debug.Log($"[GearShift] GS_Knob grabbed by {(args.interactorObject as Component)?.name}", this);

            if (uiController != null)
                uiController.Open();
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (heldInteractor != args.interactorObject)
                return;

            heldInteractor = null;
            attach = null;
            haptics = null;
            held = false;
            Debug.Log("[GearShift] GS_Knob released", this);

            if (!IsStillHovered() && uiController != null)
                uiController.Close();
        }

        bool IsStillHovered() => knobInteractable != null && knobInteractable.isHovered;

        void LateUpdate()
        {
            if (bendTransform == null)
                return;

            // XR Grab 우선, 없으면 외부 조준(morph). 둘 중 하나라도 있으면 활성.
            Transform aim = (held && attach != null) ? attach : externalAim;
            bool active = aim != null;
            if (!active) hasAnchor = false;

            Vector2 rawInput = active ? ReadInput(aim) : Vector2.zero;

            // 활성 중엔 즉각 반응, 해제되면 느긋하게 0 으로 복귀(순간이동 방지).
            float smoothTime = active ? heldSmoothTime : returnSmoothTime;
            smoothedInput = Vector2.SmoothDamp(smoothedInput, rawInput, ref inputVelocity, Mathf.Max(0.0001f, smoothTime));

            // Detent 모드: 앞뒤를 이산 단으로. 좌우 연속 틸트/Hover는 건너뜀.
            if (useDetents)
            {
                UpdateDetent();
                return;
            }

            ApplyTilt(smoothedInput);

            float magnitude = Mathf.Clamp01(smoothedInput.magnitude);
            GearDirection direction = magnitude < neutralMax
                ? GearDirection.None
                : ResolveDirection(smoothedInput);

            if (direction != hoverDir)
            {
                hoverDir = direction;
                if (direction != GearDirection.None)
                    HoverDirectionChanged?.Invoke(direction);
            }

            float hoverProgress = Mathf.InverseLerp(hoverThreshold, selectThreshold, magnitude);
            if (uiController != null)
                uiController.UpdateHover(direction, hoverProgress);

            // 중앙으로 돌아오면 다시 선택 가능, Select 구간 첫 통과에만 1회 실행.
            if (magnitude < neutralMax)
                canSelect = true;

            if (canSelect && active && direction != GearDirection.None && magnitude >= selectThreshold)
            {
                canSelect = false;
                FireSelect(direction);
            }
        }

        /// <summary>
        /// 잡은(또는 조준을 건 ) 순간의 손 위치를 중립 기준으로 삼고, 그 뒤 손이 움직인 양(델타)을
        /// gearReference 로컬 좌우/앞뒤 성분으로 분해해 -1..1 2D 입력으로 만듭니다. 손을
        /// <see cref="handTravelForFullTilt"/> 만큼 옆으로 움직이면 해당 축 입력이 1이 됩니다.
        /// 이렇게 하면 손과 기어봉의 절대 위치가 달라도(고정된 기어봉을 직접 grab) 자연스럽게 동작합니다.
        /// </summary>
        Vector2 ReadInput(Transform aim)
        {
            if (!hasAnchor)
            {
                aimAnchorWorld = aim.position;
                hasAnchor = true;
                // 잡은 지점 = 현재 단의 중심. 그래야 어느 단에서 잡든 앞뒤로 반 칸만 움직이면
                // 바로 다음 단으로 넘어간다(예전엔 잡은 지점과 단 중심이 어긋나 한참 밀어야 했음).
                grabBaseAngle = GearAngle(currentGear);
                return Vector2.zero; // 잡은 첫 프레임은 중립
            }

            Vector3 delta = aim.position - aimAnchorWorld;
            Vector3 local = gearReference.InverseTransformDirection(delta); // 방향 변환(스케일/위치 무관, 회전만)

            float h = Vector3.Dot(local, horizontalAxis.normalized);
            float v = Vector3.Dot(local, verticalAxis.normalized);

            float reach = Mathf.Max(0.001f, handTravelForFullTilt);
            Vector2 input = new Vector2(h / reach, v / reach);
            if (invertHorizontal) input.x = -input.x;
            if (invertVertical) input.y = -input.y;
            if (lockHorizontal) input.x = 0f;
            if (lockVertical) input.y = 0f;

            if (input.magnitude > 1f)
                input.Normalize();
            return input;
        }

        /// <summary>입력 방향으로 기어봉을 기울입니다. up 벡터를 목표 방향으로 회전시켜 축에 무관하게 동작.</summary>
        void ApplyTilt(Vector2 input)
        {
            float aH = Mathf.Clamp(input.x, -1f, 1f) * maxHorizontalAngle * Mathf.Deg2Rad;
            float aV = Mathf.Clamp(input.y, -1f, 1f) * maxVerticalAngle * Mathf.Deg2Rad;

            Vector3 desiredUp = (Vector3.up
                                 + horizontalAxis.normalized * Mathf.Tan(aH)
                                 + verticalAxis.normalized * Mathf.Tan(aV)).normalized;

            Quaternion lean = Quaternion.FromToRotation(Vector3.up, desiredUp);
            bendTransform.localRotation = lean * bendRest;
        }

        /// <summary>
        /// 앞뒤 입력을 이산 단(gear)으로 처리합니다. 손을 앞뒤로 밀면 히스테리시스를 넘을 때만 다음
        /// 단으로 딸깍 넘어가고(햅틱), 기어봉은 그 단의 각도로 스냅됩니다.
        /// </summary>
        void UpdateDetent()
        {
            // 잡은 단의 중심을 기준으로 손 이동량을 더한다 -> 어느 단에서 잡아도 감도가 같다.
            float aimAngle = grabBaseAngle + Mathf.Clamp(smoothedInput.y, -1f, 1f) * maxVerticalAngle;
            UpdateGear(aimAngle);

            float target = GearAngle(currentGear);
            float t = snapSmoothTime <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / snapSmoothTime);
            snappedAngle = Mathf.Lerp(snappedAngle, target, t);
            ApplyVerticalTilt(snappedAngle);

            // 현재 단에 해당하는 UI 카드를 강조(1단 = items[0] = 맨 앞).
            if (uiController != null)
                uiController.SetActiveGear(currentGear);
        }

        /// <summary>단 i 의 앞뒤 각도(도). 단들이 0 을 중심으로 균등 분포.</summary>
        float GearAngle(int i) => (i - (gearCount - 1) * 0.5f) * detentStepAngle;

        /// <summary>앞뒤 조준 각도를 기준으로 단을 올리거나 내립니다(히스테리시스 적용). 바뀌면 GearChanged 발생.</summary>
        void UpdateGear(float aimAngle)
        {
            bool changed = false;
            while (currentGear < gearCount - 1 &&
                   aimAngle > GearAngle(currentGear) + detentStepAngle * 0.5f + detentHysteresis)
            {
                currentGear++;
                changed = true;
            }
            while (currentGear > 0 &&
                   aimAngle < GearAngle(currentGear) - detentStepAngle * 0.5f - detentHysteresis)
            {
                currentGear--;
                changed = true;
            }
            if (changed)
                GearChanged?.Invoke(currentGear);
        }

        /// <summary>앞뒤 축으로만 기어봉을 기울입니다(좌우 없음).</summary>
        void ApplyVerticalTilt(float angleDeg)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            Vector3 desiredUp = (Vector3.up + verticalAxis.normalized * Mathf.Tan(a)).normalized;
            bendTransform.localRotation = Quaternion.FromToRotation(Vector3.up, desiredUp) * bendRest;
        }

        /// <summary>
        /// 우세 축으로 방향을 정하되, 현재 Hover 중인 축을 유지하려는 히스테리시스를 적용해 대각선
        /// 경계에서 UI 가 좌우로 튀지 않게 합니다. (명세 11)
        /// </summary>
        GearDirection ResolveDirection(Vector2 input)
        {
            float ax = Mathf.Abs(input.x);
            float ay = Mathf.Abs(input.y);

            bool horizontalWins;
            if (hoverDir == GearDirection.Left || hoverDir == GearDirection.Right)
                horizontalWins = ax * directionSwitchBias >= ay;      // 가로 유지 관성
            else if (hoverDir == GearDirection.Up || hoverDir == GearDirection.Down)
                horizontalWins = ax >= ay * directionSwitchBias;      // 세로 유지 관성
            else
                horizontalWins = ax >= ay;

            if (horizontalWins)
                return input.x >= 0f ? GearDirection.Right : GearDirection.Left;
            return input.y >= 0f ? GearDirection.Up : GearDirection.Down;
        }

        void FireSelect(GearDirection direction)
        {
            if (uiController != null)
                uiController.Select(direction);

            if (haptics != null && selectHapticDuration > 0f)
                haptics.SendHapticImpulse(selectHapticAmplitude, selectHapticDuration);

            DirectionSelected?.Invoke(direction);
        }
    }
}
