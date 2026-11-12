using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OvercookedReplay
{
    [Serializable] public class PlayerMeta { public string id; public bool is_human; }
    [Serializable] public class ReplayHeader {
        public int schema_version, horizon; public string layout_name, dynamics, source, mdp_params_json;
        public string[] terrain; public bool complete; public PlayerMeta[] players;
    }
    [Serializable] public class Ingredient { public string name; public int[] position; }
    [Serializable] public class Item { public string name; public int[] position; public Ingredient[] _ingredients;
        public float cooking_tick, cook_time; public bool is_idle, is_ready, is_cooking; }
    [Serializable] public class ChefState { public int[] position, orientation; public Item held_object; }
    [Serializable] public class Order { public string[] ingredients; }
    [Serializable] public class Snapshot { public int timestep; public ChefState[] players; public Item[] objects; public Order[] all_orders; }
    [Serializable] public class Frame { public float time, score; public Snapshot state; }
    [Serializable] public class ReplayEvent { public string kind; public int agent_index; public int[] position; }
    [Serializable] public class SyncHandoff {
        public int[] station, participants, attempting_agents;
        public bool success; public string reason; public int sender, receiver;
    }
    [Serializable] public class Segment { public string kind; public int[] actions; public float sparse_reward; public bool reward_known;
        public ReplayEvent[] events; public SyncHandoff[] sync_handoffs; }
    [Serializable] public class ReplayFile {
        public ReplayHeader header; public Frame[] frames; public Segment[] segments; public string[] diagnostics;
        public static ReplayFile Load(string path) { return Parse(File.ReadAllText(path)); }
        public static ReplayFile Parse(string json) {
            return Validate(JsonUtility.FromJson<ReplayFile>(json));
        }
        internal static ReplayFile Validate(ReplayFile d) {
            if(d==null || d.header==null || d.header.schema_version!=1) throw new ArgumentException("不支持的回放文件版本");
            if(d.frames==null || d.frames.Length==0 || d.segments==null || d.frames.Length!=d.segments.Length+1)
                throw new ArgumentException("回放状态与步骤数量不一致");
            var rows=d.header.terrain;
            if(rows==null || rows.Length==0 || rows[0].Length==0) throw new ArgumentException("回放缺少地图");
            foreach(var row in rows) {
                if(row.Length!=rows[0].Length) throw new ArgumentException("地图行长度不一致");
                foreach(char tile in row) if(" XOTDPSH".IndexOf(tile)<0) throw new ArgumentException("未知地图地块："+tile);
            }
            int playerCount=d.header.players?.Length??0;
            if(playerCount!=2 && playerCount!=3) throw new ArgumentException("回放需要两位或三位玩家");
            bool OnTile(int[] p,char tile) => p!=null && p.Length==2 && p[0]>=0 && p[0]<rows[0].Length &&
                p[1]>=0 && p[1]<rows.Length && rows[p[1]][p[0]]==tile;
            float previous=-1;
            for(int i=0;i<d.frames.Length;i++) {
                var f=d.frames[i];
                if(f==null || !float.IsFinite(f.time) || f.time<0 || f.time<previous || !float.IsFinite(f.score))
                    throw new ArgumentException("回放时间或分数无效："+i);
                previous=f.time;
                if(f.state?.players?.Length!=playerCount) throw new ArgumentException("玩家状态数量与名单不一致："+i);
                var occupied=new HashSet<Vector2Int>();
                foreach(var p in f.state.players) {
                    if(!OnTile(p?.position,' ') || !occupied.Add(new Vector2Int(p.position[0],p.position[1])))
                        throw new ArgumentException("玩家位置无效："+i);
                    if(p.orientation==null || p.orientation.Length!=2 || Math.Abs(p.orientation[0])+Math.Abs(p.orientation[1])!=1)
                        throw new ArgumentException("玩家朝向无效："+i);
                }
                if(i<d.segments.Length) {
                    var s=d.segments[i];
                    if(s.kind!="gap" && s.kind!="step") throw new ArgumentException("未知步骤类型："+i);
                    if(s.kind=="step") {
                        if(s.actions?.Length!=playerCount) throw new ArgumentException("动作数量与玩家人数不一致："+i);
                        foreach(int a in s.actions) if(a<0 || a>5) throw new ArgumentException("动作越界："+i);
                        if(s.sync_handoffs!=null) foreach(var handoff in s.sync_handoffs) {
                            if(!OnTile(handoff?.station,'H')) throw new ArgumentException("同步对接台位置无效："+i);
                            if(handoff.success && (handoff.sender<0 || handoff.sender>=playerCount || handoff.receiver<0 ||
                                handoff.receiver>=playerCount || handoff.sender==handoff.receiver))
                                throw new ArgumentException("同步对接玩家编号无效："+i);
                        }
                    } else if(s.actions!=null || s.reward_known || (s.sync_handoffs?.Length??0)>0)
                        throw new ArgumentException("缺帧区间不能声明已知动作或对接事件");
                }
            }
            return d;
        }
    }
    public class ReplayClock {
        readonly ReplayFile data; public int Index {get;private set;} public float Time {get;private set;}
        public bool Playing; public float Speed=1;
        public float Duration => data.frames[data.frames.Length-1].time;
        public ReplayClock(ReplayFile replay) {data=replay;}
        public void SeekFrame(int index) {Index=Mathf.Clamp(index,0,data.frames.Length-1);Time=data.frames[Index].time;}
        public void Seek(float time) {Time=Mathf.Clamp(time,0,Duration);Index=0;
            while(Index+1<data.frames.Length && data.frames[Index+1].time<=Time) Index++;}
        public void Advance(float seconds) {if(!Playing)return;Seek(Time+Mathf.Max(0,seconds)*Speed);if(Time>=Duration)Playing=false;}
        public float Alpha {get {if(Index>=data.segments.Length || data.segments[Index].kind=="gap")return 0;
            float dt=data.frames[Index+1].time-data.frames[Index].time;return dt<=0?0:Mathf.Clamp01((Time-data.frames[Index].time)/dt);}}
    }
}
