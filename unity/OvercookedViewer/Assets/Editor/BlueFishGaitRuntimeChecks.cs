using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OvercookedReplay;

[InitializeOnLoad]
public static class BlueFishGaitRuntimeChecks {
    const string Key="Overcooked.GaitRuntime";
    static ReplayPlayer player;static BlueFishActor actor;static int phase,wait;
    static Quaternion first;static float minAngle=999,maxAngle;static int samples;
    static BlueFishGaitRuntimeChecks(){if(SessionState.GetBool(Key,false))EditorApplication.update+=Poll;}
    public static void Run(){SessionState.SetBool(Key,true);SessionState.SetFloat(Key+".Start",(float)EditorApplication.timeSinceStartup);EditorSceneManager.OpenScene("Assets/Scenes/Replay.unity");EditorApplication.update+=Poll;EditorApplication.EnterPlaymode();}
    static void Poll(){try{
        if(EditorApplication.timeSinceStartup-SessionState.GetFloat(Key+".Start",0)>80)throw new Exception("Gait check timed out");
        if(!EditorApplication.isPlaying)return;
        if(!player)player=UnityEngine.Object.FindFirstObjectByType<ReplayPlayer>();if(!player||player.Data==null)return;
        if(!actor){actor=UnityEngine.Object.FindFirstObjectByType<BlueFishActor>();player.Presentation.Pause();player.enabled=false;}
        if(phase==0){
            actor.Sample(FishAction.Walk,0);first=actor.Bone(HumanBodyBones.LeftUpperLeg).localRotation;
            var model=actor.transform.Find("Model");var rest=model.localPosition;var rotation=model.localRotation;
            actor.Sample(FishAction.Walk,BlueFishMotion.Duration(FishAction.Walk)*.25f);float angle=Quaternion.Angle(first,actor.Bone(HumanBodyBones.LeftUpperLeg).localRotation);
            Debug.Log("Actual PlayMode walking upper-leg swing: "+angle);
            if(angle<15)throw new Exception("VRM erased the walking bone pose: "+angle);
            if(model.localPosition.y-rest.y<.035f||Quaternion.Angle(rotation,model.localRotation)<3)
                throw new Exception("Walking silhouette has no readable body lift and sway above the skirt");
            File.WriteAllText("../../output/gait-runtime.csv","index,time,angle,action\n");phase=1;
        }
        if(wait>Time.frameCount)return;
        if(phase>=player.Data.segments.Length){
            Debug.Log($"Actual replay gait samples={samples}, min={minAngle}, max={maxAngle}");
            if(samples<4||maxAngle-minAngle<15)throw new Exception("Actual replay walking pose does not vary");
            Finish(0);return;
        }
        int index=phase++;
        if(BlueFishReplayPose.Select(player.Data,index,0)!=FishAction.Walk)return;
        player.Clock.Seek(Mathf.Lerp(player.Data.frames[index].time,player.Data.frames[index+1].time,.5f));player.SendMessage("Apply");
        float swing=Quaternion.Angle(first,actor.Bone(HumanBodyBones.LeftUpperLeg).localRotation);minAngle=Mathf.Min(minAngle,swing);maxAngle=Mathf.Max(maxAngle,swing);samples++;
        File.AppendAllText("../../output/gait-runtime.csv",$"{index},{player.Clock.Time},{swing},Walk\n");wait=Time.frameCount+2;
    }catch(Exception ex){Debug.LogException(ex);Finish(1);}}
    static void Finish(int code){SessionState.SetBool(Key,false);EditorApplication.update-=Poll;EditorApplication.Exit(code);}
}
