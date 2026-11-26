using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace OvercookedReplay {
// Optional visual evidence for the standalone player check. Every pose is
// sampled via the normal replay clock, including the frame after the action.
internal static class TrueClaudeReplayCapture {
    public static IEnumerator Save(ReplayPlayer player,string folder) {
        Directory.CreateDirectory(folder);
        var camera=Camera.main;
        var originalRect=camera.rect;
        var originalRotation=camera.transform.rotation;
        var originalPosition=camera.transform.position;
        float originalFov=camera.fieldOfView;
        const int width=768,height=480,samples=14;
        try {
            var replay=player.Data;
            foreach(var action in new[]{FishAction.Walk,FishAction.Turn,FishAction.Carry,FishAction.Assemble,FishAction.JointUnload}) {
                int i=0;
                for(;i<replay.segments.Length;i++) {
                    var start=replay.frames[i].state.players[2];
                    var end=replay.frames[i+1].state.players[2];
                    if(BlueFishReplayPose.Select(replay,i,2,true)!=action)continue;
                    if(action==FishAction.Carry && (start.held_object?.name!="MEAL" ||
                        ReplayPlayer.World(start.position)==ReplayPlayer.World(end.position)))continue;
                    break;
                }
                if(i==replay.segments.Length)throw new InvalidDataException("Capture trace lacks "+action);
                var before=replay.frames[i].state.players[2];
                var after=replay.frames[i+1].state.players[2];
                var forward=ReplayPlayer.World(after.orientation);
                var focus=(ReplayPlayer.World(before.position)+ReplayPlayer.World(after.position))*.5f+Vector3.up*.72f;
                var offset=Quaternion.Euler(0,33,0)*forward*2.65f + Vector3.up*1.65f;
                if(action==FishAction.Assemble||action==FishAction.JointUnload) {
                    focus+=forward*(action==FishAction.JointUnload?1f:.45f);
                    offset=Vector3.Cross(Vector3.up,forward)*3.1f+forward*.42f+Vector3.up*1.95f;
                }
                for(int f=0;f<=samples;f++) {
                    player.SeekSample(i,f/(float)samples);
                    yield return null;
                    camera.rect=new Rect(0,0,1,1);camera.aspect=width/(float)height;camera.fieldOfView=38;
                    camera.transform.position=focus+offset;camera.transform.LookAt(focus);
                    ReplayPlayer.SaveCameraFrame(camera,Path.Combine(folder,$"{action}-{replay.frames[i].state.timestep:D3}-{f:D2}.png"),width,height,1);
                }
            }
        } finally {
            camera.rect=originalRect;camera.ResetAspect();camera.fieldOfView=originalFov;
            camera.transform.SetPositionAndRotation(originalPosition,originalRotation);
        }
    }
}
}
