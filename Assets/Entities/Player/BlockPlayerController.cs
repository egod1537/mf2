using UnityEngine;
using UnityEngine.InputSystem;

namespace MF2.Entities.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class BlockPlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float walkSpeed = 3.6f;
        [SerializeField, Min(0.1f)] private float sprintSpeed = 5.8f;
        [SerializeField, Min(0.1f)] private float rotationSpeed = 14f;
        [SerializeField, Min(0.1f)] private float jumpHeight = 1.15f;
        [SerializeField] private float gravity = -24f;

        [Header("Block Rig")]
        [SerializeField] private Transform leftArmPivot;
        [SerializeField] private Transform rightArmPivot;
        [SerializeField] private Transform leftLegPivot;
        [SerializeField] private Transform rightLegPivot;
        [SerializeField, Range(0f, 60f)] private float limbSwingAngle = 32f;
        [SerializeField, Min(0.1f)] private float limbSwingSpeed = 8f;

        private CharacterController characterController;
        private Camera movementCamera;
        private float verticalVelocity;
        private float stepCycle;

        public float HorizontalSpeed { get; private set; }
        public bool IsGrounded => characterController != null && characterController.isGrounded;

        public void ConfigureRig(Transform leftArm, Transform rightArm, Transform leftLeg, Transform rightLeg)
        {
            leftArmPivot = leftArm;
            rightArmPivot = rightArm;
            leftLegPivot = leftLeg;
            rightLegPivot = rightLeg;
        }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
        }

        private void Start()
        {
            movementCamera = Camera.main;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var input = ReadMoveInput(keyboard);
            var sprinting = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            var speed = sprinting ? sprintSpeed : walkSpeed;

            if (movementCamera == null)
                movementCamera = Camera.main;

            var moveDirection = GetCameraRelativeDirection(input);
            HorizontalSpeed = moveDirection.magnitude * speed;

            if (moveDirection.sqrMagnitude > 0.0001f)
            {
                characterController.Move(moveDirection * (speed * Time.deltaTime));
                var targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 1f - Mathf.Exp(-rotationSpeed * Time.deltaTime));
            }

            ApplyVerticalMovement(keyboard);
            AnimateRig(moveDirection.magnitude, sprinting);
        }

        private static Vector2 ReadMoveInput(Keyboard keyboard)
        {
            if (keyboard == null)
                return Vector2.zero;

            var x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            var y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        private Vector3 GetCameraRelativeDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            if (movementCamera == null)
                return new Vector3(input.x, 0f, input.y).normalized;

            var forward = Vector3.ProjectOnPlane(movementCamera.transform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(movementCamera.transform.right, Vector3.up).normalized;
            return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
        }

        private void ApplyVerticalMovement(Keyboard keyboard)
        {
            if (characterController.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;

            if (characterController.isGrounded && keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

            verticalVelocity += gravity * Time.deltaTime;
            characterController.Move(Vector3.up * (verticalVelocity * Time.deltaTime));
        }

        private void AnimateRig(float moveAmount, bool sprinting)
        {
            var normalizedMove = Mathf.Clamp01(moveAmount);
            var cycleSpeed = limbSwingSpeed * (sprinting ? 1.35f : 1f);
            if (normalizedMove > 0.01f)
                stepCycle += Time.deltaTime * cycleSpeed;

            var swing = Mathf.Sin(stepCycle) * limbSwingAngle * normalizedMove;
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
