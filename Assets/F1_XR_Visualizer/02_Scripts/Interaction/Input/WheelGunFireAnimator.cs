using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace F1XR.Interaction.Input
{
    /// <summary>
    /// Punches the wheel-gun's trigger piece in and back each time the grip is squeezed. Purely the
    /// visual trigger animation — no sound (the fire sound now plays when a tire is socketed onto the
    /// wheel, see <see cref="PlaySoundOnSocketAttach"/>). Grip is debounced so one squeeze = one punch.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WheelGunFireAnimator : MonoBehaviour
    {
        [Tooltip("The trigger piece that punches in. Auto-found by name if left empty.")]
        [SerializeField] Transform button;
        [SerializeField] string buttonChildName = "WheelGun_Trigger";
        [Tooltip("Which hand's grip to read.")]
        [SerializeField] bool useRightHand = true;

        [Header("Punch")]
        [SerializeField] Vector3 pressDirection = new Vector3(0f, 0f, -1f);
        [SerializeField, Min(0f)] float pressDistance = 0.025f;
        [SerializeField, Min(0f)] float pressDuration = 0.05f;
        [SerializeField, Min(0f)] float releaseDuration = 0.12f;

        [Header("Grip debounce")]
        [SerializeField, Range(0f, 1f)] float gripOnThreshold = 0.6f;
        [SerializeField, Range(0f, 1f)] float gripOffThreshold = 0.35f;
        [SerializeField, Min(0f)] float releaseDebounce = 0.08f;

        Vector3 restLocalPosition;
        bool wasHeld;
        bool gripHeld;
        float lowTimer;
        Coroutine punchRoutine;
        readonly List<InputDevice> devices = new List<InputDevice>();

        void Awake()
        {
            if (button == null)
            {
                foreach (var t in GetComponentsInChildren<Transform>(true))
                    if (t.name == buttonChildName) { button = t; break; }
            }
            if (button != null)
                restLocalPosition = button.localPosition;
        }

        void Update()
        {
            float g = ReadGripValue();
            if (!gripHeld && g >= gripOnThreshold)
                gripHeld = true;
            else if (gripHeld && g <= gripOffThreshold)
            {
                lowTimer += Time.unscaledDeltaTime;
                if (lowTimer >= releaseDebounce)
                    gripHeld = false;
            }
            if (g > gripOffThreshold)
                lowTimer = 0f;

            if (gripHeld && !wasHeld)
                Fire();

            wasHeld = gripHeld;
        }

        void Fire()
        {
            if (button == null)
                return;
            if (punchRoutine != null)
                StopCoroutine(punchRoutine);
            punchRoutine = StartCoroutine(PunchRoutine());
        }

        IEnumerator PunchRoutine()
        {
            Vector3 pressed = restLocalPosition + pressDirection.normalized * pressDistance;
            for (float t = 0f; pressDuration > 0f && t < pressDuration; t += Time.deltaTime)
            { button.localPosition = Vector3.Lerp(restLocalPosition, pressed, t / pressDuration); yield return null; }
            button.localPosition = pressed;
            for (float t = 0f; releaseDuration > 0f && t < releaseDuration; t += Time.deltaTime)
            { button.localPosition = Vector3.Lerp(pressed, restLocalPosition, t / releaseDuration); yield return null; }
            button.localPosition = restLocalPosition;
            punchRoutine = null;
        }

        float ReadGripValue()
        {
            var handedness = useRightHand ? InputDeviceCharacteristics.Right : InputDeviceCharacteristics.Left;
            devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | handedness, devices);
            float best = 0f;
            foreach (var device in devices)
            {
                if (!device.isValid) continue;
                if (device.TryGetFeatureValue(CommonUsages.grip, out var amount))
                    best = Mathf.Max(best, amount);
                else if (device.TryGetFeatureValue(CommonUsages.gripButton, out var pressed) && pressed)
                    best = Mathf.Max(best, 1f);
            }
            return best;
        }
    }
}
