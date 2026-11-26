using System;
using System.Collections.Generic;
using UnityEngine;

namespace OvercookedReplay
{
    [Serializable] public class SharedChef { public int id, x, y; public string facing, held; }
    [Serializable] public class SharedCooker { public string item, kind; public int rem; }
    [Serializable] public class SharedCutting { public string item; public int prog; }
    [Serializable] public class SharedAssembly { public string main, side, plate; }
    [Serializable] public class SharedOrder { public int slot, id; public string kind, status; }
    [Serializable] public class SharedNativeEvent { public string type; public int[] actors; public int order_id; }
    [Serializable] public class SharedFrame
    {
        public int tick, normal_completed, joint_completed, joint_wait_steps, unmatched_steps;
        public float score, reward;
        public string[] actions;
        public SharedChef[] chefs;
        public SharedCooker cooker;
        public SharedCutting cutting;
        public SharedAssembly asm;
        public string buf;
        public SharedOrder[] orders;
        public SharedNativeEvent[] events;
    }

    public static class SharedReplay
    {
        // The raw file has no wall clock. The exporter's HTML player defaults to 6 FPS.
        public const float TicksPerSecond = 6f;
        [Serializable] class Signature { public string[] layout; public int width, height; public ReplayHeader header; }
        [Serializable] class Trace { public string mode; public string[] layout; public int width, height, horizon, chop_steps; public bool complete; public SharedFrame[] frames; }

        static readonly string[] Actions = { "N", "S", "E", "W", "STAY", "INT" };
        static readonly string[] Items = { "", "RM", "RS", "CS", "CM", "PL", "MEAL" };
        static readonly int[][] Directions = { new[] { 0, -1 }, new[] { 0, 1 }, new[] { 1, 0 }, new[] { -1, 0 } };

        public static bool IsRawTrace(string json)
        {
            var signature = JsonUtility.FromJson<Signature>(json);
            return signature?.layout?.Length > 0 && signature.width > 0 && signature.height > 0 &&
                   (signature.header == null || signature.header.schema_version == 0);
        }

        static void Need(bool ok, string field)
        {
            if (!ok) throw new ArgumentException("SharedKitchen 回放数据无效：" + field);
        }

        static bool IsItem(string value) => Array.IndexOf(Items, value) >= 0;

        public static ReplayFile Read(string json)
        {
            var trace = JsonUtility.FromJson<Trace>(json);
            Need(trace != null && trace.frames?.Length > 0, "frames");
            Need(trace.width > 0 && trace.height == trace.layout?.Length && trace.height > 0, "layout");
            var stations = new Dictionary<char, int[]>();
            for (int y = 0; y < trace.height; y++)
            {
                string row = trace.layout[y];
                Need(row?.Length == trace.width, "layout[" + y + "]");
                for (int x = 0; x < row.Length; x++)
                {
                    char tile = row[x];
                    if (tile == ' ' || tile == 'X') continue;
                    Need("MGLKADBS".IndexOf(tile) >= 0 && !stations.ContainsKey(tile), "layout " + tile);
                    stations.Add(tile, new[] { x, y });
                }
            }
            Need(stations.Count == 8, "缺少工位");
            Need(trace.horizon >= 0, "horizon");
            var replay = new ReplayFile
            {
                header = new ReplayHeader
                {
                    schema_version = 1, environment = "shared_kitchen", layout_name = "SharedKitchen",
                    terrain = trace.layout, horizon = trace.horizon, source = trace.mode,
                    shared_chop_steps = trace.chop_steps > 0 ? trace.chop_steps : 2,
                    players = new[] { new PlayerMeta { id = "P0" }, new PlayerMeta { id = "P1" }, new PlayerMeta { id = "P2" } }
                },
                frames = new Frame[trace.frames.Length],
                segments = new Segment[trace.frames.Length - 1],
                diagnostics = Array.Empty<string>()
            };
            bool continuous = true;
            for (int i = 0; i < trace.frames.Length; i++)
            {
                var raw = trace.frames[i];
                Need(raw != null && raw.tick >= 0 && raw.tick <= trace.horizon &&
                     (i == 0 || raw.tick > trace.frames[i - 1].tick), "tick at frame " + i);
                var players = ReadPlayers(raw, trace);
                CheckState(raw);
                replay.frames[i] = new Frame
                {
                    time = (raw.tick - trace.frames[0].tick) / TicksPerSecond, score = raw.score,
                    state = new Snapshot { timestep = raw.tick, shared = raw, players = players,
                        objects = Array.Empty<Item>(), all_orders = Array.Empty<Order>() }
                };
                if (i == 0) continue;
                var before = replay.frames[i - 1].state;
                if (raw.tick != before.timestep + 1)
                {
                    continuous = false;
                    replay.segments[i - 1] = new Segment { kind = "gap", events = Array.Empty<ReplayEvent>() };
                    continue;
                }
                // snapshot(t + 1) stores the joint action and reward for t -> t + 1.
                Need(raw.actions?.Length == 3, "actions at tick " + raw.tick);
                var actions = Array.ConvertAll(raw.actions, action => Array.IndexOf(Actions, action));
                Need(Array.TrueForAll(actions, action => action >= 0), "action at tick " + raw.tick);
                replay.segments[i - 1] = new Segment { kind = "step", actions = actions, reward_known = true,
                    sparse_reward = raw.score - before.shared.score,
                    events = ConfirmedEvents(before, replay.frames[i].state, actions, stations) };
            }
            replay.header.complete = continuous && trace.frames[0].tick == 0 &&
                                     (trace.complete || trace.frames[trace.frames.Length - 1].tick == trace.horizon);
            return replay;
        }

