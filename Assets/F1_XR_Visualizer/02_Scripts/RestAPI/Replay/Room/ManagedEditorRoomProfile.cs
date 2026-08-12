using System;
using System.Collections.Generic;
using UnityEngine;

namespace F1XR.RestAPI.Replay.Room
{
    internal static class ManagedEditorRoomProfile
    {
        private static readonly Guid RoomId =
            new("70000000-0000-0000-0000-000000000001");
        private static readonly Guid FloorId =
            new("70000000-0000-0000-0000-000000000002");
        private static readonly Guid CeilingId =
            new("70000000-0000-0000-0000-000000000003");
        private static readonly Guid FrontWallId =
            new("70000000-0000-0000-0000-000000000004");
        private static readonly Guid BackWallId =
            new("70000000-0000-0000-0000-000000000005");
        private static readonly Guid LeftWallId =
            new("70000000-0000-0000-0000-000000000006");
        private static readonly Guid RightWallId =
            new("70000000-0000-0000-0000-000000000007");
        private static readonly Guid TableId =
            new("70000000-0000-0000-0000-000000000008");

        private const float RoomWidth = 5f;
        private const float RoomDepth = 5f;
        private const float RoomHeight = 2.5f;
        private const float TableWidth = 1.4f;
        private const float TableDepth = 0.8f;
        private const float TableHeight = 0.74f;
        private const float TableForwardDistance = 1.15f;

        private static Transform cachedOrigin;
        private static MetaSceneRoomSnapshot cachedRoom;

        internal static MetaSceneRoomSnapshot GetOrCreate(
            Transform xrOrigin,
            Transform viewer)
        {
            if (cachedRoom != null && cachedOrigin == xrOrigin)
                return cachedRoom;

            cachedOrigin = xrOrigin;
            cachedRoom = Build(xrOrigin, viewer);
            return cachedRoom;
        }

        internal static void Reset()
        {
            cachedOrigin = null;
            cachedRoom = null;
        }

        private static MetaSceneRoomSnapshot Build(
            Transform xrOrigin,
            Transform viewer)
        {
            Vector3 up = ResolveUp(xrOrigin);
            Vector3 forward = ResolveForward(xrOrigin, viewer, up);
            Vector3 right = Vector3.Cross(up, forward).normalized;
            Vector3 floorCenter = xrOrigin != null
                ? xrOrigin.position
                : ProjectViewerToFloor(viewer, up);
            Vector3 roomCenter = floorCenter + up * (RoomHeight * 0.5f);

            var walls = new List<MetaSceneSurfaceSnapshot>(4)
            {
                CreateWall(
                    FrontWallId,
                    roomCenter + forward * (RoomDepth * 0.5f),
                    -forward,
                    up,
                    RoomWidth,
                    RoomHeight),
                CreateWall(
                    BackWallId,
                    roomCenter - forward * (RoomDepth * 0.5f),
                    forward,
                    up,
                    RoomWidth,
                    RoomHeight),
                CreateWall(
                    LeftWallId,
                    roomCenter - right * (RoomWidth * 0.5f),
                    right,
                    up,
                    RoomDepth,
                    RoomHeight),
                CreateWall(
                    RightWallId,
                    roomCenter + right * (RoomWidth * 0.5f),
                    -right,
                    up,
                    RoomDepth,
                    RoomHeight)
            };

            var floors = new List<MetaSceneSurfaceSnapshot>(1)
            {
                CreateHorizontalSurface(
                    FloorId,
                    MetaSceneSurfaceKind.Floor,
                    OVRSemanticLabels.Classification.Floor,
                    floorCenter,
                    up,
                    right,
                    forward,
                    RoomWidth,
                    RoomDepth)
            };
            var ceilings = new List<MetaSceneSurfaceSnapshot>(1)
            {
                CreateHorizontalSurface(
                    CeilingId,
                    MetaSceneSurfaceKind.Ceiling,
                    OVRSemanticLabels.Classification.Ceiling,
                    floorCenter + up * RoomHeight,
                    -up,
                    right,
                    -forward,
                    RoomWidth,
                    RoomDepth)
            };

            Vector3 tableCenter = ResolveTableCenter(
                floorCenter,
                viewer,
                up,
                forward,
                right);
            var tables = new List<MetaSceneSurfaceSnapshot>(1)
            {
                CreateHorizontalSurface(
                    TableId,
                    MetaSceneSurfaceKind.Table,
                    OVRSemanticLabels.Classification.Table,
                    tableCenter + up * TableHeight,
                    up,
                    right,
                    forward,
                    TableWidth,
                    TableDepth)
            };

            return new MetaSceneRoomSnapshot(
                RoomId,
                walls,
                floors,
                ceilings,
                tables);
        }

