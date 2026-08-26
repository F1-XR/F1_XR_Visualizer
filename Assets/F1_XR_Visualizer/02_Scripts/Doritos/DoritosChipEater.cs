using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.Doritos
{
    /// <summary>
    /// 잡은 과자를 카메라(입) 가까이 가져가면 삼등분 나면서 그 자리에서 fade out 된다.
    /// 캔맥주 BeerDrinkDetector 패턴: 잡힘 + 거리 임계 → 1회 발동.
    /// 스폰되는 ChipPrefab에 붙어서 각 과자가 스스로 처리한다.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class DoritosChipEater : MonoBehaviour
    {
        [SerializeField] Transform xrCamera;              // 비우면 Camera.main
        [SerializeField] float eatDistance = 0.22f;       // 입 근처 판정 거리(m)
        [SerializeField] int pieceCount = 3;              // 삼등분
        [SerializeField, Range(0f, 1f)] float pieceScale = 0.6f;
        [SerializeField] float pieceHold = 0.35f;         // 삼등분 후 부스러기까지 간격
        [SerializeField] int crumbCount = 12;             // 부스러기 개수
        [SerializeField, Range(0f, 1f)] float crumbScale = 0.16f;
        [SerializeField] float fadeTime = 0.6f;           // fade out 시간
        [SerializeField] AudioClip crunchClip;
        [SerializeField] AudioSource audioSource;

        XRGrabInteractable grab;
        bool eaten;

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            if (xrCamera == null && Camera.main != null) xrCamera = Camera.main.transform;
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
        }

        void Update()
        {
            if (eaten || xrCamera == null || grab == null || !grab.isSelected) return;

            if (Vector3.Distance(transform.position, xrCamera.position) <= eatDistance)
                Eat();
        }

        void Eat()
        {
            eaten = true;
            StartCoroutine(EatSequence());
        }

        System.Collections.IEnumerator EatSequence()
        {
            var mf = GetComponent<MeshFilter>();
            var mr = GetComponent<MeshRenderer>();
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            Material mat = mr != null ? mr.sharedMaterial : null;

            Vector3 s = transform.lossyScale;

            // 원본 과자는 숨기고 잡기 해제 (코루틴 유지 위해 파괴는 마지막).
            if (mr != null) mr.enabled = false;
            foreach (var col in GetComponents<Collider>()) col.enabled = false;
            if (grab != null && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.SelectExit(grab.firstInteractorSelecting, grab);

            var debris = new List<GameObject>();

            // 1단계: 삼등분 — 중심에서 방사형으로 살짝 벌어진 조각. 물리로 안 떨어지고 제자리 고정.
            if (mesh != null)
            {
                for (int i = 0; i < pieceCount; i++)
                {
                    float ang = (360f / Mathf.Max(1, pieceCount)) * i + Random.Range(-15f, 15f);
                    Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                    debris.Add(SpawnStatic(mesh, mat, transform.position + dir * (s.x * 0.15f),
                        transform.rotation * Quaternion.AngleAxis(Random.Range(-20f, 20f), Random.onUnitSphere), s * pieceScale));
                }
            }

            // 삼등분되는 순간 crunch 소리.
            if (crunchClip != null && audioSource != null)
                audioSource.PlayOneShot(crunchClip);

            // 2단계: 잠깐 뒤 부스러기가 조각들 자리에 생긴다(제자리, 안 떨어짐).
            yield return new WaitForSeconds(pieceHold);

            if (mesh != null)
            {
                for (int i = 0; i < crumbCount; i++)
                {
                    Vector3 basePos = debris.Count > 0 ? debris[i % Mathf.Max(1, pieceCount)].transform.position : transform.position;
                    Vector3 jitter = Random.insideUnitSphere * (s.x * 0.25f);
                    debris.Add(SpawnStatic(mesh, mat, basePos + jitter, Random.rotationUniform, s * crumbScale));
                }
            }

            // 3단계: 조각 + 부스러기 전부 제자리에서 fade out.
            float t = 0f;
            var renderers = new List<Renderer>();
            foreach (var d in debris) if (d != null) { var r = d.GetComponent<Renderer>(); if (r != null) renderers.Add(r); }
            while (t < fadeTime)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(1f - t / fadeTime);
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    var m = r.material;
                    bool urp = m.HasProperty("_BaseColor");
                    Color c = urp ? m.GetColor("_BaseColor") : m.color;
                    c.a = a;
                    if (urp) m.SetColor("_BaseColor", c); else m.color = c;
                }
                yield return null;
            }

            foreach (var d in debris) if (d != null) Destroy(d);
            Destroy(gameObject);
        }

        /// <summary>물리 없이 제자리 고정된 조각. fade 위해 material을 Transparent로 전환한 인스턴스 사용.</summary>
        GameObject SpawnStatic(Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = new GameObject("ChipDebris");
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            MakeTransparent(r.material); // material 인스턴스화 + 알파 블렌드 가능하게
            return go;
        }

        /// <summary>URP Lit/Unlit 머티리얼을 런타임에 Transparent 표면으로 전환 (fade out용).</summary>
        static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}