        static ChefState[] ReadPlayers(SharedFrame raw, Trace trace)
        {
            Need(raw.chefs?.Length == 3, "chefs at tick " + raw.tick);
            var players = new ChefState[3];
            var occupied = new HashSet<Vector2Int>();
            foreach (var chef in raw.chefs)
            {
                Need(chef != null && chef.id >= 0 && chef.id < players.Length && players[chef.id] == null, "chef.id");
                Need(chef.y >= 0 && chef.y < trace.height && chef.x >= 0 && chef.x < trace.width &&
                     trace.layout[chef.y][chef.x] == ' ' && occupied.Add(new Vector2Int(chef.x, chef.y)), "chef position");
                int direction = Array.IndexOf(Actions, chef.facing);
                Need(direction >= 0 && direction < Directions.Length && IsItem(chef.held), "chef facing/held");
                var position = new[] { chef.x, chef.y };
                players[chef.id] = new ChefState { position = position, orientation = Directions[direction],
                    held_object = chef.held == "" ? null : new Item { name = chef.held, position = position } };
            }
            return players;
        }

        static void CheckState(SharedFrame raw)
        {
            Need(float.IsFinite(raw.score) && float.IsFinite(raw.reward), "score/reward");
            Need(raw.normal_completed >= 0 && raw.joint_completed >= 0 && raw.joint_wait_steps >= 0 &&
                 raw.unmatched_steps >= 0, "counters");
            Need(raw.cooker != null && IsItem(raw.cooker.item) && raw.cooker.rem >= 0 &&
                 (raw.cooker.item == "" || raw.cooker.kind == "joint" || raw.cooker.kind == "normal"), "cooker");
            Need(raw.cutting != null && IsItem(raw.cutting.item) && raw.cutting.prog >= 0, "cutting");
            Need(raw.asm != null && IsItem(raw.asm.main) && IsItem(raw.asm.side) && IsItem(raw.asm.plate), "asm");
            Need(IsItem(raw.buf), "buf");
            Need(raw.orders != null, "orders");
            var ids = new HashSet<int>();
            foreach (var order in raw.orders)
                Need(order != null && order.id >= 0 && ids.Add(order.id) && (order.kind == "normal" || order.kind == "joint") &&
                     (order.status == "active" || order.status == "started" || order.status == "queued" || order.status == "served"), "order");
        }

        // Station actions use the chef's position and facing at the start of the step.
        static int[] Interactors(Snapshot before, Snapshot after, int[] actions, int[] station, string held, bool sidePorts)
        {
            var actors = new List<int>();
            for (int id = 0; id < before.players.Length; id++)
            {
                var chef = before.players[id];
                var next = after.players[id];
                if (actions[id] == 5 && (chef.held_object?.name ?? "") == held && next.held_object == null &&
                    next.position[0] == chef.position[0] && next.position[1] == chef.position[1] &&
                    (!sidePorts || chef.position[1] == station[1]) &&
                    chef.position[0] + chef.orientation[0] == station[0] && chef.position[1] + chef.orientation[1] == station[1])
                    actors.Add(id);
            }
            return actors.ToArray();
        }

