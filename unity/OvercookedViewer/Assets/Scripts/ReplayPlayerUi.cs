using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OvercookedReplay {
public partial class ReplayPlayer {
    ReplayUiTheme ui;Vector2 sidebarScroll;bool kitchenListOpen;string loadedName="尚未打开回放";
    void OpenReplay() {
        if(Presentation!=null)Presentation.Pause();CancelCameraDrag();
        try {var path=NativeDialog.Open();if(path!=null)Load(path);}
        catch(Exception ex){error="无法打开文件选择器："+ex.Message;Debug.LogException(ex);}
    }
    void OpenSettings(){if(Presentation!=null)Presentation.Pause();CancelCameraDrag();settings.Show(preferences);}
    void OnGUI() {
        if(ui==null)ui=new ReplayUiTheme();
        var previous=GUI.matrix;bool enabled=GUI.enabled;float scale=ReplayUiLayout.Scale(Screen.width),height=Screen.height/scale;
        GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
        try {
            ui.Fill(new Rect(0,0,294.4f,height),ui.Mist);
            ui.Fill(new Rect(294.4f,height*.83f,985.6f,height*.17f),ui.Mist);
            GUI.enabled=!settings.Open;
            Sidebar(height);Toolbar();if(Clock!=null)Transport(height);
            if(labels&&Clock!=null)Names(scale);
            GUI.enabled=true;settings.Draw(ui,height,p=>ApplyPreferences(p,true));
        }finally{GUI.matrix=previous;GUI.enabled=enabled;}
    }
    void Sidebar(float height) {
        ui.Panel(ReplayUiLayout.Sidebar(height));
        ui.Label(new Rect(32,34,210,36),"协作厨房",ui.Title);
        ui.Label(new Rect(33,73,200,20),"把每一次默契，重新看一遍。",ui.Caption);
        // A tiny brass clip gives the information sheet its restaurant character.
        ui.Fill(new Rect(248,36,12,28),ui.Gold);ui.Fill(new Rect(251,41,6,16),ui.Paper);
        if(ui.Click(new Rect(32,110,234,36),"打开本地回放",ui.Primary))OpenReplay();
        ui.Label(new Rect(32,150,234,24),loadedName,ui.Caption);ui.Rule(32,184,234);
        var orders=Clock!=null?Data.frames[Clock.Index].state.all_orders:null;
        float contentHeight=390+(kitchenListOpen?(samples?.Length??0)*30:0)+(orders?.Length??0)*35+(error!=""?110:0)+(Data!=null&&!Data.header.complete?35:0);
        sidebarScroll=GUI.BeginScrollView(new Rect(30,198,242,height-280),sidebarScroll,new Rect(0,0,225,contentHeight),false,false,GUIStyle.none,ui.ScrollTrack);
        float y=0;
        ui.Label(new Rect(2,y,220,22),"示例厨房",ui.Heading);y+=30;
        string selected=Data==null?"选择一个厨房":ReplayUiTheme.KitchenName(Data.header.layout_name);
        if(ui.Click(new Rect(0,y,224,36),selected+(kitchenListOpen?"  ▴":"  ▾"),ui.RowSelected))kitchenListOpen=!kitchenListOpen;
        if(Data!=null)ui.Label(new Rect(2,y+37,220,19),Data.header.layout_name,ui.Caption);
        y+=56;
        if(kitchenListOpen&&samples!=null)foreach(var path in samples) {
            var name=Path.GetFileName(path).Replace(".ocrep.json","");
            if(ui.Click(new Rect(0,y,224,28),ReplayUiTheme.KitchenName(name),Data?.header.layout_name==name?ui.RowSelected:ui.Row)) {Load(path);kitchenListOpen=false;sidebarScroll=Vector2.zero;}
            y+=30;
        }
        y+=20;ui.Rule(2,y,220);y+=19;
        if(Clock!=null) {
            ui.Label(new Rect(2,y,120,22),"团队得分",ui.Heading);
            ui.Label(new Rect(2,y+26,116,50),Data.frames[Clock.Index].score.ToString("0"),ui.Score);
            ui.Label(new Rect(132,y+31,92,23),$"{Clock.Index} / {Data.frames.Length-1}");
            ui.Label(new Rect(132,y+56,92,20),"当前步数",ui.Caption);y+=100;
            for(int i=0;i<2;i++) {
                ui.Fill(new Rect(2,y+9,5,12),ReplayUiTheme.Hex(i==0?"#4C9CC3":"#D88A55"));
                ui.Label(new Rect(18,y,146,30),i==0?"蓝方 / 大肥鱼":"橙方 / 企鹅");
                ui.Label(new Rect(178,y,46,30),Data.header.players[i].is_human?"人类":"AI",ui.Caption);y+=33;
            }
            y+=15;ui.Rule(2,y,220);y+=19;ui.Label(new Rect(2,y,220,22),"当前订单",ui.Heading);y+=32;
            if(orders==null||orders.Length==0){ui.Label(new Rect(2,y,220,26),"暂无订单",ui.Caption);y+=30;}
            else foreach(var order in orders) {
                var names=new List<string>();if(order.ingredients!=null)foreach(var item in order.ingredients)names.Add(item=="onion"?"洋葱":"番茄");
                ui.Panel(new Rect(0,y,224,29),soft:true);ui.Label(new Rect(12,y,200,29),string.Join(" + ",names),ui.Caption);y+=35;
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
        ui.Panel(new Rect(310,18,654,46));
        if(ui.Click(new Rect(318,25,122,32),navigation.Locked?"镜头已锁定":"镜头可移动",navigation.Locked?ui.Selected:ui.Toolbar)){navigation.Locked=!navigation.Locked;CancelCameraDrag();}
        if(ui.Click(new Rect(452,25,102,32),"长边俯瞰",view==KitchenView.LongEdge?ui.ToolbarSelected:ui.Toolbar))SetView(KitchenView.LongEdge);
        if(ui.Click(new Rect(560,25,94,32),"斜向视角",view==KitchenView.Isometric?ui.ToolbarSelected:ui.Toolbar))SetView(KitchenView.Isometric);
        if(ui.Click(new Rect(674,25,112,32),"旋转 90°",ui.Toolbar)){yaw+=90;FrameCamera();}
        if(ui.Click(new Rect(796,25,150,32),"镜头复位",ui.Toolbar))ResetCamera();
        ui.Panel(new Rect(1014,18,250,46));
        if(ui.Click(new Rect(1022,25,106,32),preferences.night?"夜晚模式":"白天模式",ui.Toolbar))SetNight(!preferences.night);
        if(ui.Click(new Rect(1134,25,122,32),"画面设置",ui.Toolbar))OpenSettings();
        if(!navigation.Locked) {
            ui.Panel(ReplayUiLayout.NavigationHint,dark:true);
            ui.Label(new Rect(322,72,630,28),"滚轮缩放   Shift＋滚轮升降   WASD / 中键平移   右键调整俯仰",ui.White);
        }
    }
    void SetView(KitchenView next){view=next;preferences.viewMode=(int)next;preferences.Save(ViewerPreferences.DefaultPath);ResetCamera();}
    void Transport(float height) {
        var panel=ReplayUiLayout.Transport(height);ui.Panel(panel);float y=panel.y+10;
        if(ui.Click(new Rect(322,y,54,32),"重播"))Seek(0);
        if(ui.Click(new Rect(382,y,62,32),"上一步"))Seek(Clock.Index-1);
        if(ui.Click(new Rect(450,y,80,32),Presentation.Playing?"暂停":"播放",ui.Primary))Presentation.Toggle();
        if(ui.Click(new Rect(536,y,62,32),"下一步"))Seek(Clock.Index+1);
        ui.Label(new Rect(618,y,220,32),Clock.Time.ToString("0.0")+" / "+Clock.Duration.ToString("0.0")+" 秒");
        ui.Label(new Rect(921,y,42,32),"倍速",ui.Caption);
        float[] speeds={.25f,.5f,1,2,4};
        for(int i=0;i<speeds.Length;i++)if(ui.Click(new Rect(966+i*57,y,51,32),speeds[i]+"×",Mathf.Abs(Clock.Speed-speeds[i])<.001f?ui.Selected:ui.Button))Clock.Speed=speeds[i];
        float t=ui.Track(new Rect(326,y+51,914,18),Clock.Time,0,Mathf.Max(.001f,Clock.Duration));
        if(Mathf.Abs(t-Clock.Time)>.001f){if(sound)sound.Stop();bool resume=Presentation.Playing;Clock.Seek(t);Presentation.ResetAfterSeek(resume);applied=-1;Apply();}
    }
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
