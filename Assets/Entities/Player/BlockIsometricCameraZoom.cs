using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MF2.Entities.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CinemachineCamera))]
    public sealed class BlockIsometricCameraZoom : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float orthographicSize = 5.25f;
        [SerializeField] private Vector2 orthographicSizeRange = new Vector2(2.75f, 8.5f);
        [SerializeField, Min(0.0001f)] private float zoomSpeed = 0.004f;

        private CinemachineCamera cinemachineCamera;

        private void Awake()
        {
            CacheCamera();
            ApplyLens();
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f))
                return;

            orthographicSize = Mathf.Clamp(
                orthographicSize - scroll * zoomSpeed,
                orthographicSizeRange.x,
                orthographicSizeRange.y);
            ApplyLens();
        }

        private void OnValidate()
        {
            if (orthographicSizeRange.y < orthographicSizeRange.x)
                orthographicSizeRange.y = orthographicSizeRange.x;

            orthographicSize = Mathf.Clamp(
                orthographicSize,
                orthographicSizeRange.x,
                orthographicSizeRange.y);

            CacheCamera();
            if (cinemachineCamera != null)
                ApplyLens();
        }

        private void CacheCamera()
        {
            if (cinemachineCamera == null)
                cinemachineCamera = GetComponent<CinemachineCamera>();
        }

        private void ApplyLens()
        {
            var lens = cinemachineCamera.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = orthographicSize;
            cinemachineCamera.Lens = lens;
        }
    }
}
