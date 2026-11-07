using UnityEngine;
using UnityEngine.InputSystem;

namespace MF2.Entities.Player
{
    public readonly struct BlockPlayerInputFrame
    {
        public BlockPlayerInputFrame(
            Vector2 worldMove,
            Vector2 worldFacing,
            bool sprint,
            bool jump)
        {
            WorldMove = worldMove;
            WorldFacing = worldFacing;
            Sprint = sprint;
            Jump = jump;
        }

        public Vector2 WorldMove { get; }
        public Vector2 WorldFacing { get; }
        public bool Sprint { get; }
        public bool Jump { get; }
    }

    [DisallowMultipleComponent]
    public sealed class BlockPlayerInputSource : MonoBehaviour
    {
        [SerializeField] private Camera movementCamera;

        private Vector2 moveInput;
        private Vector2 worldFacing = Vector2.up;
        private bool sprintInput;
        private bool jumpQueued;

        public Vector2 CurrentWorldFacing => worldFacing;

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
            var mouse = Mouse.current;
            if (mouse != null && TryProjectPointerFacing(
                    movementCamera,
                    mouse.position.ReadValue(),
                    transform.position,
                    out var facing))
            {
                worldFacing = facing;
            }

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
                worldFacing,
                sprintInput,
                jumpQueued);
            jumpQueued = false;
            return result;
        }

        public static bool TryProjectPointerFacing(
            Camera camera,
            Vector2 screenPosition,
            Vector3 playerPosition,
            out Vector2 worldDirection)
        {
            worldDirection = default;
            if (camera == null)
                return false;

            var ray = camera.ScreenPointToRay(screenPosition);
            var groundPlane = new Plane(Vector3.up, playerPosition);
            if (!groundPlane.Raycast(ray, out var distance))
                return false;

            var offset = ray.GetPoint(distance) - playerPosition;
            worldDirection = new Vector2(offset.x, offset.z);
            if (worldDirection.sqrMagnitude <= 0.0001f)
            {
                worldDirection = default;
                return false;
            }

            worldDirection.Normalize();
            return true;
        }
    }
}
