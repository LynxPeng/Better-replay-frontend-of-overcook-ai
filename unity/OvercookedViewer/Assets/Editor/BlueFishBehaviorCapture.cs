using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using OvercookedReplay;

// Capture the actual actor adapter over a fixed timeline; no facial-shape overrides.
[InitializeOnLoad]
public static class BlueFishBehaviorCapture {
    const string Key="Overcooked.BlueFish.BehaviorCapture";
    const int FrameCount=73;
    static int shot=-1,readyFrame;static double warm=-1;
    static BlueFishBehaviorCapture(){if(SessionState.GetBool(Key,false))EditorApplication.update+=Poll;}
    public static void Run() {Begin(false);}
    public static void RunWalkCycle() {Begin(true);}
    static void Begin(bool walk) {
        shot=-1;readyFrame=0;warm=-1;
        SessionState.SetBool(Key+".Walk",walk);
        SessionState.SetBool(Key,true);SessionState.SetFloat(Key+".Start",(float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene(BlueFishBootstrap.ScenePath);
        EditorApplication.update-=Poll;EditorApplication.update+=Poll;EditorApplication.EnterPlaymode();
    }
    static void Poll() {
        try {
            if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".Start",0)>90)throw new Exception("Behavior capture timed out");
            if(!EditorApplication.isPlaying)return;
            var showcase=UnityEngine.Object.FindFirstObjectByType<BlueFishShowcase>();if(!showcase)return;showcase.enabled=false;
            if(showcase.counter)showcase.counter.gameObject.SetActive(false);
            if(warm<0){warm=EditorApplication.timeSinceStartup;return;}
            if(EditorApplication.timeSinceStartup-warm<1)return;
            var actor=showcase.actor;var camera=Camera.main;
            bool walk=SessionState.GetBool(Key+".Walk",false);
            camera.transform.position=new Vector3(.9f,1.3f,3);camera.transform.LookAt(new Vector3(0,.78f,0));camera.fieldOfView=30;
            string folder=Path.GetFullPath("../../output/bluefish/"+(walk?"walk-clearance":"oh-jump")+"/frames");Directory.CreateDirectory(folder);
            if(shot<0) {
                actor.Sample(FishAction.Idle,0);Capture(camera,Path.Combine(folder,"warmup.png"));
                File.WriteAllText(Path.Combine(folder,"timing.csv"),"seconds,file,state,lift,oh,happy,blink\n");shot=0;return;
            }
            float t=walk?shot*BlueFishMotion.Duration(FishAction.Walk)/24:shot/15f,phase=(t-3.2f)/BlueFishMotion.CelebrationDuration;
            bool delivery=!walk&&phase>=0&&phase<1;
            var action=walk?FishAction.Walk:delivery?FishAction.Deliver:FishAction.Idle;
            float local=delivery?phase*BlueFishMotion.Duration(FishAction.Deliver):t;
            if(readyFrame==0){actor.Sample(action,local,1,t,delivery?phase:-1);readyFrame=Time.frameCount+3;return;}
            if(Time.frameCount<readyFrame)return;
            var pose=BlueFishMotion.Sample(action,local,1,t,delivery?phase:-1);
            string file=$"frame-{shot:D3}.png";Capture(camera,Path.Combine(folder,file));
            if(walk&&shot<24&&shot%6==0) {
                camera.transform.position=new Vector3(3.15f,1.3f,0);camera.transform.LookAt(new Vector3(0,.78f,0));Capture(camera,Path.Combine(folder,$"side-{shot:D3}.png"));
                camera.transform.position=new Vector3(0,1.3f,-3.15f);camera.transform.LookAt(new Vector3(0,.78f,0));Capture(camera,Path.Combine(folder,$"rear-{shot:D3}.png"));
            }
            string state=walk?"walk":delivery?"delivery":pose.Blink>.01f?"blink":"default";
            File.AppendAllText(Path.Combine(folder,"timing.csv"),string.Format(CultureInfo.InvariantCulture,"{0:R},{1},{2},{3:R},{4:R},{5:R},{6:R}\n",t,file,state,pose.RootLift,pose.Oh,pose.Smile,pose.Blink));
            shot++;readyFrame=0;if(shot<(walk?49:FrameCount))return;
            Debug.Log(walk?"BlueFish walk capture complete: 49 frames, two gait loops, front/side/rear samples":"BlueFish behavior capture complete: 73 frames, default oh, timed blink, one happy delivery jump, reset");Finish(0);
        }catch(Exception ex){Debug.LogException(ex);Finish(1);}
    }
    static void Capture(Camera camera,string path) {
        var previous=RenderTexture.active;var target=new RenderTexture(800,800,24){antiAliasing=4};
        var image=new Texture2D(800,800,TextureFormat.RGB24,false);
        try {
            camera.aspect=1;RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest {destination=target});
            RenderTexture.active=target;image.ReadPixels(new Rect(0,0,800,800),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        }finally {RenderTexture.active=previous;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);camera.ResetAspect();}
    }
    static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Poll;EditorApplication.Exit(code);}
}
