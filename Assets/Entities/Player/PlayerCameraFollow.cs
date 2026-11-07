using UnityEngine;

namespace MF2.Entities.Player
{
    /// <summary>
    /// Presentation-only camera follower. The rig is detached while active so a FishNet physics
    /// tick cannot teleport the camera before LateUpdate; Rigidbody and prediction transforms are
    /// never modified by this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        // The target is already FishNet's smoothed graphical object. Zero avoids a second
        // interpolation filter while still moving the detached camera at the LateUpdate boundary.
        [SerializeField, Min(0f)] private float positionSmoothTime;
        [SerializeField, Min(0.01f)] private float teleportDistance = 8f;

        private Transform authoredParent;
        private Vector3 targetSpaceOffset;
        private Vector3 velocity;
        private bool detached;
        private bool snapPending = true;
        private Vector3 previousFramePosition;
        private Vector3 previousFrameDelta;
        private double accumulatedTrackingError;

        public Transform Target => target;
        public float PositionSmoothTime => positionSmoothTime;
        public long TelemetrySamples { get; private set; }
        public double MeanTrackingError => TelemetrySamples == 0
            ? 0d
            : accumulatedTrackingError / TelemetrySamples;
        public float MaximumTrackingError { get; private set; }
        public int DirectionReversals { get; private set; }

        public void Configure(Transform followTarget, float smoothTime = 0f)
        {
            target = followTarget != null
                ? followTarget
                : throw new System.ArgumentNullException(nameof(followTarget));
            positionSmoothTime = Mathf.Max(0f, smoothTime);
        }

        public void SnapNextFrame()
        {
            velocity = Vector3.zero;
            snapPending = true;
        }

        /// <summary>
        /// Reattaches the presentation camera before its GameObject is deactivated. Unity does not
        /// allow changing the hierarchy from OnDisable while SetActive is already walking it.
        /// </summary>
        public void RestoreAuthoredParent()
        {
            if (!detached)
                return;

            detached = false;
            if (authoredParent != null)
                transform.SetParent(authoredParent, true);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || target == null)
                return;

            if (!detached)
            {
                authoredParent = transform.parent;
                targetSpaceOffset = target.InverseTransformPoint(transform.position);
                transform.SetParent(null, true);
                detached = true;
            }
            previousFramePosition = transform.position;
            SnapNextFrame();
        }

        private void LateUpdate()
        {
            if (!detached || target == null)
                return;

            var desired = target.TransformPoint(targetSpaceOffset);
            if (snapPending || (desired - transform.position).sqrMagnitude >=
                teleportDistance * teleportDistance)
            {
                transform.position = desired;
                velocity = Vector3.zero;
                snapPending = false;
                RecordTelemetry(desired);
                return;
            }

            transform.position = positionSmoothTime <= 0f
                ? desired
                : Vector3.SmoothDamp(
                    transform.position,
                    desired,
                    ref velocity,
                    positionSmoothTime,
                    Mathf.Infinity,
                    Time.unscaledDeltaTime);
            RecordTelemetry(desired);
        }

        public void ResetTelemetry()
        {
            TelemetrySamples = 0;
            accumulatedTrackingError = 0d;
            MaximumTrackingError = 0f;
            DirectionReversals = 0;
            previousFrameDelta = Vector3.zero;
            previousFramePosition = transform.position;
        }

        private void RecordTelemetry(Vector3 desired)
        {
            var error = Vector3.Distance(transform.position, desired);
            accumulatedTrackingError += error;
            MaximumTrackingError = Mathf.Max(MaximumTrackingError, error);
            TelemetrySamples++;
            var delta = transform.position - previousFramePosition;
            if (previousFrameDelta.sqrMagnitude > 0.000001f &&
                delta.sqrMagnitude > 0.000001f &&
                Vector3.Dot(previousFrameDelta, delta) < 0f)
            {
                DirectionReversals++;
            }
            previousFrameDelta = delta;
            previousFramePosition = transform.position;
        }

        private void OnDisable()
        {
            velocity = Vector3.zero;
            snapPending = true;
        }
    }
}
