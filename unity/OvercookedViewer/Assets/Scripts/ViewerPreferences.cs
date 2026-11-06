using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OvercookedReplay {
[Serializable] public class ViewerPreferences {
    public int version=1,width=1920,height=1080,msaa=4,frameRate=60,viewMode=0;
    public float renderScale=1.25f,cameraPitch=42; public bool borderless,night;
    public static readonly int[] Widths={1366,1600,1920,2560,3840};
    public static readonly int[] Heights={768,900,1080,1440,2160};
    public static string DefaultPath=>Path.Combine(Application.persistentDataPath,"viewer-settings-v1.json");
    public void Sanitize() {
        int i=Array.IndexOf(Widths,width);
        if(i<0||Heights[i]!=height){width=1920;height=1080;}
        if(msaa!=1&&msaa!=2&&msaa!=4&&msaa!=8)msaa=4;
        renderScale=float.IsFinite(renderScale)?Mathf.Clamp(renderScale,.75f,2):1.25f;
        if(frameRate!=0&&frameRate!=30&&frameRate!=60&&frameRate!=120)frameRate=60;
        viewMode=Mathf.Clamp(viewMode,0,1);version=1;
        cameraPitch=float.IsFinite(cameraPitch)?Mathf.Clamp(cameraPitch,35,65):42;
    }
    public static ViewerPreferences Load(string path) {
        try{if(File.Exists(path)){var p=JsonUtility.FromJson<ViewerPreferences>(File.ReadAllText(path));
            if(p!=null&&p.version==1){p.Sanitize();return p;}}}catch(ArgumentException){}catch(IOException){}catch(UnauthorizedAccessException){}
        return new ViewerPreferences();
    }
    public void Save(string path) {Sanitize();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllText(path,JsonUtility.ToJson(this,true));}
    public ViewerPreferences Copy()=>JsonUtility.FromJson<ViewerPreferences>(JsonUtility.ToJson(this));
}
public static class ViewerGraphics {
    static UniversalRenderPipelineAsset active;
    public static UniversalRenderPipelineAsset Apply(ViewerPreferences p,UniversalRenderPipelineAsset source=null) {
        p.Sanitize();
        if(!active){if(!source)source=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(!source)throw new InvalidOperationException("URP render pipeline is missing");
            active=UnityEngine.Object.Instantiate(source);active.name="Viewer runtime quality";}
        active.msaaSampleCount=p.msaa;active.renderScale=p.renderScale;
        QualitySettings.renderPipeline=active;GraphicsSettings.defaultRenderPipeline=active;
        QualitySettings.vSyncCount=0;Application.targetFrameRate=p.frameRate==0?-1:p.frameRate;
        foreach(var camera in Camera.allCameras)camera.allowMSAA=true;
        return active;
    }
}
}
