// Copyright Elliot Bentine, 2022-
#if UNITY_EDITOR
using System.Collections.Generic;

namespace ProPixelizer.Tools.Migration
{
    public class ProPixelizer2_0MaterialUpdater : ProPixelizer1_8MaterialUpdater
    {
        public override List<IMigratedProperty> GetMigratedProperties()
        {
            var list = base.GetMigratedProperties();
            list.Add(new RenamedTexture { OldName = "_Albedo", NewName = "_BaseMap" });
            list.Add(new RenamedTexture { OldName = "_NormalMap", NewName = "_BumpMap" });
            list.Add(new RenamedTexture { OldName = "_Emission", NewName = "_EmissionMap" });
            //list.Add(new RenamedTexture { OldName = "_MainTex", NewName = "_BaseMap" }); // fix for old URP obselete property - some asset store packs are serialized using the old, obselete names.
            // Problem with above; it then causes _MainTex to write over the value previously saved in _Albedo, then deletes them both.
            // I need some cleverer logic to only use _MainTex if _Albedo cannot be found.
            return list;
        }
    }
}
#endif