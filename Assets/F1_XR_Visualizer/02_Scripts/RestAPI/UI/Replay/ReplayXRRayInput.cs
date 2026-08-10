// ReplayXRRayInput.cs
// 컨트롤러 Ray로 차량을 가리켜 선택 — 데스크톱(ReplayDesktopInput, 마우스)의 XR 버전.
// 마우스 대신 '컨트롤러 transform'을 Ray 원점/방향으로 쓴다. 선택되면 SetSelectedDriver 호출 →
// (이미 배선된) interaction_context 로 AI가 "이 선수/쟤"를 그 차로 해석한다.
//
// ⚠️ 미검증(내 환경에서 Unity 컴파일 불가). 팀에서 아래 '씬 세팅' 후 에디터에서 확인 필요.
// [씬 세팅]
//   1) 빈 GameObject에 이 컴포넌트 추가.
//   2) rayOrigin  = 컨트롤러(또는 XR Ray Interactor의 rayOriginTransform) 드래그.
//   3) player     = ReplayPlayer (비우면 자동 탐색).
//   4) selectionLayers = 데스크톱 버전과 동일한 '차량 레이어'로.
//   5) selectAction = 트리거 버튼(InputActionReference). 비우면 SelectHovered()를 XR 버튼 이벤트에 직접 연결.
//   (차량에 Collider가 있어야 Ray가 맞는다 — 데스크톱 선택이 되면 이미 있는 것.)
using F1XR.RestAPI.Replay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace F1XR.RestAPI.UI
{
    public sealed class ReplayXRRayInput : MonoBehaviour
    {
        private const int MaximumRaycastHits = 64;

        [Header("연결")]
        [SerializeField, Tooltip("컨트롤러/Ray 원점. 이 오브젝트의 위치·정면(forward)이 Ray가 된다")]
        Transform rayOrigin;
        [SerializeField, Tooltip("비우면 씬에서 자동 탐색")]
        ReplayPlayer player;

        [Header("설정")]
        [SerializeField, Tooltip("데스크톱 선택과 동일한 차량 레이어")]
        LayerMask selectionLayers = ~0;
        [SerializeField, Min(0.1f)] float maximumDistance = 100f;
        [SerializeField, Tooltip("차 선택 버튼(트리거). 비우면 SelectHovered()를 직접 호출해도 됨")]
        InputActionReference selectAction;

        readonly RaycastHit[] hits = new RaycastHit[MaximumRaycastHits];
        ReplayCarView hoveredCar;

        /// <summary>지금 Ray가 가리키는 차(없으면 null). 배지·하이라이트 미리보기에 쓸 수 있다.</summary>
        public ReplayCarView HoveredCar => hoveredCar;

        void Awake()
        {
            player ??= FindAnyObjectByType<ReplayPlayer>();
        }

        void OnEnable()
        {
            if (selectAction != null && selectAction.action != null)
            {
                selectAction.action.performed += OnSelectPerformed;
                selectAction.action.Enable();
            }
        }

        void OnDisable()
        {
            if (selectAction != null && selectAction.action != null)
                selectAction.action.performed -= OnSelectPerformed;
            SetHoveredCar(null);   // 비활성화 시 hover 정리
        }

        void Update()
        {
            if (rayOrigin == null)
                return;
            SetHoveredCar(FindCar());   // 매 프레임 가리키는 차 갱신 + hover 시각효과
        }

        // 마우스(ReplayDesktopInput)와 동일한 hover 처리 —
        // 가리키는 차가 바뀔 때만 이전 차 해제 + 새 차에 SetHovered(true) → 라벨/하이라이트 미리보기.
        void SetHoveredCar(ReplayCarView car)
        {
            if (hoveredCar == car)
                return;
            hoveredCar?.SetHovered(false);
            hoveredCar = car;
            hoveredCar?.SetHovered(true);
        }

        void OnSelectPerformed(InputAction.CallbackContext _) => SelectHovered();

        /// <summary>지금 가리키는 차를 선택(없으면 해제). XR 버튼 UnityEvent에 직접 연결해도 된다.</summary>
        public void SelectHovered()
        {
            player ??= FindAnyObjectByType<ReplayPlayer>();
            player?.SetSelectedDriver(hoveredCar != null ? hoveredCar.driverNumber : 0);
        }

        /// <summary>선택 해제.</summary>
        public void ClearSelection()
        {
            player ??= FindAnyObjectByType<ReplayPlayer>();
            player?.SetSelectedDriver(0);
        }

        ReplayCarView FindCar()
        {
            // 마우스 화면 Ray 대신 컨트롤러의 위치·정면을 Ray로 사용(이게 유일한 차이).
            Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
            int hitCount = Physics.RaycastNonAlloc(
                ray, hits, maximumDistance, selectionLayers, QueryTriggerInteraction.Collide);

            ReplayCarView nearest = null;
            float nearestDist = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                ReplayCarView car = hits[i].collider.GetComponentInParent<ReplayCarView>();
                if (car != null && hits[i].distance < nearestDist)
                {
                    nearest = car;
                    nearestDist = hits[i].distance;
                }
            }
            return nearest;
        }
    }
}
