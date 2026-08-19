using System.Collections.Generic;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay;
using UnityEngine;

namespace F1XR.Drone
{
    public readonly struct DroneVehicleTarget
    {
        public readonly Rect viewportRect;
        public readonly int driverNumber;
        public readonly int rank;
        public readonly string driverLabel;
        public readonly string teamName;
        public readonly Color teamColor;

        public DroneVehicleTarget(
            Rect viewportRect,
            int driverNumber,
            int rank,
            string driverLabel,
            string teamName,
            Color teamColor)
        {
            this.viewportRect = viewportRect;
            this.driverNumber = driverNumber;
            this.rank = rank;
            this.driverLabel = driverLabel;
            this.teamName = teamName;
            this.teamColor = teamColor;
        }
    }

    [DisallowMultipleComponent]
    public sealed class DroneVehicleTargeting : MonoBehaviour
    {
        [Header("Detection Frame")]
        [SerializeField] Rect viewportFrame = new(0.15f, 0.16f, 0.7f, 0.68f);
        [SerializeField, Min(0.001f)] float minimumViewportSize = 0.012f;
        [SerializeField, Min(1)] int maximumVisibleTargets = 8;

        readonly List<DroneVehicleTarget> visibleTargets = new();
        readonly List<Renderer> renderers = new();

        ReplayPlayer replayPlayer;
        VRDroneHud hud;
        Camera camera;
        bool isVisible;

        public void Configure(
            ReplayPlayer source,
            VRDroneHud targetHud,
            Camera targetCamera)
        {
            replayPlayer = source;
            hud = targetHud;
            camera = targetCamera;
        }

        public void Show(Camera targetCamera)
        {
            camera = targetCamera;
            isVisible = true;
            RefreshTargets();
        }

        public void Hide()
        {
            isVisible = false;
            visibleTargets.Clear();
            hud?.SetVehicleTargets(visibleTargets);
        }

        void LateUpdate()
        {
            if (isVisible)
                RefreshTargets();
        }

        void RefreshTargets()
        {
            visibleTargets.Clear();

            if (replayPlayer == null || hud == null || camera == null)
            {
                hud?.SetVehicleTargets(visibleTargets);
                return;
            }

            List<PositionSampleDto> positions = replayPlayer.GetPositions();
            if (positions == null)
            {
                hud.SetVehicleTargets(visibleTargets);
                return;
            }

            foreach (PositionSampleDto position in positions)
            {
                if (position == null ||
                    !replayPlayer.TryGetVisualCarTransform(
                        position.driverNumber,
                        out Transform carTransform) ||
                    !TryGetViewportRect(carTransform, out Rect rect) ||
                    !viewportFrame.Overlaps(rect) ||
                    !viewportFrame.Contains(rect.center))
                {
                    continue;
                }

                DriverInfoDto driver = replayPlayer.GetDriverInfo(
                    position.driverNumber);
                string label = !string.IsNullOrWhiteSpace(driver?.nameAcronym)
                    ? driver.nameAcronym
                    : replayPlayer.GetDriverLabel(position.driverNumber);
                string team = driver?.teamName ?? string.Empty;
                visibleTargets.Add(new DroneVehicleTarget(
                    rect,
                    position.driverNumber,
                    position.position,
                    label,
                    team,
                    replayPlayer.GetDriverColor(position.driverNumber)));
            }

            visibleTargets.Sort(CompareTargets);
            if (visibleTargets.Count > maximumVisibleTargets)
                visibleTargets.RemoveRange(
                    maximumVisibleTargets,
                    visibleTargets.Count - maximumVisibleTargets);

            hud.SetVehicleTargets(visibleTargets);
        }

        bool TryGetViewportRect(Transform carTransform, out Rect rect)
        {
            rect = default;
            if (carTransform == null)
                return false;

            renderers.Clear();
            carTransform.GetComponentsInChildren(renderers);
            if (renderers.Count == 0)
                return false;

            bool hasBounds = false;
            Bounds bounds = default;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
                return false;

            Vector3 center = camera.WorldToViewportPoint(bounds.center);
            if (center.z <= 0.01f)
                return false;

            Vector3 extents = bounds.extents;
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            int visibleCornerCount = 0;

            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 point = bounds.center + Vector3.Scale(
                    extents,
                    new Vector3(x, y, z));
                Vector3 viewportPoint = camera.WorldToViewportPoint(point);
                if (viewportPoint.z <= 0.01f)
                    continue;

                visibleCornerCount++;
                minX = Mathf.Min(minX, viewportPoint.x);
                minY = Mathf.Min(minY, viewportPoint.y);
                maxX = Mathf.Max(maxX, viewportPoint.x);
                maxY = Mathf.Max(maxY, viewportPoint.y);
            }

            if (visibleCornerCount == 0)
                return false;

            rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return rect.width >= minimumViewportSize &&
                rect.height >= minimumViewportSize;
        }

        static int CompareTargets(
            DroneVehicleTarget first,
            DroneVehicleTarget second)
        {
            float firstDistance = (first.viewportRect.center -
                new Vector2(0.5f, 0.5f)).sqrMagnitude;
            float secondDistance = (second.viewportRect.center -
                new Vector2(0.5f, 0.5f)).sqrMagnitude;
            return firstDistance.CompareTo(secondDistance);
        }
    }
}
