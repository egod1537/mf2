using UnityEngine;

namespace MF2.Entities.Player
{
    [DisallowMultipleComponent]
    public sealed class BlockPlayerView : MonoBehaviour, IPlayerView
    {
        [Header("Owner Presentation")]
        [SerializeField] private Camera ownerCamera;
        [SerializeField] private AudioListener ownerAudioListener;
        [SerializeField] private PlayerCameraFollow ownerCameraFollow;

        [Header("Entity Visual")]
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private Transform facingRoot;
        [SerializeField] private Rigidbody motionBody;

        [Header("Block Rig")]
        [SerializeField] private Transform leftArmPivot;
        [SerializeField] private Transform rightArmPivot;
        [SerializeField] private Transform leftLegPivot;
        [SerializeField] private Transform rightLegPivot;
        [SerializeField, Range(0f, 60f)] private float limbSwingAngle = 30f;
        [SerializeField, Min(0.1f)] private float limbSwingSpeed = 8f;
        [SerializeField, Min(0.1f)] private float maximumMoveSpeed = 6f;

        [Header("Model Animation")]
        [SerializeField] private Animator modelAnimator;
        [SerializeField, Min(0f)] private float locomotionDampTime = 0.08f;

        private static readonly int MoveXParameter = Animator.StringToHash("Move X");
        private static readonly int MoveYParameter = Animator.StringToHash("Move Y");
        private static readonly int IsGroundParameter = Animator.StringToHash("IsGround");

        private PlayerPresentationState presentationState;
        private bool hasPresentation;
        private float stepCycle;
        private float attackPresentationTime;
        private float dashPresentationTime;

        public Camera OwnerCamera => ownerCamera;
        public bool HasPresentation => hasPresentation;
        public PlayerPresentationState PresentationState => presentationState;

        public void ConfigureRig(
            Transform leftArm,
            Transform rightArm,
            Transform leftLeg,
            Transform rightLeg)
        {
            leftArmPivot = leftArm;
            rightArmPivot = rightArm;
            leftLegPivot = leftLeg;
            rightLegPivot = rightLeg;
        }

        public void ConfigureAnimator(Animator animator)
        {
            modelAnimator = animator;
            if (modelAnimator == null)
                return;
            modelAnimator.applyRootMotion = false;
            modelAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        public void ConfigureOwnerCamera(Camera camera, AudioListener listener)
        {
            ownerCamera = camera;
            ownerAudioListener = listener;
            ownerCameraFollow = camera != null
                ? camera.GetComponent<PlayerCameraFollow>()
                : null;
        }

        public Transform ConfigureVisual(GameObject root, Rigidbody body)
        {
            visualRoot = root;
            motionBody = body;
            return EnsureFacingRoot();
        }

        public void SetFacing(Vector2 worldDirection)
        {
            if (worldDirection.sqrMagnitude <= 0.0001f)
                return;

            var target = EnsureFacingRoot();
            if (target == null)
                return;

            target.rotation = Quaternion.LookRotation(
                new Vector3(worldDirection.x, 0f, worldDirection.y).normalized,
                Vector3.up);
        }

        public void SetLocalOwner(bool active, bool disableOtherSceneCameras = false)
        {
            if (active && disableOtherSceneCameras && ownerCamera != null)
            {
                foreach (var sceneCamera in FindObjectsByType<Camera>(
                             FindObjectsInactive.Include,
                             FindObjectsSortMode.None))
                {
                    if (sceneCamera != ownerCamera && sceneCamera.gameObject.scene == gameObject.scene)
                        sceneCamera.gameObject.SetActive(false);
                }
            }

            if (!active)
                ownerCameraFollow?.RestoreAuthoredParent();
            if (ownerCamera != null)
                ownerCamera.gameObject.SetActive(active);
            if (ownerAudioListener != null)
                ownerAudioListener.enabled = active;
            if (active)
                ownerCameraFollow?.SnapNextFrame();
        }

        public void Apply(in PlayerPresentationState state)
        {
            if (hasPresentation && state.AttackSequence > presentationState.AttackSequence)
            {
                attackPresentationTime = 0.22f;
                if (modelAnimator != null && modelAnimator.runtimeAnimatorController != null)
                    modelAnimator.Play("Attack", 1, 0f);
            }
            if (hasPresentation && state.DashSequence > presentationState.DashSequence)
                dashPresentationTime = 0.18f;
            presentationState = state;
            hasPresentation = true;
            if (visualRoot != null)
                visualRoot.SetActive(true);
        }

        public void ResetView()
        {
            presentationState = default;
            hasPresentation = false;
            stepCycle = 0f;
            attackPresentationTime = 0f;
            dashPresentationTime = 0f;
            SetLimbRotation(leftArmPivot, Quaternion.identity, 1f);
            SetLimbRotation(rightArmPivot, Quaternion.identity, 1f);
            SetLimbRotation(leftLegPivot, Quaternion.identity, 1f);
            SetLimbRotation(rightLegPivot, Quaternion.identity, 1f);
            if (modelAnimator != null && modelAnimator.runtimeAnimatorController != null)
            {
                modelAnimator.speed = 1f;
                modelAnimator.Rebind();
                if (modelAnimator.gameObject.activeInHierarchy)
                    modelAnimator.Update(0f);
            }
            if (facingRoot != null)
                facingRoot.localRotation = Quaternion.identity;
            if (visualRoot != null)
                visualRoot.SetActive(false);
        }

        private void Awake()
        {
            if (motionBody == null)
                motionBody = GetComponent<Rigidbody>();
            if (visualRoot == null)
            {
                var visual = transform.Find("Visual");
                if (visual != null)
                    visualRoot = visual.gameObject;
            }
            EnsureFacingRoot();
            if (modelAnimator == null && visualRoot != null)
                ConfigureAnimator(visualRoot.GetComponentInChildren<Animator>(true));
        }

        private void OnDestroy()
        {
            ownerCameraFollow?.RestoreAuthoredParent();
        }

        private void LateUpdate()
        {
            if (!hasPresentation || motionBody == null)
                return;

            var velocity = motionBody.linearVelocity;
            var horizontalSpeed = Mathf.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z);
            attackPresentationTime = Mathf.Max(0f, attackPresentationTime - Time.deltaTime);
            dashPresentationTime = Mathf.Max(0f, dashPresentationTime - Time.deltaTime);
            var moveAmount = Mathf.Clamp01(
                horizontalSpeed / Mathf.Max(0.01f, maximumMoveSpeed));
            AnimateRig(moveAmount, Time.deltaTime);
            AnimateModel(moveAmount, velocity.y, Time.deltaTime);
        }

