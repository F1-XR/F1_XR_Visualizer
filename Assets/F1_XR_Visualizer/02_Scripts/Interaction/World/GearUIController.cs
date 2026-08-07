using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace F1XR.Interaction.World
{
    /// <summary>
    /// Owns the stack of <see cref="GearUIItem"/>s and turns the lever's state (direction + tilt
    /// strength) into visuals. It never reads input itself - <see cref="GearShiftController"/> pushes
    /// state in via <see cref="Open"/>, <see cref="Close"/>, <see cref="UpdateHover"/> and
    /// <see cref="Select"/>.
    ///
    /// Responsibilities (spec 18.2):
    ///   * open the menu with a staggered front-to-back reveal, close it with the reverse,
    ///   * route the hovered direction to the matching item and drive its pop by the tilt strength,
    ///   * dim the other items, run the one-shot selection pop, keep the selected white fill,
    ///   * yaw-only face the camera so the card never pitches or rolls.
    /// The item list is ordered FRONT (index 0) to BACK; the reveal collapses onto the front slot.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GearUIController : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("Root object toggled on/off with the menu (GearUIRoot).")]
        [SerializeField] GameObject uiRoot;
        [Tooltip("Menu items, ordered FRONT-most first. Order drives the reveal stagger.")]
        [SerializeField] List<GearUIItem> items = new List<GearUIItem>();

        [Header("Camera facing")]
        [Tooltip("Anchor that yaws to face the viewer. Leave null to skip. Usually the UIAnchor.")]
        [SerializeField] Transform yawAnchor;
        [SerializeField] Camera targetCamera;
        [SerializeField] bool faceCameraYaw = true;
        [Tooltip("Higher = snappier yaw. The lever holds still while grabbed so the card stays put.")]
        [SerializeField, Min(0f)] float yawResponsiveness = 6f;

        [Header("Reveal timing (open)")]
        [SerializeField, Min(0.01f)] float itemRevealTime = 0.2f;
        [SerializeField, Min(0f)] float itemStagger = 0.05f;

        [Header("Reveal timing (close)")]
        [SerializeField, Min(0.01f)] float itemCloseTime = 0.18f;
        [SerializeField, Min(0f)] float itemCloseStagger = 0.04f;

        [Header("Selection pop")]
        [SerializeField, Min(0.01f)] float selectPopTime = 0.18f;
        [SerializeField, Range(0.5f, 1f)] float selectSquash = 0.92f;
        [SerializeField, Range(1f, 1.3f)] float selectRebound = 1.05f;

        [Header("Detent 강조 전환")]
        [Tooltip("현재 단 카드가 앞으로/뒤로 전환되는 속도. 클수록 즉각적.")]
        [SerializeField, Min(0.1f)] float highlightSpeed = 10f;

        public bool IsOpen => isOpen;

        /// <summary>items 리스트에서 이 카드의 단(gear) 번호. 없으면 -1.</summary>
        public int IndexOf(GearUIItem item) => items.IndexOf(item);

        /// <summary>
        /// 이름으로 단(gear) 번호를 찾습니다. 기어봉이 런타임에 프리팹으로 생성되면(컨트롤러 morph)
        /// 씬에 저장해 둔 카드 참조는 프리팹 원본을 가리키므로, 인스턴스 쪽은 이름으로 맞춥니다.
        /// </summary>
        public int IndexOfName(string itemName)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && items[i].name == itemName)
                    return i;
            return -1;
        }

        bool isOpen;
        Coroutine revealRoutine;
        float[] gearActiveness;
        readonly Dictionary<GearDirection, Coroutine> popRoutines = new Dictionary<GearDirection, Coroutine>();

        void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            // Force each item to cache its authored slot, then hide the menu.
            foreach (var item in items)
                if (item != null)
                    item.ResetToDefault();

            if (uiRoot != null)
                uiRoot.SetActive(false);
        }

        /// <summary>Show the menu (Preview / grab). Idempotent - a second call while open does nothing.</summary>
        public void Open()
        {
            if (isOpen)
                return;

            isOpen = true;
            gearActiveness = null; // 강조 상태 리셋
            if (uiRoot != null)
                uiRoot.SetActive(true);

            StopReveal();
            revealRoutine = StartCoroutine(RevealRoutine());
        }

        /// <summary>Collapse the menu back onto the front slot and hide it.</summary>
        public void Close()
        {
            if (!isOpen)
                return;

            isOpen = false;
            StopReveal();
            revealRoutine = StartCoroutine(CloseRoutine());
        }

        /// <summary>
        /// Live hover update, called every frame while grabbed. The item bound to
        /// <paramref name="direction"/> pops out by <paramref name="hoverProgress"/> (0..1); the rest
        /// settle at their slot, dimmed while a direction is active. Ignored during the reveal so the
        /// two animations don't fight.
        /// </summary>
        public void UpdateHover(GearDirection direction, float hoverProgress)
        {
            if (!isOpen || revealRoutine != null)
                return;

            Vector3 camDirLocal = CameraDirLocal();
            bool anyActive = direction != GearDirection.None;

            foreach (var item in items)
            {
                if (item == null || popRoutines.ContainsKey(item.Direction))
                    continue;

                bool isHovered = anyActive && item.Direction == direction;
                item.ApplyHover(isHovered ? hoverProgress : 0f, camDirLocal, dim: anyActive && !isHovered);
            }
        }

        /// <summary>Fire the one-shot selection feedback for a direction (spec 15).</summary>
        public void Select(GearDirection direction)
        {
            foreach (var item in items)
                if (item != null)
                    item.SetSelected(item.Direction == direction);

            var target = Find(direction);
            if (target == null)
                return;

            if (popRoutines.TryGetValue(direction, out var running) && running != null)
                StopCoroutine(running);
            popRoutines[direction] = StartCoroutine(PopRoutine(target));
        }

        /// <summary>
        /// Detent(앞뒤 4단) 연동용. 각 카드는 자기 슬롯 위치를 유지하고, 현재 단 카드만 Scale/Alpha/Z깊이/
        /// 렌더 우선순위로 앞으로 올라옵니다(위치 이동 없음). activeness 를 부드럽게 보간해 단이 바뀌면
        /// 이전 카드는 자기 자리에서 뒤로, 새 카드는 자기 자리에서 앞으로 전환됩니다.
        /// items 리스트 순서 = 단 순서(0번 = 1단).
        /// </summary>
        public void SetActiveGear(int gear)
        {
            if (!isOpen || revealRoutine != null)
                return;

            if (gearActiveness == null || gearActiveness.Length != items.Count)
                gearActiveness = new float[items.Count];

            float t = 1f - Mathf.Exp(-highlightSpeed * Time.deltaTime);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null)
                    continue;
                float target = i == gear ? 1f : 0f;
                gearActiveness[i] = Mathf.Lerp(gearActiveness[i], target, t);
                items[i].ApplyGearHighlight(gearActiveness[i]);
            }
        }

        void LateUpdate()
        {
            if (!faceCameraYaw || yawAnchor == null || targetCamera == null)
                return;

            // Yaw-only look: flatten the camera direction onto the horizontal plane so the card never
            // pitches or rolls, then ease toward it.
            Vector3 toCam = targetCamera.transform.position - yawAnchor.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude < 1e-6f)
                return;

            Quaternion desired = Quaternion.LookRotation(toCam.normalized, Vector3.up);
            float t = 1f - Mathf.Exp(-yawResponsiveness * Time.deltaTime);
            yawAnchor.rotation = Quaternion.Slerp(yawAnchor.rotation, desired, t);
        }

        IEnumerator RevealRoutine()
        {
            Vector3 frontSlot = FrontSlot();
            int n = items.Count;

            foreach (var item in items)
                if (item != null)
                    item.SetReveal(0f, frontSlot);

            float total = itemRevealTime + itemStagger * Mathf.Max(0, n - 1);
            float elapsed = 0f;
            while (elapsed < total)
            {
                elapsed += Time.deltaTime;
                for (int i = 0; i < n; i++)
                {
                    if (items[i] == null)
                        continue;
                    float start = i * itemStagger;
                    float p = Mathf.Clamp01((elapsed - start) / itemRevealTime);
                    items[i].SetReveal(EaseOut(p), frontSlot);
                }
                yield return null;
            }

            foreach (var item in items)
                if (item != null)
                    item.SetReveal(1f, frontSlot);

            revealRoutine = null;
        }

        IEnumerator CloseRoutine()
        {
            Vector3 frontSlot = FrontSlot();
            int n = items.Count;

            // Reverse order: back-most items collapse toward the front first.
            float total = itemCloseTime + itemCloseStagger * Mathf.Max(0, n - 1);
            float elapsed = 0f;
            while (elapsed < total)
            {
                elapsed += Time.deltaTime;
                for (int i = 0; i < n; i++)
                {
                    if (items[i] == null)
                        continue;
                    float start = (n - 1 - i) * itemCloseStagger;
                    float p = Mathf.Clamp01((elapsed - start) / itemCloseTime);
                    items[i].SetReveal(1f - EaseOut(p), frontSlot);
                }
                yield return null;
            }

            foreach (var item in items)
            {
                if (item == null)
                    continue;
                item.SetSelected(false);
                item.ResetToDefault();
            }

            if (uiRoot != null)
                uiRoot.SetActive(false);
            revealRoutine = null;
        }

        IEnumerator PopRoutine(GearUIItem item)
        {
            // squash -> rebound -> settle
            float half = selectPopTime * 0.5f;
            float t = 0f;
            while (t < half)
            {
                t += Time.deltaTime;
                item.SetScaleMultiplier(Mathf.Lerp(1f, selectSquash, t / half));
                yield return null;
            }
            t = 0f;
            while (t < half)
            {
                t += Time.deltaTime;
                item.SetScaleMultiplier(Mathf.Lerp(selectSquash, selectRebound, t / half));
                yield return null;
            }
            item.SetScaleMultiplier(1f);
            popRoutines.Remove(item.Direction);
        }

        void StopReveal()
        {
            if (revealRoutine != null)
            {
                StopCoroutine(revealRoutine);
                revealRoutine = null;
            }
        }

        Vector3 FrontSlot() => items.Count > 0 && items[0] != null ? items[0].DefaultLocalPosition : Vector3.zero;

        GearUIItem Find(GearDirection direction)
        {
            foreach (var item in items)
                if (item != null && item.Direction == direction)
                    return item;
            return null;
        }

        Vector3 CameraDirLocal()
        {
            if (targetCamera == null)
                return Vector3.zero;

            Transform space = uiRoot != null ? uiRoot.transform : transform;
            Vector3 world = (targetCamera.transform.position - space.position).normalized;
            return space.InverseTransformDirection(world);
        }

        static float EaseOut(float x) => 1f - Mathf.Pow(1f - Mathf.Clamp01(x), 3f);
    }
}
