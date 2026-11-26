using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OvercookedReplay {
// Independent checks against the exporter's JSON, also callable in the shipped Mono player.
public static class SharedKitchenRegression {
    static readonly string[] Actions = { "N", "S", "E", "W", "STAY", "INT" };
    static readonly Vector2Int[] Directions = { Vector2Int.down, Vector2Int.up, Vector2Int.right, Vector2Int.left };
    static readonly string[] FrameFields = {
        "tick", "actions", "score", "reward", "normal_completed", "joint_completed",
        "joint_wait_steps", "unmatched_steps", "chefs", "cooker", "cutting", "asm", "buf", "orders"
    };

    public static JToken ReadSample(string path) {
        try {
            var source = JToken.Parse(File.ReadAllText(path));
            if (source is JObject && source["layout"] is JArray && source["frames"] is JArray &&
                source["width"] != null && source["height"] != null && source["mode"] != null) return source;
            if (path.EndsWith(".replay.json",StringComparison.OrdinalIgnoreCase) && source is JObject &&
                source["header"] is JObject && source["frames"] is JArray && source["segments"] is JArray) return source;
            if (path.EndsWith("_trajectory.json",StringComparison.OrdinalIgnoreCase) && source is JArray steps &&
                steps.Count > 0 && steps[0] is JObject && steps[0]["tick"] != null &&
                steps[0]["team_reward"] != null && steps[0]["actions"] is JArray && steps[0]["events"] is JArray)
                return steps;
        } catch (JsonReaderException) { }
        return null;
    }

    static bool SameValue(JToken expected, JToken actual) {
        if (actual == null) return false;
        if (expected is JObject obj) return actual is JObject && obj.Properties().All(p => SameValue(p.Value, actual[p.Name]));
        if (expected is JArray array) return actual is JArray other && array.Count == other.Count &&
            array.Select((token, index) => SameValue(token, other[index])).All(equal => equal);
        if (expected.Type == JTokenType.Float || actual.Type == JTokenType.Float)
            return (expected.Type == JTokenType.Integer || expected.Type == JTokenType.Float) &&
                (actual.Type == JTokenType.Integer || actual.Type == JTokenType.Float) && (float)expected == (float)actual;
        return JToken.DeepEquals(expected, actual);
    }

    static int[] ActorsAt(JToken before, JToken after, string[] layout, char station, string held) {
        return before["chefs"].Where(chef => {
            int id = (int)chef["id"];
            if ((string)after["actions"][id] != "INT" || (string)chef["held"] != held) return false;
            var direction = Directions[Array.IndexOf(Actions, (string)chef["facing"])];
            if ((station == 'G' || station == 'A') && direction.x == 0) return false;
            int x = (int)chef["x"] + direction.x, y = (int)chef["y"] + direction.y;
            return y >= 0 && y < layout.Length && x >= 0 && x < layout[y].Length && layout[y][x] == station;
        }).Select(chef => (int)chef["id"]).OrderBy(id => id).ToArray();
    }

    static void CheckEvent(Segment step, string kind, int[] actors, string label, Action<bool,string> need) {
        var matches = (step.events ?? Array.Empty<ReplayEvent>()).Where(e => e.kind == kind).ToArray();
        need(matches.Length == (actors.Length > 0 ? 1 : 0), label + " " + kind + " occurrence");
        if (actors.Length > 0) {
            var found = matches[0];
            need(found.participants != null && found.participants.SequenceEqual(actors), label + " " + kind + " participants");
            need(found.agent_index == actors[0], label + " " + kind + " first actor");
        }
    }

    // hy's demo saves verified actor IDs and order IDs directly alongside the joint action.
    public static void CheckTrajectory(JArray source,string path,ReplayFile replay,Action<bool,string> need) {
        string summaryPath = path.Substring(0,path.Length-"_trajectory.json".Length)+"_summary.json";
        var summary = JObject.Parse(File.ReadAllText(summaryPath));
        var orders = summary["orders"].ToDictionary(o => (int)o["id"],o => (string)o["kind"]);
        need(replay.header.IsShared && replay.frames.Length == source.Count+1, "hy trajectory length");
        for (int i=0; i<source.Count; i++) {
            var recorded = source[i];
            var step = replay.segments[i];
            var state = replay.frames[i+1].state;
            string label = "hy tick " + recorded["tick"];
            need(state.timestep == (int)recorded["tick"] && step.actions.SequenceEqual(recorded["actions"].Values<int>()) &&
                state.shared.reward == (float)recorded["team_reward"], label + " tick / actions / reward");
            var actual = (step.events ?? Array.Empty<ReplayEvent>()).Where(e =>
                e.kind == "joint_unload" || e.kind == "normal_unload" || e.kind == "assemble" || e.kind == "meal_delivery" || e.kind == "chop").ToList();
            foreach (var native in recorded["events"]) {
                string type = (string)native["type"];
                if (type != "unload" && type != "assemble" && type != "serve" && type != "chop") continue;
                int order = (int?)native["order_id"] ?? -1;
                string kind = type == "unload" ? orders[order]+"_unload" : type == "serve" ? "meal_delivery" : type;
                var ids = native["actors"].Values<int>().OrderBy(id => id);
                int match = actual.FindIndex(e => e.kind == kind && e.order_id == order && e.participants.SequenceEqual(ids));
                need(match >= 0, label + " " + kind + " order " + order + " actors");
                actual.RemoveAt(match);
            }
            need(actual.Count == 0, label + " unexpected event");
        }
        var last = replay.frames[replay.frames.Length-1];
        var metrics = summary["metrics"];
        need(last.score == (float)metrics["team_score"] && last.state.shared.normal_completed == (int)metrics["normal_completed"] &&
            last.state.shared.joint_completed == (int)metrics["joint_completed"], "hy final score / meal counts");
    }

    public static void Check(JObject source, ReplayFile replay, Action<bool,string> need) {
        var frames = (JArray)source["frames"];
        var layout = source["layout"].Values<string>().ToArray();
        need(replay.header.IsShared && replay.header.terrain.SequenceEqual(layout), "SharedKitchen terrain");
        need(replay.header.horizon == (int)source["horizon"], "SharedKitchen horizon");
        need(replay.frames.Length == frames.Count && replay.segments.Length == frames.Count-1, "SharedKitchen snapshot count");
        var expectedMarks = new System.Collections.Generic.List<float>();
        var view = new SharedKitchenView(replay);
        bool continuous = (int)frames[0]["tick"] == 0;
        for (int i = 0; i < frames.Count; i++) {
            var raw = frames[i];
            var frame = replay.frames[i];
            string label = "SharedKitchen tick " + raw["tick"];
            need(frame.state.shared != null, label + " native snapshot");
            var preserved = JObject.Parse(JsonUtility.ToJson(frame.state.shared));
            foreach (string field in FrameFields) need(SameValue(raw[field], preserved[field]), label + " field " + field);
            need(frame.score == (float)raw["score"] && frame.state.timestep == (int)raw["tick"], label + " score / timestep");
            need(frame.state.players.Length == ((JArray)raw["chefs"]).Count, label + " player count");
            foreach (var chef in raw["chefs"]) {
                int id = (int)chef["id"];
                var projected = frame.state.players[id];
                var facing = Directions[Array.IndexOf(Actions, (string)chef["facing"])];
                need(projected.position.SequenceEqual(new[] { (int)chef["x"], (int)chef["y"] }) &&
                    projected.orientation.SequenceEqual(new[] { facing.x, facing.y }) &&
                    (projected.held_object?.name ?? "") == (string)chef["held"], label + " projected chef " + id);
            }
            if (i == 0) continue;
            var previous = frames[i-1];
            var step = replay.segments[i-1];
            if ((int)raw["tick"] != (int)previous["tick"] + 1) {
                continuous = false;
                need(step.kind == "gap" && step.actions == null && !step.reward_known && (step.events?.Length ?? 0) == 0,
                    label + " missing interval produced an action, reward or event");
                need(view.WaitSteps(frame.state.shared,i) == null, label + " cannot infer corrected wait across gap");
                continue;
            }
            var actions = raw["actions"].Values<string>().Select(a => Array.IndexOf(Actions, a));
            need(step.kind == "step" && step.actions.SequenceEqual(actions) && step.reward_known &&
                step.sparse_reward == (float)raw["score"] - (float)previous["score"], label + " arriving actions / sparse score");
            var none = Array.Empty<int>();
            bool chopped = (string)previous["cutting"]["item"] == "RS" && (int)raw["cutting"]["prog"] == (int)previous["cutting"]["prog"] + 1 &&
                ((string)raw["cutting"]["item"] == "RS" || (string)raw["cutting"]["item"] == "CS");
            var cutters = chopped ? ActorsAt(previous,raw,layout,'K',"") : none;
            CheckEvent(step, "chop", cutters.Length == 1 ? cutters : none, label, need);
            if (cutters.Length == 1) need(BlueFishReplayPose.Select(replay,i-1,cutters[0]) == FishAction.Cook, label + " cutting animation");
            bool unloaded = (string)previous["cooker"]["item"] == "CM" && (int)previous["cooker"]["rem"] == 0 &&
                (string)previous["asm"]["main"] == "" && (string)raw["cooker"]["item"] == "" && (string)raw["asm"]["main"] == "CM";
            var unloaders = unloaded ? ActorsAt(previous, raw, layout, 'G', "") : none;
            string orderKind = (string)previous["cooker"]["kind"];
            CheckEvent(step, "joint_unload", orderKind == "joint" && unloaders.Length == 2 ? unloaders : none, label, need);
            if (orderKind == "joint" && unloaders.Length == 2) {
                foreach (int chef in unloaders)
                    need(BlueFishReplayPose.Select(replay,i-1,chef) == FishAction.Cook, label + " joint unloading animation " + chef);
                foreach (var chef in raw["chefs"]) {
                    int id = (int)chef["id"];
                    var old = previous["chefs"].First(c => (int)c["id"] == id);
                    if (!unloaders.Contains(id) && (string)old["held"] == "" && (string)chef["held"] != "" && (string)raw["actions"][id] == "INT")
                        need(BlueFishReplayPose.Select(replay,i-1,id) == FishAction.Pickup, label + " independent pickup " + id);
                }
            } else if (orderKind == "joint" && (string)previous["cooker"]["item"] == "CM" && !unloaded) {
                foreach (int chef in ActorsAt(previous,raw,layout,'G',""))
                    need(BlueFishReplayPose.Select(replay,i-1,chef) != FishAction.Cook, label + " unsuccessful unload animation " + chef);
            }
            CheckEvent(step, "normal_unload", orderKind == "normal" ? unloaders : none, label, need);
            bool assembled = (string)previous["asm"]["main"] == "CM" && (string)previous["asm"]["side"] == "CS" &&
                (string)previous["asm"]["plate"] == "PL" && (string)raw["asm"]["main"] == "" &&
                (string)raw["asm"]["side"] == "" && (string)raw["asm"]["plate"] == "MEAL";
            var assemblers = assembled ? ActorsAt(previous, raw, layout, 'A', "") : none;
            CheckEvent(step, "assemble", assemblers.Length == 1 ? assemblers : none, label, need);
            bool completed = (int)raw["normal_completed"] > (int)previous["normal_completed"] ||
                (int)raw["joint_completed"] > (int)previous["joint_completed"];
            var servers = completed ? ActorsAt(previous, raw, layout, 'S', "MEAL") : none;
            var deliveries = (step.events ?? Array.Empty<ReplayEvent>()).Where(e => e.kind == "meal_delivery").ToArray();
            need(deliveries.Length == servers.Length && servers.All(id => deliveries.Any(e =>
                e.agent_index == id && e.participants.SequenceEqual(new[] { id }))), label + " deliveries / serving actors");
            if (servers.Length > 0 || frame.score > replay.frames[i-1].score) expectedMarks.Add(frame.time);
        }
        need(ReplayFile.ScoreMarkerTimes(replay).SequenceEqual(expectedMarks), "SharedKitchen score marker times");
        var last = replay.frames[frames.Count-1];
        var metrics = source["metrics"];
        if (continuous && metrics?["ticks"] != null && (int)metrics["ticks"] == last.state.timestep && metrics["joint_wait_steps"] != null)
            need(view.WaitSteps(last.state.shared,frames.Count-1) == (int)metrics["joint_wait_steps"], "SharedKitchen episode wait total");
    }
}
}
