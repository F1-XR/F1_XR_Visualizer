using System;
using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay
{
    [DisallowMultipleComponent]
    public sealed class ReplayCarDrivingPresentation : MonoBehaviour
    {
        private const float WheelRadiusMeters = 0.33f;
        private const float WheelbaseMeters = 3.6f;
        private const float MaximumSteeringDegrees = 18f;
        private const float ShowcaseMaximumSteeringDegrees = 26f;
        private const float ShowcaseSteeringGain = 5f;
        private const float ShowcaseMinimumSteeringDegrees = 9f;
        private const float ShowcaseWheelSpinScale = 0.32f;
        private const float ShowcaseGroundClearanceRadiusRatio = 0.04f;
        private const float ShowcaseMaximumGroundLiftRadiusRatio = 1.25f;
        private const float SteeringResponse = 12f;
        private const float MaximumContinuousStep = 0.5f;
        private const float BrakeCueThreshold = 0.1f;
        private const float ShowcaseBrakeCueScale = 2.4f;
        private const float SpeedStreakMinimumKph = 70f;
        private const float ContactShadowOpacity = 0.48f;
        private const int ContactShadowTextureSize = 32;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly Color IdleBrakeCueColor =
            new(0.35f, 0.005f, 0.002f, 1f);
        private static readonly Color ActiveBrakeCueColor =
            new(4f, 0.02f, 0.01f, 1f);
        private static Material brakeCueMaterial;
        private static Material speedStreakMaterial;
        private static Material contactShadowMaterial;

        private readonly List<Wheel> wheels = new();
        private Transform frontLeft;
        private Transform frontRight;
        private Transform rearLeft;
        private Transform rearRight;
        private GameObject brakeCue;
        private GameObject speedStreakRoot;
        private GameObject contactShadow;
        private readonly LineRenderer[] speedStreaks =
            new LineRenderer[2];
        private readonly Vector3[] speedStreakStarts =
            new Vector3[2];
        private Vector3 speedStreakDirection;
        private float speedStreakWheelbase;
        private MeshRenderer brakeCueRenderer;
        private MaterialPropertyBlock brakeCueProperties;
        private Vector3 brakeCueBaseScale;
        private int currentBrake;
        private float currentSpeedKph;
        private float lastReplayTime;
        private Vector3 lastForward;
        private float spinDegrees;
        private float steeringDegrees;
        private bool hasPreviousFrame;
        private bool configured;
        private bool showcaseEmphasis;
        private bool brakeCueVisible;
        private bool brakeCueColorValid;
        private bool lastBrakeCueActive;
        private bool speedStreakVisible;
        private int updatePhase;

        public float CurrentSpeedKph => currentSpeedKph;

        public int CurrentBrake => currentBrake;

        public void Configure()
        {
            if (configured)
                return;

            configured = true;
            Transform[] children =
                GetComponentsInChildren<Transform>(true);
            frontLeft = Find(children, "FL_Tire");
            frontRight = Find(children, "FR_Tire");
            rearLeft = Find(children, "RL_Tire");
            rearRight = Find(children, "RR_Tire");

            AddWheel(frontLeft, true);
            AddWheel(frontRight, true);
            AddWheel(rearLeft, false);
            AddWheel(rearRight, false);

            if (wheels.Count == 4)
                CreateBrakeCue();

            ReplayCarView carView =
                GetComponent<ReplayCarView>();
            updatePhase =
                carView != null
                    ? carView.driverNumber & 1
                    : 0;
        }

        public void Apply(
            float replayTime,
            float speedKph,
            int brake)
        {
            Configure();
            ApplyBrakeCue(brake);
            ApplySpeedStreaks(speedKph);

            if (!showcaseEmphasis &&
                ((Time.frameCount + updatePhase) & 1) != 0)
            {
                return;
            }

            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.000001f)
                forward.Normalize();

            float replayDelta = replayTime - lastReplayTime;
            if (!hasPreviousFrame ||
                replayDelta <= 0f ||
                replayDelta > MaximumContinuousStep)
            {
                lastReplayTime = replayTime;
                lastForward = forward;
                hasPreviousFrame = true;
                steeringDegrees = 0f;
                ApplyWheelRotations();
                return;
            }

            float speedMps =
                Mathf.Max(0f, speedKph) / 3.6f;
            float spinScale = showcaseEmphasis
                ? ShowcaseWheelSpinScale
                : 1f;
            spinDegrees = Mathf.Repeat(
                spinDegrees +
                    speedMps / WheelRadiusMeters *
                    Mathf.Rad2Deg *
                    replayDelta *
                    spinScale,
                360f);

            float targetSteering = ResolveSteering(
                lastForward,
                forward,
                speedMps,
                replayDelta);
            if (showcaseEmphasis)
            {
                targetSteering = Mathf.Clamp(
                    targetSteering * ShowcaseSteeringGain,
                    -ShowcaseMaximumSteeringDegrees,
                    ShowcaseMaximumSteeringDegrees);
                if (Mathf.Abs(targetSteering) > 0.25f)
                {
                    targetSteering =
                        Mathf.Sign(targetSteering) *
                        Mathf.Max(
                            Mathf.Abs(targetSteering),
                            ShowcaseMinimumSteeringDegrees);
                }
            }
            float blend =
                1f - Mathf.Exp(-SteeringResponse * replayDelta);
            steeringDegrees = Mathf.Lerp(
                steeringDegrees,
                targetSteering,
                blend);

            lastReplayTime = replayTime;
            lastForward = forward;
            ApplyWheelRotations();
        }

        public void ResetState()
        {
            hasPreviousFrame = false;
            steeringDegrees = 0f;
            currentSpeedKph = 0f;
            ApplyWheelRotations();
            ApplyBrakeCue(0);
            ApplySpeedStreaks(0f);
        }

        public void SetShowcaseEmphasis(bool enabled)
        {
            Configure();
            showcaseEmphasis = enabled;

            if (enabled && speedStreakRoot == null)
                CreateSpeedStreaks();
            if (enabled && contactShadow == null)
                CreateContactShadow();

            if (contactShadow != null)
                contactShadow.SetActive(enabled);

            if (brakeCue != null)
            {
                brakeCue.transform.localScale =
                    brakeCueBaseScale *
                    (enabled
                        ? ShowcaseBrakeCueScale
                        : 1f);
            }

            ApplyBrakeCue(currentBrake);
            ApplySpeedStreaks(currentSpeedKph);
        }

        public float ResolveShowcaseGroundLiftWorld()
        {
            Configure();
            if (wheels.Count == 0)
                return 0f;

            Vector3 up = transform.up;
            float lowestHeight = float.PositiveInfinity;
            float maximumRadius = 0f;

            foreach (Wheel wheel in wheels)
            {
                if (wheel.Transform == null)
                    continue;

                Renderer[] renderers =
                    wheel.Transform.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null)
                        continue;

                    Bounds bounds = renderer.bounds;
                    float extent = ProjectedExtent(bounds.extents, up);
                    float centerHeight = Vector3.Dot(
                        bounds.center - transform.position,
                        up);
                    lowestHeight = Mathf.Min(
                        lowestHeight,
                        centerHeight - extent);
                    maximumRadius = Mathf.Max(maximumRadius, extent);
                }
            }

            if (!float.IsFinite(lowestHeight) || maximumRadius <= 0f)
                return 0f;

            float clearance =
                maximumRadius * ShowcaseGroundClearanceRadiusRatio;
            return Mathf.Clamp(
                -lowestHeight + clearance,
                0f,
                maximumRadius * ShowcaseMaximumGroundLiftRadiusRatio);
        }

        private static float ProjectedExtent(
            Vector3 extents,
            Vector3 axis)
        {
            return Mathf.Abs(axis.x) * extents.x +
                Mathf.Abs(axis.y) * extents.y +
                Mathf.Abs(axis.z) * extents.z;
        }

        private void AddWheel(Transform wheel, bool steering)
        {
            if (wheel == null)
                return;

            wheels.Add(
                new Wheel(
                    wheel,
                    wheel.localRotation,
                    steering));
        }

        private void ApplyWheelRotations()
        {
            foreach (Wheel wheel in wheels)
            {
                if (wheel.Transform == null)
                    continue;

                Quaternion steering = Quaternion.identity;
                if (wheel.Steering)
                {
                    Transform parent = wheel.Transform.parent;
                    Vector3 steeringAxis = parent != null
                        ? parent.InverseTransformDirection(transform.up)
                        : Vector3.up;
                    if (steeringAxis.sqrMagnitude <= 0.000001f)
                        steeringAxis = Vector3.up;

                    steering = Quaternion.AngleAxis(
                        steeringDegrees,
                        steeringAxis.normalized);
                }
                Quaternion spin = Quaternion.AngleAxis(
                    spinDegrees,
                    Vector3.right);
                wheel.Transform.localRotation =
                    steering *
                    wheel.BaseRotation *
                    spin;
            }
        }

        private static float ResolveSteering(
            Vector3 previousForward,
            Vector3 currentForward,
            float speedMps,
            float replayDelta)
        {
            if (previousForward.sqrMagnitude <= 0.000001f ||
                currentForward.sqrMagnitude <= 0.000001f ||
                speedMps <= 0.5f)
            {
                return 0f;
            }

            float yawDegrees = Vector3.SignedAngle(
                previousForward,
                currentForward,
                Vector3.up);
            float yawRadiansPerSecond =
                yawDegrees * Mathf.Deg2Rad /
                Mathf.Max(0.001f, replayDelta);
            float steering = Mathf.Atan(
                    WheelbaseMeters *
                    yawRadiansPerSecond /
                    speedMps) *
                Mathf.Rad2Deg;
            return Mathf.Clamp(
                steering,
                -MaximumSteeringDegrees,
                MaximumSteeringDegrees);
        }

        private void CreateBrakeCue()
        {
            Vector3 frontCenter =
                (frontLeft.position + frontRight.position) * 0.5f;
            Vector3 rearCenter =
                (rearLeft.position + rearRight.position) * 0.5f;
            Vector3 travelDirection = frontCenter - rearCenter;
            float wheelbase = travelDirection.magnitude;
            if (wheelbase <= 0.0001f)
                return;

            Vector3 worldPosition =
                rearCenter -
                travelDirection.normalized * (wheelbase * 0.06f) +
                transform.up * (wheelbase * 0.035f);

            brakeCue = GameObject.CreatePrimitive(
                PrimitiveType.Sphere);
            brakeCue.name = "ReplayBrakeCue";
            brakeCue.transform.SetParent(transform, true);
            brakeCue.transform.position = worldPosition;
            brakeCue.transform.localScale =
                ReplayCarVisualUtil.ToLocalScale(
                    brakeCue.transform,
                    wheelbase * 0.035f);
            brakeCueBaseScale =
                brakeCue.transform.localScale;

            Collider cueCollider =
                brakeCue.GetComponent<Collider>();
            if (cueCollider != null)
                Destroy(cueCollider);

            brakeCueRenderer =
                brakeCue.GetComponent<MeshRenderer>();
            if (brakeCueRenderer != null)
            {
                brakeCueRenderer.sharedMaterial =
                    GetBrakeCueMaterial();
                brakeCueRenderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                brakeCueRenderer.receiveShadows = false;
                brakeCueProperties = new MaterialPropertyBlock();
            }

            brakeCue.SetActive(false);
        }

        private void CreateSpeedStreaks()
        {
            Vector3 frontCenter =
                (transform.InverseTransformPoint(frontLeft.position) +
                 transform.InverseTransformPoint(frontRight.position)) *
                0.5f;
            Vector3 rearLeftPosition =
                transform.InverseTransformPoint(rearLeft.position);
            Vector3 rearRightPosition =
                transform.InverseTransformPoint(rearRight.position);
            Vector3 rearCenter =
                (rearLeftPosition + rearRightPosition) * 0.5f;
            Vector3 travelDirection = frontCenter - rearCenter;
            speedStreakWheelbase = travelDirection.magnitude;
            if (speedStreakWheelbase <= 0.0001f)
                return;

            speedStreakDirection = -travelDirection.normalized;
            speedStreakStarts[0] =
                rearLeftPosition +
                Vector3.up *
                (speedStreakWheelbase * 0.015f);
            speedStreakStarts[1] =
                rearRightPosition +
                Vector3.up *
                (speedStreakWheelbase * 0.015f);

            speedStreakRoot =
                new GameObject("ReplaySpeedStreaks");
            speedStreakRoot.transform.SetParent(transform, false);
            for (int i = 0; i < speedStreaks.Length; i++)
            {
                GameObject lineObject =
                    new GameObject($"SpeedStreak_{i}");
                lineObject.transform.SetParent(
                    speedStreakRoot.transform,
                    false);
                LineRenderer line =
                    lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.positionCount = 2;
                line.sharedMaterial =
                    GetSpeedStreakMaterial();
                line.alignment = LineAlignment.View;
                line.textureMode = LineTextureMode.Stretch;
                line.numCapVertices = 2;
                line.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                speedStreaks[i] = line;
            }

            speedStreakRoot.SetActive(false);
        }

        private void CreateContactShadow()
        {
            if (frontLeft == null ||
                frontRight == null ||
                rearLeft == null ||
                rearRight == null)
            {
                return;
            }

            if (!TryGetWheelVisualBounds(
                    out Bounds wheelBounds))
            {
                return;
            }

            Vector3 minimum = wheelBounds.min;
            Vector3 maximum = wheelBounds.max;

            float wheelbase = Mathf.Max(
                0.01f,
                maximum.z - minimum.z);
            float trackWidth = Mathf.Max(
                0.01f,
                maximum.x - minimum.x);
            Vector3 center = (minimum + maximum) * 0.5f;

            contactShadow = GameObject.CreatePrimitive(
                PrimitiveType.Quad);
            contactShadow.name = "ShowcaseContactShadow";
            contactShadow.layer = gameObject.layer;
            contactShadow.transform.SetParent(transform, false);
            contactShadow.transform.localPosition = new Vector3(
                center.x,
                minimum.y + wheelbase * 0.012f,
                center.z);
            contactShadow.transform.localRotation =
                Quaternion.Euler(90f, 0f, 0f);
            contactShadow.transform.localScale = new Vector3(
                trackWidth * 1.18f,
                wheelbase * 1.12f,
                1f);

            Collider shadowCollider =
                contactShadow.GetComponent<Collider>();
            if (shadowCollider != null)
                Destroy(shadowCollider);

            MeshRenderer renderer =
                contactShadow.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial =
                    GetContactShadowMaterial();
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            contactShadow.SetActive(false);
        }

        private bool TryGetWheelVisualBounds(
            out Bounds wheelBounds)
        {
            wheelBounds = default;
            Transform[] wheelRoots =
            {
                frontLeft,
                frontRight,
                rearLeft,
                rearRight
            };
            bool hasBounds = false;
            foreach (Transform wheelRoot in wheelRoots)
            {
                if (wheelRoot == null)
                    continue;

                Renderer[] renderers =
                    wheelRoot.GetComponentsInChildren<Renderer>(
                        true);
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null)
                        continue;

                    Bounds localBounds = renderer.localBounds;
                    Vector3 minimum = localBounds.min;
                    Vector3 maximum = localBounds.max;
                    Matrix4x4 rendererToCar =
                        transform.worldToLocalMatrix *
                        renderer.transform.localToWorldMatrix;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 localPoint = new(
                            (corner & 1) == 0
                                ? minimum.x
                                : maximum.x,
                            (corner & 2) == 0
                                ? minimum.y
                                : maximum.y,
                            (corner & 4) == 0
                                ? minimum.z
                                : maximum.z);
                        Vector3 carPoint =
                            rendererToCar.MultiplyPoint3x4(
                                localPoint);
                        if (!hasBounds)
                        {
                            wheelBounds = new Bounds(
                                carPoint,
                                Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            wheelBounds.Encapsulate(carPoint);
                        }
                    }
                }
            }

            return hasBounds;
        }

        public bool OwnsRenderer(Renderer renderer)
        {
            if (renderer == null)
                return false;

            if (brakeCue != null &&
                renderer.transform.IsChildOf(brakeCue.transform))
            {
                return true;
            }

            if (speedStreakRoot != null &&
                renderer.transform.IsChildOf(
                    speedStreakRoot.transform))
            {
                return true;
            }

            if (contactShadow != null &&
                renderer.transform.IsChildOf(
                    contactShadow.transform))
            {
                return true;
            }

            return false;
        }

        private void ApplySpeedStreaks(float speedKph)
        {
            currentSpeedKph = Mathf.Max(0f, speedKph);
            if (speedStreakRoot == null)
                return;

            bool visible =
                showcaseEmphasis &&
                currentSpeedKph >= SpeedStreakMinimumKph;
            if (speedStreakVisible != visible)
            {
                speedStreakVisible = visible;
                speedStreakRoot.SetActive(visible);
            }

            if (!visible)
                return;

            float speed01 = Mathf.InverseLerp(
                SpeedStreakMinimumKph,
                340f,
                currentSpeedKph);
            float length =
                speedStreakWheelbase *
                Mathf.Lerp(0.35f, 1.15f, speed01);
            float width =
                speedStreakWheelbase *
                Mathf.Lerp(0.006f, 0.012f, speed01);
            Color startColor =
                new(
                    0.72f,
                    0.88f,
                    1f,
                    Mathf.Lerp(0.12f, 0.38f, speed01));
            Color endColor =
                new(
                    startColor.r,
                    startColor.g,
                    startColor.b,
                    0f);

            for (int i = 0; i < speedStreaks.Length; i++)
            {
                LineRenderer line = speedStreaks[i];
                if (line == null)
                    continue;

                line.startWidth = width;
                line.endWidth = width * 0.25f;
                line.startColor = startColor;
                line.endColor = endColor;
                line.SetPosition(0, speedStreakStarts[i]);
                line.SetPosition(
                    1,
                    speedStreakStarts[i] +
                    speedStreakDirection * length);
            }
        }

        private void ApplyBrakeCue(int brake)
        {
            if (brakeCue == null)
                return;

            currentBrake = brake;
            float brake01 = brake > 1
                ? Mathf.Clamp01(brake / 100f)
                : Mathf.Clamp01(brake);
            bool braking = brake01 >= BrakeCueThreshold;
            bool visible = showcaseEmphasis || braking;
            if (brakeCueVisible != visible)
            {
                brakeCueVisible = visible;
                brakeCue.SetActive(visible);
            }

            if (brakeCueRenderer == null)
                return;

            if (brakeCueColorValid &&
                lastBrakeCueActive == braking)
            {
                return;
            }

            brakeCueColorValid = true;
            lastBrakeCueActive = braking;
            Color color = braking
                ? ActiveBrakeCueColor
                : IdleBrakeCueColor;
            brakeCueRenderer.GetPropertyBlock(
                brakeCueProperties);
            brakeCueProperties.SetColor(BaseColorId, color);
            brakeCueProperties.SetColor(ColorId, color);
            brakeCueRenderer.SetPropertyBlock(
                brakeCueProperties);
        }

        private static Material GetBrakeCueMaterial()
        {
            if (brakeCueMaterial != null)
                return brakeCueMaterial;

            Shader shader =
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color");
            brakeCueMaterial = new Material(shader)
            {
                name = "Replay Brake Cue"
            };
            Color color = ActiveBrakeCueColor;
            if (brakeCueMaterial.HasProperty(BaseColorId))
                brakeCueMaterial.SetColor(BaseColorId, color);
            if (brakeCueMaterial.HasProperty(ColorId))
                brakeCueMaterial.SetColor(ColorId, color);
            return brakeCueMaterial;
        }

        private static Material GetSpeedStreakMaterial()
        {
            if (speedStreakMaterial != null)
                return speedStreakMaterial;

            Shader shader =
                Shader.Find("Sprites/Default") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color");
            speedStreakMaterial = new Material(shader)
            {
                name = "Replay Speed Streak"
            };
            return speedStreakMaterial;
        }

        private static Material GetContactShadowMaterial()
        {
            if (contactShadowMaterial != null)
                return contactShadowMaterial;

            Shader shader =
                Shader.Find("Sprites/Default") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Transparent");
            contactShadowMaterial = new Material(shader)
            {
                name = "Showcase Contact Shadow"
            };

            Texture2D texture = new(
                ContactShadowTextureSize,
                ContactShadowTextureSize,
                TextureFormat.RGBA32,
                false)
            {
                name = "Showcase Contact Shadow Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] pixels = new Color[
                ContactShadowTextureSize *
                ContactShadowTextureSize];
            for (int y = 0; y < ContactShadowTextureSize; y++)
            {
                for (int x = 0; x < ContactShadowTextureSize; x++)
                {
                    float u =
                        (x + 0.5f) /
                        ContactShadowTextureSize * 2f - 1f;
                    float v =
                        (y + 0.5f) /
                        ContactShadowTextureSize * 2f - 1f;
                    float alpha = Mathf.Pow(
                        Mathf.Clamp01(1f - u * u - v * v),
                        1.25f) * ContactShadowOpacity;
                    pixels[y * ContactShadowTextureSize + x] =
                        new Color(0f, 0f, 0f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            contactShadowMaterial.mainTexture = texture;
            return contactShadowMaterial;
        }

        private static Transform Find(
            Transform[] children,
            string targetName)
        {
            foreach (Transform child in children)
            {
                if (child != null &&
                    string.Equals(
                        child.name,
                        targetName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            return null;
        }

        private sealed class Wheel
        {
            public readonly Transform Transform;
            public readonly Quaternion BaseRotation;
            public readonly bool Steering;

            public Wheel(
                Transform transform,
                Quaternion baseRotation,
                bool steering)
            {
                Transform = transform;
                BaseRotation = baseRotation;
                Steering = steering;
            }
        }
    }

    public partial class ReplayCarView
    {
        private ReplayCarDrivingPresentation drivingPresentation;

        public void ConfigureDrivingPresentation()
        {
            drivingPresentation =
                GetComponent<ReplayCarDrivingPresentation>();
            if (drivingPresentation == null)
            {
                drivingPresentation =
                    gameObject.AddComponent<
                        ReplayCarDrivingPresentation>();
            }

            drivingPresentation.Configure();
        }

        public void ApplyDrivingPresentation(
            float replayTime,
            float speedKph,
            int brake)
        {
            if (renderLodUsingProxy)
                return;

            if (drivingPresentation == null)
                ConfigureDrivingPresentation();

            drivingPresentation.Apply(
                replayTime,
                speedKph,
                brake);
        }

        public bool TryGetDrivingTelemetry(
            out float speedKph,
            out int brake)
        {
            speedKph = 0f;
            brake = 0;
            if (drivingPresentation == null)
                return false;

            speedKph = drivingPresentation.CurrentSpeedKph;
            brake = drivingPresentation.CurrentBrake;
            return true;
        }

        public void SetDrivingPresentationEmphasis(
            bool enabled)
        {
            SetRenderLodForceDetailed(enabled);

            if (drivingPresentation == null)
                ConfigureDrivingPresentation();

            drivingPresentation.SetShowcaseEmphasis(
                enabled);

            Vector3 localLift = Vector3.zero;
            if (enabled)
            {
                float worldLift =
                    drivingPresentation.ResolveShowcaseGroundLiftWorld();
                Vector3 worldOffset = transform.up * worldLift;
                localLift = transform.parent != null
                    ? transform.parent.InverseTransformVector(worldOffset)
                    : worldOffset;
            }

            SetDrivingPresentationLocalOffset(localLift);
        }
    }
}
