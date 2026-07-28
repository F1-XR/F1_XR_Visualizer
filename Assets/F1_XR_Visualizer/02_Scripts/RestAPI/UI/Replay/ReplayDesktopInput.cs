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

            if (!mouse.leftButton.wasPressedThisFrame ||
                EventSystem.current?.IsPointerOverGameObject() == true)
            {
                return;
            }

            player ??= FindAnyObjectByType<ReplayPlayer>();
            player?.SetSelectedDriver(car != null ? car.driverNumber : 0);
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
