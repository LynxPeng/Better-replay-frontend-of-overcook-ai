using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OvercookedReplay {
// Run in the actual shipped Mono player as well as Editor-based checks.
public static class PartitionRuntimeChecks {
    [Serializable] class Report {public bool passed;public int files,assertions;public string error;}
    static int assertions;
    static void Need(bool value,string message){assertions++;if(!value)throw new Exception(message);}
    static void One(ReplayPlayer player,string path,JToken source){
        var data=ReplayFile.Load(path);var clock=new ReplayClock(data);
        for(int i=data.frames.Length-1;i>=0;i--){clock.SeekFrame(i);Need(clock.Index==i,"backward seek "+path);}
        player.BeginPresentation(data,Path.GetFileName(path));
        foreach(int i in new[]{0,data.frames.Length/2,data.frames.Length-1,0})player.Seek(i);
        Need(player.Data==data,"player did not load "+path);
        var marks=ReplayFile.ScoreMarkerTimes(data);
        for(int i=0;i<data.segments.Length;i++)if(data.segments[i].kind=="step"&&data.frames[i+1].score>data.frames[i].score)
            Need(marks.Contains(data.frames[i+1].time),"scoring step missing timeline marker: "+path+" step "+(i+1));
        if(data.header.layout_name=="v3-counter-projection")Need(data.header.IsPartition,"native_state replay not recognized");
        if(data.header.IsShared || source is JArray || (source is JObject && source["layout"] is JArray)){
            source=source??SharedKitchenRegression.ReadSample(path);
            if(source is JArray trajectory)SharedKitchenRegression.CheckTrajectory(trajectory,path,data,Need);
            else SharedKitchenRegression.Check((JObject)source,data,Need);
            return;
        }
        if(!data.header.IsPartition)return;
        for(int i=0;i<data.segments.Length;i++){
            var step=data.segments[i];var a=data.frames[i].state.partition;var b=data.frames[i+1].state.partition;
            bool delivery=false,chop=false,tend=false;
            if(step.events!=null)foreach(var e in step.events){delivery|=e.kind=="meal_delivery";chop|=e.kind=="chop";tend|=e.kind=="tend";}
            if(step.sparse_reward>0)Need(delivery,"positive reward missing delivery event: "+path+" step "+i);
            for(int j=0;j<4;j++)if(b.cuts[j]>a.cuts[j])Need(chop,"chop event missing: "+path);
            for(int j=0;j<2;j++)if(b.heat[j]>a.heat[j])Need(tend,"tend event missing: "+path);
        }
        Need(data.frames[0].state.partition!=null,"missing partition snapshot");
        var test=JsonUtility.FromJson<ReplayFile>(JsonUtility.ToJson(data));
        test.frames[0].state.partition.heat[0]=9;
        bool rejected=false;try{ReplayFile.Validate(test);}catch(ArgumentException){rejected=true;}
        Need(rejected,"invalid heat accepted");
        if(data.header.schema_version==2&&data.segments.Length>0){
            var forged=JsonUtility.FromJson<ReplayFile>(JsonUtility.ToJson(data));
            forged.segments[0].events=new[]{new ReplayEvent{kind="meal_delivery",agent_index=0,position=new[]{0,3}}};
            rejected=false;try{ReplayFile.Validate(forged);}catch(ArgumentException){rejected=true;}
            Need(rejected,"forged delivery accepted");
        }
        var state=JsonUtility.FromJson<Snapshot>(JsonUtility.ToJson(data.frames[0].state));
        state.players[0].position=new[]{1,4};state.players[0].orientation=new[]{-1,0};
        Need(InteractionHints.TryTarget(data.header,state,0,out _),"potato source hint");
        state.players[0].position=new[]{1,1};
        Need(!InteractionHints.TryTarget(data.header,state,0,out _),"solid wall hint");
        if(data.segments.Length>0){
            var synthetic=JsonUtility.FromJson<ReplayFile>(JsonUtility.ToJson(data));
            synthetic.frames[1].state.players=JsonUtility.FromJson<Snapshot>(JsonUtility.ToJson(synthetic.frames[0].state)).players;
            synthetic.segments[0].actions=new[]{5,4,4};
            synthetic.segments[0].events=new[]{new ReplayEvent{kind="order_expired",agent_index=0}};
            Need(BlueFishReplayPose.Select(synthetic,0,0)!=FishAction.Cook,"expiry triggered successful cooking");
        }
        var potatoSource=PartitionKitchenView.Station('P');
        foreach(var t in potatoSource.GetComponentsInChildren<Transform>())Need(!t.name.Contains("stove"),"potato source rendered as stove");
        UnityEngine.Object.Destroy(potatoSource);
        var root=new GameObject("v3 geometry check");
        foreach(string item in PartitionReplay.Items)if(item!=""){
            var obj=KitchenFactory.Item(item,root.transform,Vector3.zero,.3f);
            Need(obj.name==item&&obj.GetComponentsInChildren<Renderer>().Length>0,"missing item "+item);
        }
        UnityEngine.Object.Destroy(root);
        Need(PartitionReplay.IsDelivery("meal_delivery"),"v3 delivery marker");
    }
    public static IEnumerator Run(ReplayPlayer player,string directory,string reportPath){
        var report=new Report();assertions=0;
        var paths=Directory.GetFiles(directory,"*.json");
        Array.Sort(paths,StringComparer.Ordinal);
        foreach(var path in paths){
            if(string.Equals(Path.GetFullPath(path),Path.GetFullPath(reportPath),StringComparison.OrdinalIgnoreCase))continue;
            Exception failure=null;
            try{
                JToken raw=null;
                if(!path.EndsWith(".ocrep.json",StringComparison.OrdinalIgnoreCase)){
                    raw=SharedKitchenRegression.ReadSample(path);
                    if(raw==null)continue;
                }
                One(player,path,raw);
            }catch(Exception ex){failure=ex;}
            if(failure!=null){report.error=path+": "+failure;Debug.LogError(report.error);break;}
            report.files++;Debug.Log("Replay runtime check passed: "+Path.GetFileName(path));yield return null;
        }
        report.assertions=assertions;report.passed=report.error==null&&report.files>0;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
        File.WriteAllText(reportPath,JsonUtility.ToJson(report,true));
        Debug.Log("Replay runtime verification: "+JsonUtility.ToJson(report));
        Application.Quit(report.passed?0:1);
    }
}
}
