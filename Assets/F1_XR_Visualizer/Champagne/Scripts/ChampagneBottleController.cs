using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.Champagne
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class ChampagneBottleController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] XRGrabInteractable grabInteractable;
        [SerializeField] Rigidbody bottleBody;
        [SerializeField] Transform sprayOrigin;
        [SerializeField] BottleShakeDetector shakeDetector;
        [SerializeField] ChampagneSprayController sprayController;
        [SerializeField] CorkController corkController;
        [SerializeField] AudioSource popAudioSource;
        [SerializeField] AudioSource readyAudioSource;
        [SerializeField] Collider[] bottleColliders;

        [Header("Pop Conditions")]
        [SerializeField, Range(0f, 1f)] float minimumPopPressure = 0.65f;
        [SerializeField] bool allowLowPressurePop;
        [SerializeField] bool autoPopAtMaximumPressure;
        [SerializeField] float autoPopDelay = 0.25f;
        [SerializeField] bool readyHapticEnabled = true;
        [SerializeField, Range(0f, 1f)] float readyHapticAmplitude = 0.25f;
        [SerializeField] float readyHapticDuration = 0.04f;
        [SerializeField] bool readySoundEnabled;

        [Header("Haptics")]
        [SerializeField, Range(0f, 1f)] float popHapticAmplitude = 0.8f;
        [SerializeField] float popHapticDuration = 0.08f;
        [SerializeField] bool sprayHapticEnabled;
        [SerializeField, Range(0f, 1f)] float sprayHapticAmplitude = 0.15f;
        [SerializeField] float sprayHapticInterval = 0.25f;
        [SerializeField] bool shakeHapticEnabled;

        [Header("Audio")]
        [SerializeField] float popSoundDelay;
        [SerializeField, Range(0f, 1f)] float popVolume = 0.85f;
        [SerializeField] AudioSource shakeAudioSource;
        [SerializeField] bool shakeSoundEnabled;
        [SerializeField] float shakeSoundThreshold = 0.55f;
        [SerializeField] float shakeSoundCooldown = 0.25f;
        [SerializeField] bool pressureReadySoundEnabled;

        [Header("Reset")]
        [SerializeField] bool resetBottleVelocity = true;
        [SerializeField] bool resetBottleTransform;

        [Header("Debug")]
        [SerializeField] bool debugLogs;
        [SerializeField] ChampagneBottleState currentState = ChampagneBottleState.Sealed;
        [SerializeField] bool currentlyGrabbed;

        Vector3 initialPosition;
        Quaternion initialRotation;
        IXRSelectInteractor holdingInteractor;
        Coroutine autoPopRoutine;
        Coroutine popSoundRoutine;
        float nextSprayHapticTime;
        float nextShakeFeedbackTime;
        bool readyFeedbackSent;
        bool interactionEnabled = true;

        public ChampagneBottleState CurrentState => currentState;
        public bool IsGrabbed => currentlyGrabbed;

        void Reset()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
            bottleBody = GetComponent<Rigidbody>();
            shakeDetector = GetComponent<BottleShakeDetector>();
            sprayController = GetComponentInChildren<ChampagneSprayController>(includeInactive: true);
            corkController = GetComponentInChildren<CorkController>(includeInactive: true);
            bottleColliders = GetComponentsInChildren<Collider>(includeInactive: true);
        }

        void Awake()
        {
            if (grabInteractable == null)
                grabInteractable = GetComponent<XRGrabInteractable>();
            if (bottleBody == null)
                bottleBody = GetComponent<Rigidbody>();
            if (shakeDetector == null)
                shakeDetector = GetComponent<BottleShakeDetector>();
            if (sprayController == null)
                sprayController = GetComponentInChildren<ChampagneSprayController>(includeInactive: true);
            if (corkController == null)
                corkController = GetComponentInChildren<CorkController>(includeInactive: true);
            if (bottleColliders == null || bottleColliders.Length == 0)
                bottleColliders = GetComponentsInChildren<Collider>(includeInactive: true);

            initialPosition = transform.position;
            initialRotation = transform.rotation;
            ConfigureBottleBody();
            ResetBottle();
        }

        void OnEnable()
        {
            if (grabInteractable == null)
                return;

            grabInteractable.selectEntered.AddListener(OnSelectEntered);
            grabInteractable.selectExited.AddListener(OnSelectExited);
            grabInteractable.activated.AddListener(OnActivated);
        }

        void OnDisable()
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
                grabInteractable.selectExited.RemoveListener(OnSelectExited);
                grabInteractable.activated.RemoveListener(OnActivated);
            }

            StopAutoPopRoutine();
            StopPopSoundRoutine();
        }

        void Update()
        {
            UpdatePressureState();
            UpdateSprayState();
            UpdateSprayHaptic();
            UpdateShakeFeedback();
        }

        void ConfigureBottleBody()
        {
            if (bottleBody == null)
                return;

            bottleBody.useGravity = true;
            bottleBody.interpolation = RigidbodyInterpolation.Interpolate;
            bottleBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            currentlyGrabbed = true;
            holdingInteractor = args.interactorObject;
            if (shakeDetector != null)
                shakeDetector.SetGrabbed(true);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            currentlyGrabbed = false;
            if (ReferenceEquals(holdingInteractor, args.interactorObject))
                holdingInteractor = null;

            if (shakeDetector != null)
                shakeDetector.SetGrabbed(false);
        }

        void OnActivated(ActivateEventArgs args)
        {
            TryPop();
        }

        void UpdatePressureState()
        {
            if (shakeDetector == null)
                return;

            var pressure = shakeDetector.CurrentPressure;
            if (currentState == ChampagneBottleState.Sealed && pressure >= minimumPopPressure)
            {
                SetState(ChampagneBottleState.Pressurized);
                SendReadyFeedback();
            }
            else if (currentState == ChampagneBottleState.Pressurized && pressure < minimumPopPressure)
            {
                SetState(ChampagneBottleState.Sealed);
                readyFeedbackSent = false;
            }

            if (autoPopAtMaximumPressure && currentState == ChampagneBottleState.Pressurized && pressure >= 0.99f && autoPopRoutine == null)
                autoPopRoutine = StartCoroutine(AutoPopAfterDelay());
        }

        void UpdateSprayState()
        {
            if (currentState != ChampagneBottleState.Spraying || sprayController == null)
                return;

            if (!sprayController.IsSpraying)
                SetState(ChampagneBottleState.Empty);
        }

        void UpdateSprayHaptic()
        {
            if (!sprayHapticEnabled || currentState != ChampagneBottleState.Spraying || Time.time < nextSprayHapticTime)
                return;

            SendHaptic(sprayHapticAmplitude, Mathf.Min(0.04f, sprayHapticInterval));
            nextSprayHapticTime = Time.time + sprayHapticInterval;
        }

        void UpdateShakeFeedback()
        {
            if ((!shakeHapticEnabled && !shakeSoundEnabled) ||
                shakeDetector == null ||
                !currentlyGrabbed ||
                Time.time < nextShakeFeedbackTime ||
                shakeDetector.CurrentShakeStrength < shakeSoundThreshold)
            {
                return;
            }

            if (shakeHapticEnabled)
                SendHaptic(0.08f, 0.025f);

            if (shakeSoundEnabled && shakeAudioSource != null && shakeAudioSource.clip != null)
                shakeAudioSource.PlayOneShot(shakeAudioSource.clip, Mathf.Clamp01(shakeDetector.CurrentShakeStrength));

            nextShakeFeedbackTime = Time.time + shakeSoundCooldown;
        }

        IEnumerator AutoPopAfterDelay()
        {
            yield return new WaitForSeconds(autoPopDelay);
            autoPopRoutine = null;

            if (currentState == ChampagneBottleState.Pressurized)
                TryPop();
        }

        public bool TryPop()
        {
            if (!interactionEnabled)
                return RejectPop("interaction disabled");

            if (currentState != ChampagneBottleState.Sealed && currentState != ChampagneBottleState.Pressurized)
                return RejectPop($"state={currentState}");

            var pressure = GetCurrentPressure();
            if (!allowLowPressurePop && pressure < minimumPopPressure)
                return RejectPop($"pressure {pressure:0.00} below {minimumPopPressure:0.00}");

            StopAutoPopRoutine();
            SetState(ChampagneBottleState.Popped);

            var direction = sprayOrigin != null ? sprayOrigin.forward : transform.forward;
            if (corkController != null)
                corkController.Launch(direction, bottleBody, bottleColliders);
            else
                Debug.LogWarning("[ChampagneBottle] CorkController is missing.", this);

            PlayPopSound();
            SendHaptic(popHapticAmplitude, popHapticDuration);

            if (sprayController != null)
            {
                sprayController.StartSpray(Mathf.Max(pressure, minimumPopPressure));
                SetState(ChampagneBottleState.Spraying);
            }
            else
            {
                Debug.LogWarning("[ChampagneBottle] ChampagneSprayController is missing.", this);
                SetState(ChampagneBottleState.Empty);
            }

            return true;
        }

        bool RejectPop(string reason)
        {
            if (debugLogs)
                Debug.Log($"[ChampagneBottle] pop rejected: {reason}", this);

            return false;
        }

        void PlayPopSound()
        {
            if (popAudioSource == null || popAudioSource.clip == null)
                return;

            if (popSoundDelay <= 0f)
            {
                popAudioSource.PlayOneShot(popAudioSource.clip, popVolume);
                return;
            }

            StopPopSoundRoutine();
            popSoundRoutine = StartCoroutine(PlayPopSoundAfterDelay());
        }

        IEnumerator PlayPopSoundAfterDelay()
        {
            yield return new WaitForSeconds(popSoundDelay);
            if (popAudioSource != null && popAudioSource.clip != null)
                popAudioSource.PlayOneShot(popAudioSource.clip, popVolume);

            popSoundRoutine = null;
        }

        void SendReadyFeedback()
        {
            if (readyFeedbackSent)
                return;

            readyFeedbackSent = true;

            if (readyHapticEnabled)
                SendHaptic(readyHapticAmplitude, readyHapticDuration);

            if ((readySoundEnabled || pressureReadySoundEnabled) && readyAudioSource != null && readyAudioSource.clip != null)
                readyAudioSource.PlayOneShot(readyAudioSource.clip);
        }

        void SendHaptic(float amplitude, float duration)
        {
            if (holdingInteractor is XRBaseInputInteractor inputInteractor)
                inputInteractor.SendHapticImpulse(amplitude, duration);
        }

        void SetState(ChampagneBottleState nextState)
        {
            if (currentState == nextState)
                return;

            currentState = nextState;
            if (shakeDetector != null)
                shakeDetector.SetCanAccumulatePressure(nextState == ChampagneBottleState.Sealed ||
                                                       nextState == ChampagneBottleState.Pressurized);

            if (debugLogs)
                Debug.Log($"[ChampagneBottle] state={currentState}", this);
        }

        void StopAutoPopRoutine()
        {
            if (autoPopRoutine == null)
                return;

            StopCoroutine(autoPopRoutine);
            autoPopRoutine = null;
        }

        void StopPopSoundRoutine()
        {
            if (popSoundRoutine == null)
                return;

            StopCoroutine(popSoundRoutine);
            popSoundRoutine = null;
        }

        public void PrepareBottle()
        {
            ResetBottle();
            HideBottle();
        }

        public void ShowBottle()
        {
            gameObject.SetActive(true);
        }

        public void HideBottle()
        {
            gameObject.SetActive(false);
        }

        public void StartCelebration()
        {
            ShowBottle();
            SetInteractionEnabled(true);
        }

        public void EndCelebration()
        {
            SetInteractionEnabled(false);
            ResetBottle();
        }

        public void SetInteractionEnabled(bool enabled)
        {
            interactionEnabled = enabled;
            if (grabInteractable != null)
                grabInteractable.enabled = enabled;
        }

        public void SetPressure(float normalizedPressure)
        {
            if (shakeDetector != null)
                shakeDetector.SetPressure(normalizedPressure);
        }

        public float GetCurrentPressure()
        {
            return shakeDetector != null ? shakeDetector.CurrentPressure : 0f;
        }

        [ContextMenu("Force Pop")]
        public void ForcePop()
        {
            if (shakeDetector != null)
                shakeDetector.SetPressure(1f);

            TryPop();
        }

        [ContextMenu("Set Full Pressure")]
        public void SetFullPressure()
        {
            SetPressure(1f);
        }

        [ContextMenu("Reset Bottle")]
        public void ResetBottle()
        {
            StopAutoPopRoutine();
            StopPopSoundRoutine();
            readyFeedbackSent = false;
            currentlyGrabbed = false;
            holdingInteractor = null;
            SetState(ChampagneBottleState.Sealed);

            if (shakeDetector != null)
            {
                shakeDetector.SetGrabbed(false);
                shakeDetector.ResetPressure();
                shakeDetector.SetCanAccumulatePressure(true);
            }

            if (sprayController != null)
                sprayController.StopAndClear();

            if (corkController != null)
                corkController.ResetCork();

            if (popAudioSource != null)
                popAudioSource.Stop();

            if (readyAudioSource != null)
                readyAudioSource.Stop();

            if (shakeAudioSource != null)
                shakeAudioSource.Stop();

            if (bottleBody != null && resetBottleVelocity)
            {
                bottleBody.linearVelocity = Vector3.zero;
                bottleBody.angularVelocity = Vector3.zero;
            }

            if (resetBottleTransform)
                transform.SetPositionAndRotation(initialPosition, initialRotation);
        }
    }
}
