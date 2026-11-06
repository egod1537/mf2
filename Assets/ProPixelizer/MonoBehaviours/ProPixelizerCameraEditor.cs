// Copyright Elliot Bentine, 2018-
#if (UNITY_EDITOR)

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProPixelizer.Tools
{
    [CustomEditor(typeof(ProPixelizerCamera))]
    [DisallowMultipleComponent]
    public class ProPixelizerCameraEditor : Editor
    {
        public const string SHORT_HELP = "The ProPixelizerCamera component eliminates pixel creep " +
            "for static GameObjects, and for moving GameObjects (with an ObjectRenderSnapable component). " +
            "It also provides options to configure ProPixelizer per-camera.";

        public override void OnInspectorGUI()
        {
            ProPixelizerCamera snap = (ProPixelizerCamera)target;
            
            EditorGUILayout.LabelField("ProPixelizer | Camera", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(SHORT_HELP, MessageType.None);
            EditorGUILayout.LabelField("");

            var cameraComponent = snap.gameObject.GetComponent<Camera>();
            if (cameraComponent == null) {
                EditorGUILayout.HelpBox("No camera found on this GameObject.", MessageType.Info);
                return;
            }
            if (!cameraComponent.orthographic)
                EditorGUILayout.HelpBox("The camera snap behaviour is intended for orthographic cameras. Pixel creep cannot be completely eradicted for perspective projections because the object size changes as it moves across the screen.", MessageType.Info);

            serializedObject.Update();

            bool usingSubCamera = snap.OperatingAsMainCamera;
            bool hasSubCamera = snap.HasSubCamera;
            EditorGUILayout.LabelField("Pixelization", EditorStyles.boldLabel);
            bool filterIsControllable = cameraComponent.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Base;
            GUI.enabled = filterIsControllable;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("PixelizationFilter"));
            
            // EditorGUILayout.HelpBox("ProPixelizer can render the scene to low-resolution using either a virtual camera (by redirecting draw calls to a low-res target) or using a second Subcamera.", MessageType.None);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Low-res method:");
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "ProPixelizer can pixelate the full scene by rendering to a low-resolution texture. This can be done either by redirecting" +
                " the camera's draw calls (Virtual), or by rendering the scene using a second camera (Subcamera). Virtual is generally faster," +
                " but Subcamera may be more compatible with other assets. For example, DBuffer decals are only supported for Subcamera mode.",
                MessageType.None);
            GUI.enabled = false;
            EditorGUILayout.ToggleLeft("Virtual", !usingSubCamera);
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = filterIsControllable && hasSubCamera;
            var subcameraCheckbox = EditorGUILayout.ToggleLeft("Subcamera", usingSubCamera);
            GUI.enabled = filterIsControllable;
            if (!hasSubCamera)
                if (GUILayout.Button("Create SubCamera"))
                {
                    var subcamera = new GameObject("Subcamera");
                    subcamera.transform.parent = snap.transform;
                    subcamera.transform.localPosition = Vector3.zero;
                    subcamera.transform.localRotation = Quaternion.identity;
                    subcamera.transform.localScale = Vector3.one;
                    subcamera.hideFlags = HideFlags.NotEditable;
                    subcamera.AddComponent<Camera>();
                    subcamera.AddComponent<ProPixelizerSubCamera>();
                }
            if (!subcameraCheckbox && usingSubCamera)
                snap.SubCamera.gameObject.SetActive(false);
            else if (subcameraCheckbox && !usingSubCamera)
                snap.SubCamera.gameObject.SetActive(true);
            EditorGUILayout.EndHorizontal();
            GUI.enabled = filterIsControllable;
            if (subcameraCheckbox && usingSubCamera)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("SubcameraExcludedLayers"), new GUIContent("Excluded Layers"));
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
            EditorGUI.indentLevel--;

            if (!filterIsControllable)
                EditorGUILayout.HelpBox(
                    "Overlay cameras will always use the Only ProPixelizer filter mode.",
                    snap.PixelizationFilter == PixelizationFilter.OnlyProPixelizer ? MessageType.None : MessageType.Warning
                    );
            GUI.enabled = true;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("UsePixelArtUpscalingFilter"));

            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Pixel Size", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Mode"));

            GUI.enabled = snap.Mode == ProPixelizerCamera.PixelSizeMode.WorldSpacePixelSize;
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("PixelSize"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ClampCameraOrthoSize"));
            if (snap.Mode == ProPixelizerCamera.PixelSizeMode.WorldSpacePixelSize)
            {
                if (snap.WSPixelSizeConstraintStatus == ProPixelizerCamera.WorldSpacePixelSizeConstraintStatus.CannotMaintainWSPixelSize)
                    EditorGUILayout.HelpBox("The desired world-space pixel size could not be maintained for this screen resolution and camera orthographic size. Either increase the world-space pixel size, or decrease the camera orthographic size.", MessageType.Warning);
                if (snap.WSPixelSizeConstraintStatus == ProPixelizerCamera.WorldSpacePixelSizeConstraintStatus.CannotMaintainOrthoSize)
                    EditorGUILayout.HelpBox("The desired camera orthographic size could not be maintained for this screen resolution and world-space pixel size. Either increase the world-space pixel size, or decrease the camera orthographic size.", MessageType.Warning);
            }
            GUI.enabled = snap.Mode == ProPixelizerCamera.PixelSizeMode.FixedDownscalingRatio;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("TargetScale"));
            GUI.enabled = snap.Mode == ProPixelizerCamera.PixelSizeMode.FixedTargetResolution;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("FixedTargetResolution"));
            GUI.enabled = true;
            EditorGUI.indentLevel--;
            EditorGUILayout.Separator();

            //EditorGUILayout.PropertyField(serializedObject.FindProperty("EnableProPixelizer"));
            EditorGUILayout.Separator();

            EditorGUILayout.LabelField("Camera Options", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These settings are applied with the selected pixelization filter. ProPixelizer also supports per-object color grading" +
                " and edge control through individual materials.",
                MessageType.None);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("FullscreenColorGradingLUT"));


            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Render Feature Overrides", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = true;
            var enableProPixelizer = serializedObject.FindProperty("EnableProPixelizer");
            enableProPixelizer.boolValue = EditorGUILayout.Toggle(enableProPixelizer.boolValue, GUILayout.MaxWidth(15f));
            GUI.enabled = enableProPixelizer.boolValue;
            EditorGUILayout.LabelField("Enable ProPixelizer");
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = true;
            var overridePixelExpansion = serializedObject.FindProperty("overrideUsePixelExpansion");
            overridePixelExpansion.boolValue = EditorGUILayout.Toggle(overridePixelExpansion.boolValue, GUILayout.MaxWidth(15f));
            GUI.enabled = overridePixelExpansion.boolValue;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("UsePixelExpansion"));
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();
        }
    }
}

#endif