using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OvercookedReplay;

[InitializeOnLoad]
public static class BlueFishRuntimeChecks {
    const string Key="Overcooked.BlueFish.RuntimeCheck";
    static ReplayPlayer player;static BlueFishActor actor;
    static Quaternion[] rotations;static Vector3[] positions;static Vector3 root;static int observed;
    static BlueFishRuntimeChecks() {if(SessionState.GetBool(Key,false))EditorApplication.update+=Poll;}
    public static void Run() {
        SessionState.SetBool(Key,true);SessionState.SetFloat(Key+".Start",(float)EditorApplication.timeSinceStartup);
        EditorSceneManager.OpenScene("Assets/Scenes/Replay.unity");
        EditorApplication.update-=Poll;EditorApplication.update+=Poll;EditorApplication.EnterPlaymode();
    }
    static void Require(bool value,string message) {if(!value)throw new Exception(message);}
    static void Poll() {
        try {
            if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".Start",0)>60)throw new Exception("BlueFish runtime check timed out");
            if(!EditorApplication.isPlaying)return;
            if(!player)player=UnityEngine.Object.FindFirstObjectByType<ReplayPlayer>();
            if(!player||player.Data==null)return;
            if(!actor)actor=UnityEngine.Object.FindFirstObjectByType<BlueFishActor>();
            Require(actor,"Replay blue chef did not instantiate the local animated model");
            player.Presentation.Pause();Time.timeScale=0;
            if(rotations==null) {
                player.Clock.Seek(.37f);player.SendMessage("Apply");
                rotations=actor.AnimatedTransforms.Select(t=>t.localRotation).ToArray();positions=actor.AnimatedTransforms.Select(t=>t.localPosition).ToArray();root=actor.transform.position;
                player.Seek(player.Data.frames.Length-1);player.Clock.Seek(.37f);player.SendMessage("Apply");
            }
            var transforms=actor.AnimatedTransforms.ToArray();
            for(int i=0;i<transforms.Length;i++) {
                Require(Quaternion.Angle(rotations[i],transforms[i].localRotation)<.06f,"Pause/seek changed bone rotation: "+transforms[i].name);
                Require(Vector3.Distance(positions[i],transforms[i].localPosition)<.00001f,"Pause/seek changed bone position");
            }
            Require(Vector3.Distance(root,actor.transform.position)<.00001f,"Pause/seek changed recorded root position");
            if(++observed<25)return;
            // Verify default oh, timed blinking, celebration and reset on real morphs and bones.
            actor.Sample(FishAction.Idle,1.1f);
            var meshes=actor.GetComponentsInChildren<SkinnedMeshRenderer>();
            var face=meshes.FirstOrDefault(m=>m.sharedMesh.GetBlendShapeIndex("happy")>=0);
            Require(face,"Model is missing its Happy expression");
            Require(Weight(face,"oh")>99&&Weight(face,"happy")<.01f&&Weight(face,"blink")<.01f,"Default oh did not reach the mesh");
            float foot=actor.Bone(HumanBodyBones.LeftFoot).position.y;Vector3 recorded=actor.transform.position;
            actor.Sample(FishAction.Deliver,BlueFishMotion.Duration(FishAction.Deliver)/2);
            Require(Weight(face,"happy")>99&&Weight(face,"oh")<.01f&&Weight(face,"blink")<.01f,"Delivery must use pure full Happy");
            Require(actor.Bone(HumanBodyBones.LeftFoot).position.y>foot+.12f,"Celebration mesh did not visibly leave the floor");
            Require(actor.transform.position==recorded,"Jump moved the recorded chef root");
            actor.Sample(FishAction.Idle,2.6f);
            Require(Weight(face,"happy")<.01f&&Weight(face,"oh")>99&&Weight(face,"blink")>99,"Returning from Happy must restore oh and the blink clock");
            actor.Sample(FishAction.Idle,1.1f);
            Require(Mathf.Abs(actor.Bone(HumanBodyBones.LeftFoot).position.y-foot)<.0001f,"Jump offset survived a seek back to idle");
            Require(Weight(face,"blink")<.01f,"Blink must reset after seeking");
            Finish(0,"BlueFish PlayMode checks passed: pause/seek, real oh/blink/Happy morphs, visible jump and grounded reset");
        }catch(Exception ex){Debug.LogException(ex);Finish(1,"BlueFish PlayMode check failed");}
    }
    static float Weight(SkinnedMeshRenderer mesh,string shape) {int i=mesh.sharedMesh.GetBlendShapeIndex(shape);Require(i>=0,"Missing morph: "+shape);return mesh.GetBlendShapeWeight(i);}
    static void Finish(int code,string message) {Time.timeScale=1;SessionState.SetBool(Key,false);EditorApplication.update-=Poll;Debug.Log(message);EditorApplication.Exit(code);}
}
