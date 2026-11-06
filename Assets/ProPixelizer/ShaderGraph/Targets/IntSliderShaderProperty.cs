// Workaround for lack of [IntRange] in shader graph properties.

using System;
using UnityEditor.Graphing;
using UnityEngine;
using UnityEditor.ShaderGraph.Internal;
using UnityEditor.ShaderGraph;

namespace ProPixelizer.ShaderGraph.Internal
{
    [Serializable]
    public sealed class IntSliderShaderProperty : AbstractShaderProperty<float>
    {
        internal IntSliderShaderProperty()
        {
            displayName = "Float";
        }

        public override PropertyType propertyType => PropertyType.Float;

        internal override bool isExposable => true;
        internal override bool isRenamable => true;

        public override float value
        {
            get => (int)base.value;
            set => base.value = value;
        }

        internal override string GetHLSLVariableName(bool isSubgraphProperty, GenerationMode mode)
        {
            HLSLDeclaration decl = GetDefaultHLSLDeclaration();
            if (decl == HLSLDeclaration.HybridPerInstance)
                return $"UNITY_ACCESS_HYBRID_INSTANCED_PROP({referenceName}, {concretePrecision.ToShaderString()})";
            else
                return base.GetHLSLVariableName(isSubgraphProperty, mode);
        }

        internal override string GetPropertyBlockString()
        {
            string valueString = NodeUtils.FloatToShaderValueShaderLabSafe(value);
            return $"[IntRange] {hideTagString}{referenceName}(\"{displayName}\", Range({NodeUtils.FloatToShaderValueShaderLabSafe(m_RangeValues.x)}, {NodeUtils.FloatToShaderValueShaderLabSafe(m_RangeValues.y)})) = {valueString}";
        }

        internal override string GetPropertyAsArgumentString(string precisionString)
        {
            return $"{concreteShaderValueType.ToShaderString(precisionString)} {referenceName}";
        }

#if UNITY_6000_6_OR_NEWER
        internal override void ForeachHLSLProperty(GenerationMode mode, Action<HLSLProperty> action)
#else
        internal override void ForeachHLSLProperty(Action<HLSLProperty> action)
#endif
        {
            HLSLDeclaration decl = GetDefaultHLSLDeclaration();
            action(new HLSLProperty(HLSLType._float, referenceName, decl, concretePrecision));
        }

        [SerializeField]
        Vector2 m_RangeValues = new Vector2(0, 1);

        public Vector2 rangeValues
        {
            get => m_RangeValues;
            set => m_RangeValues = value;
        }

        internal override AbstractMaterialNode ToConcreteNode()
        {
            return new SliderNode { value = new Vector3(value, m_RangeValues.x, m_RangeValues.y) };
        }

        internal override PreviewProperty GetPreviewMaterialProperty()
        {
            return new PreviewProperty(propertyType)
            {
                name = referenceName,
                floatValue = value
            };
        }

        internal override ShaderInput Copy()
        {
            return new Vector1ShaderProperty()
            {
                displayName = displayName,
                value = value,
                rangeValues = rangeValues,
            };
        }

        public override int latestVersion => 1;
        public override void OnAfterDeserialize(string json)
        {
            if (sgVersion == 0)
            {
                LegacyShaderPropertyData.UpgradeToHLSLDeclarationOverride(json, this);
                ChangeVersion(1);
            }
        }
    }
}
