using UnityEngine;
using UnityEngine.InputSystem;

namespace MF2.Entities.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class OfflineBlockPlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float walkSpeed = 4f;
        [SerializeField, Min(0.1f)] private float sprintSpeed = 6f;
        [SerializeField, Min(0.1f)] private float jumpVelocity = 6.5f;
        [SerializeField, Min(0.01f)] private float groundProbeDistance = 0.12f;

        [Header("Camera Relative Movement")]
        [SerializeField] private Camera movementCamera;

        [Header("Block Rig")]
        [SerializeField] private Transform leftArmPivot;
        [SerializeField] private Transform rightArmPivot;
        [SerializeField] private Transform leftLegPivot;
        [SerializeField] private Transform rightLegPivot;
        [SerializeField, Range(0f, 60f)] private float limbSwingAngle = 30f;
        [SerializeField, Min(0.1f)] private float limbSwingSpeed = 8f;

        private readonly RaycastHit[] groundHits = new RaycastHit[8];
        private Rigidbody body;
        private CapsuleCollider capsule;
        private Vector2 moveInput;
        private bool sprintInput;
        private bool jumpQueued;
        private float stepCycle;

        public bool IsGrounded { get; private set; }

        public void ConfigureRig(Transform leftArm, Transform rightArm, Transform leftLeg, Transform rightLeg)
        {
            leftArmPivot = leftArm;
            rightArmPivot = rightArm;
            leftLegPivot = leftLeg;
            rightLegPivot = rightLeg;
        }

        public void ConfigureMovementCamera(Camera camera) => movementCamera = camera;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            body.useGravity = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezeRotation;
        }

        private void Start()
        {
            if (movementCamera == null || !movementCamera.isActiveAndEnabled)
                movementCamera = FindSceneCamera();
        }

        private void Update()
        {
            ReadInput();
            var horizontalSpeed = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z).magnitude;
            AnimateRig(Mathf.Clamp01(horizontalSpeed / Mathf.Max(0.01f, sprintSpeed)));
        }

        private void FixedUpdate()
        {
            IsGrounded = CheckGrounded();
            var speed = sprintInput ? sprintSpeed : walkSpeed;
            var worldMove = GetWorldMove();
            var velocity = body.linearVelocity;
            velocity.x = worldMove.x * speed;
            velocity.z = worldMove.z * speed;
            if (jumpQueued && IsGrounded)
                velocity.y = jumpVelocity;

            jumpQueued = false;
            body.linearVelocity = velocity;
        }

        private void ReadInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                moveInput = Vector2.zero;
                sprintInput = false;
                return;
            }

            moveInput = new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
            sprintInput = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            if (keyboard.spaceKey.wasPressedThisFrame)
                jumpQueued = true;
        }

        private Vector3 GetWorldMove()
        {
            var local = Vector2.ClampMagnitude(moveInput, 1f);
            var forward = movementCamera == null
                ? Vector3.forward
                : Vector3.ProjectOnPlane(movementCamera.transform.forward, Vector3.up).normalized;
            var right = movementCamera == null
                ? Vector3.right
                : Vector3.ProjectOnPlane(movementCamera.transform.right, Vector3.up).normalized;
            return Vector3.ClampMagnitude(forward * local.y + right * local.x, 1f);
        }

        private Camera FindSceneCamera()
        {
            foreach (var sceneCamera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (sceneCamera.gameObject.scene == gameObject.scene)
                    return sceneCamera;
            }

            return null;
        }

        private bool CheckGrounded()
        {
            var scale = transform.lossyScale;
            var worldRadius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            var halfHeight = Mathf.Max(capsule.height * Mathf.Abs(scale.y) * 0.5f, worldRadius);
            var center = transform.TransformPoint(capsule.center);
            var bottom = center - transform.up * (halfHeight - worldRadius);
            var count = Physics.SphereCastNonAlloc(
                bottom + transform.up * groundProbeDistance,
                worldRadius * 0.9f,
                -transform.up,
                groundHits,
                groundProbeDistance + worldRadius * 0.12f,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.collider == capsule || hit.rigidbody == body)
                    continue;
                if (Vector3.Dot(hit.normal, transform.up) >= 0.55f)
                    return true;
            }

            return false;
        }

        private void AnimateRig(float moveAmount)
        {
            if (moveAmount > 0.01f)
                stepCycle += Time.deltaTime * limbSwingSpeed;
            var swing = Mathf.Sin(stepCycle) * limbSwingAngle * moveAmount;
            var blend = 1f - Mathf.Exp(-12f * Time.deltaTime);
            SetLimbRotation(leftArmPivot, Quaternion.Euler(swing, 0f, 0f), blend);
            SetLimbRotation(rightArmPivot, Quaternion.Euler(-swing, 0f, 0f), blend);
            SetLimbRotation(leftLegPivot, Quaternion.Euler(-swing, 0f, 0f), blend);
            SetLimbRotation(rightLegPivot, Quaternion.Euler(swing, 0f, 0f), blend);
        }

        private static void SetLimbRotation(Transform limb, Quaternion target, float blend)
        {
            if (limb != null)
                limb.localRotation = Quaternion.Slerp(limb.localRotation, target, blend);
        }
    }
}
