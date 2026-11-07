using UnityEngine;
using UnityEngine.InputSystem;

namespace MF2.Entities.Player
{
    public readonly struct BlockPlayerInputFrame
    {
        public BlockPlayerInputFrame(Vector2 worldMove, bool sprint, bool jump)
        {
            WorldMove = worldMove;
            Sprint = sprint;
            Jump = jump;
        }

        public Vector2 WorldMove { get; }
        public bool Sprint { get; }
        public bool Jump { get; }
    }

    [DisallowMultipleComponent]
    public sealed class BlockPlayerInputSource : MonoBehaviour
    {
        [SerializeField] private Camera movementCamera;

        private Vector2 moveInput;
        private bool sprintInput;
        private bool jumpQueued;

        public void ConfigureMovementCamera(Camera camera) => movementCamera = camera;

        public void UseSceneCameraIfUnset()
        {
            if (movementCamera != null && movementCamera.isActiveAndEnabled)
                return;

            foreach (var sceneCamera in FindObjectsByType<Camera>(
                         FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                if (sceneCamera.gameObject.scene != gameObject.scene)
                    continue;
                movementCamera = sceneCamera;
                return;
            }
        }

        public void Capture()
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

        public BlockPlayerInputFrame Consume()
        {
            var local = Vector2.ClampMagnitude(moveInput, 1f);
            var forward = movementCamera == null
                ? Vector3.forward
                : Vector3.ProjectOnPlane(movementCamera.transform.forward, Vector3.up).normalized;
            var right = movementCamera == null
                ? Vector3.right
                : Vector3.ProjectOnPlane(movementCamera.transform.right, Vector3.up).normalized;
            var world = Vector3.ClampMagnitude(forward * local.y + right * local.x, 1f);
            var result = new BlockPlayerInputFrame(
                new Vector2(world.x, world.z),
                sprintInput,
                jumpQueued);
            jumpQueued = false;
            return result;
        }
    }
}
