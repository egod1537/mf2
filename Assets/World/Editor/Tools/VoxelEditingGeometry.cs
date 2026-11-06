using System;
using System.Collections.Generic;
using MF2.World.Core;
using Unity.Mathematics;
using UnityEngine;

namespace MF2.World.Editor.Tools
{
    internal readonly struct VoxelRayHit
    {
        internal readonly int3 Position;
        internal readonly int3 Normal;
        internal readonly float Distance;

        internal VoxelRayHit(int3 position, int3 normal, float distance)
        {
            Position = position;
            Normal = normal;
            Distance = distance;
        }
    }

    internal static class VoxelEditingGeometry
    {
        private const float Epsilon = 0.000001f;

        internal static bool TryRaycast(
            WorldHost host,
            Ray worldRay,
            float maxDistance,
            out VoxelRayHit hit)
        {
            hit = default;
            if (host == null || !host.IsGridTransformValid || maxDistance <= 0f)
                return false;

            var localOrigin = host.transform.InverseTransformPoint(worldRay.origin);
            var localDirection = host.transform.InverseTransformDirection(worldRay.direction).normalized;
            if (localDirection.sqrMagnitude < Epsilon)
                return false;

            var gridOrigin = localOrigin + Vector3.one * 0.5f;
            var cell = new int3(
                Mathf.FloorToInt(gridOrigin.x),
                Mathf.FloorToInt(gridOrigin.y),
                Mathf.FloorToInt(gridOrigin.z));
            var entryNormal = InitialNormal(localDirection);
            if (!host.GetBlockOrAir(cell).IsAir)
            {
                hit = new VoxelRayHit(cell, entryNormal, 0f);
                return true;
            }

            var stepX = localDirection.x > 0f ? 1 : localDirection.x < 0f ? -1 : 0;
            var stepY = localDirection.y > 0f ? 1 : localDirection.y < 0f ? -1 : 0;
            var stepZ = localDirection.z > 0f ? 1 : localDirection.z < 0f ? -1 : 0;
            var tDeltaX = stepX == 0 ? float.PositiveInfinity : Mathf.Abs(1f / localDirection.x);
            var tDeltaY = stepY == 0 ? float.PositiveInfinity : Mathf.Abs(1f / localDirection.y);
            var tDeltaZ = stepZ == 0 ? float.PositiveInfinity : Mathf.Abs(1f / localDirection.z);
            var tMaxX = FirstBoundaryDistance(gridOrigin.x, cell.x, stepX, localDirection.x);
            var tMaxY = FirstBoundaryDistance(gridOrigin.y, cell.y, stepY, localDirection.y);
            var tMaxZ = FirstBoundaryDistance(gridOrigin.z, cell.z, stepZ, localDirection.z);
            var maxSteps = Mathf.CeilToInt(maxDistance * 3f) + 3;

            for (var i = 0; i < maxSteps; i++)
            {
                float distance;
                if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
                {
                    cell.x += stepX;
                    distance = tMaxX;
                    tMaxX += tDeltaX;
                    entryNormal = new int3(-stepX, 0, 0);
                }
                else if (tMaxY <= tMaxZ)
                {
                    cell.y += stepY;
                    distance = tMaxY;
                    tMaxY += tDeltaY;
                    entryNormal = new int3(0, -stepY, 0);
                }
                else
                {
                    cell.z += stepZ;
                    distance = tMaxZ;
                    tMaxZ += tDeltaZ;
                    entryNormal = new int3(0, 0, -stepZ);
                }

                if (distance > maxDistance)
                    return false;
                if (host.GetBlockOrAir(cell).IsAir)
                    continue;

                hit = new VoxelRayHit(cell, entryNormal, distance);
                return true;
            }

            return false;
        }

        internal static bool TryIntersectWorkPlane(
            WorldHost host,
            Ray worldRay,
            WorkPlaneAxis axis,
            int planeOffset,
            float maxDistance,
            out int3 position)
        {
            position = default;
            if (host == null || !host.IsGridTransformValid)
                return false;

            var origin = host.transform.InverseTransformPoint(worldRay.origin);
            var direction = host.transform.InverseTransformDirection(worldRay.direction).normalized;
            float originAxis;
            float directionAxis;
            switch (axis)
            {
                case WorkPlaneAxis.XY:
                    originAxis = origin.z;
                    directionAxis = direction.z;
                    break;
                case WorkPlaneAxis.YZ:
                    originAxis = origin.x;
                    directionAxis = direction.x;
                    break;
                default:
                    originAxis = origin.y;
                    directionAxis = direction.y;
                    break;
            }

            if (Mathf.Abs(directionAxis) < Epsilon)
                return false;
            var distance = (planeOffset - originAxis) / directionAxis;
            if (distance < 0f || distance > maxDistance)
                return false;

            var point = origin + direction * distance;
            switch (axis)
            {
                case WorkPlaneAxis.XY:
                    position = new int3(
                        Mathf.RoundToInt(point.x),
                        Mathf.RoundToInt(point.y),
                        planeOffset);
                    break;
                case WorkPlaneAxis.YZ:
                    position = new int3(
                        planeOffset,
                        Mathf.RoundToInt(point.y),
                        Mathf.RoundToInt(point.z));
                    break;
                default:
                    position = new int3(
                        Mathf.RoundToInt(point.x),
                        planeOffset,
                        Mathf.RoundToInt(point.z));
                    break;
            }

            return true;
        }

        private static float FirstBoundaryDistance(
            float origin,
            int cell,
            int step,
            float direction)
        {
            if (step == 0)
                return float.PositiveInfinity;
            var boundary = step > 0 ? cell + 1f : cell;
            return (boundary - origin) / direction;
        }

        private static int3 InitialNormal(Vector3 direction)
        {
            var x = Mathf.Abs(direction.x);
            var y = Mathf.Abs(direction.y);
            var z = Mathf.Abs(direction.z);
            if (x >= y && x >= z)
                return new int3(direction.x > 0f ? -1 : 1, 0, 0);
            if (y >= z)
                return new int3(0, direction.y > 0f ? -1 : 1, 0);
            return new int3(0, 0, direction.z > 0f ? -1 : 1);
        }
    }

    internal static class GridStrokeRasterizer
    {
        internal const int MaxCellsPerSegment = 65_536;

        internal static bool TryFill(int3 from, int3 to, List<int3> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();
            var deltaX = (long)to.x - from.x;
            var deltaY = (long)to.y - from.y;
            var deltaZ = (long)to.z - from.z;
            var steps = Math.Max(Math.Abs(deltaX), Math.Max(Math.Abs(deltaY), Math.Abs(deltaZ)));
            if (steps > MaxCellsPerSegment)
                return false;
            if (steps == 0)
            {
                destination.Add(from);
                return true;
            }

            var previous = new int3(int.MinValue);
            for (long i = 0; i <= steps; i++)
            {
                var t = (double)i / steps;
                var current = new int3(
                    (int)Math.Round(from.x + deltaX * t, MidpointRounding.AwayFromZero),
                    (int)Math.Round(from.y + deltaY * t, MidpointRounding.AwayFromZero),
                    (int)Math.Round(from.z + deltaZ * t, MidpointRounding.AwayFromZero));
                if (current.Equals(previous))
                    continue;
                destination.Add(current);
                previous = current;
            }

            return true;
        }
    }
}
