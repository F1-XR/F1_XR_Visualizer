using UnityEngine;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace F1XR.RaceFlags
{
    [DisallowMultipleComponent]
    public sealed class RaceFlagAlert : MonoBehaviour
    {
        private static readonly int FlagModeId = Shader.PropertyToID("_FlagMode");
        private static readonly int FlagColorId = Shader.PropertyToID("_FlagColor");
        private static readonly int WaveAmplitudeId = Shader.PropertyToID("_WaveAmplitude");
        private static readonly int WaveFrequencyId = Shader.PropertyToID("_WaveFrequency");
        private static readonly int WaveSpeedId = Shader.PropertyToID("_WaveSpeed");
        private static readonly int SecondaryWaveId = Shader.PropertyToID("_SecondaryWave");
        private static readonly int MotionPhaseId = Shader.PropertyToID("_MotionPhase");

        [Header("References")]
        [SerializeField] private Transform motionPivot;
        [SerializeField] private Renderer flagRenderer;

        [Header("Flag")]
        [SerializeField] private RaceFlagType initialType = RaceFlagType.Yellow;
        [SerializeField] private bool playOnEnable = false;

        [Header("Timing")]
        [SerializeField, Min(0.05f)] private float visibleDuration = 5.0f;
        [SerializeField, Min(0.0f)] private float enterDuration = 0.20f;
        [SerializeField, Min(0.0f)] private float exitDuration = 0.50f;
        [SerializeField] private bool useUnscaledTime = false;

        [Header("Pole Motion")]
        [SerializeField] private float motionSpeed = 4.5f;
        [SerializeField] private float horizontalAngle = 22.0f;
        [SerializeField] private float verticalAngle = 9.0f;
        [SerializeField] private float twistAngle = 4.0f;
        [SerializeField] private float bobAmount = 0.015f;

        [Header("Shader Motion")]
        [SerializeField] private float waveAmplitude = 0.04f;
        [SerializeField] private float waveFrequency = 10.0f;
        [SerializeField] private float waveSpeed = 6.0f;
        [SerializeField] private float secondaryWaveAmount = 0.35f;

        [Header("Development Test")]
        [SerializeField] private bool enableKeyboardTest = false;

        private MaterialPropertyBlock propertyBlock;
        private RaceFlagType currentType;
        private bool initialized;
        private bool isShowing;
        private float timer;
        private float motionPhase;

        private Vector3 originalRootLocalPosition;
        private Quaternion originalRootLocalRotation;
        private Vector3 originalRootLocalScale;
        private Vector3 originalPivotLocalPosition;
        private Quaternion originalPivotLocalRotation;
        private Vector3 originalPivotLocalScale;

        private void Awake()
        {
            InitializeIfNeeded();
            currentType = initialType;
            ApplyShaderProperties();
        }

        private void OnEnable()
        {
            InitializeIfNeeded();
            currentType = initialType;
            ApplyShaderProperties();

            if (playOnEnable)
                Show(currentType);
        }

        private void OnDisable()
        {
            RestoreOriginalTransforms();
            ResetRuntimeState();
        }

        private void Update()
        {
            HandleKeyboardTest();

            if (!isShowing)
                return;

            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            timer += deltaTime;

            ApplyPoleMotion();
            ApplyLifetimeAnimation();
            ApplyShaderProperties();

            if (timer >= visibleDuration)
                gameObject.SetActive(false);
        }

        public void Show(RaceFlagType type)
        {
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            InitializeIfNeeded();
            RestoreOriginalTransforms();
            SetFlagType(type);

            timer = 0.0f;
            motionPhase = Random.Range(0.0f, Mathf.PI * 2.0f);
            isShowing = true;
            transform.localScale = Vector3.zero;
            ApplyShaderProperties();
        }

        public void SetFlagType(RaceFlagType type)
        {
            currentType = type;
            ApplyShaderProperties();
        }

        public void HideImmediately()
        {
            RestoreOriginalTransforms();
            ResetRuntimeState();
            gameObject.SetActive(false);
        }

        public void RestartCurrentFlag()
        {
            Show(currentType);
        }

        private void InitializeIfNeeded()
        {
            if (initialized)
                return;

            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();

            if (motionPivot == null)
                motionPivot = transform.Find("MotionPivot");

            if (flagRenderer == null)
            {
                Transform flag = transform.Find("MotionPivot/FlagMesh");
                if (flag != null)
                    flagRenderer = flag.GetComponent<Renderer>();
            }

            originalRootLocalPosition = transform.localPosition;
            originalRootLocalRotation = transform.localRotation;
            originalRootLocalScale = transform.localScale;

            if (motionPivot != null)
            {
                originalPivotLocalPosition = motionPivot.localPosition;
                originalPivotLocalRotation = motionPivot.localRotation;
                originalPivotLocalScale = motionPivot.localScale;
            }

            initialized = true;
        }

        private void ApplyLifetimeAnimation()
        {
            float safeVisibleDuration = Mathf.Max(0.05f, visibleDuration);
            float safeEnterDuration = Mathf.Min(Mathf.Max(0.0f, enterDuration), safeVisibleDuration);
            float safeExitDuration = Mathf.Min(Mathf.Max(0.0f, exitDuration), safeVisibleDuration);
            float exitStart = safeVisibleDuration - safeExitDuration;

            if (safeEnterDuration > 0.0f && timer < safeEnterDuration)
            {
                float enterT = Mathf.SmoothStep(0.0f, 1.0f, timer / safeEnterDuration);
                transform.localScale = originalRootLocalScale * enterT;
                transform.localPosition = originalRootLocalPosition;
                return;
            }

            if (safeExitDuration > 0.0f && timer >= exitStart)
            {
                float exitT = Mathf.SmoothStep(0.0f, 1.0f, (timer - exitStart) / safeExitDuration);
                transform.localScale = originalRootLocalScale * (1.0f - exitT);
                transform.localPosition = originalRootLocalPosition + Vector3.up * (0.05f * exitT);
                return;
            }

            transform.localScale = originalRootLocalScale;
            transform.localPosition = originalRootLocalPosition;
        }

        private void ApplyPoleMotion()
        {
            if (motionPivot == null)
                return;

            float t = timer * motionSpeed + motionPhase;
            float horizontal = Mathf.Sin(t) * horizontalAngle;
            float vertical = Mathf.Sin(t * 2.0f + motionPhase * 0.37f) * verticalAngle;
            float twist = Mathf.Sin(t * 1.31f + motionPhase * 0.71f) * twistAngle;
            float bob = Mathf.Sin(t * 2.0f + motionPhase) * bobAmount;

            motionPivot.localPosition = originalPivotLocalPosition + Vector3.up * bob;
            motionPivot.localRotation = originalPivotLocalRotation * Quaternion.Euler(vertical, twist, horizontal);
            motionPivot.localScale = originalPivotLocalScale;
        }

        private void ApplyShaderProperties()
        {
            if (flagRenderer == null)
                return;

            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();

            flagRenderer.GetPropertyBlock(propertyBlock);

            switch (currentType)
            {
                case RaceFlagType.Red:
                    propertyBlock.SetFloat(FlagModeId, 0.0f);
                    propertyBlock.SetColor(FlagColorId, new Color(0.85f, 0.04f, 0.03f, 1.0f));
                    break;
                case RaceFlagType.Checkered:
                    propertyBlock.SetFloat(FlagModeId, 1.0f);
                    propertyBlock.SetColor(FlagColorId, Color.white);
                    break;
                default:
                    propertyBlock.SetFloat(FlagModeId, 0.0f);
                    propertyBlock.SetColor(FlagColorId, new Color(1.0f, 0.75f, 0.03f, 1.0f));
                    break;
            }

            propertyBlock.SetFloat(WaveAmplitudeId, Mathf.Max(0.0f, waveAmplitude));
            propertyBlock.SetFloat(WaveFrequencyId, Mathf.Max(0.0f, waveFrequency));
            propertyBlock.SetFloat(WaveSpeedId, waveSpeed);
            propertyBlock.SetFloat(SecondaryWaveId, Mathf.Max(0.0f, secondaryWaveAmount));
            propertyBlock.SetFloat(MotionPhaseId, motionPhase);
            flagRenderer.SetPropertyBlock(propertyBlock);
        }

        private void RestoreOriginalTransforms()
        {
            if (!initialized)
                return;

            transform.localPosition = originalRootLocalPosition;
            transform.localRotation = originalRootLocalRotation;
            transform.localScale = originalRootLocalScale;

            if (motionPivot == null)
                return;

            motionPivot.localPosition = originalPivotLocalPosition;
            motionPivot.localRotation = originalPivotLocalRotation;
            motionPivot.localScale = originalPivotLocalScale;
        }

        private void ResetRuntimeState()
        {
            isShowing = false;
            timer = 0.0f;
        }

        private void HandleKeyboardTest()
        {
            if (!enableKeyboardTest)
                return;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
                Show(RaceFlagType.Yellow);
            else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
                Show(RaceFlagType.Red);
            else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame)
                Show(RaceFlagType.Checkered);
            else if (keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame)
                HideImmediately();
#else
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
                Show(RaceFlagType.Yellow);
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
                Show(RaceFlagType.Red);
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
                Show(RaceFlagType.Checkered);
            else if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
                HideImmediately();
#endif
        }
    }
}
