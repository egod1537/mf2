using UnityEngine;

namespace MF2.Game.Networking
{
    [DisallowMultipleComponent]
    public sealed class SpawnPoint : MonoBehaviour
    {
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.45f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);
        }
    }
}
