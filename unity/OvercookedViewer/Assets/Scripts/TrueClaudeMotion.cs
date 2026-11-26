using System;
using UnityEngine;

namespace OvercookedReplay {
// Wrist targets are shoulder-relative, in units of the authored arm length.
// A positive X in these profiles points away from the body on either side.
internal struct ClaudeHandPose {
    internal float Weight;
    internal Vector3 Wrist, Palm;

    internal ClaudeHandPose(float weight, Vector3 wrist, Vector3 palm) {
        Weight = weight;
        Wrist = wrist;
        Palm = palm;
    }
}

internal struct ClaudePose {
    internal Vector3 Hips, Spine, Head;
    internal float LeftThigh, RightThigh, LeftKnee, RightKnee;
    internal float LeftSwing, RightSwing, RootForward, HairSway;
    internal ClaudeHandPose LeftHand, RightHand;
}

internal static class TrueClaudeMotion {
    internal const float WalkCycle = .60f;
    const float CarryCycle = .66f;
    static readonly Vector3 CarryWrist = new Vector3(.09f, -.24f, .79f);
    static readonly Vector3 CarryPalm = new Vector3(-.19f, .13f, .97f);

    internal static ClaudePose Sample(FishAction action, float seconds, float locomotion, float turnSign) {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        float duration = BlueFishMotion.Duration(action);
        float u = BlueFishMotion.Loops(action) ? Mathf.Repeat(seconds, duration) / duration : Mathf.Clamp01(seconds / duration);
        float reach = Mathf.Sin(Mathf.PI * u);
        reach *= reach;
        var pose = new ClaudePose();
        switch (action) {
            case FishAction.Walk:
            case FishAction.Carry:
                Gait(ref pose, seconds, Mathf.Clamp01(locomotion), action == FishAction.Carry);
                if (action == FishAction.Carry) {
                    pose.LeftHand = pose.RightHand = new ClaudeHandPose(1, CarryWrist, CarryPalm);
                }
                break;
            case FishAction.Turn:
                float pivot = Mathf.Sin(Mathf.PI * 2 * u);
                pose.Hips = new Vector3(2 * reach, -7 * pivot * turnSign, 3 * pivot);
                pose.Spine.y = 9 * pivot * turnSign;
                pose.Head.y = 13 * pivot * turnSign;
                pose.LeftThigh = -8 * Mathf.Max(0, pivot);
                pose.RightThigh = -8 * Mathf.Max(0, -pivot);
                pose.LeftKnee = -pose.LeftThigh * 1.6f;
                pose.RightKnee = -pose.RightThigh * 1.6f;
                pose.LeftSwing = 6 * pivot;
                pose.RightSwing = -pose.LeftSwing;
                break;
            case FishAction.Pickup:
            case FishAction.Place:
            case FishAction.Deliver:
                float transfer = Mathf.SmoothStep(0, 1, u);
                float holding = action == FishAction.Pickup ? transfer : 1 - transfer;
                WorkLean(ref pose, reach, 13);
                pose.LeftHand = pose.RightHand = new ClaudeHandPose(Mathf.Max(reach, holding),
                    Vector3.Lerp(CarryWrist, new Vector3(.11f, -.33f, .91f), reach), CarryPalm);
                break;
            case FishAction.Assemble:
            case FishAction.Cook:
                // Left palm steadies the plate; the right lowers and draws the ingredient inward.
                float press = reach * Mathf.Pow(Mathf.Sin(2 * Mathf.PI * u), 2);
                WorkLean(ref pose, reach, 10);
                pose.Spine.y = -6 * reach;
                pose.Head.y = -7 * reach;
                pose.LeftHand = new ClaudeHandPose(reach, new Vector3(.14f, -.31f, .86f), CarryPalm);
                pose.RightHand = new ClaudeHandPose(reach,
                    new Vector3(-.28f * reach, .14f - .74f * press, .82f - .09f * press),
                    new Vector3(-.40f, -.42f - .35f * press, .64f));
                break;
            case FishAction.JointUnload:
                // Both hands go to the handles, then draw the load up and in together.
                float lift = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.38f, .72f, u));
                WorkLean(ref pose, reach, 17 - 9 * lift);
                pose.LeftHand = pose.RightHand = new ClaudeHandPose(reach,
                    Vector3.Lerp(new Vector3(.41f, -.50f, .74f), new Vector3(.35f, .14f, .85f), lift),
                    new Vector3(-.26f, .18f + .30f * lift, .89f));
                break;
            default:
                float breath = Mathf.Sin(seconds * 1.65f);
                pose.Spine.x = .9f * breath;
                pose.Head.z = .65f * breath;
                pose.HairSway = .55f * breath;
                break;
        }
        return pose;
    }

    static void Gait(ref ClaudePose pose, float seconds, float weight, bool carry) {
        float phase = 2 * Mathf.PI * Mathf.Repeat(seconds, carry ? CarryCycle : WalkCycle) / (carry ? CarryCycle : WalkCycle);
        float swing = Mathf.Sin(phase) * weight;
        float push = Mathf.Cos(phase) * weight;
        float stride = carry ? 21 : 28;
        pose.LeftThigh = -stride * swing;
        pose.RightThigh = stride * swing;
        pose.LeftKnee = Mathf.Max(0, push) * 34;
        pose.RightKnee = Mathf.Max(0, -push) * 34;
        pose.Hips = new Vector3(0, 4 * swing, -2.8f * swing);
        pose.Spine = new Vector3(3 * weight, -7 * swing, 4 * swing);
        pose.Head = new Vector3(-2 * weight, 3 * swing, -2 * swing);
        pose.LeftSwing = 23 * swing;
        pose.RightSwing = -pose.LeftSwing;
        pose.HairSway = 3.3f * push;
    }

    static void WorkLean(ref ClaudePose pose, float reach, float pitch) {
        pose.Hips.x = 3 * reach;
        pose.Spine.x = pitch * reach;
        pose.Head.x = -pitch * .30f * reach;
        pose.LeftThigh = pose.RightThigh = -10 * reach;
        pose.LeftKnee = pose.RightKnee = 17 * reach;
        pose.RootForward = .035f * reach;
        pose.HairSway = -2 * reach;
    }
}
}
