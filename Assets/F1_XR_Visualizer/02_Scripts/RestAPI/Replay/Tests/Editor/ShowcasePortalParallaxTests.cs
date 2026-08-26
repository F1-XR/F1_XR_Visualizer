#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using F1XR.RestAPI.Replay.Room;
using NUnit.Framework;
using UnityEngine;

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
