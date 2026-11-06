#if UNITY_6000_0_OR_NEWER
// Copyright Elliot Bentine, 2018-
using UnityEngine;
using UnityEngine.Rendering;

namespace ProPixelizer.GPUResident
{
    /// <summary>
    /// ProPixelizer's snap behaviour doesn't yet support GPU resident rendering, so this utility class encapsulates automatic adding/removal of 
    /// Unity's internal DisallowGPUDrivenRendering class in a non-persistent manner.
    /// 
    /// Hopefully this also keeps future migration simple when we do add support, because we can just drop creation of this non-persistent wrapper.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class ProPixelizerDisableGPUResidentRenderingComponent : MonoBehaviour
    {
        public static void TryAdd(GameObject gameObject)
        {
#if !UNITY_WEBGL_RENDERER_ONLY
            var component = gameObject.GetComponent<ProPixelizerDisableGPUResidentRenderingComponent>();
            if (component == null)
            {
                component = gameObject.AddComponent<ProPixelizerDisableGPUResidentRenderingComponent>();
                component.hideFlags |= HideFlags.DontSaveInEditor;
            }

            component.Apply();
#endif
        }

        public static void TryRemove(GameObject gameObject)
        {
#if !UNITY_WEBGL_RENDERER_ONLY
            var component = gameObject.GetComponent<ProPixelizerDisableGPUResidentRenderingComponent>();
            if (component == null)
                return;

            if (Application.isPlaying)
                Destroy(component);
            else
                DestroyImmediate(component);
#endif
        }

#if !UNITY_WEBGL_RENDERER_ONLY
        private DisallowGPUDrivenRendering _addedDisallowGPUDrivenRendering;

        private void Apply()
        {
            var disallowGPUDrivenRendering = GetComponent<DisallowGPUDrivenRendering>();
            if (disallowGPUDrivenRendering == null)
            {
                disallowGPUDrivenRendering = gameObject.AddComponent<DisallowGPUDrivenRendering>();
                _addedDisallowGPUDrivenRendering = disallowGPUDrivenRendering;
                disallowGPUDrivenRendering.hideFlags |= HideFlags.DontSaveInEditor;
            }

            if (!disallowGPUDrivenRendering.applyToChildrenRecursively)
                disallowGPUDrivenRendering.applyToChildrenRecursively = true;
        }

        private void OnDisable()
        {
            if (_addedDisallowGPUDrivenRendering == null)
                return;

            var addedComponent = _addedDisallowGPUDrivenRendering;
            _addedDisallowGPUDrivenRendering = null;

            if (Application.isPlaying)
                Destroy(addedComponent);
            else
                DestroyImmediate(addedComponent);
        }
#endif
    }
}
#endif
