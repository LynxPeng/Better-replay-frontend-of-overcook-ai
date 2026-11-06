using System;
using System.IO;
using UnityEngine;

namespace OvercookedReplay {
public sealed partial class LiveClient {
    Vector2 lobbyScroll;
    static string ModeName(string mode) {
        switch(mode){case "ai_ai":return "机机观战";case "human_human":return "人人联机";default:return "人机合作";}
    }
    string PhaseName() {
        if(retrying)return "正在重连";
        if(!welcomed)return "连接房间";
        switch(Phase) {
            case "running":return "对局中";case "paused":return "已暂停";
            case "countdown":return "即将开始";case "reconnecting":return "等待重连";
            case "ended":return "本局结束";default:return "房间大厅";
        }
    }
    bool HasModel(string id)=>Array.Exists(models,m=>m.id==id);
    string ModelLabel(string id) {
        if(id=="greedy")return "Greedy · 规则队友";
        var model=Array.Find(models,m=>m.id==id);
        return model==null?id:model.label??id;
    }
    static string Next(string[] choices,string current) {
        if(choices.Length==0)return current;
        return choices[(Array.IndexOf(choices,current)+1)%choices.Length];
    }
    void ChooseMode(string mode) {
        if(mode==draft.mode)return;
        draft.mode=mode;dirty=true;
        if(mode=="ai_ai") {
            if(HasModel("ppo-final")){draft.model0=draft.model1="ppo-final";draft.old_dynamics=false;draft.layout="cramped_room";}
            else if(HasModel("sp-cramped-room"))SetSp();
        }
    }
    void SetSp() {
        draft.layout="cramped_room";draft.old_dynamics=true;draft.model0=draft.model1="sp-cramped-room";dirty=true;
    }
    void ApplyDraft(){
        if(smoke==null && (!int.TryParse(seedText,out draft.seed)||draft.seed<0)){error="种子须为 0 到 2147483647 之间的整数";return;}
        Send(draft);dirty=true;applying=true;error="";
    }
    void Ready(bool value){Send(new LiveReady{type="ready",ready=value});}
    void LeaveRoom() {
        LiveResume.Forget(seatKey);
        if(welcomed)connection?.Leave();else connection?.Dispose();
        connection=null;welcomed=false;slot=-1;retrying=false;resumeToken=null;hostToken=null;
        result=null;latestTick=-1;lobby=new LiveMessage();ResetInput();Reviewing=false;error="";
        if(player.Presentation!=null)player.Presentation.Pause();
        connectionStatus="已离开房间";
    }
    internal void EndReview(){player.Presentation?.Pause();Reviewing=false;}
    int SecondsLeft(float atReceipt)=>Mathf.CeilToInt(Mathf.Max(0,atReceipt-(Time.realtimeSinceStartup-lobbyReceivedAt)));
    void ReviewResult() {
        string exe=Environment.GetEnvironmentVariable("BETTER_REPLAY_REPLAY_EXE");
        if(File.Exists(exe) && !result.record.Contains("\"")) {
            try {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe,
                    "--replay \""+Path.GetFullPath(result.record)+"\""){UseShellExecute=false});
                return;
            }catch(Exception ex){Debug.LogWarning("无法启动回放程序："+ex.Message);}
        }
        player.Load(result.record);Reviewing=true;
    }
    internal void Draw(ReplayUiTheme ui,float height) {
        ui.Panel(ReplayUiLayout.Sidebar(height));
        ui.Label(new Rect(32,34,226,36),"协作厨房",ui.Title);
        ui.Label(new Rect(32,77,226,22),PhaseName(),ui.Heading);
        ui.Rule(32,117,231);
        if(welcomed) {
            ui.Label(new Rect(32,138,230,28),ModeName(lobby.mode),ui.Heading);
            ui.Label(new Rect(32,174,230,22),host?"房主":"来宾",ui.Caption);
            string role=lobby.mode=="ai_ai"?"观战":((host?lobby.host_slot:1-lobby.host_slot)==0?"蓝方 · 大肥鱼":"橙方 · 企鹅");
            ui.Label(new Rect(32,202,230,26),role,ui.Heading);
            ui.Label(new Rect(32,245,224,26),ReplayUiTheme.KitchenName(lobby.layout??""),ui.Heading);
            ui.Label(new Rect(32,280,224,24),$"{lobby.tick_rate:0.#} 步/秒 · {lobby.horizon} 步",ui.Caption);
            if(latestTick>=0&&player.Data!=null) {
                var frames=player.Data.frames;var frame=frames[frames.Length-1];
                ui.Rule(32,325,230);ui.Label(new Rect(32,342,100,26),"团队得分",ui.Heading);
                ui.Label(new Rect(32,372,220,58),frame.score.ToString("0"),ui.Score);
                ui.Label(new Rect(32,440,225,24),$"第 {latestTick} / {lobby.horizon} 步");
                var orders=frame.state.all_orders;
                if(orders!=null && orders.Length>0) {
                    ui.Label(new Rect(32,479,220,20),"订单",ui.Caption);
                    string[] ingredients=orders[0].ingredients??Array.Empty<string>();
                    ui.Label(new Rect(32,506,225,28),string.Join(" + ",Array.ConvertAll(ingredients,i=>i=="onion"?"洋葱":"番茄")));
                }
            }
        } else ui.Label(new Rect(32,142,220,23),retrying?"保持窗口开启即可重连":"局域网合作",ui.Heading);
        if((connection!=null||retrying)&&ui.Click(new Rect(32,height-64,222,36),"离开房间"))LeaveRoom();
        if(!ShowKitchen || Phase=="lobby")DrawLobby(ui,height);
        else DrawMatch(ui,height);
    }
    static GUIStyle Wrap(GUIStyle style){var copy=new GUIStyle(style);copy.wordWrap=true;return copy;}
    void DrawLobby(ReplayUiTheme ui,float height) {
        var panel=new Rect(310,18,950,height-36);ui.Panel(panel);
        ui.Label(new Rect(338,40,560,40),welcomed?"房间大厅":"加入局域网厨房",ui.Title);
        if(!welcomed) {
            ui.Label(new Rect(338,106,395,26),"房主地址（可粘贴 IP:端口）",ui.Heading);
            address=GUI.TextField(new Rect(338,140,395,34),address,255,FieldStyle(ui));
            ui.Label(new Rect(750,106,140,26),"端口",ui.Heading);
            portText=GUI.TextField(new Rect(750,140,105,34),portText,5,FieldStyle(ui));
            ui.Label(new Rect(338,194,130,26),"昵称",ui.Heading);
            nickname=GUI.TextField(new Rect(338,225,282,34),nickname,24,FieldStyle(ui));
            bool enabled=GUI.enabled;GUI.enabled=enabled && connection==null && !retrying;
            if(ui.Click(new Rect(338,286,210,40),"加入房间",ui.Primary))Connect(false);
            GUI.enabled=enabled;
            ui.Label(new Rect(338,347,785,60),connectionStatus,Wrap(ui.Heading));
            ui.Label(new Rect(338,423,815,60),error,Wrap(ui.Caption));return;
        }
        lobbyScroll=GUI.BeginScrollView(new Rect(328,91,910,height-135),lobbyScroll,new Rect(0,0,865,540));
        try {
            string seats=$"房主 · {(lobby.ready0?"已准备":"未准备")}";
            if(lobby.mode=="human_human")seats+=lobby.connected1?(lobby.ready1?"     来宾 · 已准备":"     来宾 · 未准备"):"     等待来宾加入";
            ui.Label(new Rect(10,0,840,32),seats,ui.Heading);
            string network=host&&lanAddresses.Length>0?string.Join("   ",lanAddresses):address+":"+portText;
            ui.Label(new Rect(10,35,840,24),network,ui.Caption);
            bool enabled=GUI.enabled;
            GUI.enabled=enabled && host && Phase=="lobby" && !applying;
            string[] modes={"human_ai","human_human","ai_ai"};
            for(int i=0;i<modes.Length;i++)if(ui.Click(new Rect(10+i*178,75,170,36),ModeName(modes[i]),draft.mode==modes[i]?ui.Selected:ui.Button))ChooseMode(modes[i]);
            ui.Label(new Rect(10,133,108,30),"厨房",ui.Heading);
            if(ui.Click(new Rect(112,130,268,34),ReplayUiTheme.KitchenName(draft.layout??"")+"  ›")){draft.layout=Next(layouts,draft.layout);dirty=true;}
            if(ui.Click(new Rect(394,130,157,34),draft.old_dynamics?"旧烹饪规则  ›":"新烹饪规则  ›")){draft.old_dynamics=!draft.old_dynamics;dirty=true;}
            if(draft.mode=="human_ai" && ui.Click(new Rect(570,130,205,34),draft.host_slot==0?"房主席位 · 蓝方  ›":"房主席位 · 橙方  ›")){draft.host_slot=1-draft.host_slot;dirty=true;}
            else if(draft.mode=="human_human")ui.Label(new Rect(570,130,205,34),draft.host_slot==0?"房主席位 · 蓝方":"房主席位 · 橙方");
            string[] ids=Array.ConvertAll(models,m=>m.id);
            for(int i=0;i<2;i++) {
                int y=185+48*i;ui.Label(new Rect(10,y,99,30),i==0?"蓝方":"橙方",ui.Heading);
                bool ai=draft.mode=="ai_ai" || (draft.mode=="human_ai" && i!=draft.host_slot);
                if(ai) {
                    string id=i==0?draft.model0:draft.model1;
                    if(ui.Click(new Rect(112,y,439,33),ModelLabel(id)+"  ›")){
                        if(i==0)draft.model0=Next(ids,id);else draft.model1=Next(ids,id);dirty=true;
                    }
                } else ui.Label(new Rect(124,y,419,32),i==draft.host_slot?"房主操作":"局域网来宾操作");
            }
            if(HasModel("sp-cramped-room") && ui.Click(new Rect(570,185,205,33),"上游 SP 预设"))SetSp();
            ui.Label(new Rect(10,288,100,30),"随机种子",ui.Heading);
            string typed=GUI.TextField(new Rect(112,288,160,33),seedText,10,FieldStyle(ui));
            if(typed!=seedText){seedText=typed;dirty=true;}
            ui.Label(new Rect(300,288,245,33),$"{draft.horizon} 步   ·   每秒 {draft.tick_rate} 步",ui.Caption);
            if(ui.Click(new Rect(570,288,205,33),dirty?"应用房间设置":"应用设置",dirty?ui.Primary:ui.Row))ApplyDraft();
            GUI.enabled=enabled&&Phase=="lobby"&&!dirty;
            bool ready=host?lobby.ready0:lobby.ready1;
            if(ui.Click(new Rect(10,359,170,40),ready?"取消准备":"准备",ready?ui.Selected:ui.Primary))Ready(!ready);
            if(host) {
                GUI.enabled=GUI.enabled && lobby.ready0 && (lobby.mode!="human_human"||lobby.ready1);
                if(ui.Click(new Rect(192,359,170,40),"开始对局",ui.Primary))Control("start");
            }
            if(lobby.mode=="human_human") {
                GUI.enabled=enabled && Phase=="lobby" && !dirty && !applying && lobby.connected0 && lobby.connected1 && !lobby.ready0 && !lobby.ready1;
                if(ui.Click(new Rect(374,359,170,40),"交换角色"))Control("swap");
            }
            GUI.enabled=enabled;
            string info=Phase=="countdown"?$"即将开始 · {SecondsLeft(lobby.countdown)}":applying?"房主正在应用设置…":dirty?"先应用房间设置，再准备开始":"";
            ui.Label(new Rect(10,416,835,28),info,ui.Heading);
            if(error!="")ui.Label(new Rect(10,455,835,65),error,Wrap(ui.Caption));
        } finally {GUI.EndScrollView();}
    }
    static GUIStyle FieldStyle(ReplayUiTheme ui) {
        var style=new GUIStyle(GUI.skin.textField){font=ui.Text.font,fontSize=15,alignment=TextAnchor.MiddleLeft};
        return style;
    }
    void DrawMatch(ReplayUiTheme ui,float height) {
        Rect panel=ReplayUiLayout.Transport(height);ui.Panel(panel);
        float y=panel.y+10;
        int remaining=Mathf.CeilToInt(Mathf.Max(0,lobby.horizon-latestTick)/Mathf.Max(1,lobby.tick_rate));
        string status=Phase=="countdown"?(lobby.warming?"加载模型中":$"{SecondsLeft(lobby.countdown)} 秒后开始"):PhaseName();
        string label=ModeName(lobby.mode)+" · "+status+$"      剩余 {remaining/60}:{remaining%60:00}";
        if(Phase=="reconnecting")label+="  "+SecondsLeft(lobby.reconnect_remaining)+" 秒";
        ui.Label(new Rect(326,y,521,30),label,ui.Heading);
        if(result!=null) {
            ui.Label(new Rect(326,y+32,780,42),(result.complete?"已完成":"提前结束")+"   "+result.reason+(host?"":"   ·   记录保存在房主电脑"),ui.Caption);
            if(host&&Local(address)&&File.Exists(result.record)&&ui.Click(new Rect(932,y,135,35),"回看本局",ui.Primary)){
                ReviewResult();
            }
            if(host&&ui.Click(new Rect(1080,y,140,35),"再来一局"))Control("reset");
        }else{
            string message=error!=""?error:MayAct?"WASD / 方向键移动     空格 / E 交互":"双方 AI 正在合作";
            if(Phase=="paused")message=string.IsNullOrEmpty(lobby.policy_fault)?
                $"房主{(lobby.ready0?"已准备":"未准备")}     来宾{(lobby.ready1?"已准备":"未准备")}":lobby.policy_fault;
            ui.Label(new Rect(326,y+34,770,31),message,ui.Caption);
            if(Phase=="running"||Phase=="paused") {
                if(host&&!string.IsNullOrEmpty(lobby.policy_fault)&&ui.Click(new Rect(775,y,158,35),"重载模型"))Control("reload_model");
                if(ui.Click(new Rect(944,y,135,35),Phase=="paused"?"准备继续":"暂停对局"))Send(new LivePause{type="pause",paused=Phase!="paused"});
                if(host && ui.Click(new Rect(1091,y,135,35),"结束本局"))Control("stop");
            }
        }
    }
}
}
