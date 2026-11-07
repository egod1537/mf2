using System;
using System.Collections.Generic;
using MF2.World.Core;
using MF2.World.Registry;
using Unity.Mathematics;
using UnityEngine;

namespace MF2.Game.Presentation
{
    /// <summary>
    /// Finds solid voxels between the local owner camera and player, then asks the borrowed
    /// client world view to render only those blocks with ProPixelizer ordered dithering.
    /// Authoritative world data and the solid collider mesh are never modified.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerOcclusionDither : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField, Min(0f)] private float targetCenterHeight = 0.96f;
        [SerializeField, Min(0f)] private float verticalExtent = 0.65f;
        [SerializeField, Min(0f)] private float horizontalExtent = 0.35f;
        [SerializeField, Range(1, 128)] private int maximumOccluders = 64;

        private readonly HashSet<int3> activeOccluders = new HashSet<int3>();
        private readonly HashSet<int3> nextOccluders = new HashSet<int3>();
        private readonly List<int3> trace = new List<int3>(64);
        private Camera ownerCamera;
        private WorldHost boundHost;

        public Transform Target => target;
        public int ActiveOccluderCount => activeOccluders.Count;

        public void Configure(Transform playerTarget)
        {
            target = playerTarget != null
                ? playerTarget
                : throw new ArgumentNullException(nameof(playerTarget));
        }

        private void Awake()
        {
            ownerCamera = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (target == null ||
                !WorldHost.TryGetActiveHost(out var host) ||
                !host.IsInitialized)
            {
                ClearOcclusion();
                return;
            }

            if (ownerCamera == null)
                ownerCamera = GetComponent<Camera>();

            nextOccluders.Clear();
            var center = target.position + target.up * targetCenterHeight;
            CollectOccluders(host, center);
            CollectOccluders(host, center + target.up * verticalExtent);
            CollectOccluders(host, center - target.up * verticalExtent);
            CollectOccluders(host, center + transform.right * horizontalExtent);
            CollectOccluders(host, center - transform.right * horizontalExtent);

            var hostChanged = !ReferenceEquals(boundHost, host);
            if (!hostChanged && activeOccluders.SetEquals(nextOccluders))
                return;

            if (hostChanged && boundHost != null)
                boundHost.SetDitheredOccluders(Array.Empty<int3>());

            boundHost = host;
            activeOccluders.Clear();
            activeOccluders.UnionWith(nextOccluders);
            boundHost.SetDitheredOccluders(activeOccluders);
        }

        private void OnDisable()
        {
            ClearOcclusion();
        }

        private void OnDestroy()
        {
            ClearOcclusion();
        }

        private void CollectOccluders(WorldHost host, Vector3 worldTarget)
        {
            if (nextOccluders.Count >= maximumOccluders)
                return;

            var distance = Vector3.Dot(worldTarget - transform.position, transform.forward);
            var worldStart = ownerCamera.orthographic && distance > 0f
                ? worldTarget - transform.forward * distance
                : transform.position;
            var localStart = host.transform.InverseTransformPoint(worldStart);
            var localTarget = host.transform.InverseTransformPoint(worldTarget);
            TraceVoxels(localStart, localTarget, trace);

            for (var i = 0; i < trace.Count && nextOccluders.Count < maximumOccluders; i++)
            {
                var coordinate = trace[i];
                if (BlockTraitsCatalog.IsSolid(host.GetBlockOrAir(coordinate)))
                    nextOccluders.Add(coordinate);
            }
        }

        private void ClearOcclusion()
        {
            if (boundHost != null)
                boundHost.SetDitheredOccluders(Array.Empty<int3>());
            boundHost = null;
            activeOccluders.Clear();
            nextOccluders.Clear();
            trace.Clear();
        }

        /// <summary>
        /// Traverses center-addressed unit voxels from start up to, but excluding, the end voxel.
        /// Excluding the end prevents the player's own occupied cell from becoming an occluder.
        /// </summary>
        public static void TraceVoxels(
            Vector3 start,
            Vector3 end,
            List<int3> results,
            int maximumSteps = 256)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));
            if (maximumSteps <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumSteps));

            results.Clear();
            var shiftedStart = start + Vector3.one * 0.5f;
            var shiftedEnd = end + Vector3.one * 0.5f;
            var direction = shiftedEnd - shiftedStart;
            var cell = FloorToInt3(shiftedStart);
            var endCell = FloorToInt3(shiftedEnd);
            if (cell.Equals(endCell))
                return;

            var step = new int3(Sign(direction.x), Sign(direction.y), Sign(direction.z));
            var delta = new Vector3(
                InverseAbs(direction.x),
                InverseAbs(direction.y),
                InverseAbs(direction.z));
            var maximum = new Vector3(
                InitialBoundaryDistance(shiftedStart.x, cell.x, step.x, direction.x),
                InitialBoundaryDistance(shiftedStart.y, cell.y, step.y, direction.y),
                InitialBoundaryDistance(shiftedStart.z, cell.z, step.z, direction.z));

            for (var iteration = 0; iteration < maximumSteps && !cell.Equals(endCell); iteration++)
            {
                results.Add(cell);
                if (maximum.x <= maximum.y && maximum.x <= maximum.z)
                {
                    cell.x += step.x;
                    maximum.x += delta.x;
                }
                else if (maximum.y <= maximum.z)
                {
                    cell.y += step.y;
                    maximum.y += delta.y;
                }
                else
                {
                    cell.z += step.z;
                    maximum.z += delta.z;
                }
            }
        }

        private static int3 FloorToInt3(Vector3 value) => new int3(
            Mathf.FloorToInt(value.x),
            Mathf.FloorToInt(value.y),
            Mathf.FloorToInt(value.z));

        private static int Sign(float value) => value > 0f ? 1 : value < 0f ? -1 : 0;

        private static float InverseAbs(float value) =>
            Mathf.Abs(value) > 0.000001f ? 1f / Mathf.Abs(value) : float.PositiveInfinity;

        private static float InitialBoundaryDistance(
            float start,
            int cell,
            int step,
            float direction)
        {
            if (step == 0)
                return float.PositiveInfinity;
            var boundary = step > 0 ? cell + 1f : cell;
            return (boundary - start) / direction;
        }
    }
}
