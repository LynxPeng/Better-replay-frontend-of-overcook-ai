using System;
using System.Collections.Generic;
using UnityEngine;

namespace OvercookedReplay {
public sealed class TrueClaudeActor : MonoBehaviour, IReplayChefActor {
    sealed class Limb {
        internal Transform UpperArm, Forearm, Wrist, Grip, Thumb, Index, Thigh, Shin, Foot, Toe, Skirt;
        internal float Side, ToeRestHeight;
    }

    readonly Dictionary<string, Transform> joints = new Dictionary<string, Transform>();
    readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();
    readonly Dictionary<HumanBodyBones, Transform> humanoid = new Dictionary<HumanBodyBones, Transform>();
    Transform model, anchor, hips, spine, head, hair, ribbon;
    Vector3 modelRest;
    Limb left, right;
    bool ready;

    public float TurnSign { get; set; } = 1;
    public FishAction LastAction { get; private set; }
    public int BoneCount { get { Initialize(); return joints.Count; } }
    public Transform HeldAnchor { get { Initialize(); return anchor; } }
    public Transform RightHand { get { Initialize(); return right.Wrist; } }
    public Transform Joint(string name) { Initialize(); joints.TryGetValue(name, out var joint); return joint; }
    public Transform Bone(HumanBodyBones id) { Initialize(); humanoid.TryGetValue(id, out var joint); return joint; }

    public void Initialize() {
        if (ready) return;
        model = transform.Find("Model");
        if (!model) throw new InvalidOperationException("TrueClaude prefab is missing Model");
        modelRest = model.localPosition;
        foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
            skin.updateWhenOffscreen = true;
            foreach (var bone in skin.bones) {
                if (!bone || rest.ContainsKey(bone)) continue;
                joints.Add(bone.name, bone);
                rest.Add(bone, bone.localRotation);
            }
        }
        hips = Bind("Pelvis", HumanBodyBones.Hips);
        spine = Bind("Spine", HumanBodyBones.Spine);
        Bind("Chest", HumanBodyBones.Chest);
        Bind("Neck", HumanBodyBones.Neck);
        head = Bind("Head", HumanBodyBones.Head);
        hair = joints["HairBack1_C"];
        ribbon = joints["Ribbon1_L"];
        left = BindLimb(true);
        right = BindLimb(false);
        anchor = transform.Find("HeldAnchor");
        if (!anchor) {
            anchor = new GameObject("HeldAnchor").transform;
            anchor.SetParent(transform, false);
        }
        ready = true;
    }

    Transform Bind(string name, HumanBodyBones id) {
        var bone = joints[name];
        humanoid.Add(id, bone);
        return bone;
    }

    Limb BindLimb(bool isLeft) {
        string side = isLeft ? "_L" : "_R";
        Bind("Clavicle" + side, isLeft ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder);
        var limb = new Limb {
            Side = isLeft ? -1 : 1,
            UpperArm = Bind("UpperArm" + side, isLeft ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm),
            Forearm = Bind("Forearm" + side, isLeft ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm),
            Wrist = Bind("Hand" + side, isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand),
            Grip = joints["Grip" + side], Thumb = joints["Thumb" + side], Index = joints["Index" + side],
            Thigh = Bind("Thigh" + side, isLeft ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg),
            Shin = Bind("Shin" + side, isLeft ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg),
            Foot = Bind("Foot" + side, isLeft ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot),
            Toe = Bind("Toe" + side, isLeft ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes),
            Skirt = joints[isLeft ? "Skirt_FL" : "Skirt_FR"]
        };
        limb.ToeRestHeight = transform.InverseTransformPoint(limb.Toe.position).y;
        return limb;
    }

