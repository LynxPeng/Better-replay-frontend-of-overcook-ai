using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using OvercookedReplay;

public static class VisualEnhancementChecks {
    public static void PipelineSurvivesRestart() {
        if(!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))throw new Exception("Saved project lost URP after graphics checks");
        Debug.Log("Saved URP references survived editor restart");
    }
    public static void Run() {
        int count=0;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var camera=new GameObject("Fit Camera").AddComponent<Camera>();
        KitchenCamera.Apply(camera,9,5,KitchenView.LongEdge,1,0);
        if(Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.x,48))>.1f)throw new Exception("Default long edge angle must be the requested lower 48 degrees");count++;
        foreach(var dimensions in new[]{new Vector2Int(9,5),new Vector2Int(5,9),new Vector2Int(5,4)})
        foreach(float aspect in new[]{16f/9,16f/10,16f/9*.77f/.83f,16f/10*.77f/.83f})
        foreach(var mode in new[]{KitchenView.LongEdge,KitchenView.Isometric})
        foreach(float pitch in new[]{35f,48f,65f})foreach(float turn in new[]{0f,90f,180f,270f}) {
            camera.aspect=aspect;
            KitchenCamera.Apply(camera,dimensions.x,dimensions.y,mode,1,turn,pitch);
            Vector3 direction=camera.transform.forward;
            if(mode==KitchenView.LongEdge&&turn==0) {
                if(dimensions.x>=dimensions.y && Mathf.Abs(direction.x)>.001f)throw new Exception("Long edge camera is diagonal");
                if(dimensions.y>dimensions.x && Mathf.Abs(direction.z)>.001f)throw new Exception("Tall kitchen must face its long edge");
            }
            for(int x=0;x<2;x++)for(int z=0;z<2;z++)for(int y=0;y<2;y++) {
                var point=new Vector3(x==0?-.5f:dimensions.x-.5f,y*1.5f,z==0?.5f:-dimensions.y+.5f);
                var viewport=camera.WorldToViewportPoint(point);
                if(viewport.x<.02f||viewport.x>.98f||viewport.y<.02f||viewport.y>.98f)throw new Exception("Camera clips playable bounds");
                count++;
            }
        }
        string file=Path.GetFullPath("../../output/preferences-test.json");
        var settings=new ViewerPreferences{width=2560,height=1440,msaa=8,renderScale=1.5f,frameRate=120,cameraPitch=42,viewMode=1,borderless=true};
        settings.Save(file);var restored=ViewerPreferences.Load(file);
        if(restored.width!=2560||restored.height!=1440||restored.msaa!=8||restored.renderScale!=1.5f||restored.frameRate!=120||restored.cameraPitch!=42||restored.viewMode!=1||!restored.borderless)throw new Exception("Graphics settings did not round-trip");count++;
        File.WriteAllText(file,"broken json");restored=ViewerPreferences.Load(file);
        if(restored.width!=1920||restored.msaa!=4)throw new Exception("Corrupt graphics settings did not recover");count++;
        settings.width=-1;settings.msaa=7;settings.renderScale=float.NaN;settings.Sanitize();
        if(settings.width<=0||settings.msaa!=4||!float.IsFinite(settings.renderScale))throw new Exception("Invalid graphics settings accepted");count++;
        var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if(!pipeline)throw new Exception("URP not configured");
        var oldDefault=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;
        int oldFps=Application.targetFrameRate,oldVsync=QualitySettings.vSyncCount;UniversalRenderPipelineAsset quality=null;
        try {
            quality=ViewerGraphics.Apply(settings,pipeline);
            if(quality.msaaSampleCount!=4 || Mathf.Abs(quality.renderScale-1.25f)>.001f)throw new Exception("Graphics options not applied to actual URP asset");count++;
        }finally {
            GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;
            Application.targetFrameRate=oldFps;QualitySettings.vSyncCount=oldVsync;
            if(quality)UnityEngine.Object.DestroyImmediate(quality);
        }
        var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        if(renderer.renderingMode!=RenderingMode.Forward)throw new Exception("MSAA needs forward renderer");count++;
        var root=RestaurantBackdrop.Build(new GameObject("Background Test").transform,9,5);
        if(root.GetComponentsInChildren<Renderer>().Length<30||!root.transform.Find("Street")||!root.transform.Find("Dining"))throw new Exception("Restaurant surroundings missing");count++;
        if(!Resources.Load<GameObject>("Models/Nature/tree_default")||!Resources.Load<GameObject>("Models/City/building-a"))throw new Exception("Downloaded environment models not imported");count++;
        var station=KitchenFactory.Station('P');bool atlas=false;
        foreach(var r in station.GetComponentsInChildren<Renderer>())foreach(var m in r.sharedMaterials)
            if(m&&m.mainTexture&&(m.mainTexture.name=="restaurantbits_texture"||m.mainTexture.name=="restaurantbits_extra"))atlas=true;
        if(!atlas)throw new Exception("Kitchen must use the textured KayKit CC0 models");count++;
        File.WriteAllText("../../output/visual-upgrade-checks.json","{\"passed\":true,\"assertions\":"+count+"}");
        Debug.Log("Visual enhancement checks passed: "+count);
    }
}
