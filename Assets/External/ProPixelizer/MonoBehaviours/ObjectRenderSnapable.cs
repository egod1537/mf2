// Copyright Elliot Bentine, 2018-
using ProPixelizer.GPUResident;
using UnityEngine;
using UnityEngine.Serialization;

namespace ProPixelizer
{
    [DisallowMultipleComponent]
    public class ObjectRenderSnapable : MonoBehaviour
    {
        [Header("Snapping")]
        [Tooltip("Should position be snapped?")]
        [FormerlySerializedAs("ShouldSnapPosition")]
        public bool SnapPosition = true;

        [Tooltip("Should Euler rotation angles be snapped?")]
        [FormerlySerializedAs("shouldSnapAngles")]
        public bool SnapEulerAngles = true;

        [Tooltip("Strategy that should be used for snapping rotation angles.")]
        public eSnapAngleStrategy SnapAngleStrategy;

        [Tooltip("Resolution to which angles should be snapped")]
        public float angleResolution = 30f;

        [Space(10)]
        [Header("Pixel Alignment")]

        // Should we use a pixel grid aligned to the root entity's position? 
        [FormerlySerializedAs("UseRootPixelGrid")]
        [Tooltip("When true, the pixels of this object are snapped into alignment with the specified transform.")]
        public bool AlignPixelGrid = false;

        /// <summary>
        /// Transform to use as a reference for the pixel grid alignment.
        /// 
        /// If empty, defaults to root.
        /// </summary>
        [Tooltip("The transform to align this object's pixels to when 'Align Pixel Grid' is true. If empty, the root transform is used.")]
        public Transform PixelGridReference;

        [Tooltip("The pixel size used for alignment. Most of the time you want this to match the pixel size of your object.")]
        public int AlignmentPixelSize = 5;

        [Tooltip("If true, tries to set the AlignmentPixelSize from a ProPixelizer material on this object.")]
        public bool GetPixelSizeFromMaterial = true;

        [Space(10)]
        [Header("Materials")]

        [Tooltip("If true, also configures ProPixelizer material keywords required for snapping on child meshes in the heirachy.")]
        public bool ConfigureChildren = true;

        /// <summary>
        /// Local position of the snapable before snapping.
        /// </summary>
        private Vector3 LocalPositionPreSnap;

        /// <summary>
        /// Local rotation of the object before snapping.
        /// </summary>
        private Quaternion LocalRotationPreSnap;

        /// <summary>
        /// World-space rotation of the object before snapping.
        /// </summary>
        private Quaternion WorldRotationPreSnap;

        /// <summary>
        /// World position of the snapable before snapping.
        /// </summary>
        public Vector3 WorldPositionPreSnap { get; private set; }

        /// <summary>
        /// Depth of the given transform, in the heirachy. Used for snap ordering.
        /// </summary>
        public int TransformDepth { get; private set; }

        public enum eSnapAngleStrategy
        {
            WorldSpaceRotation,
            CameraSpaceY
        }

        /// <summary>
        /// Should angles be snapped?
        /// </summary>
        public bool ShouldSnapAngles() => SnapEulerAngles;

        /// <summary>
        /// Resolution (degrees) to which euler angles are snapped.
        /// </summary>
        public float AngleResolution() => angleResolution;

        private Renderer _renderer;

        public Vector3 PixelGridReferencePosition { get; private set; }

        public void SaveTransform()
        {
            LocalPositionPreSnap = transform.localPosition;
            WorldPositionPreSnap = transform.position;
            LocalRotationPreSnap = transform.localRotation;
            WorldRotationPreSnap = transform.rotation;
            if (PixelGridReference != null)
                PixelGridReferencePosition = PixelGridReference.position;
            else
                PixelGridReferencePosition = transform.root.position;
            _transformsSaved = true;
        }

        bool _transformsSaved = false;

        /// <summary>
        /// Restore a previously saved transform.
        /// </summary>
        public void RestoreTransform()
        {
            if (!_transformsSaved)
                return;
            transform.localPosition = LocalPositionPreSnap;
            transform.localRotation = LocalRotationPreSnap;
            _transformsSaved = false;
        }

