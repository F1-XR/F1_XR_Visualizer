using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using F1XR.Interaction.Input;

namespace F1XR.Interaction.Deformation
{
    public sealed class StretchySphere : MonoBehaviour
    {
        [SerializeField] Transform sphere;
        [SerializeField] MeshFilter meshFilter;
        [SerializeField] float pinchStartDistance = 0.035f;
        [SerializeField] float pinchEndDistance = 0.05f;
        [SerializeField] float contactSkin = 0.015f;
        [SerializeField] float maxDentDepth = 0.085f;
        [SerializeField] float dentWidth = 0.34f;
        [SerializeField] float bulgeWidth = 0.22f;
        [SerializeField] float bulgeStrength = 0.58f;
        [SerializeField] float twoHandSquash = 0.42f;
        [SerializeField] float responseSpeed = 85f;
        [SerializeField] float returnSpeed = 18f;
        [SerializeField] float wobble = 0.018f;
        [SerializeField] bool placeInFrontOfCamera = true;
        [SerializeField] float cameraDistance = 0.6f;
        [SerializeField] float cameraHeightOffset = -0.05f;

        static readonly List<SquishContact> Contacts = new();

        XRHandSubsystem handSubsystem;
        bool leftPinching;
        bool rightPinching;
        Mesh sourceMesh;
        Mesh mesh;
        Vector3[] baseVertices;
        Vector3[] currentVertices;
        Vector3[] targetVertices;
        Vector3 lastSquashAxis = Vector3.right;
        float baseLocalRadius = 0.5f;

        struct SquishContact
        {
            public Vector3 Point;
            public Vector3 Normal;
            public float Depth;
            public int Hand;
        }

        void Awake()
        {
            if (sphere == null)
                sphere = transform;

            if (meshFilter == null)
                meshFilter = sphere.GetComponent<MeshFilter>();

            SetupMesh();
        }

        void OnEnable()
        {
            handSubsystem = XRHandInput.FindRunningSubsystem();
        }

        void Start()
        {
            if (placeInFrontOfCamera)
                PlaceInFrontOfCamera();
        }

        void OnDestroy()
        {
            if (meshFilter != null && meshFilter.sharedMesh == mesh)
                meshFilter.sharedMesh = sourceMesh;

            if (mesh != null)
                Destroy(mesh);
        }

        void Update()
        {
            if (handSubsystem == null || !handSubsystem.running)
                handSubsystem = XRHandInput.FindRunningSubsystem();

            if (handSubsystem == null)
            {
                ReturnToRest();
                return;
            }

            Contacts.Clear();

            AddHandContact(handSubsystem.leftHand, 0, leftPinching, out leftPinching);
            AddHandContact(handSubsystem.rightHand, 1, rightPinching, out rightPinching);

            if (Contacts.Count == 0)
            {
                ReturnToRest();
                return;
            }

            BuildTargetShape();
            ApplyTargetShape(responseSpeed);
        }

        void AddHandContact(XRHand hand, int handIndex, bool wasPinching, out bool isPinching)
        {
            isPinching = XRHandInput.TryGetPinchPoint(hand, out var pinchPoint) &&
                XRHandInput.IsPinching(hand, wasPinching, pinchStartDistance, pinchEndDistance);
            if (isPinching)
                TryAddContact(pinchPoint, handIndex, true);

            if (XRHandInput.TryGetPalmOrWristPoint(hand, out var palmPoint) && IsNearSphere(palmPoint))
                TryAddContact(palmPoint, handIndex, false);

            AddJointContact(hand, handIndex, XRHandJointID.ThumbTip);
            AddJointContact(hand, handIndex, XRHandJointID.IndexTip);
            AddJointContact(hand, handIndex, XRHandJointID.MiddleTip);
        }

        void BuildTargetShape()
        {
            if (mesh == null)
                return;

            for (var i = 0; i < baseVertices.Length; i++)
                targetVertices[i] = baseVertices[i];

            if (HasTwoHandContact())
                ApplyTwoHandShape();

            for (var i = 0; i < targetVertices.Length; i++)
                targetVertices[i] = ApplyContactShape(targetVertices[i], baseVertices[i]);
        }

        void ApplyTwoHandShape()
        {
            FindSquashPair(out var first, out var second, out var depth);
            var distance = Vector3.Distance(first, second);
            if (distance <= Mathf.Epsilon)
                return;

            var axis = sphere.InverseTransformDirection((second - first).normalized).normalized;
            if (Vector3.Dot(axis, lastSquashAxis) < 0f)
                axis = -axis;

            lastSquashAxis = Vector3.Slerp(lastSquashAxis, axis, 1f - Mathf.Exp(-responseSpeed * Time.deltaTime)).normalized;

            var squeeze = Mathf.Clamp01(depth / (maxDentDepth * 2f));
            var axisScale = 1f - squeeze * twoHandSquash;
            var sideScale = 1f + squeeze * 0.25f;
            var pulse = 1f + Mathf.Sin(Time.time * 24f) * wobble * squeeze;

            for (var i = 0; i < targetVertices.Length; i++)
            {
                var alongAxis = Vector3.Dot(targetVertices[i], lastSquashAxis);
                var axial = lastSquashAxis * alongAxis;
                var side = targetVertices[i] - axial;
                targetVertices[i] = axial * axisScale + side * sideScale * pulse;
            }
        }

