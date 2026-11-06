using UnityEngine;

namespace OvercookedReplay {
// Original poses for photon's T-pose flippers. Facial timing and celebration share
// the replay clock, but the wide penguin body needs its own shoulder/elbow profile.
public static class GuguGagaMotion {
    public static FishPose Sample(FishAction action,float seconds,float locomotion=1,float? expressionTime=null,float? celebrationPhase=null) {
        var pose=BlueFishMotion.Sample(action,seconds,locomotion,expressionTime,celebrationPhase);
        pose.Oh=0;
        float duration=BlueFishMotion.Duration(action);
        float u=BlueFishMotion.Loops(action)?Mathf.Repeat(seconds,duration)/duration:Mathf.Clamp01(seconds/duration);
        float p=u*2*Mathf.PI,s=Mathf.Sin(p),wing=65,pitch=0,elbow=12;
        float leftPitch=0,rightPitch=0;
        if(action==FishAction.Walk||action==FishAction.Carry) {
            float stride=18*Mathf.Clamp01(locomotion);
            pose.Set(HumanBodyBones.LeftUpperLeg,-stride*s,0,2);
            pose.Set(HumanBodyBones.RightUpperLeg,stride*s,0,-2);
            pose.Set(HumanBodyBones.LeftLowerLeg,Mathf.Max(0,-s)*22*locomotion);
            pose.Set(HumanBodyBones.RightLowerLeg,Mathf.Max(0,s)*22*locomotion);
            pose.Set(HumanBodyBones.Hips,0,s*2,-s*4);
            pose.Set(HumanBodyBones.Spine,3,-s*2,s*3);
            pose.HipsOffset=new Vector3(s*.004f,(1-Mathf.Cos(2*p))*.003f,0)*locomotion;
            leftPitch=9*s;rightPitch=-9*s;
        }
        if(action==FishAction.Carry||action==FishAction.Cook) {wing=54;pitch=-34;elbow=68;}
        if(action==FishAction.Pickup||action==FishAction.Place||action==FishAction.Deliver) {
            float reach=Mathf.Pow(Mathf.Sin(u*Mathf.PI),2);
            float carry=action==FishAction.Pickup?Mathf.SmoothStep(0,1,u):1-Mathf.SmoothStep(0,1,u);
            float blend=Mathf.Max(carry,reach);
            wing=Mathf.Lerp(65,54,blend);pitch=-38*blend;elbow=Mathf.Lerp(12,68,blend);
            pose.Set(HumanBodyBones.Spine,reach*10);
        }
        pose.Set(HumanBodyBones.LeftUpperArm,pitch+leftPitch,0,wing);
        pose.Set(HumanBodyBones.RightUpperArm,pitch+rightPitch,0,-wing);
        pose.Set(HumanBodyBones.LeftLowerArm,0,elbow);
        pose.Set(HumanBodyBones.RightLowerArm,0,-elbow);
        return pose;
    }
}
}
