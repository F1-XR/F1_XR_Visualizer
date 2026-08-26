using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace F1XR.Doritos
{
    /// <summary>
    /// Doritos 봉지 칩 추출기. (구 물리 컨테이너 방식 폐기)
    ///
    /// 동작:
    /// - Awake: 레거시 물리 제거(PhysicsWalls/BagPhysics, 칩 Rigidbody/Collider) + 칩은 장식 메시로 고정.
    /// - 봉지(XRGrabInteractable)를 한 손으로 Grab 중일 때만 입구의 ChipExtractZone 활성.
    /// - 봉지를 잡은 손과 "반대쪽" Ray Interactor가 ExtractZone을 Select(호버 아님)하면
    ///   ChipSpawnPoint에서 ChipPrefab 1개 생성 + FixedChip 1개 숨김.
    /// - FixedChip 모두 소진되면 더 생성 안 함.
    ///
    /// 이름(DoritosChipPhysics)은 기존 씬 컴포넌트 바인딩 유지를 위해 그대로 둔다.
    /// </summary>
    public class DoritosChipPhysics : MonoBehaviour
    {
        [Header("References (비우면 Awake에서 자동 탐색/생성)")]
        [SerializeField] XRGrabInteractable bagGrab;
        [SerializeField] Renderer bagInsideRenderer;   // 입구/부피 기준 메시 (Bag_Inside)
        [SerializeField] XRSimpleInteractable extractZone;
        [SerializeField] Transform spawnPoint;

        [Header("Chip Prefab (비우면 FixedChip 복제로 대체)")]
        [SerializeField] GameObject chipPrefab;

        [Header("Spawn")]
        [SerializeField] float spawnInsetRatio = 0.12f;   // 입구에서 안쪽으로 얼마나
        [SerializeField] float mouthZoneHeightRatio = 0.22f; // 입구 존 두께(부피 대비)

        readonly List<GameObject> fixedChips = new List<GameObject>();
        int nextHideIndex;
        InteractorHandedness holderHand = InteractorHandedness.None;
        bool isGrabbed;

        void Awake()
        {
            StripLegacyPhysics();
            CollectFixedChips();
            EnsureRefs();
            BuildExtractionRig();
        }

        void OnEnable()
        {
            if (bagGrab != null)
            {
                bagGrab.selectEntered.AddListener(OnBagGrabbed);
                bagGrab.selectExited.AddListener(OnBagReleased);
            }
            if (extractZone != null)
            {
                extractZone.selectEntered.AddListener(OnZoneSelected);
                extractZone.hoverEntered.AddListener(OnZoneHover);
            }
            Debug.Log($"[Doritos] OnEnable bagGrab={(bagGrab!=null)} zone={(extractZone!=null)} spawn={(spawnPoint!=null)} prefab={(chipPrefab!=null)} chips={fixedChips.Count}");
        }

        void OnZoneHover(HoverEnterEventArgs a)
        {
            var h = (a.interactorObject as XRBaseInputInteractor)?.handedness ?? InteractorHandedness.None;
            Debug.Log($"[Doritos] ZONE HOVER by {h} (grabbed={isGrabbed} holder={holderHand})");
        }

        void OnDisable()
        {
            if (bagGrab != null)
            {
                bagGrab.selectEntered.RemoveListener(OnBagGrabbed);
                bagGrab.selectExited.RemoveListener(OnBagReleased);
            }
            if (extractZone != null)
                extractZone.selectEntered.RemoveListener(OnZoneSelected);
        }

        // --- 레거시 정리 -------------------------------------------------

        void StripLegacyPhysics()
        {
            // 물리벽 홀더 제거 (BagPhysics / PhysicsWalls 및 하위 Left/Right/Front/Back/BottomWall)
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c.name.Contains("Physics") || c.name.Contains("PhysicsWalls") || c.name.Contains("BagPhysics"))
                    Destroy(c.gameObject);
            }

            // 봉지 루트에 남은 Rigidbody는 Grab용으로 kinematic 유지, 나머지는 손대지 않음.
            var rb = GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
        }

        void CollectFixedChips()
        {
            fixedChips.Clear();
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.name.StartsWith("Chip_")) continue;
                var go = mf.gameObject;

                foreach (var col in go.GetComponents<Collider>()) Destroy(col);
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) Destroy(rb);

                fixedChips.Add(go);
            }
        }

        // --- 셋업 --------------------------------------------------------

        void EnsureRefs()
        {
            if (bagGrab == null) bagGrab = GetComponent<XRGrabInteractable>();

            if (bagInsideRenderer == null)
            {
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    if (r.name.Contains("Inside")) { bagInsideRenderer = r; break; }
                if (bagInsideRenderer == null)
                    bagInsideRenderer = GetComponentInChildren<Renderer>(true);
            }
        }

        void BuildExtractionRig()
        {
            // 봉지 입구(월드 상단 중심) 계산. Awake 1회 배치 후 봉지 자식으로 부모화해 함께 이동.
            Bounds b = bagInsideRenderer != null ? bagInsideRenderer.bounds
                                                 : new Bounds(transform.position, Vector3.one * 0.2f);
            Vector3 mouthCenter = new Vector3(b.center.x, b.max.y, b.center.z);

            if (spawnPoint == null)
            {
                var sp = new GameObject("ChipSpawnPoint").transform;
                sp.position = mouthCenter - Vector3.up * (b.size.y * spawnInsetRatio);
                sp.rotation = transform.rotation;
                sp.SetParent(transform, true);
                spawnPoint = sp;
            }

            if (extractZone == null)
            {
                var zoneGo = new GameObject("ChipExtractZone");
                zoneGo.transform.position = mouthCenter;
                zoneGo.transform.rotation = transform.rotation;
                zoneGo.transform.SetParent(transform, true);

                var box = zoneGo.AddComponent<BoxCollider>();
                // 로컬 크기: 입구 평면 전체 x/z, y는 얇게. 부모 스케일 보정.
                Vector3 ls = zoneGo.transform.lossyScale;
                float sx = Mathf.Approximately(ls.x, 0f) ? 1f : ls.x;
                float sy = Mathf.Approximately(ls.y, 0f) ? 1f : ls.y;
                float sz = Mathf.Approximately(ls.z, 0f) ? 1f : ls.z;
                box.size = new Vector3(b.size.x / sx, (b.size.y * mouthZoneHeightRatio) / sy, b.size.z / sz);
                box.center = Vector3.zero;
                // 물리벽 아님 — Ray Select 판정용. Solid로 두어 Ray Trigger 설정에 의존하지 않음.
                box.isTrigger = false;

                extractZone = zoneGo.AddComponent<XRSimpleInteractable>();
                zoneGo.SetActive(false);
            }
            else
            {
                extractZone.gameObject.SetActive(false);
            }
        }

        // --- 이벤트 ------------------------------------------------------

        void OnBagGrabbed(SelectEnterEventArgs args)
        {
            isGrabbed = true;
            holderHand = (args.interactorObject as XRBaseInputInteractor)?.handedness ?? InteractorHandedness.None;
            if (extractZone != null) extractZone.gameObject.SetActive(HasChipsLeft());
            Debug.Log($"[Doritos] BAG GRABBED by {holderHand}, zoneActive={extractZone!=null && extractZone.gameObject.activeSelf}");
        }

        void OnBagReleased(SelectExitEventArgs args)
        {
            isGrabbed = false;
            holderHand = InteractorHandedness.None;
            if (extractZone != null) extractZone.gameObject.SetActive(false);
        }

        void OnZoneSelected(SelectEnterEventArgs args)
        {
            var hand = (args.interactorObject as XRBaseInputInteractor)?.handedness ?? InteractorHandedness.None;
            Debug.Log($"[Doritos] ZONE SELECT by {hand} (grabbed={isGrabbed} holder={holderHand} chipsLeft={HasChipsLeft()})");

            if (!isGrabbed || !HasChipsLeft()) return;

            // 봉지 잡은 손과 같은 손 Ray는 금지. 반대쪽 손만 허용.
            if (hand == InteractorHandedness.None || hand == holderHand) { Debug.Log("[Doritos] rejected: same/none hand"); return; }

            HideNextFixedChip();
            var chip = SpawnChip();

            // 스폰 즉시 zone 비활성 → 트리거를 누르고 있어도 재select/재스폰 원천 차단 (1 Select = 1 과자).
            if (extractZone != null) extractZone.gameObject.SetActive(false);

            // 뽑은 즉시 이 Ray(반대손)로 과자를 잡게 넘김 → 레이에 딸려나옴.
            if (chip != null && args.interactorObject is UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor rayInteractor)
                StartCoroutine(HandOffToRay(rayInteractor, chip));
        }

        System.Collections.IEnumerator HandOffToRay(
            UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor interactor, GameObject chip)
        {
            yield return null; // 현재 zone select 이벤트 처리가 끝난 뒤 넘긴다.

            var grab = chip != null ? chip.GetComponent<XRGrabInteractable>() : null;
            var mgr = bagGrab != null ? bagGrab.interactionManager : null;
            if (grab == null || mgr == null) yield break; // 자동 grab 불가 → 과자는 그냥 자연 낙하

            // 순간이동 튕김 방지: 스폰 위치에서 손까지 확 당겨질 때 velocity 폭발 안 하도록 Instantaneous.
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;

            // Ray가 zone 비활성으로 이미 free지만, 남은 select 있으면 정리 후 과자로 전환.
            if (interactor.hasSelection)
                mgr.SelectExit(interactor, interactor.interactablesSelected[0]);
            mgr.SelectEnter(interactor, (UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)grab);

            // 과자를 놓으면(트리거 뗌) 봉지를 여전히 잡고 있고 칩이 남았으면 zone 재활성 → 다음 뽑기 허용.
            grab.selectExited.AddListener(_ =>
            {
                if (isGrabbed && HasChipsLeft() && extractZone != null)
                    extractZone.gameObject.SetActive(true);
            });

            // grab이 실제로 걸렸는지 한 프레임 뒤 확인. 실패했으면 zone 재활성(과자는 물리로 알아서 떨어짐).
            yield return null;
            if (grab == null || !grab.isSelected)
            {
                if (isGrabbed && HasChipsLeft() && extractZone != null)
                    extractZone.gameObject.SetActive(true);
            }
        }

        // --- 추출 --------------------------------------------------------

        bool HasChipsLeft() => nextHideIndex < fixedChips.Count;

        void HideNextFixedChip()
        {
            while (nextHideIndex < fixedChips.Count)
            {
                var chip = fixedChips[nextHideIndex++];
                if (chip != null) { chip.SetActive(false); return; }
            }
        }

        GameObject SpawnChip()
        {
            Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
            Quaternion rot = spawnPoint != null ? spawnPoint.rotation : transform.rotation;

            GameObject chip = chipPrefab != null
                ? Instantiate(chipPrefab, pos, rot)
                : CloneFixedChip(pos, rot);

            if (chip == null) { Debug.LogWarning("[Doritos] SpawnChip: chip null"); return null; }

            // 프리팹 스폰 시 크기를 봉지 안 FixedChip과 동일하게 맞춤 (부모 없이 생성돼 스케일이 달라짐).
            if (chipPrefab != null)
            {
                foreach (var f in fixedChips)
                    if (f != null) { chip.transform.localScale = f.transform.lossyScale; break; }
            }

            // 스폰 속도만 0으로 (kinematic은 건드리지 않는다 — XRGrab 원상복구 로직이 오염되면 놓아도 안 떨어짐).
            var srb = chip.GetComponent<Rigidbody>();
            if (srb != null) { srb.linearVelocity = Vector3.zero; srb.angularVelocity = Vector3.zero; }

            chip.SetActive(true);
            var r = chip.GetComponentInChildren<Renderer>();
            string rend = r != null ? (r.enabled + " size=" + r.bounds.size) : "NONE";
            Debug.Log("[Doritos] SPAWNED " + chip.name + " pos=" + chip.transform.position + " lossy=" + chip.transform.lossyScale + " active=" + chip.activeInHierarchy + " rend=" + rend);
            return chip;
        }

        /// <summary>ChipPrefab 미지정 시 FixedChip 메시를 복제해 Grab 가능한 실제 칩 생성.</summary>
        GameObject CloneFixedChip(Vector3 pos, Quaternion rot)
        {
            GameObject src = null;
            foreach (var c in fixedChips) { if (c != null) { src = c; break; } }
            if (src == null) return null;

            var chip = Instantiate(src, pos, rot);
            chip.name = "Chip_Extracted";
            chip.transform.SetParent(null, true);
            chip.transform.localScale = src.transform.lossyScale;

            var mf = chip.GetComponent<MeshFilter>();
            var box = chip.AddComponent<BoxCollider>();
            if (mf != null && mf.sharedMesh != null)
            {
                box.center = mf.sharedMesh.bounds.center;
                box.size = mf.sharedMesh.bounds.size;
            }

            var rb = chip.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.mass = 0.05f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var grab = chip.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.throwOnDetach = true;

            return chip;
        }

        // 데모/검증: 에디터에서 우클릭 → Test Extract One
        [ContextMenu("Test Extract One")]
        void TestExtractOne()
        {
            if (!HasChipsLeft()) { Debug.Log("[Doritos] FixedChip 소진됨."); return; }
            HideNextFixedChip();
            SpawnChip();
        }
    }
}
