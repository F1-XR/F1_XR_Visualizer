using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.Showroom
{
    /// <summary>
    /// Plays a sip sound when the player raises a held can to their mouth and tips it back.
    /// Lives on the can root (the object carrying the XRGrabInteractable).
    /// Fires once per sip: the can has to leave the drinking pose before it can play again.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class BeerDrinkDetector : MonoBehaviour
    {
        [Tooltip("Empty at the centre of the can's mouth. Distance is measured from here, not the can's pivot, so raising the base to your face does nothing.")]
        [SerializeField] Transform canOpening;

        [Tooltip("The XR head transform. Left empty, Camera.main is used.")]
        [SerializeField] Transform xrCamera;

        [Tooltip("Plays the sip clip. Left empty, one on this object is used.")]
        [SerializeField] AudioSource audioSource;

        [Tooltip("How close the mouth of the can must come to the head, in metres.")]
        [SerializeField] float drinkDistance = 0.25f;

        [Tooltip("How far the can must tip off vertical, in degrees.")]
        [SerializeField] float drinkAngle = 45f;

        [Tooltip("Log grab and sip transitions.")]
        [SerializeField] bool logTransitions = true;

        XRGrabInteractable grab;
        bool wasDrinking;
        bool wasGrabbed;

        /// <summary>True while the can is held at the mouth and tipped back.</summary>
        public bool IsDrinking => wasDrinking;

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            if (!audioSource) audioSource = GetComponent<AudioSource>();
            if (!xrCamera && Camera.main) xrCamera = Camera.main.transform;
            if (!canOpening) Debug.LogWarning("[BeerDrink] canOpening is not assigned; drinking cannot be detected.", this);
        }

        void Update()
        {
            if (!canOpening || !xrCamera) return;

            bool isGrabbed = grab.isSelected;
            if (logTransitions && isGrabbed != wasGrabbed)
                Debug.Log(isGrabbed ? "[BeerDrink] Grabbed" : "[BeerDrink] Released", this);
            wasGrabbed = isGrabbed;

            // transform.up runs from the base to the lid, so this is the tilt off vertical.
            float angle = Vector3.Angle(transform.up, Vector3.up);
            float distance = Vector3.Distance(canOpening.position, xrCamera.position);

            bool isDrinking = isGrabbed && distance <= drinkDistance && angle >= drinkAngle;

            if (isDrinking && !wasDrinking)
            {
                // The clip is a couple of seconds long, so let it run to the end on its own
                // rather than cutting it off the moment the can dips back down.
                if (audioSource && audioSource.clip) audioSource.Play();
                if (logTransitions)
                    Debug.Log($"[BeerDrink] Drink started | distance={distance:F2} | angle={angle:F1}", this);
            }
            else if (!isDrinking && wasDrinking && logTransitions)
            {
                Debug.Log("[BeerDrink] Drink ended", this);
            }

            wasDrinking = isDrinking;
        }
    }
}
