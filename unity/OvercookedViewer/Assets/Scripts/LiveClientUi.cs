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
    RenderTexture menuPhoto;
    bool portraitAttempted;
    ReplayUiTheme boundTheme;
    GUIStyle field,wrapped,display,whiteScore,lightWrap,portraitCaption;
    string scoreMatch="";
    float lastScore,scoreChangedAt=-10;

    void LateUpdate() {
        if(Reviewing || ShowKitchen || portraitAttempted || Application.isBatchMode)return;
        portraitAttempted=true;
        try { menuPhoto=LiveLobbyStillLife.Photograph(); }
        catch(Exception ex){Debug.LogWarning("餐厅菜单陈列暂不可用："+ex.Message);}
    }
    void OnDisable() {
        if(menuPhoto){menuPhoto.Release();Destroy(menuPhoto);menuPhoto=null;}
        portraitAttempted=false;
    }
    void EnsureLiveStyles(ReplayUiTheme ui) {
        if(ReferenceEquals(ui,boundTheme))return;
        boundTheme=ui;
        field=new GUIStyle(ui.Row){fontSize=16,fontStyle=FontStyle.Normal,alignment=TextAnchor.MiddleLeft,
            padding=new RectOffset(12,12,3,3),clipping=TextClipping.Clip};
        wrapped=new GUIStyle(ui.Caption){wordWrap=true};
        display=new GUIStyle(ui.Title){fontSize=30};
        whiteScore=new GUIStyle(ui.OnDarkScore){fontSize=48};
        lightWrap=new GUIStyle(ui.OnDarkCaption){wordWrap=true,alignment=TextAnchor.UpperLeft};
        portraitCaption=new GUIStyle(ui.OnDark){fontStyle=FontStyle.Normal,fontSize=14};
        portraitCaption.normal.textColor=new Color(1,.97f,.87f);
    }
    internal void Draw(ReplayUiTheme ui,float height) {
        EnsureLiveStyles(ui);
        if(!ShowKitchen || Phase=="lobby"){DrawLobby(ui,height);return;}
        DrawScoreboard(ui,height);
        DrawMatch(ui,height);
    }
    static string Chef(int i)=>i==0?"大肥鱼":"企鹅";
    string MySeat() => lobby.mode=="ai_ai"?"观战席":((host?lobby.host_slot:1-lobby.host_slot)==0?"蓝方 · 大肥鱼":"橙方 · 企鹅");
    string ReadyState(int seat) {
        bool present=seat==0?lobby.connected0:lobby.connected1;
        bool ready=seat==0?lobby.ready0:lobby.ready1;
        return !present?"待加入":ready?"已准备":"未准备";
    }
    static string EndReason(string reason) {
        switch(reason) {
            case "horizon":return "对局时间已到";
            case "host_stop":return "房主提前结束了对局";
            case "player_left":return "玩家已离开房间";
            case "reconnect_timeout":return "等待重连已超时";
            case "match_error":return "对局服务发生错误";
            case "server_shutdown":return "房主已关闭服务";
            default:return reason??"";
        }
    }
    void DrawScoreboard(ReplayUiTheme ui,float height) {
        ui.Panel(ReplayUiLayout.Sidebar(height));
        ui.Label(new Rect(33,31,230,34),ReplayUiTheme.KitchenName(lobby.layout??""),ui.Title);
        ui.Label(new Rect(34,68,230,24),ModeName(lobby.mode)+"   /   "+MySeat(),ui.Caption);
        var frames=player.Data?.frames;
        if(frames!=null && frames.Length>0) {
            var frame=frames[frames.Length-1];
            if(scoreMatch!=matchId){scoreMatch=matchId;lastScore=frame.score;scoreChangedAt=-10;}
            if(frame.score>lastScore)scoreChangedAt=Time.unscaledTime;
            lastScore=frame.score;
            float accent=1-Mathf.Clamp01((Time.unscaledTime-scoreChangedAt)/.5f);
            var board=new Rect(28,105,242,108);
            ui.Panel(board,true);
            ui.Label(new Rect(45,116,198,23),"团队得分",ui.OnDarkCaption);
            var saved=GUI.contentColor;GUI.contentColor=Color.Lerp(Color.white,new Color(1,.83f,.49f),accent);
            GUI.Label(new Rect(45,140-accent*4,202,55),frame.score.ToString("0"),whiteScore);
            GUI.contentColor=saved;
            float y=236;
            ui.Label(new Rect(34,y,218,24),"当前订单",ui.Heading);y+=32;
            var orders=frame.state.all_orders;
            if(orders==null||orders.Length==0){ui.Label(new Rect(34,y,217,25),"暂无订单",ui.Caption);y+=36;}
            else {
                int available=Mathf.Max(1,Mathf.FloorToInt((height-y-200)/60));
                for(int i=0;i<Mathf.Min(available,orders.Length);i++) {
                    ui.OrderTicket(new Rect(32,y,231,54),orders[i].ingredients);y+=60;
                }
                if(orders.Length>available){ui.Label(new Rect(34,y,218,23),"另有 "+(orders.Length-available)+" 项订单",ui.Caption);y+=25;}
            }
            y+=8;ui.Rule(34,y,218);y+=13;
            for(int i=0;i<2;i++) {
                bool isAi=lobby.mode=="ai_ai" || (lobby.mode=="human_ai" && i!=lobby.host_slot);
                ui.Fill(new Rect(34,y+10,5,14),i==0?ui.BlueSeat:ui.OrangeSeat);
                ui.Label(new Rect(49,y,100,34),Chef(i),ui.Heading);
                ui.Label(new Rect(155,y,99,34),isAi?"AI 厨师":i==lobby.host_slot?"房主":"来宾",ui.Caption);y+=32;
            }
        }
        if(ui.Click(new Rect(31,height-67,234,36),"离开房间",ui.Row))LeaveRoom();
    }
    void DrawLobby(ReplayUiTheme ui,float height) {
        ui.Fill(new Rect(0,0,1280,height),ui.Mist);
        // The striped awning and a photograph of the furnished table identify the
        // lobby as the same café the players are about to enter.
        for(int i=0;i<5;i++)ui.Fill(new Rect(39+i*9,30,9,i%2==0?21:17),i%2==0?ui.Marine:ui.Gold);
        ui.Label(new Rect(98,19,290,45),"协作厨房",display);
        ui.Fill(new Rect(1030,38,7,7),welcomed?ui.Marine:ui.Gold);
        ui.Label(new Rect(1049,26,188,30),welcomed?(host?"房主已连接":"来宾已连接"):retrying?"正在重连":"局域网联机",ui.Heading);
        ui.Rule(36,79,1208);
        float top=98,cardHeight=height-top-22;
        if(!welcomed) {
            float joinHeight=Mathf.Min(cardHeight,retrying || !string.IsNullOrEmpty(error)?498:451);
            top+=(cardHeight-joinHeight)*.5f;
            cardHeight=joinHeight;
        }
        DrawLobbyPortrait(ui,new Rect(36,top,432,cardHeight));
        ui.Panel(new Rect(484,top,760,cardHeight));
        ui.Label(new Rect(512,top+18,700,47),welcomed?"开局准备":"加入局域网厨房",display);
        ui.Rule(512,top+78,704);
        float available=Mathf.Max(100,cardHeight-102);
        float content=welcomed?454:connection!=null || retrying || !string.IsNullOrEmpty(error)?365:289;
        lobbyScroll=GUI.BeginScrollView(new Rect(508,top+90,716,available),lobbyScroll,new Rect(0,0,689,content),
            false,false,GUIStyle.none,ui.ScrollTrack);
        try {
            if(welcomed)DrawRoomForm(ui);
            else DrawJoinForm(ui);
        } finally {GUI.EndScrollView();}
    }
    void DrawLobbyPortrait(ReplayUiTheme ui,Rect area) {
        ui.Panel(area,true);
        float photoHeight=Mathf.Min(area.width-4,Mathf.Max(100,area.height-(welcomed?151:connection!=null || retrying?131:80)));
        var photoRect=new Rect(area.x+2,area.y+2,area.width-4,photoHeight);
        if(menuPhoto && Event.current.type==EventType.Repaint) {
            float slice=photoHeight/photoRect.width;
            GUI.DrawTextureWithTexCoords(photoRect,menuPhoto,new Rect(0,(1-slice)*.45f,1,slice),false);
        }
        float y=photoRect.yMax+12;
        ui.Label(new Rect(area.x+22,y,area.width-44,28),welcomed?ModeName(lobby.mode):"双人餐厅",ui.OnDarkTitle);
        string detail=welcomed?ReplayUiTheme.KitchenName(lobby.layout??"")+"    /    "+MySeat():"蓝方大肥鱼   /   橙方企鹅";
        ui.Label(new Rect(area.x+22,y+31,area.width-44,24),detail,portraitCaption);
        string network=host&&lanAddresses.Length>0?string.Join("   ",lanAddresses):address+":"+portText;
        if(welcomed) {
            GUI.Label(new Rect(area.x+22,y+62,area.width-44,Mathf.Max(28,area.yMax-y-119)),network,lightWrap);
        }
        if((connection!=null||retrying)&&ui.Click(new Rect(area.x+20,area.yMax-53,144,32),"离开房间",ui.Selected))LeaveRoom();
    }
    string WriteIn(ReplayUiTheme ui,Rect rect,string key,string value,int limit) {
        GUI.SetNextControlName(key);
        var edited=GUI.TextField(rect,value,limit,field);
        bool focused=GUI.GetNameOfFocusedControl()==key;
        ui.Fill(new Rect(rect.x+2,rect.yMax-2,rect.width-4,focused?2:1),focused?ui.Gold:ui.Muted);
        return edited;
    }
    void DrawJoinForm(ReplayUiTheme ui) {
        ui.Label(new Rect(5,7,480,25),"房主地址",ui.Heading);
        ui.Label(new Rect(456,7,181,25),"端口",ui.Heading);
        address=WriteIn(ui,new Rect(5,42,435,45),"live-address",address,255);
        portText=WriteIn(ui,new Rect(456,42,177,45),"live-port",portText,5);
        ui.Label(new Rect(5,94,625,22),"可直接粘贴房主大厅显示的 IP:端口",ui.Caption);
        ui.Label(new Rect(5,139,400,24),"你的昵称",ui.Heading);
        nickname=WriteIn(ui,new Rect(5,170,435,45),"live-name",nickname,24);
        bool enabled=GUI.enabled;GUI.enabled=enabled && connection==null && !retrying;
        if(ui.Click(new Rect(5,240,245,44),retrying?"正在重连":connection!=null?"正在连接":"加入房间",ui.Primary))Connect(false);
        GUI.enabled=enabled;
        if(retrying||connection!=null||!string.IsNullOrEmpty(error))
            ui.Label(new Rect(5,294,625,63),string.IsNullOrEmpty(error)?connectionStatus:error,wrapped);
    }
    static readonly string[] LobbyModes={"human_ai","human_human","ai_ai"};
    void DrawRoomForm(ReplayUiTheme ui) {
        bool enabled=GUI.enabled,edit=enabled && host && Phase=="lobby" && !applying;
        GUI.enabled=edit;
        for(int i=0;i<LobbyModes.Length;i++) {
            string mode=LobbyModes[i];
            if(ui.Click(new Rect(4+i*221,5,210,38),ModeName(mode),draft.mode==mode?ui.Primary:ui.Selected))ChooseMode(mode);
        }
        ui.Label(new Rect(5,60,280,24),"厨房",ui.Heading);
        ui.Label(new Rect(333,60,189,24),"烹饪规则",ui.Heading);
        ui.Label(new Rect(550,60,112,24),"随机种子",ui.Heading);
        if(ui.Click(new Rect(4,91,310,34),ReplayUiTheme.KitchenName(draft.layout??"")+"  ›",ui.RowSelected)){draft.layout=Next(layouts,draft.layout);dirty=true;}
        if(ui.Click(new Rect(332,91,195,34),draft.old_dynamics?"旧规则   ›":"新规则   ›",ui.RowSelected)){draft.old_dynamics=!draft.old_dynamics;dirty=true;}
        string typed=WriteIn(ui,new Rect(548,91,110,34),"live-seed",seedText,10);
        if(typed!=seedText){seedText=typed;dirty=true;}
        ui.Label(new Rect(5,145,277,24),"厨师席位",ui.Heading);
        if(draft.mode=="human_ai" && ui.Click(new Rect(332,142,155,26),"交换人机席位",ui.Row)){draft.host_slot=1-draft.host_slot;dirty=true;}
        if(draft.mode!="human_human" && HasModel("sp-cramped-room") && ui.Click(new Rect(495,142,163,26),"上游 SP 预设",ui.Row))SetSp();
        for(int i=0;i<2;i++){
            int y=181+46*i;
            ui.Fill(new Rect(6,y+5,5,25),i==0?ui.BlueSeat:ui.OrangeSeat);
            ui.Label(new Rect(23,y,172,35),(i==0?"蓝方   ":"橙方   ")+Chef(i),ui.Heading);
            bool ai=draft.mode=="ai_ai" || (draft.mode=="human_ai" && i!=draft.host_slot);
            if(ai) {
                string id=i==0?draft.model0:draft.model1;
                if(ui.Click(new Rect(205,y,453,34),ModelLabel(id)+"  ›",ui.RowSelected)) {
                    var ids=Array.ConvertAll(models,m=>m.id);
                    if(i==0)draft.model0=Next(ids,id);else draft.model1=Next(ids,id);
                    dirty=true;
                }
            } else {
                int seat=i==draft.host_slot?0:1;
                ui.Label(new Rect(216,y,237,34),seat==0?"房主操作":"来宾操作",ui.Text);
                ui.Label(new Rect(546,y,106,34),ReadyState(seat),ui.Caption);
            }
        }
        ui.Rule(4,275,654);
        if(ui.Click(new Rect(463,290,195,32),"应用房间设置",dirty?ui.Primary:ui.Row))ApplyDraft();
        GUI.enabled=enabled;
        ui.Label(new Rect(5,288,430,35),$"{draft.horizon} 步    /    每秒 {draft.tick_rate} 步",ui.Caption);
        GUI.enabled=enabled && Phase=="lobby" && !dirty && !applying;
        bool ready=host?lobby.ready0:lobby.ready1;
        if(ui.Click(new Rect(4,343,186,42),ready?"取消准备":"准备",ready?ui.Selected:ui.Primary))Ready(!ready);
        if(host) {
            GUI.enabled=GUI.enabled&&lobby.ready0&&(lobby.mode!="human_human"||lobby.ready1);
            if(ui.Click(new Rect(203,343,186,42),"开始对局",ui.Primary))Control("start");
        }
        if(lobby.mode=="human_human") {
            GUI.enabled=enabled && Phase=="lobby" && !dirty && !applying && lobby.connected0 && lobby.connected1 && !lobby.ready0 && !lobby.ready1;
            if(ui.Click(new Rect(470,343,188,42),"交换角色",ui.Selected))Control("swap");
        }
        GUI.enabled=enabled;
        string info=!string.IsNullOrEmpty(error)?error:applying?"正在应用设置…":dirty?"应用房间设置后即可准备。":
            lobby.mode=="human_human" && !lobby.connected1?"等待另一位玩家加入房间。":ready?"已准备":Phase=="countdown"?"即将开始":"";
        ui.Label(new Rect(5,394,657,51),info,wrapped);
    }
    void DrawMatch(ReplayUiTheme ui,float height) {
        Rect panel=ReplayUiLayout.Transport(height);ui.Panel(panel);
        float y=panel.y+8;
        int remaining=Mathf.CeilToInt(Mathf.Max(0,lobby.horizon-latestTick)/Mathf.Max(1,lobby.tick_rate));
        string status=Phase=="countdown"?(lobby.warming?"加载模型":$"{SecondsLeft(lobby.countdown)} 秒后开局"):PhaseName();
        if(Phase=="reconnecting")status+="   "+SecondsLeft(lobby.reconnect_remaining)+" 秒";
        ui.Label(new Rect(329,y+1,215,24),result!=null?(result.complete?"对局完成":"提前结束"):status,ui.Heading);
        ui.Label(new Rect(329,y+27,210,35),result!=null?EndReason(result.reason):$"剩余   {remaining/60}:{remaining%60:00}",ui.Text);
        float barX=557,barWidth=230;
        ui.Label(new Rect(barX,y+2,barWidth,25),$"{Mathf.Max(0,latestTick)} / {lobby.horizon} 步",ui.Caption);
        ui.Fill(new Rect(barX,y+33,barWidth,4),ui.Mist);
        ui.Fill(new Rect(barX,y+33,barWidth*Mathf.Clamp01((float)latestTick/Mathf.Max(1,lobby.horizon)),4),ui.Gold);
        if(result!=null) {
            if(host && Local(address) && File.Exists(result.record) &&
                ui.Click(new Rect(939,y+8,135,38),"回看本局",ui.Primary))ReviewResult();
            if(host && ui.Click(new Rect(1086,y+8,140,38),"再来一局",ui.Selected))Control("reset");
            if(!host)ui.Label(new Rect(908,y+8,316,36),"记录保存在房主电脑",ui.Caption);
        } else if(Phase=="running"||Phase=="paused") {
            if(host && !string.IsNullOrEmpty(lobby.policy_fault) && ui.Click(new Rect(793,y+8,133,38),"重载模型",ui.Selected))Control("reload_model");
            if(ui.Click(new Rect(939,y+8,135,38),Phase=="paused"?"准备继续":"暂停对局",ui.Primary))Send(new LivePause{type="pause",paused=Phase!="paused"});
            if(host && ui.Click(new Rect(1086,y+8,140,38),"结束本局",ui.Row))Control("stop");
        }
        if(result==null) {
            string msg=!string.IsNullOrEmpty(error)?error:MayAct?"WASD / 方向键移动     E / 空格交互":"双模型观战";
            if(Phase=="paused")msg=!string.IsNullOrEmpty(lobby.policy_fault)?lobby.policy_fault:
                "房主 "+ReadyState(0)+(lobby.mode=="human_human"?"    来宾 "+ReadyState(1):"");
            if(panel.height>=90)ui.Label(new Rect(329,y+59,887,24),msg,ui.Caption);
        }
    }
}
}
