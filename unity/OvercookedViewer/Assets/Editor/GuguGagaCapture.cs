using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using OvercookedReplay;

[InitializeOnLoad]
public static class GuguGagaCapture {
    const string Key="Overcooked.GuguGaga.Capture";
    static int shot=-1,ready;static double warm=-1;
    static GuguGagaCapture(){if(SessionState.GetBool(Key,false))EditorApplication.update+=Poll;}
    public static void Run(){SessionState.SetBool(Key,true);SessionState.SetFloat(Key+".Start",(float)EditorApplication.timeSinceStartup);EditorSceneManager.OpenScene(GuguGagaBootstrap.ScenePath);EditorApplication.update-=Poll;EditorApplication.update+=Poll;EditorApplication.EnterPlaymode();}
    static void Poll() {
        try {
            if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".Start",0)>150)throw new Exception("Penguin capture timed out");
            if(!EditorApplication.isPlaying)return;
            var showcase=UnityEngine.Object.FindFirstObjectByType<BlueFishShowcase>();if(!showcase)return;showcase.enabled=false;
            var actor=UnityEngine.Object.FindFirstObjectByType<GuguGagaActor>();if(!actor)return;
            if(showcase.counter)showcase.counter.gameObject.SetActive(false);
            if(warm<0){warm=EditorApplication.timeSinceStartup;return;}if(EditorApplication.timeSinceStartup-warm<1)return;
            var camera=Camera.main;camera.transform.position=new Vector3(.55f,1.15f,3.2f);camera.transform.LookAt(new Vector3(0,.68f,0));camera.fieldOfView=31;
            string folder=Path.GetFullPath("../../output/gugugaga/frames");Directory.CreateDirectory(folder);
            if(shot<0){actor.Sample(FishAction.Idle,0);Capture(camera,Path.Combine(folder,"warmup.png"));File.WriteAllText(Path.Combine(folder,"timing.csv"),"seconds,file\n");shot=0;return;}
            bool still=shot>=106;
            float t=shot/15f;FishAction action=FishAction.Idle;float local=t,celebration=-1;
            if(still){action=(FishAction)(shot-106);local=BlueFishMotion.Duration(action)*.5f;}
            else if(t>=2.8f&&t<4.3f){action=FishAction.Walk;local=t-2.8f;}
            else if(t>=4.3f&&t<5.3f){action=FishAction.Carry;local=t-4.3f;}
            else if(t>=5.3f&&t<5.3f+BlueFishMotion.CelebrationDuration){action=FishAction.Deliver;local=t-5.3f;celebration=local/BlueFishMotion.CelebrationDuration;}
            if(still&&action==FishAction.Deliver)celebration=.5f;
            if(ready==0){actor.Sample(action,local,1,t,celebration);ready=Time.frameCount+3;return;}
            if(Time.frameCount<ready)return;
            string file=still?"pose-"+action+".png":$"frame-{shot:D3}.png";Capture(camera,Path.Combine(folder,file));
            if(!still)File.AppendAllText(Path.Combine(folder,"timing.csv"),t.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+file+"\n");
            if(still&&action==FishAction.Idle) {
                camera.transform.position=new Vector3(3.2f,1.1f,0);camera.transform.LookAt(new Vector3(0,.68f,0));Capture(camera,Path.Combine(folder,"side.png"));
                camera.transform.position=new Vector3(0,1.1f,-3.2f);camera.transform.LookAt(new Vector3(0,.68f,0));Capture(camera,Path.Combine(folder,"rear.png"));
            }
            shot++;ready=0;if(shot<114)return;
            Debug.Log("GuguGaga capture complete: 106 timed frames, eight poses, side and rear");Finish(0);
        }catch(Exception ex){Debug.LogException(ex);Finish(1);}
    }
    public static void Capture(Camera camera,string path,int width=720,int height=720) {
        var previous=RenderTexture.active;var target=new RenderTexture(width,height,24){antiAliasing=4};var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        try {camera.aspect=(float)width/height;RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally{RenderTexture.active=previous;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);camera.ResetAspect();}
    }
    static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Poll;EditorApplication.Exit(code);}
}
