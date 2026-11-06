using System;
using System.IO;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using OvercookedReplay;

// Batch-mode PlayMode observation, including real MonoBehaviour.Update calls.
// SessionState survives the domain reload when entering PlayMode.
[InitializeOnLoad]
public static class TownLifeRuntimeChecks {
    const string Running="OvercookedReplay.AmbientRuntimeCheck";
    static ReplayPlayer player;static RiverBoatMotion boat;
    static Vector3 firstPosition;static double warmStart=-1,observedStart=-1;
    static int shot,animationFrame;static double nextAnimationShot;
    static TownLifeRuntimeChecks() {if(SessionState.GetBool(Running,false))EditorApplication.update+=Poll;}
    public static void Run() {
        SessionState.SetBool(Running,true);SessionState.SetFloat(Running+".Start",(float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene("Assets/Scenes/Replay.unity");
        EditorApplication.update-=Poll;EditorApplication.update+=Poll;EditorApplication.EnterPlaymode();
    }
    static void Poll() {
        try {
            if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Running+".Start",0)>65)throw new Exception("Ambient runtime check timed out");
            if(!EditorApplication.isPlaying)return;
            if(!player)player=UnityEngine.Object.FindFirstObjectByType<ReplayPlayer>();
            if(!player||player.Data==null)return;
            player.Clock.Playing=false;UnityEngine.Time.timeScale=0;
            if(warmStart<0){warmStart=EditorApplication.timeSinceStartup;return;}
            if(EditorApplication.timeSinceStartup-warmStart<1)return;
            if(!boat)boat=UnityEngine.Object.FindFirstObjectByType<RiverBoatMotion>();
            if(!boat)throw new Exception("No runtime boat component");
            if(observedStart<0) {
                observedStart=EditorApplication.timeSinceStartup;firstPosition=boat.transform.localPosition;
                Directory.CreateDirectory("../../output/ambient-frames");File.WriteAllText("../../output/ambient-frames/timing.csv","seconds,file\n");
                Capture(0);shot=1;return;
            }
            double elapsed=EditorApplication.timeSinceStartup-observedStart;
            if(elapsed>=nextAnimationShot&&elapsed<8) {
                string file="frame-"+animationFrame.ToString("D3")+".png";
                CaptureImage("../../output/ambient-frames/"+file,960,540);
                File.AppendAllText("../../output/ambient-frames/timing.csv",elapsed.ToString("R",CultureInfo.InvariantCulture)+","+file+"\n");
                animationFrame++;nextAnimationShot=elapsed+.25;
            }
            if(elapsed>4&&shot==1) {
                if(Mathf.Abs(boat.transform.localPosition.z-firstPosition.z)<1)throw new Exception("Boat stopped while replay and scaled game time were paused");
                int index=player.Clock.Index;var position=boat.transform.localPosition;
                player.Seek(index+1);player.Seek(index);
                if(Vector3.Distance(position,boat.transform.localPosition)>.001f)throw new Exception("Replay seek reset ambient boat motion");
                Capture(4);shot=2;
            }
            if(elapsed>8&&shot==2) {
                Capture(8);UnityEngine.Time.timeScale=1;SessionState.SetBool(Running,false);EditorApplication.update-=Poll;
                Debug.Log("Ambient PlayMode runtime check passed: boats advance with replay paused and timeScale=0; seek preserves motion; three timed renders saved");
                EditorApplication.Exit(0);
            }
        }catch(Exception ex) {
            UnityEngine.Time.timeScale=1;SessionState.SetBool(Running,false);EditorApplication.update-=Poll;Debug.LogException(ex);EditorApplication.Exit(1);
        }
    }
    static void Capture(int seconds) {CaptureImage("../../output/live-front-street-t"+seconds+".png",1920,1080);}
    static void CaptureImage(string path,int width,int height) {
        var camera=Camera.main;var rect=camera.rect;
        camera.rect=new Rect(0,0,1,1);camera.aspect=(float)width/height;
        KitchenCamera.Apply(camera,player.Data.header.terrain[0].Length,player.Data.header.terrain.Length,KitchenView.LongEdge,1,0);
        var sky=UnityEngine.Object.FindFirstObjectByType<LevelSky>();if(sky)sky.UpdatePose();
        var target=new RenderTexture(width,height,24);target.antiAliasing=4;
        RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});
        var previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
        File.WriteAllBytes(path,image.EncodeToPNG());
        RenderTexture.active=previous;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);
        camera.rect=rect;camera.ResetAspect();player.SendMessage("FrameCamera");
    }
}
