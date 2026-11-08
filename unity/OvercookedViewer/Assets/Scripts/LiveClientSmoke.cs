using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OvercookedReplay {
// Optional loopback acceptance run. Uses exactly the regular connection, input and scene.
public sealed partial class LiveClient {
    string smoke,smokeReport;int smokeSteps,smokeRate,dropTick,resyncTick=-1;
    bool dropped,resumedWelcome,recovered;
    bool configured,markedReady,started,reported,reportSucceeded,requestedResume;
    int renderFrames,ackCount;
    double latencyTotal,startTime,loggedStart,exitAt;
    readonly Dictionary<int,double> sentAt=new Dictionary<int,double>();
    [Serializable] sealed class SmokeReport {
        public bool ok,host,complete,recovered;public int tick,renderFrames,inputSamples,slot;
        public float score;public double meanFps,seconds,meanInputLatencyMs;
        public string mode,error;
    }
    void SetupSmoke() {
        smoke=Argument("--live-smoke");
        if(smoke==null)return;
        if(!Local(address) || (smoke!="hh"&&smoke!="ha"&&smoke!="aa")) {
            smoke=null;throw new ArgumentException("--live-smoke requires a loopback target and hh, ha, or aa");
        }
        smokeReport=Argument("--live-smoke-report");
        if(!int.TryParse(Argument("--live-smoke-steps"),out smokeSteps))smokeSteps=12;
        if(!int.TryParse(Argument("--live-smoke-rate"),out smokeRate))smokeRate=6;
        int.TryParse(Argument("--live-smoke-drop-step"),out dropTick);
        loggedStart=Time.realtimeSinceStartupAsDouble;
    }
    void ObserveSmokeInput(int target) {
        if(smoke!=null && !sentAt.ContainsKey(target))sentAt.Add(target,Time.realtimeSinceStartupAsDouble);
    }
    void ObserveSmokeTick(int tick) {
        if(resyncTick>=0 && tick>resyncTick)recovered=true;
        if(smoke!=null && sentAt.TryGetValue(tick,out double before)){
            latencyTotal+=Time.realtimeSinceStartupAsDouble-before;ackCount++;sentAt.Remove(tick);
        }
    }
    void SmokeFail(string reason) {
        if(smoke==null||reported)return;error=reason;WriteSmoke(false);
    }
    void WriteSmoke(bool success) {
        if(reported)return;reported=true;reportSucceeded=success;exitAt=Time.realtimeSinceStartupAsDouble+.6;
        double seconds=startTime>0?Time.realtimeSinceStartupAsDouble-startTime:0;
        var report=new SmokeReport{ok=success,error=error,host=host,slot=slot,mode=lobby.mode,tick=latestTick,
            renderFrames=renderFrames,seconds=seconds,meanFps=seconds>0?renderFrames/seconds:0,
            inputSamples=ackCount,meanInputLatencyMs=ackCount>0?1000*latencyTotal/ackCount:0,
            complete=result!=null&&result.complete,score=result?.frame?.score??0,recovered=recovered};
        string json=JsonUtility.ToJson(report,true);
        Debug.Log("LIVE_SMOKE "+json);
        if(!string.IsNullOrEmpty(smokeReport)) {
            var destination=Path.GetFullPath(smokeReport);Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.WriteAllText(destination,json);
        }
    }
    void AdvanceSmoke() {
        double now=Time.realtimeSinceStartupAsDouble;
        if(reported){if(now>=exitAt)Application.Quit(reportSucceeded?0:1);return;}
        if(now-loggedStart>Math.Max(90,90+smokeSteps/Math.Max(smokeRate,1))) {SmokeFail("Smoke session timed out");return;}
        if(result!=null) {
            bool valid=result.complete && latestTick==smokeSteps && result.tick==latestTick && player.Data!=null
                && player.Data.frames[player.Data.frames.Length-1].score==result.frame.score;
            if(!host && dropTick>0 && !recovered)valid=false;
            if(!valid)error="终态或步数错误";
            WriteSmoke(valid);return;
        }
        if(!welcomed)return;
        if(Phase=="paused"&&!requestedResume&&string.IsNullOrEmpty(lobby.policy_fault)&&!lobby.warming) {
            Send(new LivePause{type="pause",paused=false});requestedResume=true;
        }
        if(Phase=="lobby"){
            string mode=smoke=="hh"?"human_human":smoke=="ha"?"human_ai":"ai_ai";
            if(host&&!configured) {
                ChooseMode(mode);draft.layout="cramped_room";draft.horizon=smokeSteps;draft.tick_rate=smokeRate;
                draft.seed=0;draft.host_slot=0;draft.old_dynamics=smoke=="aa";
                draft.model0=draft.model1=smoke=="aa"?"sp-cramped-room":HasModel("ppo-final")?"ppo-final":"greedy";
                ApplyDraft();configured=true;return;
            }
            if(lobby.mode!=mode||lobby.horizon!=smokeSteps||Mathf.RoundToInt(lobby.tick_rate)!=smokeRate)return;
            if(!markedReady){
                Ready(true);markedReady=true;
                if(host)Debug.Log("LIVE_SMOKE_LOBBY_READY");
                return;
            }
            if(host&&!started&&lobby.ready0&&(mode!="human_human"||lobby.ready1)){Control("start");started=true;}
        }
        if(Phase=="running"&&latestTick>=0) {
            requestedResume=false;
            if(!host && !dropped && dropTick>0 && latestTick>=dropTick) {
                dropped=true;Disconnect("回归检查主动断线",true);return;
            }
            if(startTime==0)startTime=now;
            renderFrames++;
            if(MayAct)Submit((latestTick+(host?0:2))%6);
        }
    }
}
}
