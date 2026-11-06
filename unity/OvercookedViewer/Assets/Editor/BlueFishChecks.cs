using System;
using UnityEngine;
using UnityEditor;
using System.IO;
using OvercookedReplay;

public static class BlueFishChecks {
    static int count;
    static void Check(bool condition,string message) {count++;if(!condition)throw new Exception(message);}
    public static void Motion() {
        count=0;
        foreach(FishAction a in Enum.GetValues(typeof(FishAction))) {
            float d=BlueFishMotion.Duration(a);Check(d>0,"Duration must be positive");
            for(int i=0;i<=60;i++) {
                var pose=BlueFishMotion.Sample(a,d*i/60);
                Check(pose.Blink>=0&&pose.Blink<=1&&pose.Smile>=0&&pose.Smile<=1,"Expression range");
                foreach(var rotation in pose.Rotations.Values)Check(!float.IsNaN(rotation.x)&&Mathf.Abs(1-Quaternion.Dot(rotation,rotation))<.001f,"Finite unit quaternion");
            }
            var start=BlueFishMotion.Sample(a,0);var end=BlueFishMotion.Sample(a,d);
            if(BlueFishMotion.Loops(a))foreach(var b in start.Rotations.Keys)Check(Quaternion.Angle(start.Rotations[b],end.Rotations[b])<.06f,"Loop boundary: "+a+" "+b);
            var first=BlueFishMotion.Sample(a,d*.3f);BlueFishMotion.Sample(a,d*.9f);var again=BlueFishMotion.Sample(a,d*.3f);
            foreach(var b in first.Rotations.Keys)Check(Quaternion.Angle(first.Rotations[b],again.Rotations[b])<.06f,"Seek determinism");
        }
        var gait=BlueFishMotion.Sample(FishAction.Walk,BlueFishMotion.Duration(FishAction.Walk)*.25f);
        Check(gait.Rotations[HumanBodyBones.LeftUpperLeg].eulerAngles.x>300&&gait.Rotations[HumanBodyBones.RightUpperLeg].eulerAngles.x<60,"Opposed gait");
        var blink=BlueFishMotion.Sample(FishAction.Idle,2.6f);
        Check(blink.Blink>.99f&&blink.Oh==1&&blink.Smile==0,"Default oh keeps its mouth shape during scheduled blinks");
        Check(Quaternion.Angle(BlueFishMotion.Sample(FishAction.Carry,.2f,0).Rotations[HumanBodyBones.LeftUpperLeg],Quaternion.Euler(0,0,1))<.06f,"Standing carry must not walk");
        Check(Quaternion.Angle(BlueFishMotion.Sample(FishAction.Carry,.2f,0).Rotations[HumanBodyBones.LeftFoot],Quaternion.identity)<.06f,"Standing carry feet must be planted");
        Debug.Log($"BlueFish motion checks passed: {count}");
    }
    public static void Actor() {
        var model=Resources.Load<GameObject>("LocalCharacters/BlueFatFish");
        if(!model)throw new Exception("BlueFatFish has no local animated actor prefab");
        count=0;var instance=UnityEngine.Object.Instantiate(model);var actor=instance.GetComponent<BlueFishActor>();
        actor.Initialize();Check(actor.BoneCount>=15,"Required humanoid skeleton");
        Check(instance.GetComponentsInChildren<SkinnedMeshRenderer>().Length>0,"Skinned mesh");
        actor.Sample(FishAction.Carry,0);
        Debug.Log("Carry anchor local: "+actor.HeldAnchor.localPosition);
        Check(actor.HeldAnchor.localPosition.z>.1f,"Carry prop must be in front of the torso");
        Check(actor.HeldAnchor.localPosition.y<.8f,"Carry arms must not rise above the chest");
        foreach(FishAction action in Enum.GetValues(typeof(FishAction))) {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(BlueFishBootstrap.AnimationPath+"/"+action+".anim");
            Check(clip&&AnimationUtility.GetCurveBindings(clip).Length>50,"Baked clip: "+action);
            instance.transform.SetPositionAndRotation(new Vector3(2,0,-3),Quaternion.Euler(0,73,0));
            actor.Sample(action,.21f);var head=actor.Bone(HumanBodyBones.Head).localRotation;var anchor=actor.HeldAnchor.localPosition;
            actor.Sample(FishAction.Walk,.55f);actor.Sample(action,.21f);
            Check(Quaternion.Angle(head,actor.Bone(HumanBodyBones.Head).localRotation)<.06f,"Actor seek pose");
            Check(Vector3.Distance(anchor,actor.HeldAnchor.localPosition)<.0001f,"Actor seek held anchor");
            Check(instance.transform.position==new Vector3(2,0,-3)&&Mathf.Abs(instance.transform.eulerAngles.y-73)<.01f,"No root motion");
            Check(!float.IsNaN(anchor.x)&&anchor.y>.1f,"Usable held anchor");
        }
        UnityEngine.Object.DestroyImmediate(instance);Debug.Log($"BlueFish actor checks passed: {count}");
    }
    public static void ReplayMapping() {
        count=0;var data=ReplayFile.Load(Path.Combine(Application.streamingAssetsPath,"Samples/cramped_room.ocrep.json"));
        Check(BlueFishReplayPose.Select(data,18,0)==FishAction.Cook,"Unattributed start_cooking event must match pot-facing chef");
        Check(BlueFishReplayPose.Select(data,18,1)!=FishAction.Cook,"Teammate must not borrow cooking event");
        Check(BlueFishReplayPose.Select(data,46,0)==FishAction.Pickup,"Dish to soup is a pickup");
        Check(BlueFishReplayPose.Select(data,data.frames.Length-1,0)!=FishAction.Cook,"Terminal frame must not interact");
        // Model a blocked interaction next to a successful teammate event.
        data.segments[18].events=new[]{new ReplayEvent {kind="onion_pickup",agent_index=1,position=new[]{2,0}}};
        Check(BlueFishReplayPose.Select(data,18,0)==FishAction.Idle,"Blocked interaction must remain idle");
        data.segments[18].events=new[]{new ReplayEvent {kind="start_cooking",agent_index=-1,position=new[]{4,0}}};
        Check(BlueFishReplayPose.Select(data,18,0)==FishAction.Idle,"Unrelated pot must not animate this chef");
        var shared=new[]{new ChefState {position=new[]{1,2},orientation=new[]{0,-1}},new ChefState {position=new[]{1,0},orientation=new[]{0,1}}};
        var ambiguous=new ReplayFile {frames=new[]{new Frame {state=new Snapshot {players=shared}},new Frame {state=new Snapshot {players=shared}}},
            segments=new[]{new Segment {kind="step",actions=new[]{5,5},events=new[]{new ReplayEvent {kind="start_cooking",agent_index=-1,position=new[]{1,1}}}}}};
        Check(BlueFishReplayPose.Select(ambiguous,0,0)==FishAction.Idle&&BlueFishReplayPose.Select(ambiguous,0,1)==FishAction.Idle,"Ambiguous automatic event must not assert which chef succeeded");
        data.segments[18].events=new[]{new ReplayEvent {kind="start_cooking",agent_index=-1,position=new int[0]}};
        Check(BlueFishReplayPose.Select(data,18,0)==FishAction.Idle,"Malformed event position must be ignored");
        Debug.Log($"BlueFish replay mapping checks passed: {count}");
    }
    public static void Celebration() {
        count=0;var players=new[]{new ChefState {position=new[]{1,1},orientation=new[]{1,0}},new ChefState {position=new[]{3,1},orientation=new[]{0,1}}};
        var snapshot=new Snapshot {players=players};
        var data=new ReplayFile {frames=new[]{new Frame {time=0,state=snapshot},new Frame {time=.166f,state=snapshot},new Frame {time=.25f,state=snapshot},new Frame {time=.6f,state=snapshot}},segments=new[]{
            new Segment {kind="step",actions=new[]{5,4},events=new[]{new ReplayEvent {kind="soup_delivery",agent_index=0}}},
            new Segment {kind="step",actions=new[]{4,4}},new Segment {kind="step",actions=new[]{4,4}}}};
        Check(BlueFishReplayPose.CelebrationPhase(data,2,0,.25f)>.5f,"Delivery celebration must outlast the single logical step");
        Check(BlueFishReplayPose.CelebrationPhase(data,2,1,.25f)<0,"Teammate must not borrow a delivery celebration");
        Check(BlueFishReplayPose.CelebrationPhase(data,3,0,1.2f)<0,"Celebration must expire");
        data.segments[2].kind="gap";
        Check(BlueFishReplayPose.CelebrationPhase(data,2,0,.25f)<0,"Unknown gap must clear a past celebration");
        data.segments[2]=new Segment {kind="step",actions=new[]{5,4},events=new[]{new ReplayEvent {kind="soup_delivery",agent_index=0}}};
        float terminal=BlueFishReplayPose.CelebrationPhase(data,3,0,.6f);
        Check(terminal>.7f&&terminal<.9f,"Final delivery keeps its actual presentation phase for the tail clock");
        Debug.Log($"BlueFish celebration replay checks passed: {count}");
    }
    public static void PresentationTail() {
        count=0;var data=ReplayFile.Load(Path.Combine(Application.streamingAssetsPath,"Samples/cramped_room.ocrep.json"));var clock=new ReplayClock(data);
        float phase=BlueFishReplayPose.CelebrationPhase(data,data.frames.Length-1,0,clock.Duration);
        Check(phase>=0&&phase<1,"Fixture must end in a recent delivery");
        var visual=new ReplayPresentationClock(clock,(1-phase)*BlueFishMotion.CelebrationDuration);
        clock.Playing=true;visual.Advance(clock.Duration+.1f);
        Check(clock.Time==clock.Duration&&visual.Tail>.09f&&visual.Playing,"Terminal jump should continue on the visual clock");
        float paused=visual.Time;visual.Pause();visual.Advance(.2f);Check(visual.Time==paused,"Tail pause must freeze");
        visual.Toggle();visual.Advance(2);Check(!visual.Playing&&clock.Time==clock.Duration,"Tail must end without altering recorded time");
        Check(BlueFishReplayPose.CelebrationPhase(data,clock.Index,0,visual.Time)<0,"Finished tail must restore default oh");
        clock.SeekFrame(0);visual.ResetAfterSeek(false);Check(visual.Tail==0&&!visual.Playing&&visual.Time==clock.Time,"Seeking clears old visual tail");
        var noTail=new ReplayPresentationClock(clock,0);clock.Playing=true;noTail.Advance(clock.Duration+1);Check(!noTail.Playing&&noTail.Tail==0,"Record without a final celebration must stop normally");
        Debug.Log($"BlueFish presentation tail checks passed: {count}");
    }
}
