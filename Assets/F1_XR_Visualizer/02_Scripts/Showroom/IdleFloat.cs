using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.Showroom
{
    /// <summary>
    /// Gentle "alive" idle motion for an object left hanging in the air: a slow vertical bob plus a
    /// subtle yaw rock. Modelled on <see cref="F1XR.PlayPanel.FloatingIdle"/>, but aware of the grab:
    /// while the object is held the interactor owns the transform, so the drift stops and picks up
    /// again from wherever the object was let go.
    ///
    /// Only the yaw is rocked, never the pitch, so anything reading the object's up axis (the sip
    /// detector reads the can's tilt) is untouched by the idle motion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IdleFloat : MonoBehaviour
    {
        [Header("Bob (local Y)")]
        [SerializeField, Min(0f)] float bobAmplitude = 0.015f;
        [SerializeField, Min(0f)] float bobSpeed = 0.35f;

        [Header("Yaw rock (local Y axis)")]
        [SerializeField, Min(0f)] float rockAngle = 4f;
        [SerializeField, Min(0f)] float rockSpeed = 0.22f;

        [Tooltip("Pauses the drift while this is held. Left empty, one on this object is used.")]
        [SerializeField] XRBaseInteractable interactable;

        Vector3 baseLocalPos;
        Quaternion baseLocalRot;
        float phase;
        bool wasHeld;

        void Awake()
        {
            if (!interactable) interactable = GetComponent<XRBaseInteractable>();
        }

        void OnEnable()
        {
            Capture();
            wasHeld = false;
        }

        void OnDisable()
        {
            transform.localPosition = baseLocalPos;
            transform.localRotation = baseLocalRot;
        }

        void Capture()
        {
            baseLocalPos = transform.localPosition;
            baseLocalRot = transform.localRotation;
        }

        void Update()
        {
            bool isHeld = interactable && interactable.isSelected;

            if (isHeld)
            {
                // The interactor is driving the transform; keep out of its way.
                wasHeld = true;
                return;
            }

            if (wasHeld)
            {
                // Just released: drift around wherever it was put down, not where it started.
                Capture();
                wasHeld = false;
            }

            // Advancing our own phase keeps the motion continuous across a grab.
            phase += Time.deltaTime;

            float bob = Mathf.Sin(phase * bobSpeed * Mathf.PI * 2f) * bobAmplitude;
            float yaw = Mathf.Sin(phase * rockSpeed * Mathf.PI * 2f) * rockAngle;

            transform.localPosition = baseLocalPos + new Vector3(0f, bob, 0f);
            transform.localRotation = baseLocalRot * Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
