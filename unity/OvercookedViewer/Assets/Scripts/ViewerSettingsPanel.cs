using System;
using UnityEngine;

namespace OvercookedReplay {
public class ViewerSettingsPanel {
    public bool Open {get;private set;}
    ViewerPreferences draft;string message="";
    public void Show(ViewerPreferences current){draft=current.Copy();Open=true;message="";}
    public void Cancel(){Open=false;message="";}
    public void TryApply(Action<ViewerPreferences> apply) {
        try {apply(draft.Copy());Open=false;message="";}
        catch(Exception ex){message="设置未保存："+ex.Message;}
    }
    public void Draw(ReplayUiTheme ui,float height,Action<ViewerPreferences> apply) {
        if(!Open)return;
        if(Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Escape){Cancel();Event.current.Use();return;}
        ui.Fill(new Rect(0,0,1280,height),new Color(.04f,.13f,.17f,.64f));
        var panel=ReplayUiLayout.Settings(height);ui.Panel(panel);
        float x=panel.x+28,y=panel.y,w=panel.width-56;
        ui.Label(new Rect(x,y+22,w-45,34),"画面设置",ui.Title);
        ui.Label(new Rect(x,y+60,w,20),"按你的屏幕与显卡，找到最舒服的观看方式。",ui.Caption);
        if(ui.Click(new Rect(panel.xMax-58,y+22,32,32),"×"))Cancel();
        ui.Label(new Rect(x,y+99,106,30),"快速画质",ui.Heading);
        foreach(var preset in new[]{0,1,2}) {
            int samples=preset==0?2:preset==1?4:8;float scale=preset==0?1:preset==1?1.25f:1.5f;
            bool chosen=draft.msaa==samples&&Mathf.Abs(draft.renderScale-scale)<.001f&&draft.frameRate==60;
            if(ui.Click(new Rect(x+118+preset*146,y+97,136,34),preset==0?"流畅":preset==1?"精细（推荐）":"超清",chosen?ui.Selected:ui.Button)) {
                draft.msaa=samples;draft.renderScale=scale;draft.frameRate=60;
            }
        }
        ui.Rule(x,y+147,w);
        ui.Label(new Rect(x,y+162,w,24),"显示",ui.Heading);
        ui.Label(new Rect(x,y+196,110,32),"分辨率");
        int resolution=Mathf.Max(0,Array.IndexOf(ViewerPreferences.Widths,draft.width));
        resolution=Options(ui,new Rect(x+118,y+196,w-118,32),resolution,new[]{"1366 × 768","1600 × 900","1920 × 1080","2560 × 1440","3840 × 2160"});
        draft.width=ViewerPreferences.Widths[resolution];draft.height=ViewerPreferences.Heights[resolution];
        ui.Label(new Rect(x,y+240,110,32),"窗口模式");
        draft.borderless=Options(ui,new Rect(x+118,y+240,300,32),draft.borderless?1:0,new[]{"窗口","无边框全屏"})==1;
        ui.Label(new Rect(x+434,y+240,320,32),"全屏采用显示器原生分辨率",ui.Caption);
        ui.Rule(x,y+289,w);
        ui.Label(new Rect(x,y+301,w,24),"清晰度与性能",ui.Heading);
        ui.Label(new Rect(x,y+336,110,32),"抗锯齿 MSAA");
        int[] msaa={1,2,4,8};draft.msaa=msaa[Options(ui,new Rect(x+118,y+336,w-118,32),Array.IndexOf(msaa,draft.msaa),new[]{"关闭","2×","4×","8×"})];
        ui.Label(new Rect(x,y+379,110,32),"渲染倍率");
        float[] scales={.75f,1,1.25f,1.5f,2};int selected=Array.FindIndex(scales,n=>Mathf.Abs(n-draft.renderScale)<.001f);
        int next=Options(ui,new Rect(x+118,y+379,w-118,32),selected,new[]{"0.75×","1×","1.25×","1.5×","2×"});if(next>=0)draft.renderScale=scales[next];
        ui.Label(new Rect(x,y+422,110,32),"帧率上限");
        int[] rates={30,60,120,0};draft.frameRate=rates[Options(ui,new Rect(x+118,y+422,w-118,32),Array.IndexOf(rates,draft.frameRate),new[]{"30 FPS","60 FPS","120 FPS","不限制"})];
        ui.Rule(x,y+470,w);
        ui.Label(new Rect(x,y+480,110,30),"镜头俯角",ui.Heading);
        draft.cameraPitch=Mathf.Round(ui.Track(new Rect(x+118,y+493,w-205,16),draft.cameraPitch,35,65));
        ui.Label(new Rect(x+w-66,y+480,66,30),draft.cameraPitch.ToString("0")+"°",ui.Heading);
        ui.Label(new Rect(x,y+516,w,22),"俯角越小越接近平视。更高的抗锯齿与渲染倍率可改善边缘，也会增加显卡负担。",ui.Caption);
        if(message!="")ui.Label(new Rect(x,y+541,w,35),message,new GUIStyle(ui.Caption){wordWrap=true});
        ui.Rule(x,y+579,w);
        ui.Label(new Rect(x,y+593,350,30),"仅影响回放显示",ui.Caption);
        if(ui.Click(new Rect(panel.xMax-276,y+592,84,34),"取消"))Cancel();
        if(ui.Click(new Rect(panel.xMax-182,y+592,154,34),"应用并保存",ui.Primary))TryApply(apply);
        if(Event.current.isMouse)Event.current.Use();
    }
    static int Options(ReplayUiTheme ui,Rect rect,int selected,string[] labels) {
        float width=(rect.width-(labels.Length-1)*6)/labels.Length;
        for(int i=0;i<labels.Length;i++)if(ui.Click(new Rect(rect.x+i*(width+6),rect.y,width,rect.height),labels[i],selected==i?ui.Selected:ui.Button))selected=i;
        return selected;
    }
}
}
