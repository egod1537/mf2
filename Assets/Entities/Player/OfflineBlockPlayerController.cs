using UnityEngine;

namespace MF2.Entities.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    [RequireComponent(typeof(BlockPlayerInputSource), typeof(BlockPlayerView))]
    public sealed class OfflineBlockPlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float walkSpeed = 4f;
        [SerializeField, Min(0.1f)] private float sprintSpeed = 6f;
        [SerializeField, Min(0.1f)] private float jumpVelocity = 6.5f;
        [SerializeField, Min(0.01f)] private float groundProbeDistance = 0.12f;

        [Header("MVC")]
        [SerializeField] private BlockPlayerInputSource inputSource;
        [SerializeField] private BlockPlayerView view;

        private readonly RaycastHit[] groundHits = new RaycastHit[8];
        private Rigidbody body;
        private CapsuleCollider capsule;

        public bool IsGrounded { get; private set; }

        public void ConfigureRig(
            Transform leftArm,
            Transform rightArm,
            Transform leftLeg,
            Transform rightLeg) =>
            ResolveView().ConfigureRig(leftArm, rightArm, leftLeg, rightLeg);

        public void ConfigureMovementCamera(Camera camera) =>
            ResolveInput().ConfigureMovementCamera(camera);

        public void ConfigureMvc(
            BlockPlayerInputSource playerInput,
            BlockPlayerView playerView)
        {
            inputSource = playerInput;
            view = playerView;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            body.useGravity = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            ResolveInput();
            ResolveView().Apply(new PlayerPresentationState(
                default,
                1f,
                MF2.Game.Domain.PlayerLifeState.Alive,
                0));
        }

        private void Start()
        {
            ResolveInput().UseSceneCameraIfUnset();
            ResolveView().SetLocalOwner(true, true);
        }

        private void Update()
        {
            ResolveInput().Capture();
        }

        private void FixedUpdate()
        {
            IsGrounded = CheckGrounded();
            var input = ResolveInput().Consume();
            var speed = input.Sprint ? sprintSpeed : walkSpeed;
            var velocity = body.linearVelocity;
            velocity.x = input.WorldMove.x * speed;
            velocity.z = input.WorldMove.y * speed;
            if (input.Jump && IsGrounded)
                velocity.y = jumpVelocity;
            body.linearVelocity = velocity;
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

        private BlockPlayerInputSource ResolveInput() =>
            inputSource != null ? inputSource : inputSource = GetComponent<BlockPlayerInputSource>();

        private BlockPlayerView ResolveView() =>
            view != null ? view : view = GetComponent<BlockPlayerView>();
    }
}