        /// <summary>
        /// Configures ProPixelizer material keywords required for correct object snapping.
        /// </summary>
        public void ConfigureProPixelizerMaterials()
        {
            if (ConfigureChildren)
            {
                foreach (var rendererComponent in GetComponentsInChildren<Renderer>())
                {
                    if (rendererComponent == _renderer) continue;
                    // If renderer has an object render snapable on the object, don't conflict with it.
                    if (rendererComponent.GetComponent<ObjectRenderSnapable>() != null) continue;
                    TryConfigureMaterials(rendererComponent);
                }
            }
            
            if (_renderer != null)
                TryConfigureMaterials(_renderer);
        }

        void TryConfigureMaterials(Renderer renderer)
        {
            for (int i = 0; i < renderer.sharedMaterials.Length; i++)
            {
                var isProPixelizerMaterial = renderer.sharedMaterials[i].HasProperty(ProPixelizerMaterialPropertyReferences.PixelSize);
                if (isProPixelizerMaterial)
                {
                    // NB: only access renderer material if the shared material is a propixelizer material, because accessing .materials causes a clone.
                    renderer.materials[i].EnableKeyword(ProPixelizerKeywords.USE_OBJECT_POSITION_FOR_PIXEL_GRID_ON);
                    if (GetPixelSizeFromMaterial)
                        AlignmentPixelSize = Mathf.RoundToInt(renderer.materials[i].GetFloat(ProPixelizerMaterialPropertyReferences.PixelSize));
                }
            }
        }

        /// <summary>
        /// Snap euler angles to specified AngleResolution.
        /// </summary>
        public void SnapAngles(ProPixelizerCamera camera)
        {
            if (!ShouldSnapAngles())
                return;
            Vector3 angles = WorldRotationPreSnap.eulerAngles;
            var resolution = AngleResolution();
            switch (SnapAngleStrategy)
            {
                case eSnapAngleStrategy.WorldSpaceRotation:
                    {
                        Vector3 snapped = new Vector3(
                            Mathf.Round(angles.x / resolution) * resolution,
                            Mathf.Round(angles.y / resolution) * resolution,
                            Mathf.Round(angles.z / resolution) * resolution);
                        transform.eulerAngles = snapped;
                        break;
                    }
                case eSnapAngleStrategy.CameraSpaceY:
                    {
                        float cameraY = camera.PreSnapCameraRotation.eulerAngles.y;
                        //snap in relative angle space with respect to camera
                        angles.y -= cameraY;
                        Vector3 snapped = new Vector3(
                            Mathf.Round(angles.x / resolution) * resolution,
                            Mathf.Round(angles.y / resolution) * resolution,
                            Mathf.Round(angles.z / resolution) * resolution
                            );
                        snapped.y += cameraY;
                        transform.eulerAngles = snapped;
                        break;
                    }
            }

        }

        ObjectRenderSnapableKey _ID = default;
        SceneObjectSnapManager _SnapManager;

        public bool Registered() => _ID.ID != 0;

        public void Register()
        {
            //Determine depth of the given behaviour's transform
            int depth = 0;
            Transform iter = transform;
            while (iter.parent != null && depth < 100)
            {
                depth++;
                iter = iter.parent;
            }
            TransformDepth = depth;
            _SnapManager = SceneObjectSnapManager.GetSceneSingleton();
            _ID = _SnapManager.Register(this);
        }

        public void Unregister()
        {
            if (!Registered())
            {
                Debug.LogWarning("Snapable not previously registered.");
                return;
            }
            if (_SnapManager != null)
                _SnapManager.Unregister(_ID, this);
        }

        public void OnEnable()
        {
            ProPixelizerDisableGPUResidentRenderingComponent.TryAdd(gameObject);

            _renderer = GetComponent<Renderer>();
            ConfigureProPixelizerMaterials();
            Register();
        }

        public void OnDisable()
        {
            _renderer = GetComponent<Renderer>();
            ConfigureProPixelizerMaterials();
            if (gameObject.scene.isLoaded)
                Unregister();

            ProPixelizerDisableGPUResidentRenderingComponent.TryRemove(gameObject);
        }

        //public void Update()
        //{
        //    if (_SnapManager != null && !_SnapManager.isActiveAndEnabled)
        //    {
        //        Unregister();
        //    }    
        //}

        public void OnTransformParentChanged()
        {
            if (Registered())
            {
                Unregister();
                Register();
            }
        }
    }
}