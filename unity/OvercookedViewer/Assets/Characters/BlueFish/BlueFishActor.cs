using System.Collections.Generic;
using UniVRM10;
using UnityEngine;

namespace OvercookedReplay {
// Skin, weights and bind pose belong to the local VRM. These are original playback poses.
public sealed class BlueFishActor : MonoBehaviour,IReplayChefActor {
    sealed class Binding {
        public Transform Bone;
        public Quaternion Local,Global;
        public Vector3 Position;
    }
    readonly Dictionary<HumanBodyBones,Binding> bindings=new Dictionary<HumanBodyBones,Binding>();
    readonly List<Binding> secondary=new List<Binding>();
    Vrm10Instance vrm; Vrm10Runtime runtime;Transform anchor,visual;Vector3 visualRest;Quaternion visualRestRotation;
    public int BoneCount => bindings.Count;
    public IEnumerable<Transform> AnimatedTransforms {get {Initialize();yield return visual;foreach(var b in bindings.Values)yield return b.Bone;foreach(var b in secondary)yield return b.Bone;}}
    public Transform HeldAnchor {get {Initialize();return anchor;}}
    public Transform RightHand => Bone(HumanBodyBones.RightHand);
    public void Initialize() {
        if(bindings.Count>0)return;
        vrm=GetComponentInChildren<Vrm10Instance>(true);
        if(!vrm)throw new System.InvalidOperationException("BlueFatFish local VRM is missing");
        visual=vrm.transform;visualRest=visual.localPosition;visualRestRotation=visual.localRotation;
        vrm.UpdateType=Vrm10Instance.UpdateTypes.None;
        vrm.enabled=false;
        var animator=vrm.GetComponent<Animator>();if(animator){animator.applyRootMotion=false;animator.enabled=false;}
        // Capture the untouched bind pose before constructing or applying any animation.
        foreach(HumanBodyBones b in System.Enum.GetValues(typeof(HumanBodyBones))) {
            if(b==HumanBodyBones.LastBone)continue;
            var t=vrm.Humanoid.GetBoneTransform(b);if(!t)continue;
            bindings[b]=new Binding {Bone=t,Local=t.localRotation,Global=Quaternion.Inverse(transform.rotation)*t.rotation,Position=t.localPosition};
        }
        foreach(var t in vrm.GetComponentsInChildren<Transform>()) {
            // These existing accessory roots are independent of the humanoid pose.
            if(t.name=="Hou_hair"||t.name=="Hou_hair.L"||t.name=="Hou_hair.R"||t.name=="FaBing.L"||t.name=="FaBing.R"||t.name=="DaiMao"||t.name=="WeiBa"||t.name=="ErDuo.L"||t.name=="ErDuo.R")
                secondary.Add(new Binding {Bone=t,Local=t.localRotation,Global=Quaternion.Inverse(transform.rotation)*t.rotation,Position=t.localPosition});
        }
        anchor=transform.Find("HeldAnchor");if(!anchor){anchor=new GameObject("HeldAnchor").transform;anchor.SetParent(transform,false);}
        if(Application.isPlaying)runtime=new Vrm10Runtime(vrm,false,new Vrm10NopSpringboneRuntime(),vrm.DefaultTransformStates,true);
    }
    public Transform Bone(HumanBodyBones b) {Initialize();return bindings.TryGetValue(b,out var binding)?binding.Bone:null;}
    public void Sample(FishAction action,float seconds,float locomotion=1,float? expressionTime=null,float? celebrationPhase=null) {
        Initialize();var pose=BlueFishMotion.Sample(action,seconds,locomotion,expressionTime,celebrationPhase);
        visual.localPosition=visualRest+Vector3.up*pose.RootLift;
        visual.localRotation=visualRestRotation*Quaternion.Euler(0,0,pose.RootRoll);
        foreach(var pair in bindings) {
            var binding=pair.Value;
            binding.Bone.localRotation=binding.Local*Quaternion.Inverse(binding.Global)*pose.Rotations[pair.Key]*binding.Global;
            binding.Bone.localPosition=binding.Position;
        }
        if(bindings.TryGetValue(HumanBodyBones.Hips,out var hip))hip.Bone.localPosition+=pose.HipsOffset;
        float phase=seconds/BlueFishMotion.Duration(action)*2*Mathf.PI;
        if(!BlueFishMotion.Loops(action))phase=Mathf.Clamp01(seconds/BlueFishMotion.Duration(action))*2*Mathf.PI;
        for(int i=0;i<secondary.Count;i++) {
            var b=secondary[i];float strength=action==FishAction.Walk||action==FishAction.Carry?2.6f:1.5f;
            var offset=Quaternion.Euler(Mathf.Sin(phase-i*.42f)*strength,Mathf.Sin(phase)*.7f,Mathf.Cos(phase-i*.42f)*strength*.5f);
            b.Bone.localRotation=b.Local*Quaternion.Inverse(b.Global)*offset*b.Global;
        }
        var left=Bone(HumanBodyBones.LeftHand);var right=Bone(HumanBodyBones.RightHand);
        if(left&&right)anchor.position=(left.position+right.position)*.5f+transform.forward*.08f;
        anchor.rotation=transform.rotation;
        if(runtime!=null) {
            runtime.Expression.SetWeight(ExpressionKey.Blink,pose.Blink);
            runtime.Expression.SetWeight(ExpressionKey.Happy,pose.Smile);
            runtime.Expression.SetWeight(ExpressionKey.Oh,pose.Oh);
            // Deliberately skip VRM SpringBone: it accumulates wall-clock time across pauses/seeks.
            runtime.Process();
        }
    }
    void OnDestroy() {runtime?.Dispose();}
}
}
