#if !UNITY_6000_0_OR_NEWER
// Copyright Elliot Bentine, 2018-
// Helper stub to enable compilation on 2022.3, where the asmref for GPU resident rendering will be missing.
using UnityEngine;

namespace ProPixelizer.GPUResident
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class ProPixelizerDisableGPUResidentRenderingComponent : MonoBehaviour
    {
        public static void TryAdd(GameObject gameObject) { }

        public static void TryRemove(GameObject gameObject) { }
    }
}
#endif
