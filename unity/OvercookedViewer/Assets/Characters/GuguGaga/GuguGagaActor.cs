using System;
using System.Collections.Generic;
using UnityEngine;

namespace OvercookedReplay {
// Generic glTF skin: retain all authored bind matrices and face-bone weights.
// Everything is sampled from absolute time, including blinks and secondary sway.
public sealed class GuguGagaActor : MonoBehaviour,IReplayChefActor {
    sealed class Binding {
        public Transform Bone;public Quaternion Local,Global;public Vector3 Position;
    }
    readonly Dictionary<string,Binding> raw=new Dictionary<string,Binding>();
    readonly Dictionary<HumanBodyBones,Binding> body=new Dictionary<HumanBodyBones,Binding>();
    Transform model,anchor;Vector3 modelRest;
    public int BoneCount {get {Initialize();return GetComponentInChildren<SkinnedMeshRenderer>().bones.Length;}}
    public Transform HeldAnchor {get {Initialize();return anchor;}}
    public Transform RightHand => Bone(HumanBodyBones.RightHand);
    public IEnumerable<Transform> AnimatedTransforms {get {Initialize();yield return model;foreach(var b in raw.Values)yield return b.Bone;}}
    public IEnumerable<Transform> BodyTransforms {get {Initialize();yield return model;foreach(var b in body.Values)yield return b.Bone;foreach(string n in new[]{"Tail","Hair_F","Hair_L","Hair_R"})if(raw.TryGetValue(n,out var b))yield return b.Bone;}}
    public void Initialize() {
        if(raw.Count>0)return;
        model=transform.Find("Model");if(!model)throw new InvalidOperationException("GuguGaga model is missing");
        modelRest=model.localPosition;
        foreach(var a in model.GetComponentsInChildren<Animator>()){a.applyRootMotion=false;a.enabled=false;}
        foreach(var r in model.GetComponentsInChildren<SkinnedMeshRenderer>()) {
            r.updateWhenOffscreen=true;
            foreach(var t in r.bones)if(t&&!raw.ContainsKey(t.name))raw.Add(t.name,new Binding {Bone=t,Local=t.localRotation,Global=Quaternion.Inverse(transform.rotation)*t.rotation,Position=t.localPosition});
        }
        string[] names={"Pelvis","Spine","Spine.001","Spine.002","Neck","Head","Shoulder_L","Shoulder_R","Arm_L","Arm_R","Forearm_L","Forearm_R","Hand_L","Hand_R","Thigh_L","Thigh_R","Knee_L","Knee_R","Foot_L","Foot_R","Toe_L","Toe_R"};
        HumanBodyBones[] ids={HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.UpperChest,HumanBodyBones.Neck,HumanBodyBones.Head,HumanBodyBones.LeftShoulder,HumanBodyBones.RightShoulder,HumanBodyBones.LeftUpperArm,HumanBodyBones.RightUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightToes};
        for(int i=0;i<names.Length;i++)if(raw.TryGetValue(names[i],out var b))body.Add(ids[i],b);else throw new InvalidOperationException("Missing penguin joint: "+names[i]);
        anchor=transform.Find("HeldAnchor");if(!anchor){anchor=new GameObject("HeldAnchor").transform;anchor.SetParent(transform,false);}
    }
    public Transform Joint(string name) {Initialize();return raw.TryGetValue(name,out var b)?b.Bone:null;}
    public Transform Bone(HumanBodyBones bone) {Initialize();return body.TryGetValue(bone,out var b)?b.Bone:null;}
    void Rotate(Binding b,Quaternion delta) {b.Bone.localRotation=b.Local*Quaternion.Inverse(b.Global)*delta*b.Global;}
    public void Sample(FishAction action,float seconds,float locomotion=1,float? expressionTime=null,float? celebrationPhase=null) {
        Initialize();var pose=GuguGagaMotion.Sample(action,seconds,locomotion,expressionTime,celebrationPhase);
        foreach(var b in raw.Values){b.Bone.localPosition=b.Position;b.Bone.localRotation=b.Local;}
        model.localPosition=modelRest+Vector3.up*pose.RootLift;
        foreach(var b in body)Rotate(b.Value,pose.Rotations[b.Key]);
        body[HumanBodyBones.Hips].Bone.localPosition+=pose.HipsOffset;
        float p=seconds/BlueFishMotion.Duration(action)*2*Mathf.PI;
        int i=0;foreach(string n in new[]{"Tail","Hair_F","Hair_L","Hair_R"}) {
            if(raw.TryGetValue(n,out var b))Rotate(b,Quaternion.Euler(Mathf.Sin(p-i*.6f)*2,Mathf.Sin(p)*3,Mathf.Cos(p-i)*1.2f));i++;
        }
        Face(pose.Blink,pose.Smile);
        anchor.position=(Bone(HumanBodyBones.LeftHand).position+RightHand.position)*.5f+transform.forward*.08f;
        anchor.rotation=transform.rotation;
    }
    void Face(float blink,float smile) {
        float closed=Mathf.Max(blink,smile*.92f);
        foreach(string side in new[]{"L","R"})for(int i=0;i<5;i++) {
            string suffix=i==0?"":"."+i.ToString("D3");
            var upper=raw["Eyelid_upper_"+side+suffix];var lower=raw["Eyelid_lower_"+side+suffix];
            Vector3 gap=lower.Position-upper.Position;
            // Pair joints under the same Head parent, rather than guessing bone axes.
            Vector3 arch=Vector3.up*(Mathf.Sin(i*Mathf.PI/4)*.004f*smile);
            upper.Bone.localPosition=upper.Position+gap*(closed*.72f)+arch;
            lower.Bone.localPosition=lower.Position-gap*(closed*.28f)+arch;
        }
        foreach(string side in new[]{"L","R"}) {
            var corner=raw["Lip_Corner_"+side];corner.Bone.localPosition+=Vector3.up*(.005f*smile);
            var brow=raw["EyeBrow_"+side];brow.Bone.localPosition+=Vector3.up*(.002f*smile);
        }
        raw["Jaw_Lower"].Bone.localPosition+=Vector3.down*(.003f*smile);
    }
}
}
