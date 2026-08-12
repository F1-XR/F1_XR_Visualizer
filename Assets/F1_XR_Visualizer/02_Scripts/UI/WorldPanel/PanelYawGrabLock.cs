using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.UI.WorldPanel
{
    public sealed class PanelYawGrabLock : MonoBehaviour
    {
        [SerializeField] XRGrabInteractable grab;
        [SerializeField] Transform target;
        [SerializeField] Transform user;
        [SerializeField] bool faceOnlyWhileGrabbed;
        [SerializeField] bool lockUpright = true;
        [SerializeField] float rotationLerpSpeed = 18f;

        void Awake()
        {
            if (target == null)
                target = transform;

            if (grab == null)
                grab = GetComponent<XRGrabInteractable>();

            ResolveUser();
        }

        void LateUpdate()
        {
            UpdateRotation();
        }

        public void Configure(
            XRGrabInteractable panelGrab,
            Transform rotationTarget,
            bool onlyWhileGrabbed = true)
        {
            grab = panelGrab;
            target = rotationTarget != null
                ? rotationTarget
                : transform;
            faceOnlyWhileGrabbed = onlyWhileGrabbed;
            ResolveUser();
        }

        void UpdateRotation()
        {
            if (user == null)
                ResolveUser();

            if (user == null || target == null)
                return;

            if (faceOnlyWhileGrabbed && (grab == null || !grab.isSelected))
                return;

            var forward = target.position - user.position;
            if (lockUpright)
                forward = Vector3.ProjectOnPlane(forward, Vector3.up);

            if (forward.sqrMagnitude <= Mathf.Epsilon)
                return;

            var rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            target.rotation = rotationLerpSpeed <= 0f
                ? rotation
                : Quaternion.Slerp(target.rotation, rotation, GetLerpT(rotationLerpSpeed, Time.deltaTime));
        }

        void ResolveUser()
        {
            var mainCamera = Camera.main;
            if (mainCamera != null)
                user = mainCamera.transform;
        }

        static float GetLerpT(float speed, float deltaTime)
        {
            return speed <= 0f ? 1f : 1f - Mathf.Exp(-speed * deltaTime);
        }
    }
}
