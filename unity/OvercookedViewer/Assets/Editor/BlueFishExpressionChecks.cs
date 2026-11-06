using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using OvercookedReplay;

// Render the model's existing expression blendshape in real PlayMode, without desktop input.
[InitializeOnLoad]
public static class BlueFishExpressionChecks {
    const string Key="Overcooked.BlueFish.ExpressionCheck";
    static double warm=-1;
    static int shot=-1,readyFrame;
    static BlueFishExpressionChecks() {if(SessionState.GetBool(Key,false))EditorApplication.update+=Poll;}
    public static void DefaultMood() {
        foreach(FishAction action in Enum.GetValues(typeof(FishAction)))for(int i=0;i<=10;i++) {
            var pose=BlueFishMotion.Sample(action,BlueFishMotion.Duration(action)*i/10);
            bool happy=action==FishAction.Deliver&&i<10;
            if(pose.Smile!=(happy?1:0)||pose.Oh!=(happy?0:1))throw new Exception("Expected default oh, full Happy only during delivery: "+action+" at "+i);
        }
        var blink=BlueFishMotion.Sample(FishAction.Idle,2.6f);
        if(blink.Blink<.99f||BlueFishMotion.Sample(FishAction.Idle,0).Blink!=0||BlueFishMotion.Sample(FishAction.Walk,.2f,1,6.1f).Blink<.99f)
            throw new Exception("Default oh must blink every 3.5 seconds on an independent expression clock");
        float d=BlueFishMotion.Duration(FishAction.Deliver);
        if(BlueFishMotion.Sample(FishAction.Deliver,0).RootLift!=0||BlueFishMotion.Sample(FishAction.Deliver,d/2).RootLift<.15f||Mathf.Abs(BlueFishMotion.Sample(FishAction.Deliver,d).RootLift)>.0001f)
            throw new Exception("Delivery needs one visible jump with grounded endpoints");
        int peaks=0;float previous=0;bool rising=true;
        for(int i=1;i<=100;i++){float y=BlueFishMotion.Sample(FishAction.Deliver,d*i/100).RootLift;if(y<previous&&rising){peaks++;rising=false;}else if(y>previous+.0001f&&!rising)rising=true;previous=y;}
        if(peaks!=1)throw new Exception("Delivery must jump exactly once");
        var overlay=BlueFishMotion.Sample(FishAction.Walk,.2f,1,2.6f,.5f);
        if(overlay.Smile!=1||overlay.Oh!=0||overlay.Blink!=0||overlay.RootLift<.15f)throw new Exception("Recent delivery must overlay a happy jump while the recorded chef keeps moving");
        var expired=BlueFishMotion.Sample(FishAction.Deliver,.62f,1,.95f,-1);
        if(expired.Smile!=0||expired.Oh!=1||expired.RootLift!=0)throw new Exception("An explicitly expired celebration must not restart within a long delivery step");
        Debug.Log("BlueFish oh/blink and happy single-jump checks passed: 94");
    }
    public static void Run() {
        SessionState.SetBool(Key,true);SessionState.SetFloat(Key+".Start",(float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene(BlueFishBootstrap.ScenePath);
        EditorApplication.update-=Poll;EditorApplication.update+=Poll;EditorApplication.EnterPlaymode();
    }
    static void Poll() {
        try {
            if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".Start",0)>60)throw new Exception("Expression render timed out");
            if(!EditorApplication.isPlaying)return;
            var showcase=UnityEngine.Object.FindFirstObjectByType<BlueFishShowcase>();if(!showcase)return;
            showcase.enabled=false;
            if(warm<0){warm=EditorApplication.timeSinceStartup;return;}
            if(EditorApplication.timeSinceStartup-warm<1)return;
            var actor=showcase.actor;var camera=Camera.main;
            camera.transform.position=new Vector3(.65f,1.15f,2.7f);camera.transform.LookAt(new Vector3(0,.78f,0));camera.fieldOfView=30;
            string output=Path.GetFullPath("../../output/bluefish/happy");Directory.CreateDirectory(output);
            if(shot<0){Capture(camera,Path.Combine(output,"warmup.png"));shot=0;readyFrame=0;return;}
            if(readyFrame==0) {
                if(shot==5)actor.Sample(FishAction.Deliver,BlueFishMotion.Duration(FishAction.Deliver)*.5f);
                else actor.Sample(FishAction.Idle,shot==6?2.6f:1.1f);
                int bound=0;
                foreach(var mesh in actor.GetComponentsInChildren<SkinnedMeshRenderer>()) {
                    int shape=mesh.sharedMesh.GetBlendShapeIndex("happy");
                    if(shape>=0){if(shot<4)mesh.SetBlendShapeWeight(shape,new[]{0f,.35f,.6f,.9f}[shot]*100);bound++;}
                }
                if(bound==0)throw new Exception("Model has no happy blendshape");
                readyFrame=Time.frameCount+3;return;
            }
            if(Time.frameCount<readyFrame)return;
            string file=shot<4?"happy-"+new[]{0,35,60,90}[shot]+".png":shot==4?"default.png":shot==5?"delivery.png":"blink.png";
            Capture(camera,Path.Combine(output,file));shot++;readyFrame=0;
            if(shot<7)return;
            Debug.Log("BlueFish expression renders saved: "+output);Finish(0);
        }catch(Exception ex){Debug.LogException(ex);Finish(1);}
    }
    static void Capture(Camera camera,string path) {
        var target=new RenderTexture(960,960,24){antiAliasing=4};var previous=RenderTexture.active;
        var image=new Texture2D(960,960,TextureFormat.RGB24,false);
        try {
            camera.aspect=1;RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest {destination=target});
            RenderTexture.active=target;image.ReadPixels(new Rect(0,0,960,960),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        }finally {RenderTexture.active=previous;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);camera.ResetAspect();}
    }
    static void Finish(int code) {SessionState.SetBool(Key,false);EditorApplication.update-=Poll;EditorApplication.Exit(code);}
}
