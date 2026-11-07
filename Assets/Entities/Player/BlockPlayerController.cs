using System;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using FishNet.Utility.Template;
using MF2.Game.Domain;
using UnityEngine;

namespace MF2.Entities.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(NetworkObject))]
    [RequireComponent(typeof(BlockPlayerInputSource), typeof(BlockPlayerView), typeof(EntityViewBinder))]
    [RequireComponent(typeof(PlayerPresentationBindingGate))]
    public sealed class BlockPlayerController : TickNetworkBehaviour
    {
        public struct MoveData : IReplicateData
        {
            public Vector2 WorldMove;
            public Vector2 WorldFacing;
            public bool Sprint;
            public bool Jump;
            public bool Dash;
            public bool Attack;
            private uint tick;

            public MoveData(
                Vector2 worldMove,
                Vector2 worldFacing,
                bool sprint,
                bool jump,
                bool dash,
                bool attack)
            {
                WorldMove = worldMove;
                WorldFacing = worldFacing;
                Sprint = sprint;
                Jump = jump;
                Dash = dash;
                Attack = attack;
                tick = 0;
            }

            public void Dispose() { }
            public uint GetTick() => tick;
            public void SetTick(uint value) => tick = value;
        }

        public struct ReconcileData : IReconcileData
        {
            public PredictionRigidbody Body;
            public int DashCharges;
            public uint NextDashRechargeTick;
            public uint LastDashTick;
            public bool HasDashed;
            private uint tick;

            public ReconcileData(
                PredictionRigidbody body,
                int dashCharges,
                uint nextDashRechargeTick,
                uint lastDashTick,
                bool hasDashed)
            {
                Body = body;
                DashCharges = dashCharges;
                NextDashRechargeTick = nextDashRechargeTick;
                LastDashTick = lastDashTick;
                HasDashed = hasDashed;
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
        [SerializeField, Min(0.1f)] private float dashImpulse = 10f;

        [Header("MVC")]
        [SerializeField] private BlockPlayerInputSource inputSource;
        [SerializeField] private BlockPlayerView view;
        [SerializeField] private EntityViewBinder viewBinder;
        [SerializeField] private PlayerPresentationBindingGate presentationBinding;

        private readonly RaycastHit[] groundHits = new RaycastHit[8];
        private readonly PredictionRigidbody predictionBody = new();
        private readonly SyncVar<Guid> playerId = new();
        private Rigidbody body;
        private CapsuleCollider capsule;
        private int predictedDashCharges = PlayerActionState.MaximumDashCharges;
        private uint nextDashRechargeTick;
        private uint lastDashTick;
        private bool hasDashed;

        public bool IsGrounded { get; private set; }
        public Vector2 CurrentWorldFacing => ResolveInput().CurrentWorldFacing;
        public PlayerPresentationBindingGate PresentationBinding => ResolvePresentationBinding();

        public void ConfigureRig(
            Transform leftArm,
            Transform rightArm,
            Transform leftLeg,
            Transform rightLeg) =>
            ResolveView().ConfigureRig(leftArm, rightArm, leftLeg, rightLeg);

        public void ConfigureOwnerCamera(Camera camera, AudioListener listener)
        {
            ResolveView().ConfigureOwnerCamera(camera, listener);
            ResolveInput().ConfigureMovementCamera(camera);
        }

        public void ConfigureMvc(
            BlockPlayerInputSource playerInput,
            BlockPlayerView playerView,
            EntityViewBinder binder,
            PlayerPresentationBindingGate bindingGate)
        {
            inputSource = playerInput;
            view = playerView;
            viewBinder = binder;
            presentationBinding = bindingGate;
            viewBinder.Configure(view);
            presentationBinding.Configure(viewBinder);
        }

        public void AssignPlayerId(PlayerId id)
        {
            if (id.Value == Guid.Empty)
                throw new ArgumentException("A network player requires a valid domain id.", nameof(id));
            if (IsSpawned)
                throw new InvalidOperationException("The domain id must be assigned before spawning.");
            playerId.Value = id.Value;
        }

        public void BindSpawn(in PlayerSpawnContext context)
        {
            if (IsSpawned)
                throw new InvalidOperationException("Spawn context must be bound before network spawn.");
            transform.SetPositionAndRotation(context.InitialPosition, context.InitialRotation);
            AssignPlayerId(context.PlayerId);
        }

        public bool TryGetPlayerId(out PlayerId id)
        {
            var value = playerId.Value;
            if (value == Guid.Empty)
            {
                id = default;
                return false;
            }
            id = new PlayerId(value);
            return true;
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

            playerId.OnChange += OnPlayerIdChanged;
            ResolveInput();
            ResolveView().SetLocalOwner(false);
            ResolveBinder().Configure(view);
            ResolvePresentationBinding().Configure(viewBinder);
        }

        public override void OnStartNetwork()
        {
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ResolveBinder().Configure(ResolveView());
            view.SetLocalOwner(IsOwner, IsOwner);
            if (TryGetPlayerId(out var id))
                ResolvePresentationBinding().SetNetworkReady(id);
        }

        public override void OnStopClient()
        {
            ResolvePresentationBinding().SetNetworkUnavailable();
            ResolveView().SetLocalOwner(false);
            base.OnStopClient();
        }

        private void Update()
        {
            if (!IsOwner)
                return;

            var input = ResolveInput();
            input.Capture();
            ResolveView().SetFacing(input.CurrentWorldFacing);
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

            var input = ResolveInput().Consume();
            if (input.Dash)
                PlayerActionCommandBridge.Sender?.SendDash();
            if (input.Attack)
                PlayerActionCommandBridge.Sender?.SendAttack(
                    input.WorldFacing.x,
                    input.WorldFacing.y);
            return new MoveData(
                input.WorldMove,
                input.WorldFacing,
                input.Sprint,
                input.Jump,
                input.Dash,
                input.Attack);
        }

        [Replicate]
        private void PerformReplicate(
            MoveData data,
            ReplicateState state = ReplicateState.Invalid,
            Channel channel = Channel.Unreliable)
        {
            ResolveView().SetFacing(data.WorldFacing);
            IsGrounded = CheckGrounded();
            var speed = data.Sprint ? sprintSpeed : walkSpeed;
            var velocity = body.linearVelocity;
            velocity.x = data.WorldMove.x * speed;
            velocity.z = data.WorldMove.y * speed;
            AdvancePredictedDash(data.GetTick());
            if (data.Dash && CanPredictedDash(data.GetTick()))
            {
                var direction = data.WorldMove.sqrMagnitude > 0.01f
                    ? data.WorldMove.normalized
                    : data.WorldFacing.normalized;
                if (direction.sqrMagnitude > 0.01f)
                {
                    velocity.x += direction.x * dashImpulse;
                    velocity.z += direction.y * dashImpulse;
                    BeginPredictedDash(data.GetTick());
                }
            }
            if (data.Jump && IsGrounded)
                velocity.y = jumpVelocity;
            predictionBody.Velocity(velocity);
            predictionBody.Simulate();
        }

        public override void CreateReconcile()
        {
            PerformReconcile(new ReconcileData(
                predictionBody,
                predictedDashCharges,
                nextDashRechargeTick,
                lastDashTick,
                hasDashed));
        }

        [Reconcile]
        private void PerformReconcile(
            ReconcileData data,
            Channel channel = Channel.Unreliable)
        {
            predictionBody.Reconcile(data.Body);
            predictedDashCharges = data.DashCharges;
            nextDashRechargeTick = data.NextDashRechargeTick;
            lastDashTick = data.LastDashTick;
            hasDashed = data.HasDashed;
        }

        private bool CanPredictedDash(uint tick) =>
            predictedDashCharges > 0 &&
            (!hasDashed || tick - lastDashTick >= PlayerActionState.DashCooldownTicks);

        private void BeginPredictedDash(uint tick)
        {
            var wasFull = predictedDashCharges == PlayerActionState.MaximumDashCharges;
            predictedDashCharges--;
            if (wasFull)
                nextDashRechargeTick = tick + PlayerActionState.DashRechargeTicks;
            lastDashTick = tick;
            hasDashed = true;
        }

        private void AdvancePredictedDash(uint tick)
        {
            while (predictedDashCharges < PlayerActionState.MaximumDashCharges &&
                   nextDashRechargeTick != 0 && tick >= nextDashRechargeTick)
            {
                predictedDashCharges++;
                nextDashRechargeTick += PlayerActionState.DashRechargeTicks;
            }
            if (predictedDashCharges == PlayerActionState.MaximumDashCharges)
                nextDashRechargeTick = 0;
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

        private EntityViewBinder ResolveBinder() =>
            viewBinder != null ? viewBinder : viewBinder = GetComponent<EntityViewBinder>();

        private PlayerPresentationBindingGate ResolvePresentationBinding() =>
            presentationBinding != null
                ? presentationBinding
                : presentationBinding = GetComponent<PlayerPresentationBindingGate>();

        private void OnPlayerIdChanged(Guid previous, Guid next, bool asServer)
        {
            if (asServer || !IsClientStarted)
                return;
            if (next == Guid.Empty)
                ResolvePresentationBinding().SetNetworkUnavailable();
            else
                ResolvePresentationBinding().SetNetworkReady(new PlayerId(next));
        }

        private void OnDestroy()
        {
            playerId.OnChange -= OnPlayerIdChanged;
        }
    }
}
