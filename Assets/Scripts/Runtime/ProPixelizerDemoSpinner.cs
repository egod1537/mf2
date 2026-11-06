using UnityEngine;

namespace MF2.Demo
{
    [DisallowMultipleComponent]
    public sealed class ProPixelizerDemoSpinner : MonoBehaviour
    {
        [SerializeField] private Vector3 axis = Vector3.up;
        [SerializeField, Min(0f)] private float degreesPerSecond = 30f;

        private void Update()
        {
            transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.World);
        }
    }
}
