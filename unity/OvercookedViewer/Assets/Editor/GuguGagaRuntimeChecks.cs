using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OvercookedReplay;

// Use a genuine recorded delivery with player identities swapped, so agent1 owns
// the terminal event. Do not fabricate an extra game step to finish the animation.
[InitializeOnLoad]
public static class GuguGagaRuntimeChecks {
    const string Key="Overcooked.GuguGaga.Runtime";
    static ReplayPlayer player;static GuguGagaActor actor;static Transform model;
    static int stage,wait,lastFrame;static float restY,maxLift,pausedTime,pausedY;
    static GuguGagaRuntimeChecks(){if(SessionState.GetBool(Key,false))EditorApplication.update+=Poll;}
    public static void Run(){SessionState.SetBool(Key,true);SessionState.SetFloat(Key+".Start",(float)EditorApplication.timeSinceStartup);EditorSceneManager.OpenScene("Assets/Scenes/Replay.unity");EditorApplication.update-=Poll;EditorApplication.update+=Poll;EditorApplication.EnterPlaymode();}
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void Poll() {
        try {
            if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".Start",0)>75)throw new Exception("Penguin runtime check timed out");
            if(!EditorApplication.isPlaying)return;
            if(!player)player=UnityEngine.Object.FindFirstObjectByType<ReplayPlayer>();if(!player||player.Data==null)return;
            string folder=Path.GetFullPath("../../output/gugugaga");Directory.CreateDirectory(folder);
            if(stage==0) {
                var data=ReplayFile.Load(Path.Combine(Application.streamingAssetsPath,"Samples/cramped_room.ocrep.json"));
                foreach(var f in data.frames){var temp=f.state.players[0];f.state.players[0]=f.state.players[1];f.state.players[1]=temp;}
                foreach(var s in data.segments){if(s.actions!=null)Array.Reverse(s.actions);if(s.events!=null)foreach(var e in s.events)if(e.agent_index>=0)e.agent_index=1-e.agent_index;}
                Array.Reverse(data.header.players);
                string path=Path.Combine(folder,"orange-terminal-delivery.ocrep.json");File.WriteAllText(path,JsonUtility.ToJson(data));
                player.Load(path);wait=Time.frameCount+5;stage=1;return;
            }
            if(stage==1) {
                if(Time.frameCount<wait)return;
                actor=UnityEngine.Object.FindFirstObjectByType<GuguGagaActor>();var fish=UnityEngine.Object.FindFirstObjectByType<BlueFishActor>();
                Require(actor&&fish,"Both blue fish and orange penguin must be instantiated");
                Require(Vector3.Distance(actor.transform.position,ReplayPlayer.World(player.Data.frames[0].state.players[1].position))<.0001f,"Penguin must retain agent1 identity");
                Require(Vector3.Distance(fish.transform.position,ReplayPlayer.World(player.Data.frames[0].state.players[0].position))<.0001f,"Fish must retain agent0 identity");
                var camera=Camera.main;var rect=camera.rect;camera.rect=new Rect(0,0,1,1);
                GuguGagaCapture.Capture(camera,Path.Combine(folder,"two-chefs-kitchen.png"),1200,750);camera.rect=rect;
                // A cold Editor render/indexing stall can skip the jump peak in
                // wall-clock time. Exercise the real player pose and clock at a
                // deterministic 60 Hz; screenshots and EditMode check the skin.
                player.enabled=false;lastFrame=Time.frameCount;
                model=actor.transform.Find("Model");player.Presentation.Pause();player.Seek(player.Data.segments.Length-1);restY=model.localPosition.y;
                player.Presentation.Toggle();stage=2;return;
            }
            if((stage==2||stage==4)&&lastFrame!=Time.frameCount) {
                lastFrame=Time.frameCount;player.Presentation.Advance(1f/60);player.SendMessage("Apply",SendMessageOptions.RequireReceiver);
            }
            maxLift=Mathf.Max(maxLift,model.localPosition.y-restY);
            Require(player.Clock.Time<=player.Clock.Duration,"Animation changed recorded time");
            if(stage==2&&player.Clock.Time>=player.Clock.Duration&&player.Presentation.Tail>.1f){player.Presentation.Pause();pausedTime=player.Presentation.Time;pausedY=model.localPosition.y;wait=Time.frameCount+4;stage=3;return;}
            if(stage==3){if(Time.frameCount<wait)return;Require(player.Presentation.Time==pausedTime&&Mathf.Abs(model.localPosition.y-pausedY)<.0001f,"Paused penguin jump changed");player.Presentation.Toggle();stage=4;return;}
            if(stage==4&&!player.Presentation.Playing) {
                Require(maxLift>.15f&&Mathf.Abs(model.localPosition.y-restY)<.0001f,$"Terminal orange delivery failed to jump and land: maxLift={maxLift:R}, landingDelta={model.localPosition.y-restY:R}, tail={player.Presentation.Tail:R}, time={player.Presentation.Time:R}");
                Require(player.Clock.Time==player.Clock.Duration&&player.Data.frames[player.Clock.Index].score==20,"Final score/state changed");
                Require(Vector3.Distance(actor.transform.position,ReplayPlayer.World(player.Data.frames[player.Clock.Index].state.players[1].position))<.0001f,"Penguin jump changed recorded root");
                player.Seek(0);actor.Sample(FishAction.Carry,.1f);Vector3 anchor=actor.HeldAnchor.localPosition;
                Require(anchor.z>.1f&&anchor.y>.2f&&anchor.y<1,"Carry item must be in front below the head");
                Debug.Log("GuguGaga PlayMode passed: both player identities, orange terminal delivery, airborne pause/resume, land, final score/root, held anchor "+anchor);Finish(0);
            }
        }catch(Exception ex){Debug.LogException(ex);Finish(1);}
    }
    static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Poll;EditorApplication.Exit(code);}
}
