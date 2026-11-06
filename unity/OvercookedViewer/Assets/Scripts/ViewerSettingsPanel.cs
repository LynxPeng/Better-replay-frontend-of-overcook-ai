using System;
using UnityEngine;

namespace OvercookedReplay {
public class ViewerSettingsPanel {
    public bool Open {get;private set;}
    ViewerPreferences draft;string message="";
    static readonly int[] Samples={1,2,4,8},Rates={30,60,120,0};
    static readonly float[] Scales={.75f,1,1.25f,1.5f,2};
    static readonly string[] Resolutions={"1366 × 768","1600 × 900","1920 × 1080","2560 × 1440","3840 × 2160"};
    static readonly string[] Modes={"窗口","无边框全屏"},Msaa={"关闭","2×","4×","8×"},ScaleLabels={"0.75×","1×","1.25×","1.5×","2×"},RateLabels={"30 FPS","60 FPS","120 FPS","不限"};
    static readonly string[] Presets={"流畅","精细","超清"};
    public void Show(ViewerPreferences current){draft=current.Copy();Open=true;message="";}
    public void Cancel(){Open=false;message="";}
    public void TryApply(Action<ViewerPreferences> apply) {
        try {apply(draft.Copy());Open=false;message="";}
        catch(Exception ex){message="设置未保存："+ex.Message;}
    }
    public void Draw(ReplayUiTheme ui,float height,Action<ViewerPreferences> apply) {
        if(!Open)return;
        if(Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Escape){Cancel();Event.current.Use();return;}
        ui.Fill(new Rect(0,0,1280,height),new Color(.04f,.10f,.075f,.64f));
        var panel=ReplayUiLayout.Settings(height);ui.Panel(panel);
        float x=panel.x+28,y=panel.y,w=panel.width-56;
        ui.Panel(new Rect(panel.x,y,panel.width,69),dark:true);
        ui.ServiceBell(new Rect(x,y+18,35,33));
        ui.Label(new Rect(x+48,y+14,w-116,42),"画面设置",ui.OnDarkTitle);
        if(ui.Click(new Rect(panel.xMax-76,y+18,48,32),"关闭",ui.Toolbar))Cancel();
        ui.Label(new Rect(x,y+86,94,27),"画质预设",ui.Heading);
        for(int preset=0;preset<Presets.Length;preset++) {
            int samples=preset==0?2:preset==1?4:8;float scale=preset==0?1:preset==1?1.25f:1.5f;
            bool chosen=draft.msaa==samples&&Mathf.Abs(draft.renderScale-scale)<.001f&&draft.frameRate==60;
            float px=x+110+preset*215;
            if(ui.Click(new Rect(px,y+82,204,44),Presets[preset]+"   "+samples+"× MSAA",chosen?ui.Primary:ui.Button)) {
                draft.msaa=samples;draft.renderScale=scale;draft.frameRate=60;
            }
        }
        ui.Perforation(x,y+143,w);
        ui.Label(new Rect(x,y+156,w,23),"显示",ui.Heading);
        ui.Label(new Rect(x,y+185,106,31),"分辨率");
        int resolution=Mathf.Max(0,Array.IndexOf(ViewerPreferences.Widths,draft.width));
        resolution=Options(ui,new Rect(x+110,y+185,w-110,31),resolution,Resolutions);
        draft.width=ViewerPreferences.Widths[resolution];draft.height=ViewerPreferences.Heights[resolution];
        ui.Label(new Rect(x,y+224,106,31),"窗口模式");
        draft.borderless=Options(ui,new Rect(x+110,y+224,300,31),draft.borderless?1:0,Modes)==1;
        ui.Label(new Rect(x+428,y+224,320,31),"全屏采用显示器原生分辨率",ui.Caption);
        ui.Rule(x,y+271,w);
        ui.Label(new Rect(x,y+283,w,23),"清晰度与性能",ui.Heading);
        ui.Label(new Rect(x,y+315,106,31),"抗锯齿 MSAA");
        draft.msaa=Samples[Options(ui,new Rect(x+110,y+315,w-110,31),Array.IndexOf(Samples,draft.msaa),Msaa)];
        ui.Label(new Rect(x,y+354,106,31),"渲染倍率");
        int selected=Array.IndexOf(Scales,draft.renderScale);
        int next=Options(ui,new Rect(x+110,y+354,w-110,31),selected,ScaleLabels);
        if(next>=0)draft.renderScale=Scales[next];
        ui.Label(new Rect(x,y+393,106,31),"帧率上限");
        draft.frameRate=Rates[Options(ui,new Rect(x+110,y+393,w-110,31),Array.IndexOf(Rates,draft.frameRate),RateLabels)];
        ui.Rule(x,y+442,w);
        ui.Label(new Rect(x,y+457,106,31),"镜头俯角",ui.Heading);
        draft.cameraPitch=Mathf.Round(ui.Track(new Rect(x+110,y+470,w-186,16),draft.cameraPitch,35,65));
        ui.Label(new Rect(x+w-59,y+455,59,32),draft.cameraPitch.ToString("0")+"°",ui.Numeric);
        ui.Label(new Rect(x+110,y+495,w-110,22),"俯角越小，视线越接近水平。",ui.Caption);
        ui.Perforation(x,y+534,w);
        if(message!="")ui.Label(new Rect(x,y+545,w-260,51),message,new GUIStyle(ui.Caption){wordWrap=true});
        if(ui.Click(new Rect(panel.xMax-277,y+551,85,38),"取消"))Cancel();
        if(ui.Click(new Rect(panel.xMax-180,y+551,152,38),"应用并保存",ui.Primary))TryApply(apply);
        if(Event.current.isMouse)Event.current.Use();
    }
    static int Options(ReplayUiTheme ui,Rect rect,int selected,string[] labels) {
        float width=(rect.width-(labels.Length-1)*6)/labels.Length;
        for(int i=0;i<labels.Length;i++)if(ui.Click(new Rect(rect.x+i*(width+6),rect.y,width,rect.height),labels[i],selected==i?ui.Selected:ui.Button))selected=i;
        return selected;
    }
}
}
