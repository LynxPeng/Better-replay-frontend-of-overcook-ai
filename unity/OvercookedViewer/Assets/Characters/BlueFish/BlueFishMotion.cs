using System;
using System.Collections.Generic;
using UnityEngine;

namespace OvercookedReplay {
public enum FishAction { Idle, Walk, Carry, Pickup, Place, Deliver, Cook, Turn }

// All poses use a normalized, forward-facing T-pose. No accumulated time or root motion.
public sealed class FishPose {
    public readonly Dictionary<HumanBodyBones,Quaternion> Rotations=new Dictionary<HumanBodyBones,Quaternion>();
    public Vector3 HipsOffset;
    public float Blink,Smile,Grip,Oh,RootLift,RootRoll;
    public void Set(HumanBodyBones bone,float x=0,float y=0,float z=0) {Rotations[bone]=Quaternion.Euler(x,y,z);}
}

public static class BlueFishMotion {
    public const float CelebrationDuration=.45f;
    public static float Duration(FishAction action) {
        switch(action) {
            case FishAction.Idle:return 6;
            case FishAction.Walk:return .18f;
            case FishAction.Carry:return .205f;
            case FishAction.Pickup:return 1.05f;
            case FishAction.Place:return 1;
            case FishAction.Deliver:return CelebrationDuration;
            case FishAction.Cook:return 2.2f;
            default:return .6f;
        }
    }
    public static bool Loops(FishAction action) => action==FishAction.Idle||action==FishAction.Walk||action==FishAction.Carry||action==FishAction.Cook;
    public static FishPose Sample(FishAction action,float seconds,float locomotion=1,float? expressionTime=null,float? celebrationPhase=null) {
        if(float.IsNaN(seconds)||float.IsInfinity(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
        float d=Duration(action),u=Loops(action)?Mathf.Repeat(seconds,d)/d:Mathf.Clamp01(seconds/d);
        float p=u*2*Mathf.PI,s=Mathf.Sin(p),c=Mathf.Cos(p),breath=Mathf.Sin(p*2);
        var pose=new FishPose {Oh=1};
        // Explicit neutral values also clear the previous action when seeking.
        foreach(HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))if(b!=HumanBodyBones.LastBone)pose.Rotations[b]=Quaternion.identity;
        // The flared skirt is wider than the relaxed humanoid hand path. Keep empty
        // arms outside it, with a light forward elbow bend, including action endpoints.
        float leftArm=38,rightArm=-38,leftSwing=0,rightSwing=0,leftElbow=24,rightElbow=-24;
        pose.Set(HumanBodyBones.Spine,1.5f+breath*.7f,0,s*1.3f);
        pose.Set(HumanBodyBones.Chest,0,s*1.5f,0);
        pose.Set(HumanBodyBones.Head,Mathf.Sin(p+.4f)*1.5f,s*3.5f,-s*1.2f);
        pose.HipsOffset=new Vector3(s*.004f,(1-c)*.0025f,0);
        pose.Grip=.08f;
        if(action==FishAction.Walk||action==FishAction.Carry) {
            float stride=(action==FishAction.Carry?18:24)*Mathf.Clamp01(locomotion);
            pose.Set(HumanBodyBones.LeftUpperLeg,-stride*s,0,1);
            pose.Set(HumanBodyBones.RightUpperLeg,stride*s,0,-1);
            float lk=Mathf.Max(0,-s)*38*locomotion,rk=Mathf.Max(0,s)*38*locomotion;
            pose.Set(HumanBodyBones.LeftLowerLeg,lk);pose.Set(HumanBodyBones.RightLowerLeg,rk);
            pose.Set(HumanBodyBones.LeftFoot,-lk*.45f+6*s*locomotion);pose.Set(HumanBodyBones.RightFoot,-rk*.45f-6*s*locomotion);
            pose.HipsOffset=new Vector3(s*.008f,(1-Mathf.Cos(2*p))*.008f,0)*locomotion;
            // The long skirt hides the legs in the kitchen camera. Let the whole
            // silhouette show each footfall; carrying keeps the motion restrained.
            float weight=(action==FishAction.Carry?.6f:1)*Mathf.Clamp01(locomotion);
            pose.RootLift=(1-Mathf.Cos(2*p))*.022f*weight;
            pose.RootRoll=s*3.5f*weight;
            pose.Set(HumanBodyBones.Hips,0,s*3,-s*2);
            pose.Set(HumanBodyBones.Spine,4,-s*4,s*2.5f);
            pose.Set(HumanBodyBones.Head,Mathf.Sin(2*p+.3f)*1.2f,s*2,-s*1.5f);
            leftSwing=12*s;rightSwing=-12*s;
        }
        if(action==FishAction.Carry) {
            leftArm=55;rightArm=-55;leftElbow=76;rightElbow=-76;
            leftSwing=rightSwing=-32+Mathf.Cos(2*p)*1.2f;pose.Grip=.48f;
        }
        float reach=Mathf.Pow(Mathf.Sin(u*Mathf.PI),2);
        if(action==FishAction.Pickup||action==FishAction.Place||action==FishAction.Deliver) {
            bool delivery=action==FishAction.Deliver;
            pose.Set(HumanBodyBones.Spine,reach*(delivery?9:18));
            pose.Set(HumanBodyBones.Chest,reach*5);
            pose.Set(HumanBodyBones.Head,-reach*7,reach*2);
            float carryWeight=action==FishAction.Pickup?Mathf.SmoothStep(0,1,u):1-Mathf.SmoothStep(0,1,u);
            float blend=Mathf.Max(carryWeight,reach);
            leftArm=Mathf.Lerp(38,50,blend);rightArm=-leftArm;
            leftSwing=rightSwing=-32*blend-reach*9;
            leftElbow=Mathf.Lerp(24,78,blend);rightElbow=-leftElbow;
            pose.Set(HumanBodyBones.LeftLowerLeg,reach*8);pose.Set(HumanBodyBones.RightLowerLeg,reach*8);
            pose.Set(HumanBodyBones.LeftUpperLeg,-reach*5);pose.Set(HumanBodyBones.RightUpperLeg,-reach*5);
            pose.HipsOffset=new Vector3(0,-reach*.014f,0);
            pose.Grip=blend*.48f;
        }
        if(action==FishAction.Cook) {
            leftArm=53;rightArm=-54;leftElbow=74;rightElbow=-72;
            leftSwing=-32;rightSwing=-36+s*5;pose.Grip=.5f;
            pose.Set(HumanBodyBones.RightHand,Mathf.Cos(p)*13,Mathf.Sin(p)*10,0);
            pose.Set(HumanBodyBones.Spine,8,-s*2,Mathf.Cos(p)*1.3f);
            pose.Set(HumanBodyBones.Head,6,-s*3,0);
        }
        if(action==FishAction.Turn) {
            pose.Set(HumanBodyBones.Head,0,Mathf.Sin(p)*15,Mathf.Sin(p)*-2);
            pose.Set(HumanBodyBones.Chest,0,Mathf.Sin(p)*7,0);
        }
        pose.Set(HumanBodyBones.LeftUpperArm,leftSwing,0,leftArm);
        pose.Set(HumanBodyBones.RightUpperArm,rightSwing,0,rightArm);
        pose.Set(HumanBodyBones.LeftLowerArm,0,leftElbow,3);
        pose.Set(HumanBodyBones.RightLowerArm,0,rightElbow,-3);
        for(int side=0;side<2;side++) {
            string prefix=side==0?"Left":"Right";float sign=side==0?1:-1;
            foreach(string finger in new[]{"Index","Middle","Ring","Little"})foreach(string joint in new[]{"Proximal","Intermediate","Distal"}) {
                var bone=(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),prefix+finger+joint);
                pose.Set(bone,0,0,sign*(10+pose.Grip*42));
            }
        }
        // An independent absolute clock keeps blink cadence stable across action changes.
        float blinkClock=expressionTime??seconds;
        if(float.IsNaN(blinkClock)||float.IsInfinity(blinkClock))throw new ArgumentOutOfRangeException(nameof(expressionTime));
        pose.Blink=Mathf.Clamp01(1-Mathf.Abs(Mathf.Repeat(Mathf.Max(0,blinkClock),3.5f)-2.6f)/.11f);
        float celebration=celebrationPhase??(action==FishAction.Deliver?u:-1);
        if(celebration>=0&&celebration<1) {
            pose.Smile=1;pose.Oh=0;pose.Blink=0;
            pose.RootLift+=.18f*Mathf.Pow(Mathf.Sin(Mathf.PI*celebration),2);
        }
        return pose;
    }
}
}
