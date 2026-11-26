using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OvercookedReplay {
// Run from the built player against a complete, unmodified three-chef SharedKitchen trace.
public static class TrueClaudePlaybackChecks {
    const int Green = 2;

    [Serializable] sealed class Sample {
        public string action, held;
        public int startTick;
        public float replayTime, alpha, palmTravel, footTravel, turnDegrees, plateToGripMidpoint, seekError;
    }
    [Serializable] sealed class Report {
        public string replay, error, controlReplay;
        public bool passed;
        public int assertions, frames, normalMeals, jointMeals, greenAssemblies, greenJointUnloads;
        public int foreignAssemblies, foreignJointUnloads, coincidentGreenInteractions;
        public float score, workPoseDifference;
        public List<Sample> samples = new List<Sample>();
        public void Require(bool condition, string message) {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    sealed class Pose {
        public Vector3 root;
        public Quaternion facing;
        public Vector3[] marks;
        // Actor-local joint positions measure actual skin motion independently of navigation.
        public Pose(TrueClaudeActor actor) {
            var body = actor.transform;
            root = body.position;
            facing = body.rotation;
            marks = Array.ConvertAll(new[] {
                actor.Joint("Grip_L"), actor.Joint("Grip_R"),
                actor.Bone(HumanBodyBones.LeftFoot), actor.Bone(HumanBodyBones.RightFoot),
                actor.Bone(HumanBodyBones.Hips), actor.Bone(HumanBodyBones.Head), actor.HeldAnchor
            }, joint => body.InverseTransformPoint(joint.position));
        }
        public float Difference(Pose other) {
            float distance = Vector3.Distance(root, other.root);
            for (int i = 0; i < marks.Length; i++)
                distance = Mathf.Max(distance, Vector3.Distance(marks[i], other.marks[i]));
            return distance;
        }
        public float PairTravel(Pose other, int start) =>
            Mathf.Max(Vector3.Distance(marks[start], other.marks[start]),
                      Vector3.Distance(marks[start + 1], other.marks[start + 1]));
    }

    static bool HasEvent(Segment step, string kind, int chef) =>
        Array.Exists(step.events, entry => entry.kind == kind && entry.HasActor(chef));

    static IEnumerator Verify(ReplayPlayer player, Report report) {
        player.Load(report.replay);
        yield return null;
        var data = player.Data;
        report.Require(data != null && data.header.IsShared && data.header.complete &&
                       data.header.players.Length == 3, "Expected a complete three-chef SharedKitchen recording.");
        report.frames = data.frames.Length;
        var last = data.frames[data.frames.Length - 1];
        report.score = last.score;
        report.normalMeals = last.state.shared.normal_completed;
        report.jointMeals = last.state.shared.joint_completed;

        var actors = UnityEngine.Object.FindObjectsByType<TrueClaudeActor>();
        report.Require(actors.Length == 1, "Expected exactly one instantiated TrueClaudeActor in the kitchen.");
        var actor = actors[0];
        var siblings = actor.transform.parent;
        report.Require(siblings.GetComponentsInChildren<BlueFishActor>().Length == 1 &&
                       siblings.GetComponentsInChildren<GuguGagaActor>().Length == 1,
                       "Blue and orange actors must retain their own adapters.");
        report.Require(actor.GetComponentsInChildren<SkinnedMeshRenderer>().Length == 3 &&
                       actor.Joint("Grip_L") && actor.Joint("Grip_R"), "Missing body meshes or weighted grip joints.");

        // Derive test cases from recorded movement, held items and attributed station events.
        var cases = new Dictionary<string,int>();
        for (int i = 0; i < data.segments.Length; i++) {
            var step = data.segments[i];
            report.Require(step.kind == "step", "Missing recorded transition at frame " + i);
            var a = data.frames[i].state.players[Green];
            var b = data.frames[i + 1].state.players[Green];
            string held = a.held_object?.name ?? "";
            bool moving = ReplayPlayer.World(a.position) != ReplayPlayer.World(b.position);
            bool turning = ReplayPlayer.World(a.orientation) != ReplayPlayer.World(b.orientation);
            bool assembly = HasEvent(step, "assemble", Green);
            bool unloading = HasEvent(step, "joint_unload", Green);
            if (assembly) report.greenAssemblies++;
            if (unloading) report.greenJointUnloads++;
            string key = assembly ? "Assemble" : unloading ? "JointUnload" :
                moving && held == "PL" ? "PL" : moving && held == "MEAL" ? "MEAL" :
                moving && held == "" ? "Walk" : !moving && turning && held == "" ? "Turn" : "";
            if (key != "" && !cases.ContainsKey(key)) cases.Add(key, i);

            player.SeekSample(i, .5f);
            report.Require((actor.LastAction == FishAction.Assemble) == assembly &&
                           (actor.LastAction == FishAction.JointUnload) == unloading,
                           "Green action disagrees with recorded participants at frame " + i);
            for (int chef = 0; chef < Green; chef++) {
                var regular = BlueFishReplayPose.Select(data, i, chef);
                report.Require(regular != FishAction.Assemble && regular != FishAction.JointUnload,
                               "Another adapter received the specialized green action at frame " + i);
            }
            yield return null;
        }

        Pose assemblyPose = null, unloadPose = null;
        foreach (string key in new[]{"Walk", "Turn", "PL", "MEAL", "Assemble", "JointUnload"}) {
            report.Require(cases.ContainsKey(key), "The recording lacks a green " + key + " transition.");
            int i = cases[key];
            var a = data.frames[i];
            var b = data.frames[i + 1];
            var green = a.state.players[Green];
            string held = green.held_object?.name ?? "";
            var expected = key == "PL" || key == "MEAL" ? FishAction.Carry : (FishAction)Enum.Parse(typeof(FishAction), key);
            player.SeekSample(i, 0);
            yield return null;
            Pose start = new Pose(actor);
            player.Presentation.Toggle();
            float target = expected == FishAction.Turn ? .29f : .42f;
            float deadline = Time.realtimeSinceStartup + 15;
            while (player.Clock.Index == i && player.Clock.Alpha < target) {
                report.Require(Time.realtimeSinceStartup < deadline, "Playback clock stalled during " + key);
                // Let the regular ReplayPlayer.Update sample the pose. Limit the increment on slow graphics devices.
                player.Clock.Speed = Mathf.Min(.25f, .0035f / Mathf.Max(.001f, Time.unscaledDeltaTime));
                yield return null;
            }
            player.Presentation.Pause();
            float alpha = player.Clock.Alpha;
            report.Require(player.Clock.Index == i && alpha >= target && alpha < .85f &&
                           actor.LastAction == expected, "Did not observe the live mid-step " + key + " pose.");
            var mid = new Pose(actor);
            var measurement = new Sample {
                action = expected.ToString(), held = held, startTick = a.state.timestep,
                replayTime = player.Clock.Time, alpha = alpha, palmTravel = mid.PairTravel(start, 0),
                footTravel = mid.PairTravel(start, 2), turnDegrees = Quaternion.Angle(start.facing, mid.facing)
            };
            report.samples.Add(measurement);
            var destination = ReplayPlayer.World(b.state.players[Green].position);
            report.Require(Vector3.Distance(mid.root, Vector3.Lerp(ReplayPlayer.World(green.position), destination, alpha)) < .001f,
                           "The instantiated model does not follow the recorded green seat during " + key);
            if (expected == FishAction.Walk || expected == FishAction.Carry)
                report.Require(measurement.footTravel > .002f, "Feet failed to articulate during " + key);
            if (expected == FishAction.Turn) {
                float total = Vector3.Angle(ReplayPlayer.World(green.orientation), ReplayPlayer.World(b.state.players[Green].orientation));
                report.Require(measurement.turnDegrees > 1 && measurement.turnDegrees < total - 1,
                               "The model did not interpolate between the recorded headings.");
            }
            if (expected == FishAction.Assemble || expected == FishAction.JointUnload) {
                report.Require(measurement.palmTravel > .01f, "Weighted palms stayed still during " + key);
                if (expected == FishAction.Assemble) assemblyPose = mid; else unloadPose = mid;
            }
            CheckHeld(actor, held, report, measurement);
            yield return null;
            report.Require(new Pose(actor).Difference(mid) < .0003f, "The posed model drifted while paused.");

            // Seek past the event, then back from an unrelated pose. Held objects use Unity's deferred Destroy.
            player.SeekSample(data.frames.Length - 1, 0);
            yield return null;
            player.SeekSample(i, alpha);
            yield return null;
            var restored = new Pose(actor);
            measurement.seekError = restored.Difference(mid);
            report.Require(measurement.seekError < .0003f && Quaternion.Angle(restored.facing, mid.facing) < .1f &&
                           actor.LastAction == expected && Mathf.Abs(player.Clock.Time - measurement.replayTime) < .0001f,
                           "Backward seek diverged from forward playback for " + key);
            CheckHeld(actor, held, report, measurement);
        }
        report.workPoseDifference = assemblyPose.PairTravel(unloadPose, 0);
        report.Require(report.workPoseDifference > .015f, "Assemble and joint unload yielded the same palm placement.");
        player.Clock.Speed = 1;
    }

    static void CheckHeld(TrueClaudeActor actor, string held, Report report, Sample sample) {
        var anchor = actor.HeldAnchor;
        if (held == "") {
            report.Require(anchor.childCount == 0, "An item remained in the green chef's empty hands.");
            return;
        }
        var item = anchor.Find("SharedItem_" + held);
        report.Require(anchor.childCount == 1 && item && item.Find("Ceramic rim") &&
                       item.GetComponentsInChildren<Renderer>().Length >= 3, "Missing displayed " + held + " plate geometry.");
        report.Require((bool)item.Find("SharedItem_CM") == (held == "MEAL") &&
                       (bool)item.Find("SharedItem_CS") == (held == "MEAL"), "Plate contents disagree with " + held);
        var midpoint = (actor.Joint("Grip_L").position + actor.Joint("Grip_R").position) * .5f;
        sample.plateToGripMidpoint = Vector3.Distance(midpoint, item.position);
        report.Require(sample.plateToGripMidpoint < .10f && Vector3.Dot(item.up, Vector3.up) > .9f,
                       "Plate moved away from the supporting hands or tipped over.");
    }

    static IEnumerator VerifyForeignEvents(ReplayPlayer player, Report report) {
        player.BeginPresentation(ReplayFile.Load(report.controlReplay), Path.GetFileName(report.controlReplay));
        yield return null;
        var actor = UnityEngine.Object.FindFirstObjectByType<TrueClaudeActor>();
        report.Require(actor && player.Data.header.IsShared, "Missing green actor in negative control replay.");
        var data = player.Data;
        for (int i = 0; i < data.segments.Length; i++) {
            var step = data.segments[i];
            if (step.kind != "step") continue;
            foreach (var entry in step.events) {
                if (entry.HasActor(Green) || (entry.kind != "assemble" && entry.kind != "joint_unload")) continue;
                player.SeekSample(i, .48f);
                yield return null;
                report.Require(actor.LastAction != FishAction.Assemble && actor.LastAction != FishAction.JointUnload,
                               "Green performed another chef's " + entry.kind + " at tick " + data.frames[i + 1].state.timestep);
                if (entry.kind == "assemble") report.foreignAssemblies++; else report.foreignJointUnloads++;
                if (step.actions[Green] == 5) {
                    report.coincidentGreenInteractions++;
                    report.Require(actor.LastAction == BlueFishReplayPose.Select(data, i, Green),
                                   "Another chef's event overrode green's simultaneous interaction.");
                }
            }
        }
        report.Require(report.foreignAssemblies > 0 && report.foreignJointUnloads > 0 &&
                       report.coincidentGreenInteractions > 0,
                       "Control must include another chef's assembly and joint unload, with a parallel green interaction.");
    }

    public static IEnumerator Run(ReplayPlayer player, string replayPath, string reportPath) {
        var report = new Report { replay = Path.GetFullPath(replayPath) };
        var args = Environment.GetCommandLineArgs();
        int capture = Array.IndexOf(args, "--capture-green-actions");
        var jobs = new List<IEnumerator> { Verify(player, report) };
        if (capture >= 0 && capture + 1 < args.Length) jobs.Add(TrueClaudeReplayCapture.Save(player, args[capture + 1]));
        int control = Array.IndexOf(args, "--check-claude-control");
        if (control >= 0 && control + 1 < args.Length) {
            report.controlReplay = Path.GetFullPath(args[control + 1]);
            jobs.Add(VerifyForeignEvents(player, report));
        }
        foreach (var job in jobs) {
            while (true) {
                bool running = false;
                try { running = job.MoveNext(); }
                catch (Exception failure) { report.error = failure.ToString(); Debug.LogException(failure); }
                if (!running) break;
                yield return job.Current;
            }
            if (report.error != null) break;
        }
        report.passed = report.error == null;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        Debug.Log("True Claude recorded playback verification: " + report.passed);
        Application.Quit(report.passed ? 0 : 1);
    }
}
}
