using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay.Room
{
    public enum PitReplayViewMode
    {
        Immersive,
        Overhead,
        TopDown
    }

    public sealed partial class ShowcasePortalPresentation
    {
        private const string PitChoreographyOriginName =
            "PitChoreographyOrigin";
        private const string FrontLeftTyreName = "FL_Tire";
        private const string SuzukaContextSurfaceName =
            "ContextSurface";
        private const string SuzukaContextMeshName =
            "SuzukaPitLaneContextMesh";
        private const string SuzukaWallMaterialName = "WALL1";
        private static readonly HashSet<string>
            SuzukaDetachedBackdropMaterialNames = new(
                System.StringComparer.OrdinalIgnoreCase)
            {
                "garageback",
                "TWALL",
                "TWALL1",
                "GRDR",
                "GRDR_TOP",
                "GRDR8",
                "GURDRC"
            };
        private const float PitOverheadFieldOfView = 42f;
        private const float PitOverheadFramingPadding = 0.68f;
        private const float PitTopDownFieldOfView = 42f;
        private const float PitTopDownFramingPadding = 0.68f;
        private const float PitOverheadNearClip = 0.03f;

        private static readonly string[] PitOverheadFramingNames =
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

        private PitReplayViewMode pitReplayViewMode;
        private Pose pitOverheadPose;
        private bool pitOverheadPoseValid;
        private Pose pitTopDownPose;
        private bool pitTopDownPoseValid;
        private MeshFilter pitOverheadContextFilter;
        private Mesh pitOverheadContextSourceMesh;
        private Mesh pitOverheadContextMesh;

        public PitReplayViewMode PitReplayViewMode =>
            pitReplayViewMode;

        public bool CanChangePitReplayView =>
            IsPitStopConfigured &&
            pitOverheadPoseValid &&
            pitTopDownPoseValid;

        public bool PitOverheadOccluderSuppressed =>
            pitOverheadContextFilter != null &&
            pitOverheadContextMesh != null &&
            pitOverheadContextFilter.sharedMesh ==
            pitOverheadContextMesh;

        public bool TogglePitReplayView()
        {
            PitReplayViewMode next = pitReplayViewMode switch
            {
                PitReplayViewMode.Immersive =>
                    PitReplayViewMode.Overhead,
                PitReplayViewMode.Overhead =>
                    PitReplayViewMode.TopDown,
                _ => PitReplayViewMode.Immersive
            };
            return SetPitReplayView(next);
        }

        public bool SetPitReplayView(PitReplayViewMode mode)
        {
            if (!IsPitStopConfigured ||
                !System.Enum.IsDefined(typeof(PitReplayViewMode), mode) ||
                mode == PitReplayViewMode.Overhead &&
                !pitOverheadPoseValid ||
                mode == PitReplayViewMode.TopDown &&
                !pitTopDownPoseValid)
            {
                return false;
            }

            ApplyPitOverheadOccluderSuppression();

            pitReplayViewMode = mode;
            RefreshPortalViews();
            return true;
        }

        private void ConfigurePitOverheadView(Transform stage)
        {
            pitReplayViewMode = PitReplayViewMode.Immersive;
            bool posesValid = TryResolvePitTacticalPoses(
                stage,
                out pitOverheadPose,
                out pitTopDownPose);
            pitOverheadPoseValid = posesValid;
            pitTopDownPoseValid = posesValid;
            ResolvePitOverheadOccluder(stage);
            ApplyPitOverheadOccluderSuppression();
        }

        private void ClearPitReplayView()
        {
            RestorePitOverheadOccluder();
            if (pitOverheadContextMesh != null)
            {
                runtimeMeshes.Remove(pitOverheadContextMesh);
                if (Application.isPlaying)
                    Destroy(pitOverheadContextMesh);
                else
                    DestroyImmediate(pitOverheadContextMesh);
            }

            pitReplayViewMode = PitReplayViewMode.Immersive;
            pitOverheadPose = default;
            pitOverheadPoseValid = false;
            pitTopDownPose = default;
            pitTopDownPoseValid = false;
            pitOverheadContextFilter = null;
            pitOverheadContextSourceMesh = null;
            pitOverheadContextMesh = null;
        }

        private void ResolvePitOverheadOccluder(Transform stage)
        {
            pitOverheadContextFilter = null;
            pitOverheadContextSourceMesh = null;
            if (stage == null)
                return;

            MeshFilter[] filters =
                stage.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                Mesh source = filter != null
                    ? filter.sharedMesh
                    : null;
                MeshRenderer renderer = filter != null
                    ? filter.GetComponent<MeshRenderer>()
                    : null;
                Material[] materials = renderer != null
                    ? renderer.sharedMaterials
                    : null;
                if (filter == null ||
                    filter.gameObject.name !=
                    SuzukaContextSurfaceName ||
                    source == null ||
                    source.name != SuzukaContextMeshName ||
                    materials == null ||
                    !HasSuzukaDetachedBackdrop(
                        source,
                        materials))
                {
                    continue;
                }

                pitOverheadContextFilter = filter;
                pitOverheadContextSourceMesh = source;
                return;
            }
        }

        private void ApplyPitOverheadOccluderSuppression()
        {
            if (pitOverheadContextFilter == null ||
                pitOverheadContextSourceMesh == null)
            {
                return;
            }

            if (pitOverheadContextMesh == null)
            {
                pitOverheadContextMesh = Instantiate(
                    pitOverheadContextSourceMesh);
                pitOverheadContextMesh.name =
                    pitOverheadContextSourceMesh.name +
                    "_PitArchitectureFiltered";
                MeshRenderer renderer =
                    pitOverheadContextFilter.GetComponent<MeshRenderer>();
                Material[] materials = renderer != null
                    ? renderer.sharedMaterials
                    : null;
                int count = Mathf.Min(
                    pitOverheadContextSourceMesh.subMeshCount,
                    materials != null ? materials.Length : 0);
                for (int subMesh = 0; subMesh < count; subMesh++)
                {
                    Material material = materials[subMesh];
                    if (material == null)
                    {
                        continue;
                    }

                    if (material.name == SuzukaWallMaterialName)
                    {
                        pitOverheadContextMesh.SetIndices(
                            System.Array.Empty<int>(),
                            pitOverheadContextSourceMesh.GetTopology(
                                subMesh),
                            subMesh,
                            false);
                    }
                    else if (SuzukaDetachedBackdropMaterialNames.Contains(
                                 material.name))
                    {
                        pitOverheadContextMesh.SetIndices(
                            System.Array.Empty<int>(),
                            pitOverheadContextSourceMesh.GetTopology(
                                subMesh),
                            subMesh,
                            false);
                    }
                }
                runtimeMeshes.Add(pitOverheadContextMesh);
            }

            pitOverheadContextFilter.sharedMesh =
                pitOverheadContextMesh;
        }

        private void RestorePitOverheadOccluder()
        {
            if (pitOverheadContextFilter != null &&
                pitOverheadContextSourceMesh != null &&
                pitOverheadContextFilter.sharedMesh ==
                pitOverheadContextMesh)
            {
                pitOverheadContextFilter.sharedMesh =
                    pitOverheadContextSourceMesh;
            }
        }

        private static bool HasSuzukaDetachedBackdrop(
            Mesh source,
            IReadOnlyList<Material> materials)
        {
            int count = Mathf.Min(
                source != null ? source.subMeshCount : 0,
                materials != null ? materials.Count : 0);
            for (int subMesh = 0; subMesh < count; subMesh++)
            {
                Material material = materials[subMesh];
                if (material != null &&
                    (material.name == SuzukaWallMaterialName ||
                     SuzukaDetachedBackdropMaterialNames.Contains(
                         material.name)))
                {
                    return true;
                }
            }

            return false;
        }

        private bool UsesPitTacticalView(Camera portalCamera) =>
            pitStopOnly &&
            (pitReplayViewMode == PitReplayViewMode.Overhead &&
             pitOverheadPoseValid ||
             pitReplayViewMode == PitReplayViewMode.TopDown &&
             pitTopDownPoseValid) &&
            portalCamera == entryCamera;

        private void ApplyPitTacticalView(Camera portalCamera)
        {
            bool topDown =
                pitReplayViewMode == PitReplayViewMode.TopDown;
            Pose pose = topDown
                ? pitTopDownPose
                : pitOverheadPose;
            portalCamera.enabled = true;
            portalCamera.transform.SetPositionAndRotation(
                pose.position,
                pose.rotation);
            portalCamera.orthographic = false;
            portalCamera.usePhysicalProperties = false;
            portalCamera.fieldOfView = topDown
                ? PitTopDownFieldOfView
                : PitOverheadFieldOfView;
            portalCamera.nearClipPlane = PitOverheadNearClip;
            portalCamera.farClipPlane = Mathf.Max(
                PitOverheadNearClip + 1f,
                viewerCamera.farClipPlane);
            RenderTexture texture = portalCamera.targetTexture;
            if (texture != null && texture.height > 0)
            {
                portalCamera.aspect =
                    texture.width / (float)texture.height;
            }
            portalCamera.ResetProjectionMatrix();
        }

        private static bool TryResolvePitTacticalPoses(
            Transform stage,
            out Pose overheadPose,
            out Pose topDownPose)
        {
            overheadPose = default;
            topDownPose = default;
            if (stage == null)
                return false;

            Transform[] transforms =
                stage.GetComponentsInChildren<Transform>(true);
            Dictionary<string, Transform> framing = new(
                PitOverheadFramingNames.Length);
            Transform origin = null;
            Transform frontLeftTyre = null;
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == null)
                    continue;

                if (candidate.name == PitChoreographyOriginName)
                    origin = candidate;
                else if (candidate.name == FrontLeftTyreName)
                    frontLeftTyre = candidate;

                for (int j = 0;
                     j < PitOverheadFramingNames.Length;
                     j++)
                {
                    string framingName =
                        PitOverheadFramingNames[j];
                    if (candidate.name == framingName)
                    {
                        framing[framingName] = candidate;
                        break;
                    }
                }
            }

            if (origin == null ||
                framing.Count != PitOverheadFramingNames.Length)
            {
                return false;
            }

            Bounds bounds = new(origin.position, Vector3.zero);
            for (int i = 0;
                 i < PitOverheadFramingNames.Length;
                 i++)
            {
                bounds.Encapsulate(
                    framing[PitOverheadFramingNames[i]].position);
            }

            float tyreDiameter = ResolvePitOverheadTyreDiameter(
                frontLeftTyre);
            bounds.Expand(new Vector3(
                tyreDiameter * 2.25f,
                tyreDiameter * 3.5f,
                tyreDiameter * 2.25f));
            Vector3 vehicleUp = origin.up;
            Vector3 target = bounds.center +
                vehicleUp * tyreDiameter * 0.12f;
            Vector3 viewDirection = (
                vehicleUp * 1.6f +
                origin.forward * 0.5f -
                origin.right * 0.866f).normalized;
            float radius = Mathf.Max(
                bounds.extents.magnitude,
                tyreDiameter * 3f);
            float halfFov =
                PitOverheadFieldOfView * Mathf.Deg2Rad * 0.5f;
            float distance =
                radius /
                Mathf.Sin(halfFov) *
                PitOverheadFramingPadding;
            Vector3 overheadPosition =
                target + viewDirection * distance;
            Vector3 overheadForward =
                (target - overheadPosition).normalized;
            float topDownHalfFov =
                PitTopDownFieldOfView * Mathf.Deg2Rad * 0.5f;
            float topDownDistance =
                radius /
                Mathf.Sin(topDownHalfFov) *
                PitTopDownFramingPadding;
            Vector3 topDownPosition =
                target + vehicleUp * topDownDistance;
            Vector3 topDownForward = -vehicleUp;
            if (!IsFinite(overheadPosition) ||
                !IsFinite(overheadForward) ||
                overheadForward.sqrMagnitude <= 0.5f ||
                !IsFinite(topDownPosition) ||
                !IsFinite(topDownForward) ||
                topDownForward.sqrMagnitude <= 0.5f)
            {
                return false;
            }

            overheadPose = new Pose(
                overheadPosition,
                Quaternion.LookRotation(
                    overheadForward,
                    vehicleUp));
            topDownPose = new Pose(
                topDownPosition,
                Quaternion.LookRotation(
                    topDownForward,
                    origin.right));
            return true;
        }

        private static float ResolvePitOverheadTyreDiameter(
            Transform tyre)
        {
            if (tyre == null)
                return 0.72f;

            Renderer[] renderers =
                tyre.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds bounds = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds
                ? Mathf.Max(
                    bounds.size.x,
                    Mathf.Max(bounds.size.y, bounds.size.z))
                : 0.72f;
        }
    }
}
