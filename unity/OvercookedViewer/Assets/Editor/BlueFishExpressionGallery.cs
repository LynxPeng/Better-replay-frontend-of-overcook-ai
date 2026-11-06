using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using OvercookedReplay;

// Local, model-faithful portrait renders. Does not modify the runtime's fixed Happy mood.
[InitializeOnLoad]
public static class BlueFishExpressionGallery {
    const string Key="Overcooked.BlueFish.ExpressionGallery";
    static readonly string[] Names={"happy","angry","sad","relaxed","neutral"};
    static readonly string[] MouthNames={"happy-50","oh-100","happy-50-oh-100"};
    static readonly string[] GazeAndVisemeNames={"lookUp","lookDown","lookLeft","lookRight","aa","ih","ou","ee","oh"};
    static int shot=-1,readyFrame;static double warm=-1;
    static BlueFishExpressionGallery() {if(SessionState.GetBool(Key,false))EditorApplication.update+=Poll;}
    public static void Run() {Begin(0);}
    public static void RunMouthComparison() {Begin(1);}
    public static void RunGazeAndVisemes() {Begin(2);}
    static void Begin(int mode) {
        shot=-1;readyFrame=0;warm=-1;
        SessionState.SetInt(Key+".Mode",mode);
        SessionState.SetBool(Key,true);SessionState.SetFloat(Key+".Start",(float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene(BlueFishBootstrap.ScenePath);
        EditorApplication.update-=Poll;EditorApplication.update+=Poll;EditorApplication.EnterPlaymode();
    }
    static void Poll() {
        try {
            if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".Start",0)>60)throw new Exception("Expression gallery timed out");
            if(!EditorApplication.isPlaying)return;
            var showcase=UnityEngine.Object.FindFirstObjectByType<BlueFishShowcase>();if(!showcase)return;
            showcase.enabled=false;
            if(warm<0){warm=EditorApplication.timeSinceStartup;return;}
            if(EditorApplication.timeSinceStartup-warm<1)return;
            var actor=showcase.actor;var camera=Camera.main;
            int mode=SessionState.GetInt(Key+".Mode",0);var names=mode==1?MouthNames:mode==2?GazeAndVisemeNames:Names;
            camera.transform.position=new Vector3(0,1.02f,1.75f);camera.transform.LookAt(new Vector3(0,.90f,0));camera.fieldOfView=34;
            string folder=Path.GetFullPath("../../output/bluefish/"+(mode==1?"mouth-comparison":mode==2?"gaze-and-visemes":"expressions"));Directory.CreateDirectory(folder);
            if(shot<0){Capture(camera,Path.Combine(folder,"warmup.png"));shot=0;return;}
            if(readyFrame==0) {
                actor.Sample(FishAction.Idle,1.1f);int bound=0;
                var weights=new Dictionary<string,float>();
                if(mode==1) {
                    if(shot!=1)weights["happy"]=50;
                    if(shot!=0)weights["oh"]=100;
                } else if(names[shot]!="neutral")weights[names[shot]]=100;
                foreach(var mesh in actor.GetComponentsInChildren<SkinnedMeshRenderer>()) {
                    // Presets in this model each bind one named face morph. Clear other shapes
                    // so gaze and visemes can be compared without a Happy expression overlay.
                    for(int i=0;i<mesh.sharedMesh.blendShapeCount;i++)mesh.SetBlendShapeWeight(i,0);
                    foreach(var weight in weights) {
                        int shape=-1;
                        // VRM lookUp/lookLeft use lowercase names lookup/lookleft in this mesh.
                        for(int i=0;i<mesh.sharedMesh.blendShapeCount;i++)
                            if(string.Equals(mesh.sharedMesh.GetBlendShapeName(i),weight.Key,StringComparison.OrdinalIgnoreCase)){shape=i;break;}
                        if(shape>=0){mesh.SetBlendShapeWeight(shape,weight.Value);bound++;}
                    }
                }
                if(bound<weights.Count)throw new Exception("Missing expression: "+names[shot]);
                readyFrame=Time.frameCount+4;return;
            }
            if(Time.frameCount<readyFrame)return;
            string path=Path.Combine(folder,$"{shot+1:D2}-{names[shot]}.png");Capture(camera,path);
            Debug.Log("Expression gallery captured: "+names[shot]+", "+path);
            shot++;readyFrame=0;if(shot<names.Length)return;
            Debug.Log("BlueFish expression gallery complete: "+names.Length+" equal-pose portraits");Finish(0);
        }catch(Exception ex){Debug.LogException(ex);Finish(1);}
    }
    static void Capture(Camera camera,string path) {
        var previous=RenderTexture.active;var target=new RenderTexture(960,960,24){antiAliasing=4};
        var image=new Texture2D(960,960,TextureFormat.RGB24,false);
        try {
            camera.aspect=1;RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest {destination=target});
            RenderTexture.active=target;image.ReadPixels(new Rect(0,0,960,960),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        }finally {RenderTexture.active=previous;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);camera.ResetAspect();}
    }
    static void Finish(int code) {SessionState.SetBool(Key,false);EditorApplication.update-=Poll;EditorApplication.Exit(code);}
}