        static ReplayEvent Event(string kind, int[] station, int[] actors)
        {
            return new ReplayEvent { kind = kind, participants = actors, agent_index = actors[0], position = station,
                                     order_id = -1, recipe = -1, wait_steps = -1 };
        }

        static ReplayEvent[] ConfirmedEvents(Snapshot before, Snapshot after, int[] actions, Dictionary<char, int[]> stations)
        {
            var a = before.shared;
            var b = after.shared;
            var events = new List<ReplayEvent>();
            if (a.cooker.item == "CM" && a.cooker.rem == 0 && b.cooker.item == "" && b.cooker.rem == 0 &&
                a.asm.main == "" && b.asm.main == "CM")
            {
                int[] actors = Interactors(before, after, actions, stations['G'], "", true);
                if ((a.cooker.kind == "joint" && actors.Length == 2) || (a.cooker.kind == "normal" && actors.Length >= 1))
                    events.Add(Event(a.cooker.kind + "_unload", stations['G'], actors));
            }
            if (a.asm.main == "CM" && a.asm.side == "CS" && a.asm.plate == "PL" &&
                b.asm.main == "" && b.asm.side == "" && b.asm.plate == "MEAL")
            {
                int[] actors = Interactors(before, after, actions, stations['A'], "", true);
                if (actors.Length == 1) events.Add(Event("assemble", stations['A'], actors));
            }
            if (a.cutting.item == "RS" && (b.cutting.item == "RS" || b.cutting.item == "CS") &&
                (long)b.cutting.prog == (long)a.cutting.prog + 1)
            {
                int[] actors = Interactors(before, after, actions, stations['K'], "", false);
                var native = b.events == null ? null : Array.Find(b.events, e => e?.type == "chop");
                if (actors.Length == 1 && (native == null || (native.actors?.Length == 1 && native.actors[0] == actors[0])))
                    events.Add(Event("chop", stations['K'], actors));
            }
            int normal = b.normal_completed - a.normal_completed;
            int joint = b.joint_completed - a.joint_completed;
            if (normal >= 0 && joint >= 0 && (normal > 0 || joint > 0))
            {
                int[] actors = Interactors(before, after, actions, stations['S'], "MEAL", false);
                if ((long)normal + joint == actors.Length)
                {
                    int orderId = -1;
                    // The exporter lists active/started tickets. A served ticket leaves that
                    // list even when its slot immediately receives a different order ID.
                    if (actors.Length == 1)
                    {
                        string kind = normal == 1 ? "normal" : "joint";
                        var finished = Array.FindAll(a.orders, old => old.kind == kind && old.status == "started" &&
                            !Array.Exists(b.orders, now => now.id == old.id && now.status != "served"));
                        if (finished.Length == 1) orderId = finished[0].id;
                    }
                    foreach (int id in actors)
                    {
                        var delivery = Event("meal_delivery", stations['S'], new[] { id });
                        delivery.order_id = orderId;
                        events.Add(delivery);
                    }
                }
            }
            // The hy trajectory importer also supplies verified upstream events. Borrow
            // the order ID only for an event whose station transition and actors agree.
            if (b.events != null)
                foreach (var confirmed in events)
                {
                    // Chopping advances unassigned side units. Native chop events carry no order ID.
                    if (confirmed.kind == "chop") continue;
                    string type = confirmed.kind == "assemble" ? "assemble" :
                                  confirmed.kind == "meal_delivery" ? "serve" : "unload";
                    var native = Array.Find(b.events, e => e != null && e.type == type && e.actors != null &&
                        e.actors.Length == confirmed.participants.Length &&
                        Array.TrueForAll(confirmed.participants, id => Array.IndexOf(e.actors, id) >= 0));
                    if (native != null && (confirmed.order_id < 0 || native.order_id == confirmed.order_id) &&
                        Array.Exists(a.orders, ticket => ticket.id == native.order_id && ticket.status == "started"))
                        confirmed.order_id = native.order_id;
                }
            return events.ToArray();
        }
    }
}
