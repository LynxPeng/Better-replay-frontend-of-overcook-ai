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
            if(labels&&Clock!=null&&(!IsLive||live.ShowKitchen))Names(scale);
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
        ui.Label(new Rect(32,101,128,48),Clock==null?"—":Data.frames[Clock.Index].score.ToString("0"),ui.OnDarkScore);
        ui.Label(new Rect(175,83,92,20),"当前步数",ui.OnDarkCaption);
        ui.Label(new Rect(175,108,95,34),Clock==null?"—":$"{Clock.Index} / {Data.frames.Length-1}",ui.OnDark);
        if(ui.Click(new Rect(32,178,234,36),"打开本地回放",ui.Primary))OpenReplay();
        string fileLabel=loadedName.Length>31?loadedName.Substring(0,15)+"…"+loadedName.Substring(loadedName.Length-15):loadedName;
        GUI.Label(new Rect(33,217,232,24),new GUIContent(fileLabel,loadedName),ui.Caption);
        ui.Perforation(32,250,234);
        var orders=Clock!=null?Data.frames[Clock.Index].state.all_orders:null;
        float contentHeight=235+(kitchenListOpen?(samples?.Length??0)*30:0)+Mathf.Max(1,orders?.Length??0)*54+(error!=""?110:0)+(Data!=null&&!Data.header.complete?35:0);
        sidebarScroll=GUI.BeginScrollView(new Rect(30,264,242,Mathf.Max(60,height-335)),sidebarScroll,new Rect(0,0,225,contentHeight),false,false,GUIStyle.none,ui.ScrollTrack);
        float y=0;
        ui.Label(new Rect(2,y,220,22),"厨房选择",ui.Heading);y+=28;
        string selected=Data==null?"选择一个厨房":ReplayUiTheme.KitchenName(Data.header.layout_name);
        if(ui.Click(new Rect(0,y,224,36),selected+(kitchenListOpen?"  ▴":"  ▾"),ui.RowSelected))kitchenListOpen=!kitchenListOpen;
        y+=40;
        if(kitchenListOpen&&samples!=null)foreach(var path in samples) {
            var name=Path.GetFileName(path).Replace(".ocrep.json","");
            if(ui.Click(new Rect(0,y,224,28),ReplayUiTheme.KitchenName(name),Data?.header.layout_name==name?ui.RowSelected:ui.Row)) {Load(path);kitchenListOpen=false;sidebarScroll=Vector2.zero;}
            y+=30;
        }
        y+=8;ui.Rule(2,y,220);y+=14;
        if(Clock!=null) {
            ui.Label(new Rect(2,y,220,22),"当前订单",ui.Heading);y+=30;
            if(orders==null||orders.Length==0){ui.Label(new Rect(2,y,220,26),"暂无订单",ui.Caption);y+=30;}
            else foreach(var order in orders) {
                ui.OrderTicket(new Rect(0,y,224,46),order.ingredients);y+=54;
            }
            y+=4;ui.Perforation(2,y,220);y+=14;
            ui.Label(new Rect(2,y,220,22),"出场厨师",ui.Heading);y+=24;
            for(int i=0;i<2;i++) {
                ui.Fill(new Rect(2,y+9,5,12),i==0?ui.BlueSeat:ui.OrangeSeat);
                ui.Label(new Rect(16,y,146,30),i==0?"蓝方   大肥鱼":"橙方   企鹅");
                ui.Label(new Rect(178,y,46,30),Data.header.players[i].is_human?"人类":"AI",ui.Caption);y+=33;
            }
            if(!Data.header.complete){ui.Label(new Rect(2,y+8,220,22),"记录不完整，播放至最后已知状态",ui.Caption);y+=35;}
        }else {ui.Label(new Rect(2,y,220,28),"打开回放，或选择一个示例厨房。",ui.Caption);}
        if(error!="") {
            var wrap=new GUIStyle(ui.Caption){wordWrap=true};ui.Label(new Rect(2,y+8,220,90),error,wrap);
        }
        GUI.EndScrollView(!settings.Open);
        ui.Rule(32,height-66,234);
        if(ui.Click(new Rect(32,height-56,234,30),(labels?"✓  ":"    ")+"显示角色名称",ui.Row))labels=!labels;
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
            for(int i=0;i<Data.segments.Length;i++) {
                var events=Data.segments[i].events;
                if(events!=null && Array.Exists(events,e=>e.kind=="soup_delivery"))deliveryTimes.Add(Data.frames[i+1].time);
            }
        }
        var panel=ReplayUiLayout.Transport(height);ui.Panel(panel);float y=panel.y+10;
        if(ui.Click(new Rect(324,y,94,34),Presentation.Playing?"Ⅱ  暂停":"▶  播放",ui.Primary))Presentation.Toggle();
        if(ui.Click(new Rect(426,y,62,34),"上一步"))Seek(Clock.Index-1);
        if(ui.Click(new Rect(493,y,62,34),"下一步"))Seek(Clock.Index+1);
        if(ui.Click(new Rect(566,y,53,34),"重播",ui.Toolbar))Seek(0);
        ui.Label(new Rect(640,y,165,34),ClockText(Clock.Time)+" / "+ClockText(Clock.Duration),ui.Numeric);
        if(deliveryTimes.Count>0) {
            ui.Fill(new Rect(806,y+14,6,6),ui.Gold);
            ui.Label(new Rect(818,y,75,34),"交付标记",ui.Caption);
        }
        ui.Label(new Rect(904,y,42,34),"倍速",ui.Caption);
        for(int i=0;i<PlaybackSpeeds.Length;i++)if(ui.Click(new Rect(948+i*60,y,56,34),PlaybackSpeeds[i]+"×",Mathf.Abs(Clock.Speed-PlaybackSpeeds[i])<.001f?ui.Selected:ui.Toolbar))Clock.Speed=PlaybackSpeeds[i];
        float timelineY=Mathf.Min(panel.yMax-22,y+49);
        float t=ui.Timeline(new Rect(324,timelineY,921,18),Clock.Time,Clock.Duration,deliveryTimes);
        if(Mathf.Abs(t-Clock.Time)>.001f){if(sound)sound.Stop();bool resume=Presentation.Playing;Clock.Seek(t);Presentation.ResetAfterSeek(resume);applied=-1;Apply();}
    }
    static string ClockText(float seconds){int s=Mathf.Max(0,Mathf.FloorToInt(seconds));return (s/60)+":"+(s%60).ToString("00");}
    void Names(float scale) {
        for(int i=0;i<2;i++) {
            var point=Camera.main.WorldToScreenPoint(chefs[i].position+Vector3.up*ReplayUiLayout.NameHeight);
            if(point.z<=0)continue;
            var name=new Rect(point.x/scale-40,(Screen.height-point.y)/scale-12,80,18);
            if(name.xMin<302||name.xMax>1264||name.yMin<78||name.yMax>Screen.height/scale*.83f)continue;
            ui.Label(new Rect(name.x+1,name.y+1,name.width,name.height),i==0?"蓝方":"橙方",ui.NameShadow);
            ui.Label(name,i==0?"蓝方":"橙方",ui.Name);
        }
    }
    IEnumerator CaptureInterface(string path) {
        yield return new WaitForEndOfFrame();
        var shot=ScreenCapture.CaptureScreenshotAsTexture();
        try {File.WriteAllBytes(path,shot.EncodeToPNG());}finally{Destroy(shot);}
    }
}
}
