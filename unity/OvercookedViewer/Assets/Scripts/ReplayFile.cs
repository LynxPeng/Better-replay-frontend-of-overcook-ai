using System;
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
    [Serializable] public class Segment { public string kind; public int[] actions; public float sparse_reward; public bool reward_known; public ReplayEvent[] events; }
    [Serializable] public class ReplayFile {
        public ReplayHeader header; public Frame[] frames; public Segment[] segments; public string[] diagnostics;
        public static ReplayFile Load(string path) { return Parse(File.ReadAllText(path)); }
        public static ReplayFile Parse(string json) {
            var d=JsonUtility.FromJson<ReplayFile>(json);
            if(d==null || d.header==null || d.header.schema_version!=1) throw new ArgumentException("不支持的回放文件版本");
            if(d.frames==null || d.frames.Length==0 || d.segments==null || d.frames.Length!=d.segments.Length+1)
                throw new ArgumentException("回放状态与步骤数量不一致");
            var rows=d.header.terrain;
            if(rows==null || rows.Length==0 || rows[0].Length==0) throw new ArgumentException("回放缺少地图");
            foreach(var row in rows) if(row.Length!=rows[0].Length) throw new ArgumentException("地图行长度不一致");
            float previous=-1;
            for(int i=0;i<d.frames.Length;i++) {
                var f=d.frames[i];
                if(f==null || !float.IsFinite(f.time) || f.time<0 || f.time<previous || !float.IsFinite(f.score))
                    throw new ArgumentException("回放时间或分数无效："+i);
                previous=f.time;
                if(f.state==null || f.state.players==null || f.state.players.Length!=2) throw new ArgumentException("需要双人状态："+i);
                foreach(var p in f.state.players) {
                    if(p.position==null || p.position.Length!=2 || p.position[0]<0 || p.position[0]>=rows[0].Length || p.position[1]<0 || p.position[1]>=rows.Length)
                        throw new ArgumentException("玩家位置无效："+i);
                    if(p.orientation==null || p.orientation.Length!=2 || Math.Abs(p.orientation[0])+Math.Abs(p.orientation[1])!=1)
                        throw new ArgumentException("玩家朝向无效："+i);
                }
                if(i<d.segments.Length) {
                    var s=d.segments[i];
                    if(s.kind!="gap" && s.kind!="step") throw new ArgumentException("未知步骤类型："+i);
                    if(s.kind=="step") {
                        if(s.actions==null || s.actions.Length!=2) throw new ArgumentException("缺少双方动作："+i);
                        foreach(int a in s.actions) if(a<0 || a>5) throw new ArgumentException("动作越界："+i);
                    } else if(s.actions!=null || s.reward_known) throw new ArgumentException("缺帧区间不能声明已知动作");
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
