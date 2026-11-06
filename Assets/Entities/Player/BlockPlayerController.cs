using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MF2.Entities.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(NetworkObject))]
    public sealed class BlockPlayerController : TickNetworkBehaviour
    {
        public struct MoveData : IReplicateData
        {
            public Vector2 WorldMove;
            public bool Sprint;
            public bool Jump;
            private uint tick;

            public MoveData(Vector2 worldMove, bool sprint, bool jump)
            {
                WorldMove = worldMove;
                Sprint = sprint;
                Jump = jump;
                tick = 0;
            }

            public void Dispose() { }
            public uint GetTick() => tick;
            public void SetTick(uint value) => tick = value;
        }

        public struct ReconcileData : IReconcileData
        {
            public PredictionRigidbody Body;
            private uint tick;

            public ReconcileData(PredictionRigidbody body)
            {
                Body = body;
                tick = 0;
            }

            public void Dispose() { }
            public uint GetTick() => tick;
            public void SetTick(uint value) => tick = value;
        }

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float walkSpeed = 4f;
        [SerializeField, Min(0.1f)] private float sprintSpeed = 6f;
        [SerializeField, Min(0.1f)] private float jumpVelocity = 6.5f;
        [SerializeField, Min(0.01f)] private float groundProbeDistance = 0.12f;

        [Header("Owner Presentation")]
        [SerializeField] private Camera ownerCamera;
        [SerializeField] private AudioListener ownerAudioListener;

        [Header("Block Rig")]
        [SerializeField] private Transform leftArmPivot;
        [SerializeField] private Transform rightArmPivot;
        [SerializeField] private Transform leftLegPivot;
        [SerializeField] private Transform rightLegPivot;
        [SerializeField, Range(0f, 60f)] private float limbSwingAngle = 30f;
        [SerializeField, Min(0.1f)] private float limbSwingSpeed = 8f;

        private readonly RaycastHit[] groundHits = new RaycastHit[8];
        private readonly PredictionRigidbody predictionBody = new();
        private Rigidbody body;
        private CapsuleCollider capsule;
        private Vector2 moveInput;
        private bool sprintInput;
        private bool jumpQueued;
        private bool offlineControl;
        private float stepCycle;

        public bool IsGrounded { get; private set; }

        public void ConfigureRig(Transform leftArm, Transform rightArm, Transform leftLeg, Transform rightLeg)
        {
            leftArmPivot = leftArm;
            rightArmPivot = rightArm;
            leftLegPivot = leftLeg;
            rightLegPivot = rightLeg;
        }

        public void ConfigureOwnerCamera(Camera camera, AudioListener listener)
        {
            ownerCamera = camera;
            ownerAudioListener = listener;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            body.useGravity = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            predictionBody.Initialize(body);
            SetOwnerPresentation(false);
        }

        private void Start()
        {
            if (IsSpawned)
                return;

            offlineControl = true;
            if (FindActiveSceneCamera() == null)
                SetOwnerPresentation(true);
        }

        public override void OnStartNetwork()
        {
            offlineControl = false;
            SetOwnerPresentation(false);
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner && ownerCamera != null)
            {
                foreach (var sceneCamera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (sceneCamera != ownerCamera && sceneCamera.gameObject.scene == gameObject.scene)
                        sceneCamera.gameObject.SetActive(false);
                }
            }
            SetOwnerPresentation(IsOwner);
        }

        public override void OnStopClient()
        {
            SetOwnerPresentation(false);
            base.OnStopClient();
        }

        private void Update()
        {
            if (offlineControl || IsOwner)
                ReadInput();

            var horizontal = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z).magnitude;
            AnimateRig(Mathf.Clamp01(horizontal / Mathf.Max(0.01f, sprintSpeed)));
        }

        private void FixedUpdate()
        {
            if (!offlineControl)
                return;

            ApplyVelocity(BuildLocalMoveData(), false);
        }

        protected override void TimeManager_OnTick()
        {
            PerformReplicate(BuildMoveData());
        }

        protected override void TimeManager_OnPostTick()
        {
            CreateReconcile();
        }

        private MoveData BuildMoveData()
        {
            if (!IsOwner)
                return default;

            return BuildLocalMoveData();
        }

        private MoveData BuildLocalMoveData()
        {
            var local = Vector2.ClampMagnitude(moveInput, 1f);
            var movementCamera = offlineControl ? FindActiveSceneCamera() : ownerCamera;
            var forward = movementCamera == null
                ? Vector3.forward
                : Vector3.ProjectOnPlane(movementCamera.transform.forward, Vector3.up).normalized;
            var right = movementCamera == null
                ? Vector3.right
                : Vector3.ProjectOnPlane(movementCamera.transform.right, Vector3.up).normalized;
            var world = Vector3.ClampMagnitude(forward * local.y + right * local.x, 1f);
            var data = new MoveData(new Vector2(world.x, world.z), sprintInput, jumpQueued);
            jumpQueued = false;
            return data;
        }

        [Replicate]
        private void PerformReplicate(MoveData data, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            ApplyVelocity(data, true);
        }

        private void ApplyVelocity(MoveData data, bool predicted)
        {
            IsGrounded = CheckGrounded();
            var speed = data.Sprint ? sprintSpeed : walkSpeed;
            var velocity = body.linearVelocity;
            velocity.x = data.WorldMove.x * speed;
            velocity.z = data.WorldMove.y * speed;
            if (data.Jump && IsGrounded)
                velocity.y = jumpVelocity;

            if (predicted)
            {
                predictionBody.Velocity(velocity);
                predictionBody.Simulate();
            }
            else
            {
                body.linearVelocity = velocity;
            }
        }

        public override void CreateReconcile()
        {
            PerformReconcile(new ReconcileData(predictionBody));
        }

        [Reconcile]
        private void PerformReconcile(ReconcileData data, Channel channel = Channel.Unreliable)
        {
            predictionBody.Reconcile(data.Body);
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

        private void SetOwnerPresentation(bool active)
        {
            if (ownerCamera != null)
                ownerCamera.gameObject.SetActive(active);
            if (ownerAudioListener != null)
                ownerAudioListener.enabled = active;
        }

        private Camera FindActiveSceneCamera()
        {
            foreach (var sceneCamera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (sceneCamera != ownerCamera && sceneCamera.gameObject.scene == gameObject.scene)
                    return sceneCamera;
            }

            return ownerCamera != null && ownerCamera.isActiveAndEnabled ? ownerCamera : null;
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
