// Copyright Elliot Bentine, 2022-
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ProPixelizer.Tools.Migration
{
    /// <summary>
    /// Reads and writes ProPixelizer's editor-only material migration version.
    /// </summary>
    public static class ProPixelizerMaterialVersion
    {
        private const string SavedProperties = "m_SavedProperties";
        private const string IntegerProperties = "m_Ints";
        private const string PropertyName = "first";
        private const string PropertyValue = "second";
        private const string PROPERTY_NAME = "_ProPixelizerMaterialVersion";

        public static void SetProPixelizerMaterialVersion(Material material, int version)
        {
            if (material == null)
                return;

            SetProPixelizerMaterialVersion(new SerializedObject(material), version);
        }

        public static void SetProPixelizerMaterialVersion(SerializedObject so, int version)
        {
            var serializedIntegers = GetSerializedIntegerProperties(so);
            if (serializedIntegers == null)
                return;

            int versionIndex = GetVersionPropertyIndex(serializedIntegers);
            if (versionIndex == -1)
            {
                versionIndex = serializedIntegers.arraySize;
                serializedIntegers.InsertArrayElementAtIndex(versionIndex);
                serializedIntegers
                    .GetArrayElementAtIndex(versionIndex)
                    .FindPropertyRelative(PropertyName)
                    .stringValue = PROPERTY_NAME;
            }

            serializedIntegers
                .GetArrayElementAtIndex(versionIndex)
                .FindPropertyRelative(PropertyValue)
                .intValue = version;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static bool TryGetProPixelizerMaterialVersion(SerializedObject so, out int version)
        {
            version = 0;
            var serializedIntegers = GetSerializedIntegerProperties(so);
            if (serializedIntegers == null)
                return false;

            int versionIndex = GetVersionPropertyIndex(serializedIntegers);
            if (versionIndex == -1)
                return false;

            var serializedVersion = serializedIntegers
                .GetArrayElementAtIndex(versionIndex)
                .FindPropertyRelative(PropertyValue);
            if (serializedVersion == null)
                return false;

            version = serializedVersion.intValue;
            return true;
        }

        private static SerializedProperty GetSerializedIntegerProperties(SerializedObject so)
        {
            if (so == null)
                return null;

            var savedProperties = so.FindProperty(SavedProperties);
            return savedProperties?.FindPropertyRelative(IntegerProperties);
        }

        private static int GetVersionPropertyIndex(SerializedProperty serializedIntegers)
        {
            for (int i = 0; i < serializedIntegers.arraySize; i++)
            {
                var propertyName = serializedIntegers
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative(PropertyName);
                if (propertyName != null &&
                    propertyName.stringValue == PROPERTY_NAME)
                    return i;
            }

            return -1;
        }
    }
}
#endif
