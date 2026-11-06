using System;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using OvercookedReplay;

public static class CharacterTimingChecks {
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    public static void Gait() {
        foreach(int agent in new[]{0,1}) {
            var instance=UnityEngine.Object.Instantiate(Resources.Load<GameObject>(ReplayPlayer.CharacterResource(agent,true)));
            try {
                var actor=instance.GetComponent<IReplayChefActor>();
                Func<HumanBodyBones,Transform> bone=agent==0?(Func<HumanBodyBones,Transform>)instance.GetComponent<BlueFishActor>().Bone:instance.GetComponent<GuguGagaActor>().Bone;
                foreach(var action in new[]{FishAction.Walk,FishAction.Carry}) {
                    actor.Sample(action,0,1,0,-1);var rest=bone(HumanBodyBones.LeftUpperLeg).localRotation;
                    // Fixed wall times pin twice the previous gait cadence, on the real rigs.
                    float peak=action==FishAction.Walk?.045f:.05125f;
                    actor.Sample(action,peak,1,0,-1);
                    float swing=Quaternion.Angle(rest,bone(HumanBodyBones.LeftUpperLeg).localRotation);
                    Require(swing>(agent==0&&action==FishAction.Walk?23:17),"Short-step gait too slow: agent="+agent+", action="+action+", swing="+swing);
                    var saved=bone(HumanBodyBones.LeftFoot).localRotation;
                    actor.Sample(FishAction.Idle,4,0,4,-1);actor.Sample(action,peak,1,0,-1);
                    Require(Quaternion.Angle(saved,bone(HumanBodyBones.LeftFoot).localRotation)<.05f,"Gait changed after seek");
                    actor.Sample(action,peak,0,0,-1);var planted=bone(HumanBodyBones.LeftFoot).localRotation;
                    actor.Sample(action,peak+.1f,0,0,-1);
                    Require(Quaternion.Angle(planted,bone(HumanBodyBones.LeftFoot).localRotation)<.05f,"Stationary character is walking");
                }
            } finally {UnityEngine.Object.DestroyImmediate(instance);}
        }
        Debug.Log("Both character short-step gait cadence, standing feet and seek checks passed");
    }
    public static void Jump() {
        foreach(int agent in new[]{0,1}) {
            var instance=UnityEngine.Object.Instantiate(Resources.Load<GameObject>(ReplayPlayer.CharacterResource(agent,true)));
            try {
                var actor=instance.GetComponent<IReplayChefActor>();var model=instance.transform.Find("Model");
                actor.Sample(FishAction.Idle,0,0,0,-1);float rest=model.localPosition.y;
                actor.Sample(FishAction.Deliver,.225f,0,0);
                Require(Mathf.Abs(model.localPosition.y-rest-.18f)<.0001f,"Jump must reach the original height at 225ms: "+agent);
                actor.Sample(FishAction.Deliver,.45f,0,0);
                Require(Mathf.Abs(model.localPosition.y-rest)<.0001f,"Jump must land at 450ms: "+agent);
                Require(instance.transform.position==Vector3.zero,"Jump moved recorded grid root");
            }finally{UnityEngine.Object.DestroyImmediate(instance);}
        }
        Debug.Log("Both character 450ms jump and unchanged peak height checks passed");
    }
    public static void Turning() {
        EditorSceneManager.OpenScene("Assets/Scenes/Replay.unity");
        var player=UnityEngine.Object.FindFirstObjectByType<ReplayPlayer>();
        player.Load(Path.Combine(Application.streamingAssetsPath,"Samples/cramped_room.ocrep.json"));
        Transform[] roots={UnityEngine.Object.FindFirstObjectByType<BlueFishActor>().transform,UnityEngine.Object.FindFirstObjectByType<GuguGagaActor>().transform};
        int[] counts={0,0};
        // The built-in sample leaves agent1 stationary. Reversing player identity,
        // actions and event owners exercises those same real turns on the penguin.
        for(int pass=0;pass<2;pass++) {
        if(pass==1) {
            foreach(var frame in player.Data.frames)Array.Reverse(frame.state.players);
            foreach(var segment in player.Data.segments) {
                if(segment.actions!=null)Array.Reverse(segment.actions);
                if(segment.events!=null)foreach(var e in segment.events)if(e.agent_index>=0)e.agent_index=1-e.agent_index;
            }
        }
        for(int i=0;i<player.Data.segments.Length;i++)for(int agent=0;agent<2;agent++) {
            if(counts[agent]>=4)continue;
            var a=player.Data.frames[i];var b=player.Data.frames[i+1];
            var from=a.state.players[agent].orientation;var to=b.state.players[agent].orientation;
            if(from.SequenceEqual(to)||player.Data.segments[i].kind!="step"||b.time-a.time<.12f)continue;
            var start=Quaternion.Euler(0,Mathf.Atan2(from[0],-from[1])*Mathf.Rad2Deg,0);
            var end=Quaternion.Euler(0,Mathf.Atan2(to[0],-to[1])*Mathf.Rad2Deg,0);
            player.Clock.Seek(a.time+.055f);player.SendMessage("Apply");
            float ratio=Quaternion.Angle(roots[agent].localRotation,end)/Quaternion.Angle(start,end);
            Require(ratio>.45f&&ratio<.55f,"Turn should be halfway at 55ms: agent="+agent+", remaining="+ratio);
            player.Clock.Seek(a.time+.111f);player.SendMessage("Apply");
            Require(Quaternion.Angle(roots[agent].localRotation,end)<.1f,"Turn must complete at 110ms");
            player.Seek(i);
            Require(Quaternion.Angle(roots[agent].localRotation,start)<.1f,"Seek must restore exact recorded facing");counts[agent]++;
        }
        }
        Require(counts.All(n=>n>0),"No turning samples for both actors");
        Debug.Log("Both character 110ms turning checks passed: "+string.Join(",",counts));
    }
    public static void Run(){Jump();Gait();Turning();}
    public static void Build(){Run();GuguGagaChecks.All();StreetRepairChecks.Turning();NativeDialogChecks.Build();}
}