    public void Sample(FishAction action, float seconds, float locomotion = 1, float? expressionTime = null,
                       float? celebrationPhase = null) {
        Initialize();
        var pose = TrueClaudeMotion.Sample(action, seconds, locomotion, TurnSign);
        foreach (var bone in rest) bone.Key.localRotation = bone.Value;
        model.localPosition = modelRest + Vector3.forward * pose.RootForward;
        Rotate(hips, pose.Hips);
        Rotate(spine, pose.Spine);
        Rotate(head, pose.Head);
        Rotate(hair, new Vector3(pose.HairSway, 0, pose.HairSway * .5f));
        Rotate(ribbon, new Vector3(0, 0, -pose.HairSway));
        PoseLeg(left, pose.LeftThigh, pose.LeftKnee);
        PoseLeg(right, pose.RightThigh, pose.RightKnee);
        // Keep the lower toe at its authored floor clearance through knee flexion.
        float leftGap = transform.InverseTransformPoint(left.Toe.position).y - left.ToeRestHeight;
        float rightGap = transform.InverseTransformPoint(right.Toe.position).y - right.ToeRestHeight;
        model.localPosition -= Vector3.up * Mathf.Min(leftGap, rightGap);
        PoseArm(left, pose.LeftSwing, pose.LeftHand);
        PoseArm(right, pose.RightSwing, pose.RightHand);
        anchor.position = (left.Grip.position + right.Grip.position) * .5f + transform.TransformVector(Vector3.up * .005f);
        anchor.rotation = transform.rotation;
        LastAction = action;
    }

    // Deltas are expressed in the chef's forward/up frame. Each imported joint
    // retains its own bind roll and inherits the sampled rotations of its parents.
    void Rotate(Transform joint, Vector3 euler) {
        var frame = transform.rotation;
        joint.rotation = frame * Quaternion.Euler(euler) * Quaternion.Inverse(frame) * joint.rotation;
    }

    void PoseLeg(Limb limb, float thigh, float knee) {
        Rotate(limb.Thigh, Vector3.right * thigh);
        Rotate(limb.Shin, Vector3.right * knee);
        Rotate(limb.Foot, Vector3.right * (-thigh - knee) * .84f);
        Rotate(limb.Skirt, Vector3.right * Mathf.Min(0, thigh) * .52f);
    }

    void PoseArm(Limb limb, float swing, ClaudeHandPose hand) {
        Rotate(limb.UpperArm, Vector3.right * swing);
        Rotate(limb.Forearm, Vector3.right * (-5 - Mathf.Abs(swing) * .30f));
        if (hand.Weight <= 0) return;

        var shoulder = limb.UpperArm.position;
        var elbow = limb.Forearm.position;
        var wrist = limb.Wrist.position;
        float upper = Vector3.Distance(shoulder, elbow);
        float lower = Vector3.Distance(elbow, wrist);
        // Solve in world space with the supplied chef-facing target; this also
        // handles model scale and the importer's rig-axis conversion.
        Vector3 toward = FacingVector(hand.Wrist, limb.Side);
        Vector3 desired = Vector3.Lerp(wrist - shoulder, toward * (upper + lower), hand.Weight);
        float distance = Mathf.Clamp(desired.magnitude, Mathf.Abs(upper - lower) + .0001f, upper + lower - .0001f);
        Vector3 axis = desired.normalized;
        Vector3 target = shoulder + axis * distance;
        Vector3 workPole = FacingVector(new Vector3(.85f, -.65f, -.12f), limb.Side);
        Vector3 originalPole = Vector3.ProjectOnPlane(elbow - shoulder, wrist - shoulder).normalized;
        Vector3 pole = Vector3.ProjectOnPlane(Vector3.Lerp(originalPole, workPole, hand.Weight), axis).normalized;
        float along = (upper * upper + distance * distance - lower * lower) / (2 * distance);
        float bend = Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
        Vector3 desiredElbow = shoulder + along * axis + bend * pole;
        AimAt(limb.UpperArm, limb.Forearm, desiredElbow);
        AimAt(limb.Forearm, limb.Wrist, target);

        Vector3 palm = limb.Grip.position - limb.Wrist.position;
        Vector3 hold = FacingVector(hand.Palm, limb.Side);
        limb.Wrist.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(palm, hold), hand.Weight) * limb.Wrist.rotation;
        Rotate(limb.Thumb, new Vector3(0, limb.Side * 10 * hand.Weight, 0));
        Rotate(limb.Index, new Vector3(9 * hand.Weight, 0, 0));
    }

    Vector3 FacingVector(Vector3 offset, float side) {
        offset.x *= side;
        return transform.TransformDirection(offset);
    }

    static void AimAt(Transform joint, Transform tip, Vector3 target) {
        joint.rotation = Quaternion.FromToRotation(tip.position - joint.position, target - joint.position) * joint.rotation;
    }
}
}
