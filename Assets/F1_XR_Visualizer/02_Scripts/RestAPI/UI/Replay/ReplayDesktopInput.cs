using F1XR.RestAPI.Replay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace F1XR.RestAPI.UI
{
    public sealed class ReplayDesktopInput : MonoBehaviour
    {
        private const int MaximumRaycastHits = 64;

        [SerializeField] Camera targetCamera;
        [SerializeField] ReplayPlayer player;
        [SerializeField] LayerMask selectionLayers = ~0;
        [SerializeField, Min(0.1f)] float maximumDistance = 100f;

        readonly RaycastHit[] hits = new RaycastHit[MaximumRaycastHits];
        ReplayCarView hoveredCar;

        public void Configure(Camera cameraSource, ReplayPlayer replayPlayer)
        {
            targetCamera = cameraSource;
            player = replayPlayer;
        }

        void Awake()
        {
            targetCamera ??= GetComponent<Camera>();
            player ??= FindAnyObjectByType<ReplayPlayer>();
        }

        void Update()
        {
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                player?.SetSelectedDriver(0);

            Mouse mouse = Mouse.current;
            if (mouse == null || targetCamera == null)
            {
                SetHoveredCar(null);
                return;
            }

            ReplayCarView car = FindCar(mouse.position.ReadValue());
            SetHoveredCar(car);

            if (!mouse.leftButton.wasPressedThisFrame)
                return;

            // 3D 레이가 차를 직접 맞췄을 때만 선택한다. UI 위 여부와 무관(XR용 EventSystem에서
            // IsPointerOverGameObject가 어긋나 차 클릭이 막히던 문제 수정 — 호버만 되고 선택 0이던 증상).
            // 빈 공간/UI 클릭으로는 선택을 풀지 않는다(카메라 이동 중 실수 해제 방지). 해제는 Esc로만.
            if (car != null)
            {
                player ??= FindAnyObjectByType<ReplayPlayer>();
                player?.SetSelectedDriver(car.driverNumber);
            }
        }

        void OnDisable()
        {
            SetHoveredCar(null);
        }

        ReplayCarView FindCar(Vector2 screenPosition)
        {
            Ray ray = targetCamera.ScreenPointToRay(screenPosition);
            int hitCount = Physics.RaycastNonAlloc(
                ray,
                hits,
                maximumDistance,
                selectionLayers,
                QueryTriggerInteraction.Collide);

            ReplayCarView closestCar = null;
            float closestDistance = float.PositiveInfinity;

            for (int i = 0; i < hitCount; i++)
            {
                ReplayCarView candidate =
                    hits[i].collider.GetComponentInParent<ReplayCarView>();
                if (candidate == null || hits[i].distance >= closestDistance)
                    continue;

                closestCar = candidate;
                closestDistance = hits[i].distance;
            }

            return closestCar;
        }

        void SetHoveredCar(ReplayCarView car)
        {
            if (hoveredCar == car)
                return;

            hoveredCar?.SetHovered(false);
            hoveredCar = car;
            hoveredCar?.SetHovered(true);
        }
    }
}
