using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.Doritos
{
    /// <summary>
    /// 봉지를 잡으면 두께축으로 살짝 납작해져 "쥔 것처럼" 보이게 한다. 놓으면 복원.
    /// mesh 정점은 건드리지 않고 Bag transform 스케일만 눌러 안전하다.
    /// </summary>
    public sealed class BagGrabCrumple : MonoBehaviour
    {
        [SerializeField] XRGrabInteractable grab;
        [SerializeField] Transform bagTransform;          // 비우면 자식 "Bag"
        [SerializeField, Range(0.3f, 1f)] float squash = 0.7f;   // 두께축(얇은 축) 눌림 비율
        [SerializeField] float speed = 12f;               // 눌림/복원 속도

        Vector3 origScale;
        int thinAxis;      // 0=x,1=y,2=z 중 가장 얇은 축
        bool grabbed;

        void Awake()
        {
            if (grab == null) grab = GetComponent<XRGrabInteractable>();
            if (bagTransform == null)
            {
                var t = transform.Find("Bag");
                bagTransform = t != null ? t : transform;
            }
            origScale = bagTransform.localScale;

            // 가장 얇은 축을 눌린다 (봉지 두께 방향).
            var mf = bagTransform.GetComponent<MeshFilter>();
            Vector3 sz = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size : Vector3.one;
            thinAxis = (sz.x <= sz.y && sz.x <= sz.z) ? 0 : (sz.y <= sz.z ? 1 : 2);
        }

        void OnEnable()
        {
            if (grab == null) return;
            grab.selectEntered.AddListener(OnGrab);
            grab.selectExited.AddListener(OnRelease);
        }

        void OnDisable()
        {
            if (grab == null) return;
            grab.selectEntered.RemoveListener(OnGrab);
            grab.selectExited.RemoveListener(OnRelease);
        }

        void OnGrab(SelectEnterEventArgs a) => grabbed = true;
        void OnRelease(SelectExitEventArgs a) => grabbed = false;

        void Update()
        {
            Vector3 target = origScale;
            if (grabbed) target[thinAxis] = origScale[thinAxis] * squash;

            if ((bagTransform.localScale - target).sqrMagnitude < 1e-8f) return;
            bagTransform.localScale = Vector3.Lerp(bagTransform.localScale, target, speed * Time.deltaTime);
        }
    }
}
