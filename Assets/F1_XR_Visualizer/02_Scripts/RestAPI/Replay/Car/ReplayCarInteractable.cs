using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.RestAPI.Replay
{
    public sealed class ReplayCarInteractable : MonoBehaviour
    {
        ReplayCarView car;
        ReplayPlayer player;
        XRSimpleInteractable interactable;
        BoxCollider targetCollider;

        public void Configure(ReplayCarView carView, ReplayPlayer replayPlayer)
        {
            car = carView != null ? carView : GetComponent<ReplayCarView>();
            player = replayPlayer != null ? replayPlayer : FindAnyObjectByType<ReplayPlayer>();
            EnsureInteractionTarget();
        }

        void Awake()
        {
            Configure(GetComponent<ReplayCarView>(), null);
        }

        void OnEnable()
        {
            EnsureInteractionTarget();
            if (interactable == null)
                return;

            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
            interactable.activated.AddListener(OnActivated);
        }

        void OnDisable()
        {
            if (interactable != null)
            {
                interactable.hoverEntered.RemoveListener(OnHoverEntered);
                interactable.hoverExited.RemoveListener(OnHoverExited);
                interactable.activated.RemoveListener(OnActivated);
            }

            car?.SetHovered(false);
        }

        void EnsureInteractionTarget()
        {
            car ??= GetComponent<ReplayCarView>();

            if (targetCollider == null)
            {
                Transform existing = transform.Find("SelectionTarget");
                GameObject target = existing != null
                    ? existing.gameObject
                    : new GameObject("SelectionTarget");
                target.transform.SetParent(transform, false);
                targetCollider = target.GetComponent<BoxCollider>();
                if (targetCollider == null)
                    targetCollider = target.AddComponent<BoxCollider>();

                targetCollider.isTrigger = false;
                FitTargetCollider(targetCollider);
            }

            interactable ??= GetComponent<XRSimpleInteractable>();
            if (interactable == null)
                interactable = gameObject.AddComponent<XRSimpleInteractable>();

            if (!interactable.colliders.Contains(targetCollider))
            {
                bool reregister = interactable.isActiveAndEnabled;
                if (reregister)
                    interactable.enabled = false;

                interactable.colliders.Add(targetCollider);

                if (reregister)
                    interactable.enabled = true;
            }
        }

        void FitTargetCollider(BoxCollider collider)
        {
            bool hasBounds = false;
            Bounds localBounds = default;

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.transform.IsChildOf(collider.transform))
                    continue;

                Bounds bounds = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 world = bounds.center + Vector3.Scale(
                                bounds.extents,
                                new Vector3(x, y, z));
                            Vector3 local = transform.InverseTransformPoint(world);
                            if (!hasBounds)
                            {
                                localBounds = new Bounds(local, Vector3.zero);
                                hasBounds = true;
                            }
                            else
                            {
                                localBounds.Encapsulate(local);
                            }
                        }
                    }
                }
            }

            if (!hasBounds)
                localBounds = new Bounds(Vector3.zero, new Vector3(0.18f, 0.08f, 0.32f));

            Vector3 size = localBounds.size;
            size.x = Mathf.Max(0.18f, size.x * 1.35f);
            size.y = Mathf.Max(0.08f, size.y * 1.5f);
            size.z = Mathf.Max(0.32f, size.z * 1.35f);
            collider.center = localBounds.center;
            collider.size = size;
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (args.interactorObject is XRBaseInputInteractor inputInteractor)
                inputInteractor.allowHoveredActivate = true;

            car?.SetHovered(true);
        }

        void OnHoverExited(HoverExitEventArgs args)
        {
            car?.SetHovered(false);
        }

        void OnActivated(ActivateEventArgs args)
        {
            if (car == null)
                return;

            player ??= FindAnyObjectByType<ReplayPlayer>();
            player?.SetSelectedDriver(car.driverNumber);
        }
    }
}
