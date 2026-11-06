// Copyright Elliot Bentine, 2018-
using System.Collections.Generic;
using UnityEngine;
#if (UNITY_EDITOR)
using UnityEditor;
#endif

namespace ProPixelizer
{
    public class ProPixelizerNote : MonoBehaviour
    {
        public List<string> Message;
        public bool ShowUserGuide = false;
    }

#if (UNITY_EDITOR)
    [CustomEditor(typeof(ProPixelizerNote))]
    public class ProPixelizerNoteEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            ProPixelizerNote note = target as ProPixelizerNote;
            EditorGUILayout.LabelField("ProPixelizer | Note from Elliot", EditorStyles.boldLabel);
            EditorGUILayout.Separator();
            if (note == null)
                return;

            if (note.Message != null)
            {
                foreach (var s in note.Message)
                    EditorGUILayout.LabelField(s, EditorStyles.wordWrappedLabel);
            }
            
            if (note.ShowUserGuide)
            {
                if (GUILayout.Button("User Guide")) 
                    Application.OpenURL(ProPixelizerUtils.USER_GUIDE_URL);
            }
        }
    }
#endif
}