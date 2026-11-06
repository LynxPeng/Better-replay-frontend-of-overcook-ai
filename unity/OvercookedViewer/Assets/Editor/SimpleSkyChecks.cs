using System;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

public static class SimpleSkyChecks {
    public static void Exterior() {
        var root=OvercookedReplay.RestaurantBackdrop.Build(new GameObject("Exterior check").transform,5,4).transform;
        var exterior=root.Find("Town Exterior");
        if(!exterior)throw new Exception("Requested CartoonTown exterior has not been added");
        if(exterior.GetComponentsInChildren<Renderer>().Length<10||exterior.GetComponentsInChildren<Collider>().Length!=0)throw new Exception("Town exterior models or presentation isolation missing");
        var street=exterior.Find("Rear Street");if(!street)throw new Exception("Rear street grouping missing");
        foreach(Transform item in street)
            if((item.name.StartsWith("Buildings/")||item.name.StartsWith("Roads/"))&&item.localPosition.z<4)
                throw new Exception("Street and houses must stay beyond the rear windows");
        Debug.Log("CartoonTown compact exterior checks passed");
    }
    public static void Run() {
        var prefab=Resources.Load<GameObject>("Prefabs/ReplaySky");
        if(!prefab)throw new Exception("Requested Simple Sky is not integrated into the viewer");
        var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SimpleSky/Textures/SimpleSky.png");int renderers=0;
        if(!atlas)throw new Exception("Source SimpleSky atlas is missing");
        foreach(var renderer in prefab.GetComponentsInChildren<Renderer>(true)) {
            foreach(var material in renderer.sharedMaterials) {
                if(!material.shader.isSupported||material.GetTexture("_BaseMap")!=atlas)throw new Exception("SimpleSky native texture or compatible shader missing");
                if(material.renderQueue>=2000||material.GetFloat("_ZWrite")!=0)throw new Exception("Sky could occlude foreground kitchen geometry");
            }
            if(renderer.shadowCastingMode!=ShadowCastingMode.Off)throw new Exception("Sky clouds must not darken the kitchen");renderers++;
        }
        if(renderers<8||prefab.GetComponentsInChildren<Light>(true).Length!=0||prefab.GetComponentsInChildren<Collider>(true).Length!=0)throw new Exception("SimpleSky geometry/clouds or isolation is incorrect");
        Debug.Log("SimpleSky integration checks passed: "+renderers+" renderers");
    }
}
