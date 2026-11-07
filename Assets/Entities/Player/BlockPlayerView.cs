using UnityEngine;

namespace MF2.Entities.Player
{
    [DisallowMultipleComponent]
    public sealed class BlockPlayerView : MonoBehaviour, IPlayerView
    {
        [Header("Owner Presentation")]
        [SerializeField] private Camera ownerCamera;
        [SerializeField] private AudioListener ownerAudioListener;

        [Header("Entity Visual")]
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private Rigidbody motionBody;

        [Header("Block Rig")]
        [SerializeField] private Transform leftArmPivot;
        [SerializeField] private Transform rightArmPivot;
        [SerializeField] private Transform leftLegPivot;
        [SerializeField] private Transform rightLegPivot;
        [SerializeField, Range(0f, 60f)] private float limbSwingAngle = 30f;
        [SerializeField, Min(0.1f)] private float limbSwingSpeed = 8f;
        [SerializeField, Min(0.1f)] private float maximumMoveSpeed = 6f;

        private PlayerPresentationState presentationState;
        private bool hasPresentation;
        private float stepCycle;

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

        public void ConfigureOwnerCamera(Camera camera, AudioListener listener)
        {
            ownerCamera = camera;
            ownerAudioListener = listener;
        }

        public void ConfigureVisual(GameObject root, Rigidbody body)
        {
            visualRoot = root;
            motionBody = body;
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

            if (ownerCamera != null)
                ownerCamera.gameObject.SetActive(active);
            if (ownerAudioListener != null)
                ownerAudioListener.enabled = active;
        }

        public void Apply(in PlayerPresentationState state)
        {
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
            SetLimbRotation(leftArmPivot, Quaternion.identity, 1f);
            SetLimbRotation(rightArmPivot, Quaternion.identity, 1f);
            SetLimbRotation(leftLegPivot, Quaternion.identity, 1f);
            SetLimbRotation(rightLegPivot, Quaternion.identity, 1f);
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
        }

        private void LateUpdate()
        {
            if (!hasPresentation || motionBody == null)
                return;

            var velocity = motionBody.linearVelocity;
            var horizontalSpeed = Mathf.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z);
            AnimateRig(
                Mathf.Clamp01(horizontalSpeed / Mathf.Max(0.01f, maximumMoveSpeed)),
                Time.deltaTime);
        }

        private void AnimateRig(float moveAmount, float deltaTime)
        {
            if (moveAmount > 0.01f)
                stepCycle += deltaTime * limbSwingSpeed;
            var swing = Mathf.Sin(stepCycle) * limbSwingAngle * moveAmount;
            var blend = 1f - Mathf.Exp(-12f * deltaTime);
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
