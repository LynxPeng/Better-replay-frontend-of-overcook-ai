using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

public static class ViewerBackgroundAssets {
    public static void InspectSkyUV() {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SimpleSky/Prefabs/SkyDome.prefab");
        var report=new System.Text.StringBuilder();
        foreach(var filter in source.GetComponentsInChildren<MeshFilter>(true)) {
            float min=999,max=-999;foreach(var uv in filter.sharedMesh.uv){min=Mathf.Min(min,uv.x);max=Mathf.Max(max,uv.x);}
            report.AppendLine(filter.name+" U="+min+".."+max);
        }
        System.IO.File.WriteAllText("../../output/simple-sky-uv.txt",report.ToString());
    }
    public static void BakeSky() {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SimpleSky/Prefabs/SkyDome.prefab");
        if(!source)throw new InvalidOperationException("Import the requested SimpleSky package into Assets/SimpleSky first");
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SimpleSky/Textures/SimpleSky.png");
        if(!texture)throw new InvalidOperationException("SimpleSky atlas is missing; import the complete SimpleSky package");
        var background=SkyMaterial("ReplaySky_Background",texture,1000);
        var details=SkyMaterial("ReplaySky_Details",texture,1001);
        var root=new GameObject("Simple Sky Background");root.AddComponent<OvercookedReplay.LevelSky>();
        var dome=UnityEngine.Object.Instantiate(source,root.transform);
        var bounds=OvercookedReplay.KitchenFactory.LocalBounds(dome,root.transform);
        dome.transform.localScale*=200/Mathf.Max(bounds.extents.x,Mathf.Max(bounds.extents.y,bounds.extents.z));
        foreach(var renderer in dome.GetComponentsInChildren<Renderer>(true)) {
            renderer.sharedMaterial=renderer.gameObject.name=="SkyDome"?background:details;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        foreach(var node in dome.GetComponentsInChildren<Transform>(true))
            if(node.name=="Moon"||node.name=="Stars")node.gameObject.SetActive(false);
        var positions=new[]{new Vector3(-100,45,180),new Vector3(110,40,200),new Vector3(-125,-45,210),new Vector3(130,-55,230)};
        for(int i=0;i<positions.Length;i++) {
            var cloudSource=AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/SimpleSky/Prefabs/Cloud_0{i+1}.prefab");
            if(!cloudSource)throw new InvalidOperationException("SimpleSky Cloud_0"+(i+1)+" prefab is missing");
            var cloud=UnityEngine.Object.Instantiate(cloudSource,root.transform);cloud.name="SimpleSky Cloud "+i;
            var b=OvercookedReplay.KitchenFactory.LocalBounds(cloud,root.transform);float scale=45/Mathf.Max(b.size.x,Mathf.Max(b.size.y,b.size.z));
            cloud.transform.localScale*=scale;cloud.transform.localPosition=positions[i]-b.center*scale;
            foreach(var renderer in cloud.GetComponentsInChildren<Renderer>()) {
                renderer.sharedMaterial=details;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            }
        }
        foreach(var collider in root.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(collider);
        foreach(var light in root.GetComponentsInChildren<Light>(true))UnityEngine.Object.DestroyImmediate(light);
        PrefabUtility.SaveAsPrefabAsset(root,"Assets/Resources/Prefabs/ReplaySky.prefab");UnityEngine.Object.DestroyImmediate(root);
    }
    static Material SkyMaterial(string name,Texture texture,int queue) {
        string path="Assets/GeneratedMaterials/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(Shader.Find("OvercookedReplay/SimpleSky"));AssetDatabase.CreateAsset(material,path);}
        material.shader=Shader.Find("OvercookedReplay/SimpleSky");material.SetTexture("_BaseMap",texture);
        // Author's dome U is fixed at 0.4998: zero offset samples the blue noon
        // column; positive offsets move through sunset and then night.
        material.SetTextureOffset("_BaseMap",Vector2.zero);material.SetColor("_BaseColor",Color.white);
        material.SetFloat("_Cull",0);material.SetFloat("_ZWrite",0);material.renderQueue=queue;EditorUtility.SetDirty(material);return material;
    }
    public static void UpgradeTownMaterials() {
        foreach(string model in OvercookedReplay.TownExterior.RequiredModels)
            if(!Resources.Load<GameObject>("CartoonTown/Models/"+model))throw new InvalidOperationException("CartoonTown model missing: "+model);
        foreach(string atlas in new[]{"Colors","GlossyColors","Floor"})
            if(!Resources.Load<Texture2D>("CartoonTown/Textures/"+atlas))throw new InvalidOperationException("CartoonTown texture missing: "+atlas);
        foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{"Assets/Resources/CartoonTown"})) {
            var material=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            var texture=material.mainTexture;Color color=material.HasProperty("_Color")?material.color:Color.white;
            material.shader=Shader.Find("Universal Render Pipeline/Lit");material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",color);
            material.SetFloat("_Smoothness",.12f);material.SetFloat("_Metallic",0);material.SetColor("_EmissionColor",Color.black);
            material.DisableKeyword("_EMISSION");material.SetFloat("_SpecularHighlights",0);material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            EditorUtility.SetDirty(material);
        }
    }
}
