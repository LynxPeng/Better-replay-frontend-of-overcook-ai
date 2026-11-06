using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OvercookedReplay;

[InitializeOnLoad]
public static class BlueFishTailRuntimeChecks {
    const string Key="Overcooked.BlueFish.TailRuntimeCheck";
    static ReplayPlayer player;static BlueFishActor actor;static Transform model;
    static int stage,waitFrame;static float restY,maxLift,pausedTime,pausedY;
    static BlueFishTailRuntimeChecks(){if(SessionState.GetBool(Key,false))EditorApplication.update+=Poll;}
    public static void Run(){SessionState.SetBool(Key,true);SessionState.SetFloat(Key+".Start",(float)EditorApplication.timeSinceStartup);EditorSceneManager.OpenScene("Assets/Scenes/Replay.unity");EditorApplication.update-=Poll;EditorApplication.update+=Poll;EditorApplication.EnterPlaymode();}
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void Poll(){
        try {
            if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".Start",0)>65)throw new Exception("Tail runtime check timed out");
            if(!EditorApplication.isPlaying)return;
            if(!player)player=UnityEngine.Object.FindFirstObjectByType<ReplayPlayer>();if(!player||player.Data==null)return;
            if(stage==0){player.Load(Path.Combine(Application.streamingAssetsPath,"Samples/cramped_room.ocrep.json"));waitFrame=Time.frameCount+2;stage=1;return;}
            if(stage==1){
                if(Time.frameCount<waitFrame)return;
                Require(player.Presentation!=null,"ReplayPlayer has no presentation tail clock");
                actor=UnityEngine.Object.FindFirstObjectByType<BlueFishActor>();Require(actor,"No local blue fish actor");model=actor.transform.Find("Model");Require(model,"No visual Model child");
                player.Presentation.Pause();player.Seek(player.Data.segments.Length-1);restY=model.localPosition.y;
                player.Presentation.Toggle();stage=2;return;
            }
            maxLift=Mathf.Max(maxLift,model.localPosition.y-restY);
            Require(player.Clock.Time<=player.Clock.Duration,"Visual tail modified recorded time");
            if(stage==2&&player.Clock.Time>=player.Clock.Duration&&player.Presentation.Tail>.1f){player.Presentation.Pause();pausedTime=player.Presentation.Time;pausedY=model.localPosition.y;waitFrame=Time.frameCount+4;stage=3;return;}
            if(stage==3){if(Time.frameCount<waitFrame)return;Require(player.Presentation.Time==pausedTime&&Mathf.Abs(model.localPosition.y-pausedY)<.0001f,"Pause failed during the airborne terminal celebration");player.Presentation.Toggle();stage=4;return;}
            if(stage==4&&!player.Presentation.Playing){
                Require(maxLift>.15f,"Final delivery never completed its visible jump");
                Require(Mathf.Abs(model.localPosition.y-restY)<.0001f,"Final jump failed to land");
                Require(player.Clock.Time==player.Clock.Duration&&player.Data.frames[player.Clock.Index].score==20,"Final snapshot or score changed");
                Require(Vector3.Distance(actor.transform.position,ReplayPlayer.World(player.Data.frames[player.Clock.Index].state.players[0].position))<.0001f,"Jump changed the recorded position");
                var meshes=actor.GetComponentsInChildren<SkinnedMeshRenderer>();bool checkedFace=false;
                foreach(var mesh in meshes){int oh=mesh.sharedMesh.GetBlendShapeIndex("oh"),happy=mesh.sharedMesh.GetBlendShapeIndex("happy");if(oh<0||happy<0)continue;Require(mesh.GetBlendShapeWeight(oh)>99&&mesh.GetBlendShapeWeight(happy)<.01f,"Final jump did not restore oh");checkedFace=true;}
                Require(checkedFace,"No real facial morphs checked");Debug.Log("BlueFish terminal delivery PlayMode passed: complete jump, airborne pause/resume, land, restore oh, preserve final state/score");Finish(0);
            }
        }catch(Exception ex){Debug.LogException(ex);Finish(1);}
    }
    static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Poll;EditorApplication.Exit(code);}
}
