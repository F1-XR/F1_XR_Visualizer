using UnityEngine;
using F1XR.Interaction.Input;

namespace F1XR.Interaction.World
{
    /// <summary>
    /// 양손 컨트롤러를 좌우(X축)로 가까이 모으면 핸들이 반투명 프리뷰로 뜨고, 그 상태에서 양손
    /// 트리거를 동시에 누르면 원래 재질로 확정 생성됩니다. 프리뷰 동안 핸들은 두 손 중간을
    /// 따라다니고, 손을 다시 벌리면 사라집니다.
    ///
    /// 씬 어디든 빈 오브젝트에 붙이고 Handle / 좌우 컨트롤러만 연결하면 됩니다.
    /// 재질 교체 방식이라 셰이더 종류와 무관하게 동작합니다(원본 재질은 확정 시 복원).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HandleSummonGesture : MonoBehaviour
    {
        public enum State { Hidden, Ghost, Solid }

        [Header("References")]
        [Tooltip("소환할 핸들 프리팹. 첫 소환 때 한 번만 생성해 재사용합니다.")]
        [SerializeField] GameObject handlePrefab;
        [Tooltip("비우면 씬에서 이름(Left Controller)으로 자동 연결.")]
        [SerializeField] Transform leftHand;
        [Tooltip("비우면 씬에서 이름(Right Controller)으로 자동 연결.")]
        [SerializeField] Transform rightHand;
        [Tooltip("좌우 판정 기준이 되는 머리(카메라). 비우면 Camera.main, 그것도 없으면 월드 X축.")]
        [SerializeField] Transform head;
        [Tooltip("프리뷰용 반투명 재질. 비우면 F1XR/PreviewTransparentURP 로 런타임 생성.")]
        [SerializeField] Material ghostMaterial;

        [Header("Gesture")]
        [Tooltip("두 손의 좌우(X) 간격이 이 값(m) 이하로 좁혀지면 프리뷰가 뜹니다.")]
        [SerializeField, Min(0.01f)] float summonDistance = 0.22f;
        [Tooltip("프리뷰 상태에서 좌우 간격이 이 값(m)을 넘으면 다시 사라집니다. 소환 거리보다 크게 두어 깜빡임 방지.")]
        [SerializeField, Min(0.01f)] float dismissDistance = 0.40f;
        [Tooltip("확정에 쓰는 버튼. 기본 트리거, 양손 동시.")]
        [SerializeField] MorphHoldButton confirmButton = MorphHoldButton.Trigger;

        [Header("Placement (프리뷰 동안만 적용)")]
        [Tooltip("두 손 중점 기준 오프셋. 핸들 자기 좌표계 기준.")]
        [SerializeField] Vector3 spawnOffset = Vector3.zero;
        [Tooltip("사용자를 바라보게 맞춘 뒤 추가로 돌릴 yaw(도).")]
        [SerializeField] float yawOffset;
        [Tooltip("프리뷰 동안 콜라이더를 꺼서 잡히지 않게 합니다.")]
        [SerializeField] bool disableCollidersWhileGhost = true;

        public State Current => state;

        State state = State.Hidden;
        GameObject handle;          // 프리팹에서 만든 인스턴스. 첫 소환 때 생성.
        Quaternion restRotation;
        Renderer[] renderers;
        Material[][] originalMaterials;
        Collider[] disabledColliders;

        void Awake()
        {
            // 씬마다 드래그하지 않아도 되도록, 비어 있는 참조만 이름으로 자동 연결.
            if (head == null && Camera.main != null)
                head = Camera.main.transform;
            if (leftHand == null)
                leftHand = Find("Left Controller");
            if (rightHand == null)
                rightHand = Find("Right Controller");
        }

        static Transform Find(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.transform : null;
        }

        void Update()
        {
            if (handlePrefab == null || leftHand == null || rightHand == null || state == State.Solid)
                return;

            float lateral = LateralDistance();

            if (state == State.Hidden)
            {
                if (lateral <= summonDistance)
                    ShowGhost();
                return;
            }

            if (lateral > dismissDistance)
            {
                Hide();
                return;
            }

            Place();

            if (XRControllerButton.IsPressed(confirmButton, true) &&
                XRControllerButton.IsPressed(confirmButton, false))
                Materialize();
        }

        /// <summary>머리 기준 좌우축(없으면 월드 X) 위로 투영한 두 손 사이 거리.</summary>
        float LateralDistance()
        {
            Vector3 axis = head != null
                ? Vector3.ProjectOnPlane(head.right, Vector3.up).normalized
                : Vector3.right;
            if (axis.sqrMagnitude < 1e-6f)
                axis = Vector3.right;

            return Mathf.Abs(Vector3.Dot(rightHand.position - leftHand.position, axis));
        }

        /// <summary>프리팹 인스턴스를 한 번만 만들고, 원본 재질을 캐시해 둡니다.</summary>
        void EnsureInstance()
        {
            if (handle != null)
                return;

            handle = Instantiate(handlePrefab);
            handle.name = handlePrefab.name;
            restRotation = handlePrefab.transform.rotation;

            renderers = handle.GetComponentsInChildren<Renderer>(true);
            originalMaterials = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
                originalMaterials[i] = renderers[i].sharedMaterials;

            handle.SetActive(false);
        }

        void ShowGhost()
        {
            EnsureInstance();
            state = State.Ghost;
            handle.SetActive(true);
            Place();
            ApplyGhostMaterial();

            if (disableCollidersWhileGhost)
            {
                var all = handle.GetComponentsInChildren<Collider>(true);
                int n = 0;
                foreach (var c in all)
                    if (c.enabled) n++;

                disabledColliders = new Collider[n];
                n = 0;
                foreach (var c in all)
                {
                    if (!c.enabled) continue;
                    c.enabled = false;
                    disabledColliders[n++] = c;
                }
            }
        }

        void Materialize()
        {
            state = State.Solid;
            RestoreMaterials();
            RestoreColliders();
        }

        /// <summary>다시 숨깁니다. 확정된 핸들을 치울 때도 외부에서 호출하세요.</summary>
        public void Dismiss() => Hide();

        void Hide()
        {
            state = State.Hidden;
            RestoreMaterials();
            RestoreColliders();
            if (handle != null)
                handle.SetActive(false);
        }

        /// <summary>두 손 중점에 놓고, 원래 기울기는 유지한 채 yaw 만 사용자 정면으로 돌립니다.</summary>
        void Place()
        {
            var t = handle.transform;
            t.rotation = Quaternion.Euler(0f, UserFacingYaw() - restRotation.eulerAngles.y + yawOffset, 0f) * restRotation;
            t.position = Vector3.Lerp(leftHand.position, rightHand.position, 0.5f) + t.TransformVector(spawnOffset);
        }

        float UserFacingYaw()
        {
            Vector3 fwd = head != null ? Vector3.ProjectOnPlane(head.forward, Vector3.up) : Vector3.zero;
            if (fwd.sqrMagnitude < 1e-6f)
                return 0f;
            return Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        }

        void ApplyGhostMaterial()
        {
            if (ghostMaterial == null)
            {
                var shader = Shader.Find("F1XR/PreviewTransparentURP");
                if (shader == null)
                    return;
                ghostMaterial = new Material(shader);
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                var swap = new Material[renderers[i].sharedMaterials.Length];
                for (int m = 0; m < swap.Length; m++)
                    swap[m] = ghostMaterial;
                renderers[i].sharedMaterials = swap;
            }
        }

        void RestoreMaterials()
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].sharedMaterials = originalMaterials[i];
            }
        }

        void RestoreColliders()
        {
            if (disabledColliders == null) return;
            foreach (var c in disabledColliders)
                if (c != null) c.enabled = true;
            disabledColliders = null;
        }
    }
}
