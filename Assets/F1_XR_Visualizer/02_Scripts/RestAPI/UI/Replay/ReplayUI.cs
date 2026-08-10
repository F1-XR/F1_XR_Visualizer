using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using F1XR.RestAPI.Replay;
using F1XR.RestAPI.Api;
using F1XR.UI.WorldPanel;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.RestAPI.UI
{
    public partial class ReplayUI : MonoBehaviour
    {
        public ReplayPlayer player;
        [FormerlySerializedAs("progressBar")]
        [FormerlySerializedAs("_bar")]
        public ReplayBar bar;

        public Button playPauseButton;
        public TMP_Text playPauseLabel;

        public TMP_Dropdown speedDropdown;
        public float[] speedValues = { 0.5f, 1f, 2f, 4f, 6f, 8f, 16f };

        public TMP_Text timeLabel;
        public TMP_Text rankText;
        public Vector2 standingsSize = new Vector2(196f, 470f);
        public Vector2 driverDetailSize = new Vector2(180f, 150f);
        public Vector2 driverOnboardSize = new Vector2(260f, 176f);
        public Vector2 driverOnboardTextureSize = new Vector2(512f, 288f);
        public float standingsRowHeight = 19f;
        public float positionChangeFlashSeconds = 1.2f;
        public float standingsMoveSpeed = 10f;
        [SerializeField, Min(0.02f)]
        private float refreshInterval = 0.1f;

        private RectTransform standingsRoot;
        private RectTransform driverDetailRoot;
        private Image driverDetailTeamBar;
        private TMP_Text driverDetailName;
        private TMP_Text driverDetailNumber;
        private TMP_Text driverDetailTeam;
        private TMP_Text driverDetailPosition;
        private TMP_Text driverDetailTire;
        private RectTransform driverOnboardRoot;
        private RawImage driverOnboardImage;
        private TMP_Text driverOnboardTitle;
        private DriverOnboardView driverOnboardView;
        private TMP_Text lapHeader;
        private TMP_Text tireHeader;
        private readonly List<StandingRow> standingRows = new();
        private readonly Dictionary<int, StandingRow> rowsByDriver = new();
        private readonly HashSet<int> visibleDrivers = new();
        private readonly Dictionary<GameObject, bool>
            collisionCaptureStates = new();
        private readonly PositionChangeTracker positionChangeTracker = new();
        private int selectedDriverNumber;
        private bool controlsStyled;
        private float lastPlayPauseTime = float.NegativeInfinity;
        private float nextRefreshTime;
        private bool collisionCaptureApplied;

        private void Awake()
        {
            if (bar != null && bar.player == null)
                bar.player = player;
        }
        
        private void Start()
        {
            if (speedDropdown != null)
            {
                speedDropdown.ClearOptions();

                List<string> labels = new();
                foreach (float speed in speedValues)
                    labels.Add($"{speed:0.##}x");

                speedDropdown.AddOptions(labels);
                speedDropdown.value = DefaultSpeedIndex();
                speedDropdown.RefreshShownValue();

                SetSpeedIndex(speedDropdown.value);
            }

            StyleControls();
            EnsurePlacementControls();
            EnsureEventControls();
            RefreshReplayPanelGrabBounds();
        }

        private void OnEnable()
        {
            nextRefreshTime = 0f;

            if (playPauseButton != null)
                playPauseButton.onClick.AddListener(TogglePlayPause);

            if (speedDropdown != null)
                speedDropdown.onValueChanged.AddListener(SetSpeedIndex);

            if (player != null)
            {
                player.SelectedDriverChanged -= OnSelectedDriverChanged;
                player.SelectedDriverChanged += OnSelectedDriverChanged;
                OnSelectedDriverChanged(player.SelectedDriverNumber);
            }
        }

        private void OnDisable()
        {
            RestoreCollisionCaptureProfile();
            ReleasePitPortalEventControls();
            if (collisionOpenWhenPreparedRoutine != null)
            {
                StopCoroutine(collisionOpenWhenPreparedRoutine);
                collisionOpenWhenPreparedRoutine = null;
                collisionDatasetLoading = false;
            }

            if (playPauseButton != null)
                playPauseButton.onClick.RemoveListener(TogglePlayPause);

            if (speedDropdown != null)
                speedDropdown.onValueChanged.RemoveListener(SetSpeedIndex);

            if (player != null)
                player.SelectedDriverChanged -= OnSelectedDriverChanged;
        }

        private void RefreshReplayPanelGrabBounds()
        {
            XRGrabInteractable grab =
                GetComponentInParent<XRGrabInteractable>(true);
            if (grab == null)
                return;

            BoxCollider body = null;
            for (int i = 0; i < grab.colliders.Count; i++)
            {
                if (grab.colliders[i] is BoxCollider candidate &&
                    candidate.name == "Interaction")
                {
                    body = candidate;
                    break;
                }
            }

            if (body == null)
                return;

            Vector3 center = body.center;
            Vector3 size = body.size;
            float halfWidth = Mathf.Abs(size.x) * 0.5f;
            float halfHeight = Mathf.Abs(size.y) * 0.5f;
            EncapsulateReplayRect(
                transform as RectTransform,
                body.transform,
                center,
                ref halfWidth,
                ref halfHeight);
            EncapsulateReplayRect(
                placementControls,
                body.transform,
                center,
                ref halfWidth,
                ref halfHeight);
            EncapsulateReplayRect(
                eventControls,
                body.transform,
                center,
                ref halfWidth,
                ref halfHeight);

            body.size = new Vector3(
                Mathf.Max(Mathf.Abs(size.x), halfWidth * 2f),
                Mathf.Max(Mathf.Abs(size.y), halfHeight * 2f),
                Mathf.Abs(size.z));

            PanelEdgeGrab edgeGrab =
                grab.GetComponent<PanelEdgeGrab>();
            RectTransform panelRect = grab
                .GetComponentInChildren<Canvas>(true)
                ?.GetComponent<RectTransform>();
            if (edgeGrab != null && panelRect != null)
            {
                edgeGrab.Configure(
                    grab,
                    body,
                    panelRect,
                    true,
                    true);
            }
        }

        private static void EncapsulateReplayRect(
            RectTransform rect,
            Transform colliderTransform,
            Vector3 colliderCenter,
            ref float halfWidth,
            ref float halfHeight)
        {
            if (rect == null || colliderTransform == null)
                return;

            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 local = colliderTransform
                    .InverseTransformPoint(corners[i]);
                halfWidth = Mathf.Max(
                    halfWidth,
                    Mathf.Abs(local.x - colliderCenter.x));
                halfHeight = Mathf.Max(
                    halfHeight,
                    Mathf.Abs(local.y - colliderCenter.y));
            }
        }

        private void OnSelectedDriverChanged(int driverNumber)
        {
            if (driverNumber > 0)
                ShowDriverDetail(driverNumber, false);
            else
                HideDriverDetail(false);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime =
                Time.unscaledTime +
                Mathf.Max(0.02f, refreshInterval);
            Refresh();
        }

        public void TogglePlayPause()
        {
            if (player == null)
                return;

            if (Time.unscaledTime - lastPlayPauseTime < 0.15f)
                return;

            lastPlayPauseTime = Time.unscaledTime;

            player.TogglePlay();
            Refresh();
        }

        public void SetSpeed(float speed)
        {
            if (player == null)
                return;

            player.SetSpeed(speed);
            Refresh();
        }
        
        private static string TireSymbol(string compound)
        {
            if (string.IsNullOrWhiteSpace(compound))
                return "-";

            string value = compound.ToUpperInvariant();

            if (value.StartsWith("SOFT"))
                return "S";

            if (value.StartsWith("MEDIUM"))
                return "M";

            if (value.StartsWith("HARD"))
                return "H";

            if (value.StartsWith("INTER"))
                return "I";

            if (value.StartsWith("WET"))
                return "W";

            return value.Length > 0 ? value.Substring(0, 1) : "-";
        }

        private static Color TireColor(string compound)
        {
            if (string.IsNullOrWhiteSpace(compound))
                return new Color(0.55f, 0.6f, 0.68f);

            string value = compound.ToUpperInvariant();

            if (value.StartsWith("SOFT"))
                return new Color(0.95f, 0.08f, 0.08f);

            if (value.StartsWith("MEDIUM"))
                return new Color(1f, 0.84f, 0.08f);

            if (value.StartsWith("HARD"))
                return new Color(0.95f, 0.95f, 0.95f);

            if (value.StartsWith("INTER"))
                return new Color(0.1f, 0.78f, 0.28f);

            if (value.StartsWith("WET"))
                return new Color(0.1f, 0.42f, 1f);

            return new Color(0.55f, 0.6f, 0.68f);
        }

        public void Refresh()
        {
            if (player == null)
                return;

            if (playPauseLabel != null)
                playPauseLabel.text = player.IsPlaying ? "Pause" : "Play";

            if (timeLabel != null)
                timeLabel.text = $"{FormatTime(player.PlaybackElapsedTime)} / {FormatTime(player.Duration)}";

            if (bar != null)
                bar.Refresh();

            StyleControls();
            RefreshPlacementControls();
            RefreshEventControls();
            RefreshStandings(player.GetPositions());
            UpdateCollisionCaptureProfile();
        }

        private void UpdateCollisionCaptureProfile()
        {
            EventPopoutReplay eventReplay = player != null
                ? player.EventReplay
                : null;
            bool active = eventReplay != null &&
                eventReplay.IsActive &&
                eventReplay.IsCurrentCollision &&
                eventReplay.UseCollisionCaptureProfile;
            if (!active)
            {
                RestoreCollisionCaptureProfile();
                return;
            }

            collisionCaptureApplied = true;
            HideForCollisionCapture(bar != null
                ? bar.barRect != null
                    ? bar.barRect.gameObject
                    : bar.gameObject
                : null);
            HideForCollisionCapture(
                playPauseButton != null
                    ? playPauseButton.gameObject
                    : null);
            HideForCollisionCapture(
                speedDropdown != null
                    ? speedDropdown.gameObject
                    : null);
            HideForCollisionCapture(
                timeLabel != null
                    ? timeLabel.gameObject
                    : null);
            HideForCollisionCapture(
                standingsRoot != null
                    ? standingsRoot.gameObject
                    : null);
            HideForCollisionCapture(
                driverDetailRoot != null
                    ? driverDetailRoot.gameObject
                    : null);
            HideForCollisionCapture(
                driverOnboardRoot != null
                    ? driverOnboardRoot.gameObject
                    : null);
            HideForCollisionCapture(
                placementControls != null
                    ? placementControls.gameObject
                    : null);
        }

        private void HideForCollisionCapture(GameObject target)
        {
            if (target == null ||
                target == gameObject ||
                (eventControls != null &&
                 eventControls.IsChildOf(target.transform)))
            {
                return;
            }

            if (!collisionCaptureStates.ContainsKey(target))
                collisionCaptureStates.Add(target, target.activeSelf);
            if (target.activeSelf)
                target.SetActive(false);
        }

        private void RestoreCollisionCaptureProfile()
        {
            if (!collisionCaptureApplied &&
                collisionCaptureStates.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<GameObject, bool> state
                     in collisionCaptureStates)
            {
                if (state.Key != null &&
                    state.Key.activeSelf != state.Value)
                {
                    state.Key.SetActive(state.Value);
                }
            }
            collisionCaptureStates.Clear();
            collisionCaptureApplied = false;
        }

        private static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            int totalSeconds = Mathf.FloorToInt(seconds);
            int minutes = totalSeconds / 60;
            int secs = totalSeconds % 60;

            return $"{minutes:00}:{secs:00}";
        }
        
        private void SetSpeedIndex(int index)
        {
            if (speedValues == null || index < 0 || index >= speedValues.Length)
                return;

            SetSpeed(speedValues[index]);
        }

        private int DefaultSpeedIndex()
        {
            if (speedValues == null || speedValues.Length == 0)
                return 0;

            float speed = player != null ? player.playbackSpeed : 6f;
            int bestIndex = 0;
            float bestDistance = Mathf.Abs(speedValues[0] - speed);

            for (int i = 1; i < speedValues.Length; i++)
            {
                float distance = Mathf.Abs(speedValues[i] - speed);
                if (distance >= bestDistance)
                    continue;

                bestIndex = i;
                bestDistance = distance;
            }

            return bestIndex;
        }

    }
}
