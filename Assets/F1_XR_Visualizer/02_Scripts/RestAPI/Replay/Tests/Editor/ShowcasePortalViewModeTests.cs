#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using F1XR.RestAPI.Api;
using F1XR.RestAPI.Replay.Room;
using NUnit.Framework;
using UnityEngine;

namespace F1XR.RestAPI.Replay.Tests
{
    public sealed class ShowcasePortalViewModeTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> objects = new();
        private readonly List<Object> resources = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                    Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();

            for (int i = resources.Count - 1; i >= 0; i--)
            {
                if (resources[i] != null)
                    Object.DestroyImmediate(resources[i]);
            }
            resources.Clear();
        }

        [Test]
        public void CyclesOneCameraWithoutAccumulatingProjectionState()
        {
            ShowcasePortalPresentation presentation =
                Create("PortalViewModeOwner")
                    .AddComponent<ShowcasePortalPresentation>();
            Camera viewer = CreateCamera("PortalViewModeViewer");
            Camera portalCamera = CreateCamera("PortalViewModeCamera");
            GameObject surfaceObject =
                GameObject.CreatePrimitive(PrimitiveType.Quad);
            objects.Add(surfaceObject);
            surfaceObject.hideFlags = HideFlags.HideAndDontSave;

            Vector2 size = new(4.2f, 2.4f);
            surfaceObject.transform.SetPositionAndRotation(
                new Vector3(0f, 1.2f, 3f),
                Quaternion.LookRotation(Vector3.back, Vector3.up));
            surfaceObject.transform.localScale =
                new Vector3(size.x, size.y, 1f);
            viewer.transform.SetPositionAndRotation(
                new Vector3(-0.5f, 1.6f, 0f),
                Quaternion.LookRotation(
                    (surfaceObject.transform.position -
                     new Vector3(-0.5f, 1.6f, 0f)).normalized,
                    Vector3.up));
            viewer.farClipPlane = 100f;

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
            Set(presentation, "configured", true);
            Set(presentation, "pitStopOnly", true);
            Set(presentation, "pitOverheadPoseValid", true);
            Pose overheadPose = new(
                new Vector3(5f, 18f, 2f),
                Quaternion.Euler(65f, 329f, 0f));
            Set(presentation, "pitOverheadPose", overheadPose);
            Set(presentation, "pitTopDownPoseValid", true);
            Pose topDownPose = new(
                new Vector3(0f, 20f, 3f),
                Quaternion.Euler(90f, 0f, 0f));
            Set(presentation, "pitTopDownPose", topDownPose);
            Get<Dictionary<Camera, float>>(
                presentation,
                "portalCameraForwardSigns")[portalCamera] = -1f;

            int cameraId = portalCamera.GetInstanceID();
            Assert.That(
                presentation.PitReplayViewMode,
                Is.EqualTo(PitReplayViewMode.Immersive));

            Assert.That(
                presentation.SetPitReplayView(
                    PitReplayViewMode.Overhead),
                Is.True);
            Assert.That(portalCamera.GetInstanceID(), Is.EqualTo(cameraId));
            Assert.That(
                Vector3.Distance(
                    portalCamera.transform.position,
                    overheadPose.position),
                Is.LessThan(0.0001f));
            Assert.That(portalCamera.projectionMatrix.m02, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(portalCamera.projectionMatrix.m12, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(portalCamera.fieldOfView, Is.EqualTo(42f).Within(0.001f));

            Assert.That(
                presentation.SetPitReplayView(
                    PitReplayViewMode.Immersive),
                Is.True);
            Assert.That(portalCamera.GetInstanceID(), Is.EqualTo(cameraId));
            Assert.That(
                Vector3.Distance(
                    portalCamera.transform.position,
                    viewer.transform.position),
                Is.LessThan(0.0001f));
            Assert.That(
                Mathf.Abs(portalCamera.projectionMatrix.m02),
                Is.GreaterThan(0.01f));

            Assert.That(presentation.TogglePitReplayView(), Is.True);
            Assert.That(
                presentation.PitReplayViewMode,
                Is.EqualTo(PitReplayViewMode.Overhead));
            Assert.That(portalCamera.projectionMatrix.m02, Is.EqualTo(0f).Within(0.0001f));

            Assert.That(presentation.TogglePitReplayView(), Is.True);
            Assert.That(
                presentation.PitReplayViewMode,
                Is.EqualTo(PitReplayViewMode.TopDown));
            Assert.That(portalCamera.GetInstanceID(), Is.EqualTo(cameraId));
            Assert.That(
                Vector3.Distance(
                    portalCamera.transform.position,
                    topDownPose.position),
                Is.LessThan(0.0001f));
            Assert.That(portalCamera.orthographic, Is.False);
            Assert.That(
                portalCamera.fieldOfView,
                Is.EqualTo(42f).Within(0.001f));
            Assert.That(
                portalCamera.projectionMatrix.m02,
                Is.EqualTo(0f).Within(0.0001f));

            Assert.That(presentation.TogglePitReplayView(), Is.True);
            Assert.That(
                presentation.PitReplayViewMode,
                Is.EqualTo(PitReplayViewMode.Immersive));
            Assert.That(
                Mathf.Abs(portalCamera.projectionMatrix.m02),
                Is.GreaterThan(0.01f));

            presentation.Clear();
            Assert.That(
                presentation.PitReplayViewMode,
                Is.EqualTo(PitReplayViewMode.Immersive));
        }

        [Test]
        public void LeftPrimaryShortcutCyclesOncePerPressOnlyDuringPitReplay()
        {
            ShowcasePortalPresentation presentation =
                Create("PortalShortcutPresentation")
                    .AddComponent<ShowcasePortalPresentation>();
            PitWallShowcasePresenter presenter =
                Create("PortalShortcutPresenter")
                    .AddComponent<PitWallShowcasePresenter>();
            EventPopoutReplay eventReplay =
                Create("PortalShortcutReplay")
                    .AddComponent<EventPopoutReplay>();

            Set(presentation, "configured", true);
            Set(presentation, "pitStopOnly", true);
            Set(presentation, "pitOverheadPoseValid", true);
            Set(presentation, "pitTopDownPoseValid", true);
            Set(eventReplay, "isActive", true);
            Set(
                eventReplay,
                "currentEvent",
                new ReplayEventDto { eventType = "PitStop" });
            Set(presenter, "portalPresentation", presentation);
            Set(presenter, "eventReplay", eventReplay);
            ReplayTimeline timeline =
                Get<ReplayTimeline>(eventReplay, "timeline");
            timeline.Reset(10f, 20f);
            timeline.SetTime(12.5f);
            timeline.Play();
            float replayTime = eventReplay.CurrentTime;

            Assert.That(
                Invoke(
                    presenter,
                    "ProcessPitReplayViewShortcut",
                    false),
                Is.False);
            Assert.That(
                Invoke(
                    presenter,
                    "ProcessPitReplayViewShortcut",
                    true),
                Is.True);
            Assert.That(
                presentation.PitReplayViewMode,
                Is.EqualTo(PitReplayViewMode.Overhead));

            Assert.That(
                Invoke(
                    presenter,
                    "ProcessPitReplayViewShortcut",
                    true),
                Is.False);
            Assert.That(
                presentation.PitReplayViewMode,
                Is.EqualTo(PitReplayViewMode.Overhead));

            Invoke(
                presenter,
                "ProcessPitReplayViewShortcut",
                false);
            Assert.That(
                Invoke(
                    presenter,
                    "ProcessPitReplayViewShortcut",
                    true),
                Is.True);
            Assert.That(
                presentation.PitReplayViewMode,
                Is.EqualTo(PitReplayViewMode.TopDown));

            Invoke(
                presenter,
                "ProcessPitReplayViewShortcut",
                false);
            Assert.That(
                Invoke(
                    presenter,
                    "ProcessPitReplayViewShortcut",
                    true),
                Is.True);
            Assert.That(
                presentation.PitReplayViewMode,
                Is.EqualTo(PitReplayViewMode.Immersive));
            Assert.That(eventReplay.IsPlaying, Is.True);
            Assert.That(eventReplay.CurrentTime, Is.EqualTo(replayTime));

            Set(eventReplay, "isActive", false);
            Invoke(
                presenter,
                "ProcessPitReplayViewShortcut",
                false);
            Assert.That(
                Invoke(
                    presenter,
                    "ProcessPitReplayViewShortcut",
                    true),
                Is.False);
            Assert.That(
                presentation.PitReplayViewMode,
                Is.EqualTo(PitReplayViewMode.Immersive));
        }

        [Test]
        public void ResolvesVerticalTopDownPoseFromServiceBounds()
        {
            GameObject stage = Create("PitStage");
            GameObject originObject = Create("PitChoreographyOrigin");
            Transform origin = originObject.transform;
            origin.SetParent(stage.transform, false);
            origin.localPosition = new Vector3(2f, 0.4f, -3f);
            origin.localRotation = Quaternion.Euler(0f, 37f, 0f);

            string[] names =
            {
                "FL_Hub",
                "FR_Hub",
                "RL_Hub",
                "RR_Hub",
                "FL_WheelGunner_Service",
                "FL_WheelOff_Service",
                "FL_WheelOn_Service",
                "FR_WheelGunner_Service",
                "FR_WheelOff_Service",
                "FR_WheelOn_Service",
                "RL_WheelGunner_Service",
                "RL_WheelOff_Service",
                "RL_WheelOn_Service",
                "RR_WheelGunner_Service",
                "RR_WheelOff_Service",
                "RR_WheelOn_Service"
            };
            Bounds bounds = new(origin.position, Vector3.zero);
            for (int i = 0; i < names.Length; i++)
            {
                GameObject anchor = Create(names[i]);
                anchor.transform.SetParent(origin, false);
                anchor.transform.localPosition = new Vector3(
                    i % 4 < 2 ? -2.2f : 2.2f,
                    0f,
                    i % 2 == 0 ? -1.7f : 1.7f);
                bounds.Encapsulate(anchor.transform.position);
            }

            MethodInfo resolver = typeof(ShowcasePortalPresentation)
                .GetMethod(
                    "TryResolvePitTacticalPoses",
                    BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(resolver, Is.Not.Null);
            object[] arguments =
            {
                stage.transform,
                default(Pose),
                default(Pose)
            };
            Assert.That(
                (bool)resolver.Invoke(null, arguments),
                Is.True);
            Pose topDownPose = (Pose)arguments[2];
            Vector3 target =
                bounds.center + origin.up * (0.72f * 0.12f);

            Assert.That(
                Vector3.Dot(
                    topDownPose.rotation * Vector3.forward,
                    -origin.up),
                Is.GreaterThan(0.9999f));
            Assert.That(
                Vector3.Dot(
                    topDownPose.rotation * Vector3.up,
                    origin.right),
                Is.GreaterThan(0.9999f));
            Assert.That(
                Vector3.ProjectOnPlane(
                    topDownPose.position - target,
                    origin.up).magnitude,
                Is.LessThan(0.0001f));
            Assert.That(
                Vector3.Dot(
                    topDownPose.position - target,
                    origin.up),
                Is.GreaterThan(0f));
        }

        [Test]
        public void RestoresOnlyRuntimeWallMeshAfterTacticalModes()
        {
            ShowcasePortalPresentation presentation =
                Create("PortalOccluderOwner")
                    .AddComponent<ShowcasePortalPresentation>();
            GameObject stage = Create("PitStage");
            GameObject context = Create("ContextSurface");
            context.transform.SetParent(stage.transform, false);
            MeshFilter filter = context.AddComponent<MeshFilter>();
            MeshRenderer renderer = context.AddComponent<MeshRenderer>();

            Mesh source = new()
            {
                name = "SuzukaPitLaneContextMesh",
                vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up,
                    Vector3.forward,
                    Vector3.right + Vector3.forward,
                    Vector3.up + Vector3.forward
                },
                subMeshCount = 2
            };
            source.SetIndices(
                new[] { 0, 1, 2 },
                MeshTopology.Triangles,
                0);
            source.SetIndices(
                new[] { 3, 4, 5 },
                MeshTopology.Triangles,
                1);
            resources.Add(source);
            filter.sharedMesh = source;

            Shader shader = Shader.Find("Unlit/Color");
            Assert.That(shader, Is.Not.Null);
            Material baseMaterial = new(shader) { name = "BASE" };
            Material wallMaterial = new(shader) { name = "WALL1" };
            resources.Add(baseMaterial);
            resources.Add(wallMaterial);
            renderer.sharedMaterials =
                new[] { baseMaterial, wallMaterial };

            Invoke(
                presentation,
                "ResolvePitOverheadOccluder",
                stage.transform);
            Set(presentation, "configured", true);
            Set(presentation, "pitStopOnly", true);
            Set(presentation, "pitOverheadPoseValid", true);
            Set(presentation, "pitTopDownPoseValid", true);
            Assert.That(
                presentation.SetPitReplayView(
                    PitReplayViewMode.TopDown),
                Is.True);

            Mesh overhead = filter.sharedMesh;
            Assert.That(overhead, Is.Not.SameAs(source));
            Assert.That(source.GetIndexCount(1), Is.EqualTo(3));
            Assert.That(overhead.GetIndexCount(0), Is.EqualTo(3));
            Assert.That(overhead.GetIndexCount(1), Is.EqualTo(0));
            Assert.That(
                presentation.PitOverheadOccluderSuppressed,
                Is.True);

            Assert.That(
                presentation.SetPitReplayView(
                    PitReplayViewMode.Overhead),
                Is.True);
            Assert.That(filter.sharedMesh, Is.SameAs(overhead));

            Assert.That(
                presentation.SetPitReplayView(
                    PitReplayViewMode.Immersive),
                Is.True);
            Assert.That(filter.sharedMesh, Is.SameAs(source));
            Assert.That(
                presentation.PitOverheadOccluderSuppressed,
                Is.False);

            Assert.That(
                presentation.SetPitReplayView(
                    PitReplayViewMode.TopDown),
                Is.True);
            presentation.Clear();
            Assert.That(filter.sharedMesh, Is.SameAs(source));
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

        private static object Invoke(
            object target,
            string methodName,
            params object[] arguments)
        {
            return target.GetType()
                .GetMethod(methodName, PrivateInstance)
                .Invoke(target, arguments);
        }
    }
}
#endif
