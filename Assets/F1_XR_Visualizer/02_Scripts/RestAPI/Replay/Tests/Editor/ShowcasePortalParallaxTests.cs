#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using F1XR.Interaction.World;
using F1XR.RestAPI.Replay.Room;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace F1XR.RestAPI.Replay.Tests
{
    public sealed class ShowcasePortalParallaxTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> objects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                    UnityEngine.Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
        }

        [Test]
        public void ViewerTranslationProducesStableDistinctOffAxisViews()
        {
            ShowcasePortalPresentation presentation =
                Create("PortalParallaxValidationOwner")
                    .AddComponent<ShowcasePortalPresentation>();
            Camera viewer = CreateCamera(
                "PortalParallaxValidationViewer");
            Camera portalCamera = CreateCamera(
                "PortalParallaxValidationCamera");
            viewer.nearClipPlane = 0.01f;
            viewer.farClipPlane = 100f;
            viewer.fieldOfView = 70f;
            viewer.aspect = 16f / 9f;

            Vector2 size = new(4.2f, 2.4f);
            GameObject surfaceObject =
                GameObject.CreatePrimitive(PrimitiveType.Quad);
            objects.Add(surfaceObject);
            surfaceObject.hideFlags = HideFlags.HideAndDontSave;
            surfaceObject.name = "PortalParallaxValidationSurface";
            surfaceObject.transform.SetPositionAndRotation(
                new Vector3(0f, 1.2f, 3f),
                Quaternion.LookRotation(Vector3.back, Vector3.up));
            surfaceObject.transform.localScale =
                new Vector3(size.x, size.y, 1f);

            Set(presentation, "viewerCamera", viewer);
            Set(presentation, "entryCamera", portalCamera);
            Set(
                presentation,
                "entrySurface",
                surfaceObject.transform);
            Set(
                presentation,
                "entrySurfaceRenderer",
                surfaceObject.GetComponent<Renderer>());
            Set(presentation, "entryPortalSize", size);
            Get<Dictionary<Camera, float>>(
                presentation,
                "portalCameraForwardSigns")[portalCamera] = -1f;

            MethodInfo refresh =
                typeof(ShowcasePortalPresentation).GetMethod(
                    "RefreshPortalViews",
                    PrivateInstance);
            Assert.That(refresh, Is.Not.Null);

            Vector3[] positions =
            {
                new(0f, 1.6f, 0f),
                new(-0.5f, 1.6f, 0f),
                new(0.5f, 1.6f, 0f),
                new(0f, 1.6f, 0.5f),
                new(0f, 1.9f, 0f),
                new(-0.5f, 1.75f, 0.4f)
            };
            string[] names =
            {
                "Center",
                "Left",
                "Right",
                "Closer",
                "Vertical",
                "Oblique"
            };
            Vector3 ferrari = new(0f, 0.6f, 9f);
            Vector3 garageColumn = new(-1.4f, 2f, 11f);
            Vector3 laneMark = new(1f, 0.05f, 12f);
            Matrix4x4 centerProjection = default;
            Vector3 centerFerrariViewport = default;
            Vector3 leftFerrariViewport = default;
            Vector3 rightFerrariViewport = default;
            StringBuilder evidence = new();

            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 target = surfaceObject.transform.position;
                if (names[i] == "Oblique")
                {
                    target +=
                        surfaceObject.transform.right *
                        (size.x * 0.42f);
                }
                viewer.transform.SetPositionAndRotation(
                    positions[i],
                    Quaternion.LookRotation(
                        (target - positions[i]).normalized,
                        Vector3.up));

                refresh.Invoke(presentation, null);
                Assert.That(
                    portalCamera.enabled,
                    Is.True,
                    names[i]);
                Assert.That(
                    Vector3.Distance(
                        portalCamera.transform.position,
                        positions[i]),
                    Is.LessThan(0.00001f),
                    names[i]);

                Matrix4x4 projection =
                    portalCamera.projectionMatrix;
                Assert.That(IsFinite(projection), Is.True, names[i]);
                Vector3 ferrariViewport =
                    portalCamera.WorldToViewportPoint(ferrari);
                Vector3 columnViewport =
                    portalCamera.WorldToViewportPoint(garageColumn);
                Vector3 laneViewport =
                    portalCamera.WorldToViewportPoint(laneMark);
                Assert.That(
                    IsFinite(ferrariViewport) &&
                    IsFinite(columnViewport) &&
                    IsFinite(laneViewport),
                    Is.True,
                    names[i]);

                if (i == 0)
                {
                    centerProjection = projection;
                    centerFerrariViewport = ferrariViewport;
                }
                else
                {
                    Assert.That(
                        MaximumDifference(
                            centerProjection,
                            projection),
                        Is.GreaterThan(0.0001f),
                        $"{names[i]} projection matched Center.");
                }

                if (names[i] == "Left")
                    leftFerrariViewport = ferrariViewport;
                else if (names[i] == "Right")
                    rightFerrariViewport = ferrariViewport;

                AppendEvidence(
                    evidence,
                    names[i],
                    surfaceObject.transform.InverseTransformPoint(
                        positions[i]),
                    portalCamera,
                    ferrariViewport,
                    columnViewport,
                    laneViewport);
            }

            Assert.That(
                leftFerrariViewport.x,
                Is.LessThan(centerFerrariViewport.x));
            Assert.That(
                rightFerrariViewport.x,
                Is.GreaterThan(centerFerrariViewport.x));

            viewer.transform.SetPositionAndRotation(
                new Vector3(0f, 1.6f, 3.2f),
                Quaternion.LookRotation(Vector3.back, Vector3.up));
            refresh.Invoke(presentation, null);
            Assert.That(
                portalCamera.enabled,
                Is.False,
                "A viewer behind the aperture must not flip the projection.");

            evidence.Append(
                "BackSide: portal camera disabled safely.");
            TestContext.Out.WriteLine(evidence.ToString());
        }

        [Test]
        public void ManualPitReferencePersistsAfterConfirmAndKeepsParallax()
        {
            ShowcasePortalPresentation presentation =
                Create("PitPortalManualReferenceOwner")
                    .AddComponent<ShowcasePortalPresentation>();
            Camera viewer = CreateCamera("PitPortalManualReferenceViewer");
            Camera portalCamera = CreateCamera(
                "PitPortalManualReferenceCamera");
            GameObject surfaceObject =
                GameObject.CreatePrimitive(PrimitiveType.Quad);
            objects.Add(surfaceObject);
            surfaceObject.hideFlags = HideFlags.HideAndDontSave;

            Vector3 portalPosition = new(0f, 1.2f, 3f);
            Quaternion portalRotation =
                Quaternion.LookRotation(Vector3.back, Vector3.up);
            Vector2 portalSize = new(4.2f, 2.4f);
            Vector3 viewerPosition = new(0f, 1.6f, 0f);

            surfaceObject.transform.SetPositionAndRotation(
                portalPosition,
                portalRotation);
            surfaceObject.transform.localScale =
                new Vector3(portalSize.x, portalSize.y, 1f);
            viewer.transform.SetPositionAndRotation(
                viewerPosition,
                Quaternion.LookRotation(
                    portalPosition - viewerPosition,
                    Vector3.up));
            viewer.farClipPlane = 100f;

            Set(presentation, "viewerCamera", viewer);
            Set(presentation, "entryCamera", portalCamera);
            Set(presentation, "entrySurface", surfaceObject.transform);
            Set(
                presentation,
                "entrySurfaceRenderer",
                surfaceObject.GetComponent<Renderer>());
            Set(presentation, "entryPortalSize", portalSize);
            Set(presentation, "configured", true);
            Set(presentation, "pitStopOnly", true);
            Get<Dictionary<Camera, float>>(
                presentation,
                "portalCameraForwardSigns")[portalCamera] = -1f;

            BoxCollider editCollider =
                surfaceObject.AddComponent<BoxCollider>();
            Rigidbody body = surfaceObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            XRGrabInteractable grab =
                surfaceObject.AddComponent<XRGrabInteractable>();
            ScaleController scale =
                surfaceObject.AddComponent<ScaleController>();
            scale.Configure(
                surfaceObject.transform,
                grab,
                body,
                0.55f,
                1.1f);
            WorldGrabPolicy policy =
                surfaceObject.AddComponent<WorldGrabPolicy>();
            policy.UseGrabPoint(grab, surfaceObject.transform);
            PitWallPortalEditState editState =
                surfaceObject.AddComponent<PitWallPortalEditState>();
            ShowcaseWallFrame wall = new(
                default,
                portalPosition,
                Vector3.back,
                Vector3.right,
                Vector3.up,
                8f,
                5f,
                -4f,
                4f,
                -2.5f,
                2.5f);
            MethodInfo configure =
                typeof(PitWallPortalEditState).GetMethod(
                    "Configure",
                    PrivateInstance);
            Assert.That(configure, Is.Not.Null);
            configure.Invoke(
                editState,
                new object[]
                {
                    presentation,
                    wall,
                    portalSize,
                    grab,
                    scale,
                    policy,
                    editCollider,
                    true
                });
            Set(presentation, "pitWallEditState", editState);
            editState.SetEditMode(true);

            MethodInfo adjustReference =
                typeof(ShowcasePortalPresentation).GetMethod(
                    "AdjustPitWallImmersiveReference",
                    PrivateInstance);
            MethodInfo refresh =
                typeof(ShowcasePortalPresentation).GetMethod(
                    "RefreshPortalViews",
                    PrivateInstance);
            MethodInfo confirm =
                typeof(ShowcasePortalPresentation).GetMethod(
                    "TogglePitWallEditMode",
                    PrivateInstance);
            Assert.That(adjustReference, Is.Not.Null);
            Assert.That(refresh, Is.Not.Null);
            Assert.That(confirm, Is.Not.Null);

            adjustReference.Invoke(
                presentation,
                new object[] { new Vector2(0.8f, 0.4f), 1f });
            Assert.That(portalCamera.enabled, Is.True);
            Vector3 expectedEye =
                viewerPosition + new Vector3(0.56f, 0.28f, 0f);
            Assert.That(
                Vector3.Distance(
                    portalCamera.transform.position,
                    expectedEye),
                Is.LessThan(0.0001f));
            Matrix4x4 confirmedProjection =
                portalCamera.projectionMatrix;
            Vector3 chosenOffset = Get<Vector3>(
                presentation,
                "pitWallImmersiveEyeOffset");

            Set(
                presentation,
                "pitReplayViewMode",
                PitReplayViewMode.Overhead);
            adjustReference.Invoke(
                presentation,
                new object[] { Vector2.left, 1f });
            Assert.That(
                Get<Vector3>(
                    presentation,
                    "pitWallImmersiveEyeOffset"),
                Is.EqualTo(chosenOffset));
            Set(
                presentation,
                "pitReplayViewMode",
                PitReplayViewMode.Immersive);

            Assert.That((bool)confirm.Invoke(presentation, null), Is.True);
            Assert.That(editState.IsEditMode, Is.False);
            Assert.That(
                Get<Vector3>(
                    presentation,
                    "pitWallImmersiveEyeOffset"),
                Is.EqualTo(chosenOffset));
            adjustReference.Invoke(
                presentation,
                new object[] { Vector2.left, 1f });
            Assert.That(
                Get<Vector3>(
                    presentation,
                    "pitWallImmersiveEyeOffset"),
                Is.EqualTo(chosenOffset));

            Vector3 viewerDelta = new(0.2f, 0.1f, -0.15f);
            viewer.transform.position += viewerDelta;
            refresh.Invoke(presentation, null);

            Assert.That(
                Vector3.Distance(
                    portalCamera.transform.position,
                    expectedEye + viewerDelta),
                Is.LessThan(0.0001f));
            Assert.That(
                MaximumDifference(
                    confirmedProjection,
                    portalCamera.projectionMatrix),
                Is.GreaterThan(0.0001f));
        }

        [Test]
        public void PitPortalEditPreservesMoveAndBoundedScaleWhenDisabled()
        {
            ShowcasePortalPresentation presentation =
                Create("PitPortalEditOwner")
                    .AddComponent<ShowcasePortalPresentation>();
            GameObject surface = Create("PitPortalEditSurface");
            BoxCollider editCollider =
                surface.AddComponent<BoxCollider>();
            Rigidbody body = surface.AddComponent<Rigidbody>();
            body.isKinematic = true;
            XRGrabInteractable grab =
                surface.AddComponent<XRGrabInteractable>();
            ScaleController scale =
                surface.AddComponent<ScaleController>();
            scale.Configure(surface.transform, grab, body, 0.55f, 1.1f);
            WorldGrabPolicy policy =
                surface.AddComponent<WorldGrabPolicy>();
            policy.UseGrabPoint(grab, surface.transform);
            PitWallPortalEditState editState =
                surface.AddComponent<PitWallPortalEditState>();
            ShowcaseWallFrame wall = new(
                default,
                Vector3.zero,
                Vector3.forward,
                Vector3.right,
                Vector3.up,
                8f,
                5f,
                -4f,
                4f,
                -2.5f,
                2.5f);
            MethodInfo configure =
                typeof(PitWallPortalEditState).GetMethod(
                    "Configure",
                    PrivateInstance);
            Assert.That(configure, Is.Not.Null);
            configure.Invoke(
                editState,
                new object[]
                {
                    presentation,
                    wall,
                    new Vector2(4f, 2f),
                    grab,
                    scale,
                    policy,
                    editCollider,
                    true
                });

            Assert.That(editState.IsEditMode, Is.False);
            Assert.That(grab.enabled, Is.False);
            Assert.That(scale.enabled, Is.False);
            Assert.That(policy.enabled, Is.False);
            Assert.That(editCollider.enabled, Is.False);

            editState.SetEditMode(true);
            Assert.That(grab.enabled, Is.True);
            Assert.That(scale.enabled, Is.True);
            Assert.That(policy.enabled, Is.True);
            Assert.That(editCollider.enabled, Is.True);

            Vector3 editedPosition = new(1.2f, 1.7f, 2.4f);
            Quaternion editedRotation = Quaternion.Euler(0f, 25f, 0f);
            surface.transform.SetPositionAndRotation(
                editedPosition,
                editedRotation);
            surface.transform.localScale = Vector3.one * 2f;
            InvokeEditLateUpdate(editState);
            Assert.That(
                Vector3.Distance(surface.transform.position, editedPosition),
                Is.LessThan(0.0001f));
            Assert.That(
                Quaternion.Angle(surface.transform.rotation, editedRotation),
                Is.LessThan(0.0001f));
            Assert.That(
                surface.transform.localScale.x,
                Is.EqualTo(1.1f).Within(0.0001f));

            surface.transform.localScale = Vector3.one * 0.8f;
            InvokeEditLateUpdate(editState);
            editState.SetEditMode(false);

            Assert.That(
                Vector3.Distance(surface.transform.position, editedPosition),
                Is.LessThan(0.0001f));
            Assert.That(
                surface.transform.localScale.x,
                Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(grab.enabled, Is.False);
            Assert.That(scale.enabled, Is.False);
            Assert.That(policy.enabled, Is.False);
            Assert.That(editCollider.enabled, Is.False);
        }

        private GameObject Create(string name)
        {
            GameObject created = new(name)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            objects.Add(created);
            return created;
        }

        private Camera CreateCamera(string name) =>
            Create(name).AddComponent<Camera>();

        private static void InvokeEditLateUpdate(
            PitWallPortalEditState editState)
        {
            MethodInfo lateUpdate =
                typeof(PitWallPortalEditState).GetMethod(
                    "LateUpdate",
                    PrivateInstance);
            Assert.That(lateUpdate, Is.Not.Null);
            lateUpdate.Invoke(editState, null);
        }

        private static T Get<T>(object target, string fieldName) =>
            (T)target.GetType()
                .GetField(fieldName, PrivateInstance)
                .GetValue(target);

        private static void Set(
            object target,
            string fieldName,
            object value)
        {
            target.GetType()
                .GetField(fieldName, PrivateInstance)
                .SetValue(target, value);
        }

        private static void AppendEvidence(
            StringBuilder evidence,
            string poseName,
            Vector3 viewerLocal,
            Camera portalCamera,
            Vector3 ferrari,
            Vector3 column,
            Vector3 lane)
        {
            Matrix4x4 projection = portalCamera.projectionMatrix;
            float near = portalCamera.nearClipPlane;
            float left =
                near * (projection.m02 - 1f) / projection.m00;
            float right =
                near * (projection.m02 + 1f) / projection.m00;
            float bottom =
                near * (projection.m12 - 1f) / projection.m11;
            float top =
                near * (projection.m12 + 1f) / projection.m11;
            evidence.AppendFormat(
                CultureInfo.InvariantCulture,
                "{0}: viewerLocal={1}, camera={2}, " +
                "frustum=({3:F4},{4:F4},{5:F4},{6:F4}), " +
                "near={7:F4}, m00={8:F4}, m02={9:F4}, " +
                "m11={10:F4}, m12={11:F4}, Ferrari={12}, " +
                "Column={13}, Lane={14}{15}",
                poseName,
                viewerLocal.ToString("F3"),
                portalCamera.transform.position.ToString("F3"),
                left,
                right,
                bottom,
                top,
                near,
                projection.m00,
                projection.m02,
                projection.m11,
                projection.m12,
                ferrari.ToString("F4"),
                column.ToString("F4"),
                lane.ToString("F4"),
                Environment.NewLine);
        }

        private static float MaximumDifference(
            Matrix4x4 left,
            Matrix4x4 right)
        {
            float maximum = 0f;
            for (int i = 0; i < 16; i++)
            {
                maximum = Mathf.Max(
                    maximum,
                    Mathf.Abs(left[i] - right[i]));
            }
            return maximum;
        }

        private static bool IsFinite(Matrix4x4 value)
        {
            for (int i = 0; i < 16; i++)
            {
                if (!float.IsFinite(value[i]))
                    return false;
            }
            return true;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) &&
            float.IsFinite(value.y) &&
            float.IsFinite(value.z);
    }
}
#endif