        private void AnimateModel(float moveAmount, float verticalSpeed, float deltaTime)
        {
            if (modelAnimator == null || modelAnimator.runtimeAnimatorController == null)
                return;

            var locomotion = dashPresentationTime > 0f
                ? Mathf.Max(0.85f, moveAmount)
                : moveAmount;
            modelAnimator.speed = dashPresentationTime > 0f ? 1.35f : 1f;
            modelAnimator.SetFloat(
                MoveXParameter,
                locomotion,
                locomotionDampTime,
                deltaTime);
            modelAnimator.SetFloat(MoveYParameter, 0f, locomotionDampTime, deltaTime);
            modelAnimator.SetBool(IsGroundParameter, Mathf.Abs(verticalSpeed) <= 0.2f);
        }

        private void AnimateRig(float moveAmount, float deltaTime)
        {
            if (moveAmount > 0.01f)
                stepCycle += deltaTime * limbSwingSpeed;
            var swing = Mathf.Sin(stepCycle) * limbSwingAngle * moveAmount;
            if (dashPresentationTime > 0f)
                swing *= 1.7f;
            var blend = 1f - Mathf.Exp(-12f * deltaTime);
            SetLimbRotation(leftArmPivot, Quaternion.Euler(swing, 0f, 0f), blend);
            var rightArm = attackPresentationTime > 0f
                ? Quaternion.Euler(-105f, 0f, -18f)
                : Quaternion.Euler(-swing, 0f, 0f);
            SetLimbRotation(rightArmPivot, rightArm, blend);
            SetLimbRotation(leftLegPivot, Quaternion.Euler(-swing, 0f, 0f), blend);
            SetLimbRotation(rightLegPivot, Quaternion.Euler(swing, 0f, 0f), blend);
        }

        private static void SetLimbRotation(Transform limb, Quaternion target, float blend)
        {
            if (limb != null)
                limb.localRotation = Quaternion.Slerp(limb.localRotation, target, blend);
        }

        private Transform EnsureFacingRoot()
        {
            if (visualRoot == null)
                return null;
            if (facingRoot != null && facingRoot.parent == visualRoot.transform)
                return facingRoot;

            facingRoot = visualRoot.transform.Find("FacingPivot");
            if (facingRoot != null)
                return facingRoot;

            var existingChildren = new Transform[visualRoot.transform.childCount];
            for (var i = 0; i < existingChildren.Length; i++)
                existingChildren[i] = visualRoot.transform.GetChild(i);

            facingRoot = new GameObject("FacingPivot").transform;
            facingRoot.SetParent(visualRoot.transform, false);
            foreach (var child in existingChildren)
                child.SetParent(facingRoot, false);
            return facingRoot;
        }
    }
}
