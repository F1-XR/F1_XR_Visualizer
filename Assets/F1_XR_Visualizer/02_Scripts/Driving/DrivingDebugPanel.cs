using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace F1XR.Driving
{
    [DisallowMultipleComponent]
    public sealed class DrivingDebugPanel : MonoBehaviour
    {
        const float RefreshInterval = 0.1f;

        [Header("Display")]
        [SerializeField] bool showPanel;
        [SerializeField] bool showSteeringLine = true;
        [SerializeField, Min(0.0005f)] float steeringLineWidth = 0.002f;

        VRVehicleDriver vehicle;
        GameObject panelRoot;
        LineRenderer steeringLine;
        Material steeringLineMaterial;
        TextMeshProUGUI statusText;
        float nextRefreshTime;

        void Awake()
        {
            vehicle = GetComponentInParent<VRVehicleDriver>();
            if (vehicle == null)
                vehicle = FindFirstObjectByType<VRVehicleDriver>();
        }

        void OnEnable()
        {
            if (panelRoot != null)
                panelRoot.SetActive(showPanel);
        }

        void OnDisable()
        {
            if (panelRoot != null)
                panelRoot.SetActive(false);
            if (steeringLine != null)
                steeringLine.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (panelRoot != null)
                Destroy(panelRoot);
            if (steeringLine != null)
                Destroy(steeringLine.gameObject);
            if (steeringLineMaterial != null)
                Destroy(steeringLineMaterial);
        }

        void Update()
        {
            UpdateSteeringLine();

            if (!showPanel)
            {
                if (panelRoot != null && panelRoot.activeSelf)
                    panelRoot.SetActive(false);
                return;
            }

            if (panelRoot == null)
                CreatePanel();

            if (panelRoot == null)
                return;

            if (!panelRoot.activeSelf)
                panelRoot.SetActive(true);

            if (!panelRoot.activeSelf || Time.time < nextRefreshTime)
                return;

            nextRefreshTime = Time.time + RefreshInterval;
            statusText.text =
                "DRIVING DEBUG\n" +
                $"Speed     {vehicle.SpeedKph,6:F1} km/h\n" +
                $"Forward   {vehicle.ForwardSpeedMps,6:F2} m/s\n" +
                $"Throttle  {vehicle.ThrottleInput,6:F2}\n" +
                $"Brake     {vehicle.BrakeInput,6:F2}\n" +
                $"Steering  {vehicle.SteeringInput,6:F2}\n" +
                $"Grounded  {vehicle.IsGrounded}\n" +
                $"Velocity  {vehicle.Velocity:F2}\n" +
                $"Angular   {vehicle.AngularVelocity:F2}\n" +
                $"Input     XRI:{vehicle.IsThrottleActionEnabled} Direct:{vehicle.IsDirectThrottleActionEnabled}";
        }

        void UpdateSteeringLine()
        {
            if (!showSteeringLine || vehicle == null || !vehicle.IsSteeringGrabbing ||
                !vehicle.TryGetSteeringHandPositions(out Vector3 left, out Vector3 right))
            {
                if (steeringLine != null && steeringLine.gameObject.activeSelf)
                    steeringLine.gameObject.SetActive(false);
                return;
            }

            if (steeringLine == null)
                CreateSteeringLine();

            if (steeringLine == null)
                return;

            steeringLine.gameObject.SetActive(true);
            steeringLine.SetPosition(0, left);
            steeringLine.SetPosition(1, right);
        }

        void CreateSteeringLine()
        {
            GameObject lineObject = new GameObject("Steering Line", typeof(LineRenderer));
            lineObject.transform.SetParent(transform, false);

            steeringLine = lineObject.GetComponent<LineRenderer>();
            steeringLine.positionCount = 2;
            steeringLine.useWorldSpace = true;
            steeringLine.alignment = LineAlignment.View;
            steeringLine.widthMultiplier = steeringLineWidth;
            steeringLine.numCapVertices = 4;
            steeringLine.startColor = Color.cyan;
            steeringLine.endColor = Color.cyan;
            steeringLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            steeringLine.receiveShadows = false;

            Shader lineShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (lineShader == null)
                return;

            steeringLineMaterial = new Material(lineShader);
            steeringLineMaterial.SetColor("_BaseColor", Color.cyan);
            steeringLineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
            steeringLineMaterial.renderQueue = 5000;
            steeringLine.material = steeringLineMaterial;
        }

        void CreatePanel()
        {
            Camera camera = Camera.main;
            if (camera == null || TMP_Settings.defaultFontAsset == null)
                return;

            panelRoot = new GameObject("Driving Debug Panel", typeof(RectTransform), typeof(Canvas),
                typeof(Image));
            panelRoot.transform.SetParent(vehicle.transform, false);

            RectTransform panelRect = panelRoot.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(460f, 310f);
            panelRect.localPosition = new Vector3(-0.7f, 1.6f, 3f);
            panelRect.localRotation = Quaternion.identity;
            panelRect.localScale = Vector3.one * 0.002f;

            Canvas canvas = panelRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;

            Image background = panelRoot.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.72f);

            GameObject textObject = new GameObject("Status", typeof(RectTransform),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(panelRoot.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(24f, 18f);
            textRect.offsetMax = new Vector2(-24f, -18f);

            statusText = textObject.GetComponent<TextMeshProUGUI>();
            statusText.font = TMP_Settings.defaultFontAsset;
            statusText.fontSize = 24f;
            statusText.color = Color.white;
            statusText.alignment = TextAlignmentOptions.TopLeft;
        }
    }
}
