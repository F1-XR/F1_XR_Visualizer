using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using F1XR.Interaction.Input;
using F1XR.Interaction.World;

namespace F1XR.Debugging
{
    public sealed class ScaleDebugLogger : MonoBehaviour
    {
        [SerializeField] ScaleController scaleController;
        [SerializeField] Transform target;
        [SerializeField] float rayDistance = 20f;
        [SerializeField, Min(1)] int logEveryNthFrame = 2;
        [SerializeField, Min(1)] int maxLines = 400;

        static readonly List<InputDevice> Devices = new();

        XRBaseInputInteractor left;
        XRBaseInputInteractor right;
        XRHandSubsystem handSubsystem;
        bool wasScaling;
        int frameCounter;
        int lineCount;

        void Awake()
        {
            if (scaleController == null)
                scaleController = GetComponent<ScaleController>();

            if (target == null)
                target = transform;
        }

        void OnEnable()
        {
            handSubsystem = XRHandInput.FindRunningSubsystem();
            lineCount = 0;
            Debug.Log("[ScaleDbg] logger enabled");
        }

        void Update()
        {
            if (lineCount >= maxLines)
                return;

            var scaling = scaleController != null && scaleController.IsScaling;
            if (scaling != wasScaling)
            {
                wasScaling = scaling;
                Log("IsScaling -> " + scaling + "   rootScale=" + target.localScale.x.ToString("F6"));
            }

            var leftHeld = TriggerHeld(XRNode.LeftHand);
            var rightHeld = TriggerHeld(XRNode.RightHand);
            if (!leftHeld || !rightHeld)
                return;

            FindInteractors();

            var leftHit = Probe(left, out var leftPoint, out var leftOrigin);
            var rightHit = Probe(right, out var rightPoint, out var rightOrigin);

            var hitDistance = leftPoint.HasValue && rightPoint.HasValue
                ? Vector3.Distance(leftPoint.Value, rightPoint.Value)
                : -1f;
            var originDistance = Vector3.Distance(leftOrigin, rightOrigin);

            if (handSubsystem == null || !handSubsystem.running)
                handSubsystem = XRHandInput.FindRunningSubsystem();

            var bothHandsTracked = handSubsystem != null &&
                handSubsystem.leftHand.isTracked &&
                handSubsystem.rightHand.isTracked;

            frameCounter++;
            if (frameCounter % logEveryNthFrame != 0)
                return;

            Log("scaling=" + scaling
                + " handsTracked=" + bothHandsTracked
                + " rootScale=" + target.localScale.x.ToString("F6")
                + " | hitDist=" + hitDistance.ToString("F4")
                + " originDist=" + originDistance.ToString("F4")
                + " | L=" + leftHit
                + " R=" + rightHit);
        }

        void Log(string message)
        {
            lineCount++;
            Debug.Log("[ScaleDbg] f" + Time.frameCount + " " + message);
            if (lineCount == maxLines)
                Debug.Log("[ScaleDbg] line limit reached, stopping");
        }

        void FindInteractors()
        {
            if (left != null && right != null)
                return;

            foreach (var interactor in FindObjectsByType<XRBaseInputInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (interactor is not IXRRayProvider)
                    continue;

                if (interactor.name.Contains("Left"))
                    left = interactor;
                else if (interactor.name.Contains("Right"))
                    right = interactor;
            }
        }

        string Probe(XRBaseInputInteractor interactor, out Vector3? point, out Vector3 origin)
        {
            point = null;
            origin = Vector3.zero;

            if (interactor is not IXRRayProvider rayProvider)
                return "<no ray>";

            var rayOrigin = rayProvider.GetOrCreateRayOrigin();
            if (rayOrigin == null)
                return "<no origin>";

            origin = rayOrigin.position;

            var hits = Physics.RaycastAll(rayOrigin.position, rayOrigin.forward, rayDistance, ~0, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            string name = "<miss>";

            foreach (var hit in hits)
            {
                if (hit.distance >= best)
                    continue;

                var onTarget = hit.transform == target || hit.transform.IsChildOf(target);
                if (!onTarget)
                    continue;

                best = hit.distance;
                point = hit.point;
                name = hit.collider.name + "@" + hit.distance.ToString("F3");
            }

            return name;
        }

        static bool TriggerHeld(XRNode node)
        {
            Devices.Clear();
            InputDevices.GetDevicesAtXRNode(node, Devices);

            foreach (var device in Devices)
            {
                if (device.TryGetFeatureValue(CommonUsages.triggerButton, out var pressed) && pressed)
                    return true;
            }

            return false;
        }
    }
}