        Vector3 ApplyContactShape(Vector3 vertex, Vector3 baseVertex)
        {
            var result = vertex;
            var baseDir = baseVertex.sqrMagnitude > Mathf.Epsilon ? baseVertex.normalized : Vector3.up;

            foreach (var contact in Contacts)
            {
                var normal = contact.Normal.normalized;
                var align = Vector3.Dot(baseDir, normal);
                if (align <= 0f)
                    continue;

                var dent = Smooth01(Mathf.InverseLerp(1f - dentWidth, 1f, align));
                var ring = Mathf.Exp(-Mathf.Pow((align - (1f - dentWidth)) / bulgeWidth, 2f));
                var depth = contact.Depth * dent;
                var bulge = contact.Depth * bulgeStrength * ring;

                result -= normal * depth;
                result += baseDir * bulge;
            }

            return result;
        }

        void ReturnToRest()
        {
            if (mesh == null)
                return;

            for (var i = 0; i < baseVertices.Length; i++)
                targetVertices[i] = baseVertices[i];

            ApplyTargetShape(returnSpeed);
        }

        void ApplyTargetShape(float speed)
        {
            var blend = 1f - Mathf.Exp(-speed * Time.deltaTime);
            for (var i = 0; i < currentVertices.Length; i++)
                currentVertices[i] = Vector3.Lerp(currentVertices[i], targetVertices[i], blend);

            mesh.vertices = currentVertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        bool IsNearSphere(Vector3 point)
        {
            return Vector3.Distance(sphere.position, point) <= SurfaceRadius() + contactSkin;
        }

        void AddJointContact(XRHand hand, int handIndex, XRHandJointID jointId)
        {
            if (XRHandInput.TryGetJointPose(hand, jointId, out var pose))
                TryAddContact(pose.position, handIndex, false);
        }

        bool TryAddContact(Vector3 handPoint, int handIndex, bool pinch)
        {
            var center = sphere.position;
            var centerToHand = handPoint - center;
            var distance = centerToHand.magnitude;
            var radius = SurfaceRadius();
            var penetration = radius + contactSkin - distance;

            if (penetration <= 0f)
                return false;

            var direction = distance > Mathf.Epsilon ? centerToHand / distance : Vector3.up;
            var depth = Mathf.Min(maxDentDepth, penetration * (pinch ? 2.2f : 1.25f));
            Contacts.Add(new SquishContact
            {
                Point = center + direction * radius,
                Normal = sphere.InverseTransformDirection(direction).normalized,
                Depth = depth,
                Hand = handIndex
            });

            return true;
        }

        static bool HasTwoHandContact()
        {
            for (var i = 0; i < Contacts.Count - 1; i++)
            {
                for (var j = i + 1; j < Contacts.Count; j++)
                {
                    if (Contacts[i].Hand != Contacts[j].Hand)
                        return true;
                }
            }

            return false;
        }

        void FindSquashPair(out Vector3 first, out Vector3 second, out float depth)
        {
            first = Vector3.zero;
            second = Vector3.right;
            depth = 0f;
            var bestDistance = -1f;

            for (var i = 0; i < Contacts.Count - 1; i++)
            {
                for (var j = i + 1; j < Contacts.Count; j++)
                {
                    if (Contacts[i].Hand == Contacts[j].Hand)
                        continue;

                    var distance = Vector3.Distance(Contacts[i].Point, Contacts[j].Point);
                    if (distance <= bestDistance)
                        continue;

                    bestDistance = distance;
                    first = Contacts[i].Point;
                    second = Contacts[j].Point;
                    depth = Contacts[i].Depth + Contacts[j].Depth;
                }
            }
        }

        float SurfaceRadius()
        {
            if (mesh == null)
                return 0.1f;

            var scale = sphere.lossyScale;
            return baseLocalRadius * Mathf.Max(scale.x, scale.y, scale.z);
        }

        static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        void PlaceInFrontOfCamera()
        {
            var cameraTransform = Camera.main != null ? Camera.main.transform : null;
            if (cameraTransform == null)
                return;

            var forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude <= Mathf.Epsilon)
                forward = cameraTransform.forward;

            sphere.position = cameraTransform.position + forward * cameraDistance + Vector3.up * cameraHeightOffset;
            sphere.rotation = Quaternion.identity;
        }

        void SetupMesh()
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return;

            sourceMesh = meshFilter.sharedMesh;
            mesh = Instantiate(sourceMesh);
            mesh.name = "StretchySphereMesh";
            meshFilter.sharedMesh = mesh;

            baseVertices = mesh.vertices;
            currentVertices = (Vector3[])baseVertices.Clone();
            targetVertices = (Vector3[])baseVertices.Clone();
            baseLocalRadius = Mathf.Max(mesh.bounds.extents.x, mesh.bounds.extents.y, mesh.bounds.extents.z);
        }
    }
}
