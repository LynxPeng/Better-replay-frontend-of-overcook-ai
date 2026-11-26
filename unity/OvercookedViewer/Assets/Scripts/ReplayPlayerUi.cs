using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OvercookedReplay {
public partial class ReplayPlayer {
    ReplayUiTheme ui,dayUi,nightUi;Vector2 sidebarScroll;bool kitchenListOpen;string loadedName="尚未打开回放";
    ReplayFile markedReplay;
    readonly List<float> deliveryTimes=new List<float>();
    static readonly float[] PlaybackSpeeds={.25f,.5f,1,2,4};
    void OnDestroy(){dayUi?.Dispose();nightUi?.Dispose();}
    static readonly string[] ChefNames={"蓝方","橙方","绿方"};
    static readonly string[] ChefDescriptions={"蓝方   大肥鱼","橙方   企鹅","绿方   厨师"};
    static string HandoffStatus(SyncHandoff handoff) {
        if(handoff.success)return $"{ChefNames[handoff.sender]} → {ChefNames[handoff.receiver]}";
        return handoff.reason switch {
            "waiting_for_partner"=>"等待搭档",
            "not_simultaneous"=>"未同时交互",
            "both_empty"=>"双方都空手",
            "both_holding"=>"双方都持有物品",
            _=>"交接未完成"
        };
    }
    void OpenReplay() {
        if(Presentation!=null)Presentation.Pause();CancelCameraDrag();
        try {var path=NativeDialog.Open();if(path!=null)Load(path);}
        catch(Exception ex){error="无法打开文件选择器："+ex.Message;Debug.LogException(ex);}
    }
    void OpenSettings(){if(!IsLive && Presentation!=null)Presentation.Pause();CancelCameraDrag();settings.Show(preferences);}
    void OnGUI() {
        bool nightSkin=preferences.night && (!IsLive || live.ShowKitchen);
        ui=nightSkin?(nightUi??=new ReplayUiTheme(true)):(dayUi??=new ReplayUiTheme());
        var previous=GUI.matrix;bool enabled=GUI.enabled;float scale=ReplayUiLayout.Scale(Screen.width),height=Screen.height/scale;
        GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
        try {
            ui.Fill(new Rect(0,0,294.4f,height),ui.Mist);
            ui.Fill(new Rect(294.4f,height*.83f,985.6f,height*.17f),ui.Mist);
            GUI.enabled=!settings.Open;
            if(IsLive)live.Draw(ui,height);
            else {Sidebar(height);if(Clock!=null)Transport(height);}
            if(!IsLive||live.ShowKitchen)Toolbar();
            if(Clock!=null&&sharedKitchen!=null)SharedOverlay(scale);
            else if(labels&&Clock!=null&&(!IsLive||live.ShowKitchen))Names(scale);
            if(labels&&Clock!=null&&Data.header.IsPartition)PartitionLabels(scale);
            if(live && live.Reviewing && ui.Click(new Rect(310,84,110,30),"返回联机房间"))live.EndReview();
            GUI.enabled=true;settings.Draw(ui,height,p=>ApplyPreferences(p,true));
        }finally{GUI.matrix=previous;GUI.enabled=enabled;}
    }
    void Sidebar(float height) {
        ui.Panel(ReplayUiLayout.Sidebar(height));
        var mast=new Rect(14,14,270,148);
        ui.Panel(mast,dark:true);
        ui.Label(new Rect(32,26,175,39),"厨房回放",ui.OnDarkTitle);
        ui.ServiceBell(new Rect(227,28,38,36));
        ui.Fill(new Rect(32,73,234,1),ReplayUiTheme.Hex("#547761"));
        ui.Label(new Rect(32,82,98,20),"团队得分",ui.OnDarkCaption);
        ui.Label(new Rect(32,101,128,48),Clock==null?"—":Data.frames[Clock.Index].score.ToString(sharedKitchen!=null?"0.##":"0"),ui.OnDarkScore);
        ui.Label(new Rect(175,83,92,20),"当前步数",ui.OnDarkCaption);
        string currentStep=Clock==null?"—":$"{Clock.Index} / {Data.frames.Length-1}";
        if(sharedKitchen!=null)currentStep=$"{Data.frames[Clock.Index].state.timestep} / {Data.header.horizon}";
        ui.Label(new Rect(175,108,95,34),currentStep,ui.OnDark);
        if(ui.Click(new Rect(32,178,234,36),"打开本地回放",ui.Primary))OpenReplay();
        string fileLabel=loadedName.Length>31?loadedName.Substring(0,15)+"…"+loadedName.Substring(loadedName.Length-15):loadedName;
        GUI.Label(new Rect(33,217,232,24),new GUIContent(fileLabel,loadedName),ui.Caption);
        ui.Perforation(32,250,234);
        var orders=Clock!=null?Data.frames[Clock.Index].state.all_orders:null;
        var shared=Clock!=null&&sharedKitchen!=null?Data.frames[Clock.Index].state.shared:null;
        int orderCount=shared!=null?shared.orders.Length:(orders?.Length??0);
        var handoffs=Clock!=null?LastHandoffs:null;
        float contentHeight=235+(kitchenListOpen?(samples?.Length??0)*30:0)+Mathf.Max(1,orderCount)*54+(error!=""?110:0)+(Data!=null&&!Data.header.complete?35:0);
        if(shared!=null)contentHeight+=100;
        if(Data?.header.players.Length==3)contentHeight+=33;
        if(Data?.header.IsPartition==true)contentHeight+=180;
        contentHeight+=(handoffs?.Length??0)*29;
        sidebarScroll=GUI.BeginScrollView(new Rect(30,264,242,Mathf.Max(60,height-335)),sidebarScroll,new Rect(0,0,225,contentHeight),false,false,GUIStyle.none,ui.ScrollTrack);
        float y=0;
        ui.Label(new Rect(2,y,220,22),"厨房选择",ui.Heading);y+=28;
        string selected=Data==null?"选择一个厨房":shared!=null?"共享厨房":ReplayUiTheme.KitchenName(Data.header.layout_name);
        if(ui.Click(new Rect(0,y,224,36),selected+(kitchenListOpen?"  ▴":"  ▾"),ui.RowSelected))kitchenListOpen=!kitchenListOpen;
        y+=40;
        if(kitchenListOpen&&samples!=null)foreach(var path in samples) {
            var name=Path.GetFileName(path).Replace(".ocrep.json","");
            if(ui.Click(new Rect(0,y,224,28),ReplayUiTheme.KitchenName(name),Data?.header.layout_name==name?ui.RowSelected:ui.Row)) {Load(path);kitchenListOpen=false;sidebarScroll=Vector2.zero;}
            y+=30;
        }
        y+=8;ui.Rule(2,y,220);y+=14;
        if(Clock!=null) {
            if(shared!=null)SharedSummary(shared,ref y);
            ui.Label(new Rect(2,y,220,22),"当前订单",ui.Heading);y+=30;
            if(orderCount==0){ui.Label(new Rect(2,y,220,26),"暂无订单",ui.Caption);y+=30;}
            else if(shared!=null) {
                foreach(var order in shared.orders) {
                    bool joint=order.kind=="joint";
                    ui.Panel(new Rect(0,y,224,47),soft:true);
                    ui.Fill(new Rect(0,y+5,4,37),joint?ui.OrangeSeat:ui.BlueSeat);
                    ui.Label(new Rect(12,y+1,140,24),$"#{order.id}  {(joint?"联合单":"普通单")}",ui.Heading);
                    ui.Label(new Rect(162,y+1,60,24),SharedKitchenView.OrderStatus(order.status),ui.Text);
                    ui.Label(new Rect(12,y+22,204,23),joint?"两侧厨师同步出锅":"一位厨师即可出锅",ui.Caption);
                    y+=54;
                }
            }
            else foreach(var order in orders) {
                if(Data.header.IsPartition){PartitionOrder(order,y);y+=68;}
                else {ui.OrderTicket(new Rect(0,y,224,46),order.ingredients);y+=54;}
            }
            if(Data.header.IsPartition){PartitionEventRows(ref y);}
            y+=4;ui.Perforation(2,y,220);y+=14;
            ui.Label(new Rect(2,y,220,22),"出场厨师",ui.Heading);y+=24;
            for(int i=0;i<Data.header.players.Length;i++) {
                var seatColor=i switch {0=>ui.BlueSeat,1=>ui.OrangeSeat,_=>ChefColors[i]};
                ui.Fill(new Rect(2,y+9,5,12),seatColor);
                string description=i==2&&characterActors[i] is TrueClaudeActor?"绿方   真·Claude娘":ChefDescriptions[i];
                ui.Label(new Rect(16,y,146,30),description);
                ui.Label(new Rect(178,y,46,30),Data.header.players[i].is_human?"人类":"AI",ui.Caption);y+=33;
            }
            if(handoffs!=null)foreach(var handoff in handoffs) {
                ui.Label(new Rect(2,y,222,26),"同步对接 · "+HandoffStatus(handoff),ui.Caption);y+=29;
            }
            if(!Data.header.complete){ui.Label(new Rect(2,y+8,220,22),"记录不完整，播放至最后已知状态",ui.Caption);y+=35;}
        }else {ui.Label(new Rect(2,y,220,28),"打开回放，或选择一个示例厨房。",ui.Caption);}
        if(error!="") {
            var wrap=new GUIStyle(ui.Caption){wordWrap=true};ui.Label(new Rect(2,y+8,220,90),error,wrap);
        }
        GUI.EndScrollView(!settings.Open);
        ui.Rule(32,height-66,234);
        if(ui.Click(new Rect(32,height-56,234,30),(labels?"✓  ":"    ")+(shared!=null?"显示角色和工位标签":"显示角色名称"),ui.Row))labels=!labels;
    }
    void SharedSummary(SharedFrame frame,ref float y) {
        ui.Label(new Rect(2,y,222,22),"协作进度",ui.Heading);y+=24;
        ui.Label(new Rect(2,y,222,21),$"已出餐 {frame.normal_completed+frame.joint_completed}  ·  普通 {frame.normal_completed}  /  联合 {frame.joint_completed}");y+=22;
        var wait=sharedKitchen.WaitSteps(frame,Clock.Index);
        string elapsed=wait.HasValue?$"累计联合等待 {wait} 步":$"累计联合就绪 {frame.joint_wait_steps} 步";
        ui.Label(new Rect(2,y,222,21),elapsed,ui.Caption);y+=20;
        ui.Label(new Rect(2,y,222,21),$"仅一人尝试出锅 {frame.unmatched_steps} 步",ui.Caption);y+=27;
    }
    readonly List<Rect> sharedLabelBounds=new List<Rect>(16);
    static readonly Color SharedLabelBackground=new Color(.08f,.15f,.13f,.91f);
    void SharedOverlay(float scale) {
        var frame=Data.frames[Clock.Index].state.shared;
        sharedLabelBounds.Clear();
        int eventFrame=sharedKitchen.LatestEventFrame(Clock.Index);
        bool showEvent=eventFrame>0&&Clock.Time-Data.frames[eventFrame].time<=1f;
        int actorMask=0;
        if(showEvent) {
            float y=navigation.Locked?73:107;
            foreach(var entry in Data.segments[eventFrame-1].events) {
                string operation=SharedKitchenView.EventName(entry.kind);
                if(operation==null)continue;
                var names=new List<string>(3);
                for(int i=0;i<chefs.Length;i++)if(entry.HasActor(i)) {
                    names.Add(ChefNames[i]);actorMask|=1<<i;
                }
                string title=$"第 {Data.frames[eventFrame].state.timestep} 步   {operation}";
                if(entry.order_id>=0)title+="  #"+entry.order_id;
                if(names.Count>0)title+="  ·  "+string.Join(" + ",names);
                if(entry.kind=="meal_delivery") {
                    float scoreDelta=Data.frames[eventFrame].score-Data.frames[eventFrame-1].score;
                    title+=$"  本步 {scoreDelta:+0.##;-0.##;0} 分";
                }
                var rect=new Rect(312,y,ui.OnDark.CalcSize(new GUIContent(title)).x+28,28);
                ui.Panel(rect,dark:true);ui.Fill(new Rect(rect.x+1,y+6,3,16),ui.Gold);
                ui.Label(new Rect(rect.x+12,y,rect.width-16,28),title,ui.OnDark);
                sharedLabelBounds.Add(rect);y+=31;
            }
        }
        if(!labels)return;
        for(int i=0;i<chefs.Length;i++) {
            string held=Data.frames[Clock.Index].state.players[i].held_object?.name;
            string text=string.IsNullOrEmpty(held)?ChefNames[i]:ChefNames[i]+" · "+SharedKitchenView.ItemName(held);
            SharedWorldLabel(chefs[i].position+Vector3.up*ReplayUiLayout.NameHeight,text,scale,
                (actorMask&(1<<i))!=0?ui.Gold:ChefColors[i]);
        }
        foreach(var station in sharedKitchen.Stations) {
            string label=SharedKitchenView.StationLabel(station.Key,frame);
            SharedWorldLabel(station.Value+Vector3.up*.38f,label,scale,Color.clear);
        }
    }
    void SharedWorldLabel(Vector3 point,string title,float scale,Color accent) {
        var screen=Camera.main.WorldToScreenPoint(point);
        if(screen.z<=0)return;
        var anchor=new Vector2(screen.x/scale,(Screen.height-screen.y)/scale);
        float bottom=Screen.height/scale*.83f-3;
        if(anchor.x<302||anchor.x>1268||anchor.y<72||anchor.y>bottom)return;
        float width=ui.White.CalcSize(new GUIContent(title)).x+14;
        var rect=new Rect(Mathf.Clamp(anchor.x-width*.5f,302,1268-width),anchor.y-16,width,19);
        for(int attempt=0;attempt<11;attempt++) {
            int shift=(attempt+1)/2*(attempt%2==1?-1:1);
            rect.y=Mathf.Clamp(anchor.y-16+shift*20,70,bottom-19);
            bool overlap=false;
            foreach(var occupied in sharedLabelBounds)if(occupied.Overlaps(rect)){overlap=true;break;}
            if(overlap)continue;
            sharedLabelBounds.Add(rect);
            if(shift!=0) {
                float edge=anchor.y>rect.yMax?rect.yMax:rect.yMin;
                ui.Fill(new Rect(anchor.x,Mathf.Min(edge,anchor.y),1,Mathf.Abs(edge-anchor.y)),Color.white);
            }
            ui.Fill(rect,SharedLabelBackground);
            if(accent.a>0)ui.Fill(new Rect(rect.x,rect.y,3,rect.height),accent);
            ui.Label(new Rect(rect.x+7,rect.y,rect.width-9,rect.height),title,ui.White);
            return;
        }
    }
    void Toolbar() {
        ui.Panel(new Rect(310,18,568,46));
        if(ui.Click(new Rect(318,25,118,32),navigation.Locked?"镜头已锁定":"镜头可移动",navigation.Locked?ui.Selected:ui.Toolbar)){navigation.Locked=!navigation.Locked;CancelCameraDrag();}
        ui.Fill(new Rect(445,29,1,24),ui.Line);
        if(ui.Click(new Rect(455,25,94,32),"长边俯瞰",view==KitchenView.LongEdge?ui.ToolbarSelected:ui.Toolbar))SetView(KitchenView.LongEdge);
        if(ui.Click(new Rect(552,25,83,32),"斜向视角",view==KitchenView.Isometric?ui.ToolbarSelected:ui.Toolbar))SetView(KitchenView.Isometric);
        ui.Fill(new Rect(644,29,1,24),ui.Line);
        if(ui.Click(new Rect(653,25,103,32),"旋转 90°",ui.Toolbar)){yaw+=90;FrameCamera();}
        if(ui.Click(new Rect(759,25,111,32),"镜头复位",ui.Toolbar))ResetCamera();
        ui.Panel(new Rect(1014,18,250,46));
        if(ui.Click(new Rect(1022,25,106,32),preferences.night?"夜晚模式":"白天模式",ui.Toolbar))SetNight(!preferences.night);
        if(ui.Click(new Rect(1134,25,122,32),"画面设置",ui.Toolbar))OpenSettings();
        if(!navigation.Locked) {
            ui.Panel(ReplayUiLayout.NavigationHint,dark:true);
            ui.Label(new Rect(322,72,630,28),IsLive && !live.Spectating?"滚轮缩放   Shift＋滚轮升降   中键平移   右键调整俯仰":"滚轮缩放   Shift＋滚轮升降   WASD / 中键平移   右键调整俯仰",ui.White);
        }
    }
    void SetView(KitchenView next){view=next;preferences.viewMode=(int)next;preferences.Save(ViewerPreferences.DefaultPath);ResetCamera();}
    void Transport(float height) {
        if(markedReplay!=Data) {
            markedReplay=Data;deliveryTimes.Clear();
            deliveryTimes.AddRange(ReplayFile.ScoreMarkerTimes(Data));
        }
        var panel=ReplayUiLayout.Transport(height);ui.Panel(panel);float y=panel.y+10;
        if(ui.Click(new Rect(324,y,94,34),Presentation.Playing?"Ⅱ  暂停":"▶  播放",ui.Primary))Presentation.Toggle();
        if(ui.Click(new Rect(426,y,62,34),"上一步"))Seek(Clock.Index-1);
        if(ui.Click(new Rect(493,y,62,34),"下一步"))Seek(Clock.Index+1);
        if(ui.Click(new Rect(566,y,53,34),"重播",ui.Toolbar))Seek(0);
        ui.Label(new Rect(640,y,165,34),ClockText(Clock.Time)+" / "+ClockText(Clock.Duration),ui.Numeric);
        if(deliveryTimes.Count>0) {
            ui.Fill(new Rect(806,y+14,6,6),ui.Gold);
            ui.Label(new Rect(818,y,75,34),"得分标记",ui.Caption);
        }
        ui.Label(new Rect(904,y,42,34),"倍速",ui.Caption);
        for(int i=0;i<PlaybackSpeeds.Length;i++)if(ui.Click(new Rect(948+i*60,y,56,34),PlaybackSpeeds[i]+"×",Mathf.Abs(Clock.Speed-PlaybackSpeeds[i])<.001f?ui.Selected:ui.Toolbar))Clock.Speed=PlaybackSpeeds[i];
        float timelineY=Mathf.Min(panel.yMax-22,y+49);
        float t=ui.Timeline(new Rect(324,timelineY,921,18),Clock.Time,Clock.Duration,deliveryTimes);
        if(Mathf.Abs(t-Clock.Time)>.001f){if(sound)sound.Stop();bool resume=Presentation.Playing;Clock.Seek(t);Presentation.ResetAfterSeek(resume);applied=-1;Apply();}
    }
    static string ClockText(float seconds){int s=Mathf.Max(0,Mathf.FloorToInt(seconds));return (s/60)+":"+(s%60).ToString("00");}
    void Names(float scale) {
        for(int i=0;i<chefs.Length;i++) {
            var point=Camera.main.WorldToScreenPoint(chefs[i].position+Vector3.up*ReplayUiLayout.NameHeight);
            if(point.z<=0)continue;
            var name=new Rect(point.x/scale-40,(Screen.height-point.y)/scale-12,80,18);
            if(name.xMin<302||name.xMax>1264||name.yMin<78||name.yMax>Screen.height/scale*.83f)continue;
            ui.Label(new Rect(name.x+1,name.y+1,name.width,name.height),ChefNames[i],ui.NameShadow);
            ui.Label(name,ChefNames[i],ui.Name);
        }
    }
    IEnumerator CaptureInterface(string path) {
        yield return new WaitForEndOfFrame();
        var shot=ScreenCapture.CaptureScreenshotAsTexture();
        try {File.WriteAllBytes(path,shot.EncodeToPNG());}finally{Destroy(shot);}
    }
}
}