        private static MetaSceneSurfaceSnapshot CreateWall(
            Guid id,
            Vector3 center,
            Vector3 inwardNormal,
            Vector3 up,
            float width,
            float height)
        {
            Vector3 normal = inwardNormal.normalized;
            Vector3 vertical = Vector3.ProjectOnPlane(up, normal).normalized;
            Vector3 horizontal = Vector3.Cross(vertical, normal).normalized;
            return CreateSurface(
                id,
                MetaSceneSurfaceKind.Wall,
                OVRSemanticLabels.Classification.WallFace,
                center,
                normal,
                horizontal,
                vertical,
                width,
                height);
        }

        private static MetaSceneSurfaceSnapshot CreateHorizontalSurface(
            Guid id,
            MetaSceneSurfaceKind kind,
            OVRSemanticLabels.Classification classification,
            Vector3 center,
            Vector3 normal,
            Vector3 right,
            Vector3 forward,
            float width,
            float depth)
        {
            Vector3 normalizedNormal = normal.normalized;
            Vector3 horizontal = Vector3.ProjectOnPlane(
                right,
                normalizedNormal).normalized;
            Vector3 vertical = Vector3.Cross(
                normalizedNormal,
                horizontal).normalized;
            if (Vector3.Dot(vertical, forward) < 0f)
                vertical = -vertical;

            horizontal = Vector3.Cross(vertical, normalizedNormal).normalized;
            return CreateSurface(
                id,
                kind,
                classification,
                center,
                normalizedNormal,
                horizontal,
                vertical,
                width,
                depth);
        }

        private static MetaSceneSurfaceSnapshot CreateSurface(
            Guid id,
            MetaSceneSurfaceKind kind,
            OVRSemanticLabels.Classification classification,
            Vector3 center,
            Vector3 normal,
            Vector3 horizontal,
            Vector3 vertical,
            float width,
            float height)
        {
            var rect = new Rect(
                -width * 0.5f,
                -height * 0.5f,
                width,
                height);
            var localBoundary = new List<Vector2>(4)
            {
                rect.min,
                new(rect.xMin, rect.yMax),
                rect.max,
                new(rect.xMax, rect.yMin)
            };
            var worldBoundary = new List<Vector3>(localBoundary.Count);
            for (int i = 0; i < localBoundary.Count; i++)
            {
                Vector2 point = localBoundary[i];
                worldBoundary.Add(
                    center + horizontal * point.x + vertical * point.y);
            }

            Quaternion rotation = Quaternion.LookRotation(normal, vertical);
            Matrix4x4 localToWorld = Matrix4x4.TRS(
                center,
                rotation,
                Vector3.one);
            return new MetaSceneSurfaceSnapshot(
                id,
                kind,
                classification,
                center,
                rotation,
                normal,
                horizontal,
                vertical,
                rect,
                worldBoundary,
                localBoundary,
                localToWorld.inverse);
        }

        private static Vector3 ResolveUp(Transform xrOrigin)
        {
            Vector3 up = xrOrigin != null ? xrOrigin.up : Vector3.up;
            return up.sqrMagnitude > 0.5f ? up.normalized : Vector3.up;
        }

        private static Vector3 ResolveForward(
            Transform xrOrigin,
            Transform viewer,
            Vector3 up)
        {
            Vector3 forward = xrOrigin != null
                ? Vector3.ProjectOnPlane(xrOrigin.forward, up)
                : Vector3.zero;
            if (forward.sqrMagnitude < 0.0001f && viewer != null)
                forward = Vector3.ProjectOnPlane(viewer.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(Vector3.forward, up);
            return forward.normalized;
        }

        private static Vector3 ResolveTableCenter(
            Vector3 floorCenter,
            Transform viewer,
            Vector3 up,
            Vector3 roomForward,
            Vector3 roomRight)
        {
            Vector3 viewerFloor = viewer != null
                ? viewer.position -
                    up * Vector3.Dot(viewer.position - floorCenter, up)
                : floorCenter;
            Vector3 viewerForward = viewer != null
                ? Vector3.ProjectOnPlane(viewer.forward, up).normalized
                : roomForward;
            if (viewerForward.sqrMagnitude < 0.0001f)
                viewerForward = roomForward;

            Vector3 desired = viewerFloor +
                viewerForward * TableForwardDistance;
            Vector3 offset = desired - floorCenter;
            float maxX = RoomWidth * 0.5f - TableWidth * 0.5f - 0.25f;
            float maxZ = RoomDepth * 0.5f - TableDepth * 0.5f - 0.25f;
            return floorCenter +
                roomRight * Mathf.Clamp(
                    Vector3.Dot(offset, roomRight),
                    -maxX,
                    maxX) +
                roomForward * Mathf.Clamp(
                    Vector3.Dot(offset, roomForward),
                    -maxZ,
                    maxZ);
        }

        private static Vector3 ProjectViewerToFloor(
            Transform viewer,
            Vector3 up)
        {
            if (viewer == null)
                return Vector3.zero;
            return viewer.position - up * Vector3.Dot(viewer.position, up);
        }
    }
}
