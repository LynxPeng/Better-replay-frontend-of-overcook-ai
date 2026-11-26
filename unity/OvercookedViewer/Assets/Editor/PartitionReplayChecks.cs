using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using OvercookedReplay;

public static class PartitionReplayChecks {
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    public static void SharedKitchen() {
        string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../tests/fixtures"));
        string excerpt=Path.Combine(directory,"shared-kitchen-excerpt.json");
        var replay=ReplayFile.Load(excerpt);
        SharedKitchenRegression.Check((JObject)SharedKitchenRegression.ReadSample(excerpt),replay,Require);
        Require(!replay.header.complete && replay.frames.Last().score==420,"SharedKitchen recorded fragment");
        Require(replay.segments.Count(s=>s.kind=="gap")>0,"SharedKitchen recorded gaps");
        string unmatched=Path.Combine(directory,"shared-kitchen-unmatched.json");
        var trial=ReplayFile.Load(unmatched);
        SharedKitchenRegression.Check((JObject)SharedKitchenRegression.ReadSample(unmatched),trial,Require);
        Require(trial.frames[1].state.shared.unmatched_steps==1 && trial.frames[1].state.shared.reward>0 &&
            trial.frames[1].score==0 && trial.segments[0].sparse_reward==0 && ReplayFile.ScoreMarkerTimes(trial).Count==0,
            "Unmatched cooker attempt with shaping is scoreless");
        var joint=trial.segments[1].events.Single(e=>e.kind=="joint_unload");
        Require(joint.participants.SequenceEqual(new[]{0,1}) && BlueFishReplayPose.Select(trial,1,2)==FishAction.Pickup,
            "Third chef works the cutting board during the other two chefs' joint unload");
        // A different third-chef action may succeed on the very same tick, at another station.
        var simultaneous=(JObject)SharedKitchenRegression.ReadSample(unmatched);
        ((JArray)simultaneous["frames"]).RemoveAt(0);
        var before=simultaneous["frames"][0];
        var after=simultaneous["frames"][1];
        before["cutting"]["item"]="RS";before["cutting"]["prog"]=0;
        after["cutting"]["item"]="RS";after["cutting"]["prog"]=1;
        after["chefs"][2]["held"]="";
        after["reward"]=0.30000001192092896;
        var concurrent=ReplayFile.Parse(simultaneous.ToString());
        SharedKitchenRegression.Check(simultaneous,concurrent,Require);
        Require(concurrent.segments[0].events.Single(e=>e.kind=="chop").participants.SequenceEqual(new[]{2}) &&
            concurrent.segments[0].events.Single(e=>e.kind=="joint_unload").participants.SequenceEqual(new[]{0,1}),
            "Concurrent chop and joint unload retain separate actors");
        var bad=(JObject)SharedKitchenRegression.ReadSample(unmatched);
        bad["frames"][1]["actions"][0]="UNKNOWN";
        try { ReplayFile.Parse(bad.ToString());throw new Exception("Invalid SharedKitchen action accepted"); }
        catch(ArgumentException) { }
        Debug.Log("SharedKitchen raw frame, event, gap, wait and shaped-reward checks passed");
    }
    public static void Run() {
        SharedKitchen();
        var files=Directory.GetFiles(Application.streamingAssetsPath+"/Samples","partition_v3_*.ocrep.json");
        Require(files.Length>=8,"Expected two full v3 games and six historical fragments");
        foreach(var path in files) {
            var data=ReplayFile.Load(path);
            Require(data.header.schema_version==2,"v3 schema");
            Require(data.frames.Length==data.segments.Length+1,"v3 final frame");
            var clock=new ReplayClock(data);
            for(int i=data.frames.Length-1;i>=0;i--){clock.SeekFrame(i);Require(clock.Index==i,"v3 seek");}
            Require(data.frames.Last().score>0,"v3 sample deliveries");
            if(data.header.complete)Require(data.frames.Length==301,"Full episode horizon");
            else Require(data.frames.Length<301,"Historical fragments must stay short");
            // Rendering and interaction are checked by seeking actual recorded states.
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            var camera=new GameObject("Test Camera").AddComponent<Camera>();camera.tag="MainCamera";
            var player=new GameObject("v3 player").AddComponent<ReplayPlayer>();
            player.Load(path);Require(player.Data==data||player.Data!=null,"v3 player loads");
            foreach(int frame in new[]{0,data.frames.Length/2,data.frames.Length-1,0})player.Seek(frame);
            var unknown=GameObject.Find("Kitchen/Station_P/stove_single");
            Require(unknown==null,"v3 potato source must not become an old-style stove");
            Require(GameObject.Find("Kitchen/Station_K")!=null,"v3 pot station missing");
        }
        Debug.Log("Partition replay format, bidirectional seek and rendering checks passed: "+files.Length);
    }
}
