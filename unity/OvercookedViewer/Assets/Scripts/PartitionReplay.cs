using System;
using System.Collections.Generic;
using UnityEngine;

namespace OvercookedReplay {
[Serializable] public class PartitionConfigData {public string layout;public int horizon;public int[] deadlines;public float delivery_reward;public float[] recipe_rewards;}
[Serializable] public class PartitionState {
    public int[] tick,pos,facing,held,board,cuts,mix,locked,heat,counter,deposited,ids,recipe,issued,due,next_id;
    // Existing experiment recordings use state.v3 and did not record all fields.
    public int[] action,score;
}
[Serializable] public class PartitionStepInfo {
    public int[] recipe,order_ids,age;public bool[] delivered,expired;
    public int sync_attempts,sync_successes,wait_steps,pickups;
}
public static class PartitionReplay {
    public static readonly int[] Boards={28,56,36,60},Pots={30,34};
    public static readonly string[] Items={"","tomato","potato","egg","beef","cut_tomato","cut_potato",
        "meal_tomato_egg","meal_potato","meal_potato_tomato_beef"};
    public static readonly string[] Recipes={"柿子鸡蛋","煮马铃薯","马铃薯柿子牛肉"};
    public static bool IsDelivery(string kind)=>kind=="soup_delivery"||kind=="meal_delivery";
    public static bool IsSuccessfulAction(string kind)=>kind=="chop"||kind=="tend"||kind=="board_place"||
        kind=="board_pickup"||kind=="pot_deposit"||kind=="pot_pickup"||kind=="item_pickup"||
        kind=="item_place"||kind=="counter_put"||kind=="counter_pickup"||kind=="meal_delivery"||
        kind=="sync_handoff_give"||kind=="sync_handoff_receive";
    public static int[] XY(int c)=>new[]{c%13,c/13};
    public static string[] Terrain(string layout) {
        var rows=new[]{"#############","#     #     #","T C K H K C E","S     #     S",
            "P   C H C   B","#     #     #","#############"};
        for(int y=1;y<=5;y++){var r=rows[y].ToCharArray();r[6]=layout=="counter"?'X':(y==2||y==4?'H':'#');rows[y]=new string(r);}
        return rows;
    }
    public static void PrepareLegacy(ReplayFile d) {
        if(d?.header?.schema_version!=1||d.frames==null||d.frames.Length==0)return;
        var initial=d.frames[0]?.state;
        if(!(initial?.native_state?.pos?.Length>0)&&!(initial?.v3?.pos?.Length>0))return;
        string layout=d.header.layout_name?.IndexOf("sync",StringComparison.OrdinalIgnoreCase)>=0?"sync":"counter";
        d.header.environment="partition_v3_legacy";
        d.header.partition_config=new PartitionConfigData{layout=layout,horizon=d.header.horizon,recipe_rewards=d.header.recipe_rewards};
        d.header.terrain=Terrain(layout);
        foreach(var frame in d.frames){
            if(frame?.state==null)throw new ArgumentException("V3 旧记录中间缺少状态");
            var s=frame.state;s.partition=s.native_state?.pos?.Length>0?s.native_state:s.v3;
            s.partition.tick=new[]{s.timestep};
            CheckArrays(s.partition,false);
            s.players=Players(s.partition);s.objects=Objects(s.partition);s.all_orders=Orders(s.partition);
        }
        // These experiments saved real states/actions but empty events. Recover
        // confirmed changes in memory; never fabricate unknown IDs or wait times.
        if(d.segments!=null)for(int i=0;i<d.segments.Length;i++){
            var step=d.segments[i];var info=step.native_info;var raw=d.frames[i].state.partition;
            bool detailed=info?.recipe?.Length==3&&info.order_ids?.Length==3&&info.delivered?.Length==3&&
                info.expired?.Length==3&&raw.deposited?.Length==5;
            if(detailed)step.partition_info=info;
            step.events=ConfirmedEvents(d,i,detailed);
        }
    }
    public static ChefState[] Players(PartitionState s) {
        var players=new ChefState[3];var directions=new[]{new[]{0,-1},new[]{0,1},new[]{1,0},new[]{-1,0}};
        for(int i=0;i<3;i++)players[i]=new ChefState{position=XY(s.pos[i]),orientation=directions[s.facing[i]],
            held_object=s.held[i]==0?null:new Item{name=Items[s.held[i]],position=XY(s.pos[i])}};
        return players;
    }
    public static Item[] Objects(PartitionState s) {
        var objects=new List<Item>();
        for(int i=0;i<4;i++)if(s.board[i]!=0)objects.Add(new Item{name=Items[s.board[i]+(s.cuts[i]==2?4:0)],position=XY(Boards[i])});
        for(int i=0;i<5;i++)if(s.counter[i]!=0)objects.Add(new Item{name=Items[s.counter[i]],position=new[]{6,i+1}});
        return objects.ToArray();
    }
    public static Order[] Orders(PartitionState s) {
        var ingredients=new[]{new[]{"tomato","egg"},new[]{"potato"},new[]{"potato","tomato","beef"}};
        var orders=new Order[3];
        for(int i=0;i<3;i++)orders[i]=new Order{id=s.ids?.Length==3?s.ids[i]:-1,recipe=s.recipe[i],
            issued=s.issued?.Length==3?s.issued[i]:-1,due=s.due[i],side=s.recipe[i]==0?1:0,ingredients=ingredients[s.recipe[i]]};
        return orders;
    }
    static int[] Target(ChefState p)=>new[]{p.position[0]+p.orientation[0],p.position[1]+p.orientation[1]};
    static bool Same(int[] a,int[] b)=>a!=null&&b!=null&&a.Length==2&&b.Length==2&&a[0]==b[0]&&a[1]==b[1];
    public static ReplayEvent[] ConfirmedEvents(ReplayFile d,int index,bool full){
        var before=d.frames[index].state;var after=d.frames[index+1].state;var step=d.segments[index];
        var a=before.partition;var b=after.partition;var events=new List<ReplayEvent>();var used=new HashSet<int>();
        Need(step?.actions?.Length==3,"联合动作");
        ReplayEvent Event(string kind,int chef,int[] p)=>new ReplayEvent{kind=kind,agent_index=chef,position=p,order_id=-1,wait_steps=-1};
        for(int chef=0;chef<3;chef++){
            if(step.actions[chef]!=5||a.held[chef]==b.held[chef])continue;
            var target=Target(before.players[chef]);Need(target[0]>=0&&target[0]<13&&target[1]>=0&&target[1]<7,"交互位置");
            char tile=d.header.terrain[target[1]][target[0]];
            if(a.held[chef]!=0&&b.held[chef]==0){
                string kind=tile switch {'X'=>"counter_put",'H'=>"sync_handoff_give",'C'=>"board_place",'K'=>"pot_deposit",_=>"item_place"};
                if(tile=='S'&&a.held[chef]>=7){
                    var e=Event("meal_delivery",chef,target);e.recipe=a.held[chef]-7;
                    if(full){int slot=-1;for(int j=0;j<3;j++)if(step.partition_info.delivered[j]&&!used.Contains(j)&&step.partition_info.recipe[j]==e.recipe){slot=j;break;}
                        Need(slot>=0,"交付事件与订单不一致");used.Add(slot);e.order_id=step.partition_info.order_ids[slot];}
                    events.Add(e);
                }else events.Add(Event(kind,chef,target));
            }else if(a.held[chef]==0&&b.held[chef]!=0){
                string kind=tile switch {'X'=>"counter_pickup",'H'=>"sync_handoff_receive",'C'=>"board_pickup",'K'=>"pot_pickup",_=>"item_pickup"};
                var e=Event(kind,chef,target);
                if(tile=='X'&&full)e.wait_steps=after.timestep-a.deposited[target[1]-1];events.Add(e);
            }
        }
        for(int facility=0;facility<2;facility++){
            int[] cells=facility==0?Boards:Pots,old=facility==0?a.cuts:a.heat,next=facility==0?b.cuts:b.heat;
            for(int j=0;j<cells.Length;j++){
                if(next[j]>old[j])for(int chef=0;chef<3;chef++)
                    if(step.actions[chef]==5&&a.held[chef]==0&&Same(Target(before.players[chef]),XY(cells[j])))
                        events.Add(Event(facility==0?"chop":"tend",chef,XY(cells[j])));
                if(facility==1&&next[j]==4&&old[j]!=4)events.Add(Event("pot_ready",-1,XY(cells[j])));
            }
        }
        if(full)for(int j=0;j<3;j++)if(step.partition_info.expired[j]){
            int recipe=step.partition_info.recipe[j];var e=Event("order_expired",-1,new[]{recipe==0?12:0,3});
            e.recipe=recipe;e.order_id=step.partition_info.order_ids[j];events.Add(e);
        }
        return events.ToArray();
    }
    static void Need(bool condition,string message){if(!condition)throw new ArgumentException("V3 回放无效："+message);}
    static void ArrayRange(int[] a,int length,int min,int max,string name){
        Need(a!=null&&a.Length==length,name+" 数量");foreach(int x in a)Need(x>=min&&x<=max,name+" 范围");
    }
    static void CheckArrays(PartitionState s,bool full) {
        Need(s!=null,"缺少工位状态");ArrayRange(s.pos,3,0,90,"位置");ArrayRange(s.facing,3,0,3,"朝向");
        ArrayRange(s.held,3,0,9,"持物");ArrayRange(s.board,4,0,2,"菜板");ArrayRange(s.cuts,4,0,2,"切配进度");
        ArrayRange(s.mix,2,0,15,"锅内配料");ArrayRange(s.heat,2,0,4,"看锅进度");
        ArrayRange(s.counter,5,0,9,"台面存货");ArrayRange(s.recipe,3,0,2,"菜谱");ArrayRange(s.due,3,0,int.MaxValue,"期限");
        if(full){ArrayRange(s.locked,2,-1,2,"锁定菜谱");ArrayRange(s.ids,3,0,int.MaxValue,"订单编号");
            ArrayRange(s.issued,3,0,int.MaxValue,"派单时间");ArrayRange(s.deposited,5,0,int.MaxValue,"放置时间");
            ArrayRange(s.tick,1,0,int.MaxValue,"步数");ArrayRange(s.next_id,1,0,int.MaxValue,"后续订单编号");}
    }
    public static ReplayFile Validate(ReplayFile d) {
        bool full=d.header.environment=="partition_v3";
        Need(d.header.schema_version==(full?2:1),"格式版本");
        Need(d.frames!=null&&d.segments!=null&&d.frames.Length==d.segments.Length+1&&d.frames.Length>0,"T+1 帧");
        var cfg=d.header.partition_config;Need(cfg!=null&&(cfg.layout=="sync"||cfg.layout=="counter"),"地图配置");
        Need(d.header.horizon>0&&cfg.horizon==d.header.horizon,"局长");
        if(full){ArrayRange(cfg.deadlines,3,1,int.MaxValue,"订单时限");
            if(cfg.recipe_rewards?.Length>0){Need(cfg.recipe_rewards.Length==3,"分菜谱奖励");foreach(float r in cfg.recipe_rewards)Need(float.IsFinite(r)&&r>=0,"分菜谱奖励");}
            else Need(float.IsFinite(cfg.delivery_reward)&&cfg.delivery_reward>=0,"交付分");}
        Need(d.header.players?.Length==3,"三位厨师");var terrain=Terrain(cfg.layout);
        Need(d.header.terrain?.Length==7,"地图大小");
        for(int y=0;y<7;y++)Need(d.header.terrain[y]==terrain[y],"设施位置");
        float time=-1,score=0;
        for(int i=0;i<d.frames.Length;i++){
            var f=d.frames[i];Need(f!=null&&float.IsFinite(f.time)&&f.time>=0&&f.time>time&&float.IsFinite(f.score),"时间和分数");time=f.time;
            Need(f.state!=null,"缺少帧状态");var s=f.state;CheckArrays(s.partition,full);
            Need(s.timestep>=0&&s.timestep<=d.header.horizon,"环境步数");
            if(full)Need(s.partition.tick[0]==s.timestep,"步数不一致");
            Need(s.players?.Length==3&&s.objects!=null&&s.all_orders?.Length==3,"状态投影");
            var expected=Players(s.partition);var occupied=new HashSet<int>();
            for(int c=0;c<3;c++){
                var p=s.players[c];var q=expected[c];Need(p?.position?.Length==2&&p.orientation?.Length==2,"厨师状态");
                int cell=s.partition.pos[c];Need(terrain[cell/13][cell%13]==' '&&occupied.Add(cell),"厨师位置");
                Need(p.position[0]==q.position[0]&&p.position[1]==q.position[1]&&p.orientation[0]==q.orientation[0]&&
                     p.orientation[1]==q.orientation[1]&&p.held_object?.name==q.held_object?.name,"人物投影");
            }
            var projected=Objects(s.partition);Need(s.objects.Length==projected.Length,"物品投影");
            for(int j=0;j<projected.Length;j++)Need(s.objects[j]?.name==projected[j].name&&
                s.objects[j].position?.Length==2&&s.objects[j].position[0]==projected[j].position[0]&&
                s.objects[j].position[1]==projected[j].position[1],"物品状态");
            var orders=Orders(s.partition);
            for(int j=0;j<3;j++){var o=s.all_orders[j];Need(o!=null&&o.recipe==orders[j].recipe&&o.due==orders[j].due&&
                o.side==orders[j].side&&o.id==orders[j].id,"订单投影");}
            if(i>0){var step=d.segments[i-1];Need(step?.kind=="step"&&step.actions?.Length==3,"联合动作");
                foreach(int a in step.actions)Need(a>=0&&a<6,"动作范围");
                Need(s.timestep==d.frames[i-1].state.timestep+1&&float.IsFinite(step.sparse_reward),"连续步数");
                double scoreTolerance=Math.Max(.001,Math.Max(Math.Abs(f.score),Math.Abs(score))*1e-6);
                Need(Math.Abs((double)f.score-score-step.sparse_reward)<=scoreTolerance,"累积分数");
                if(full){var info=step.partition_info;Need(info?.delivered?.Length==3&&info.expired?.Length==3&&info.order_ids?.Length==3&&info.recipe?.Length==3&&info.age?.Length==3,"结算信息");
                    float expectedReward=0;for(int j=0;j<3;j++){var prior=d.frames[i-1].state.partition;
                        Need(info.order_ids[j]==prior.ids[j]&&info.recipe[j]==prior.recipe[j],"旧订单编号");
                        if(info.delivered[j])expectedReward+=cfg.recipe_rewards?.Length==3?cfg.recipe_rewards[info.recipe[j]]:cfg.delivery_reward;
                        Need(info.age[j]==s.timestep-prior.issued[j]&&!(info.delivered[j]&&info.expired[j]),"订单结算");}
                    Need(info.sync_attempts>=0&&info.sync_successes>=0&&info.sync_successes<=info.sync_attempts&&info.wait_steps>=0&&info.pickups>=0,"交接统计");
                    Need(Math.Abs(step.sparse_reward-expectedReward)<=Math.Max(.001,Math.Abs(step.sparse_reward)*1e-6),"交付奖励");
                    var confirmed=ConfirmedEvents(d,i-1,true);Need(step.events!=null&&step.events.Length==confirmed.Length,"事件数量");
                    for(int e=0;e<confirmed.Length;e++){
                        var actual=step.events[e];var expectedEvent=confirmed[e];Need(actual!=null&&actual.kind==expectedEvent.kind&&
                            actual.agent_index==expectedEvent.agent_index&&Same(actual.position,expectedEvent.position),"真实操作事件");
                        if(actual.kind=="meal_delivery"||actual.kind=="order_expired")Need(actual.order_id==expectedEvent.order_id&&actual.recipe==expectedEvent.recipe,"结算事件");
                        if(actual.kind=="counter_pickup")Need(actual.wait_steps==expectedEvent.wait_steps,"台面等待时间");
                    }
                }
                if(step.sync_handoffs!=null)foreach(var handoff in step.sync_handoffs){
                    Need(handoff?.station?.Length==2&&handoff.station[0]==6&&(handoff.station[1]==2||handoff.station[1]==4)&&cfg.layout=="sync","同步口位置");
                    if(handoff.success)Need(handoff.sender>=0&&handoff.sender<3&&handoff.receiver>=0&&handoff.receiver<3&&handoff.sender!=handoff.receiver,"交接厨师");}
            }
            score=f.score;
        }
        Need(!d.header.complete||d.frames[d.frames.Length-1].state.timestep==d.header.horizon,"未结束的对局标成完整");
        return d;
    }
}
}
