using System.Collections.Generic;
using UnityEngine;

namespace OvercookedReplay {
public static class ReplayUiLayout {
    public const float Width=1280;
    public static float Scale(float screenWidth)=>screenWidth/Width;
    public static float Height(Vector2 screen)=>screen.y/Scale(screen.x);
    public static Vector2 CanvasPoint(Vector2 screenPoint,Vector2 screen)=>new Vector2(screenPoint.x,(screen.y-screenPoint.y))/Scale(screen.x);
    public static Rect Sidebar(float height)=>new Rect(14,14,270,height-28);
    public static Rect Transport(float height)=>new Rect(310,height*.83f+8,954,height*.17f-22);
    public static Rect Settings(float height)=>new Rect(360,(height-640)*.5f,810,640);
    public static readonly Rect NavigationHint=new Rect(310,72,650,28);
    public const float NameHeight=1.95f;
}

// A shared, cached IMGUI skin. Nine-sliced corners remain round at every size.
public sealed class ReplayUiTheme {
    public static Color Hex(string value){ColorUtility.TryParseHtmlString(value,out var c);return c;}
    public readonly Color Paper=Hex("#F7FAFB"),Mist=Hex("#E2EDF0"),Marine=Hex("#174D60"),Ink=Hex("#233F4A"),Muted=Hex("#70858F"),Gold=Hex("#E3AD59");
    public readonly GUIStyle Text,Caption,Heading,Title,Score,White,Name,NameShadow,Button,Primary,Selected,Toolbar,ToolbarSelected,Row,RowSelected;
    public readonly GUIStyle Slider,Thumb,ScrollTrack,ScrollThumb;
    readonly GUIStyle paperPanel,darkPanel,softPanel,shadowPanel,rule;
    readonly Dictionary<Color,Texture2D> fills=new Dictionary<Color,Texture2D>();
    public ReplayUiTheme() {
        var font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei UI","Microsoft YaHei","PingFang SC","Hiragino Sans GB","Heiti SC","Segoe UI","Arial"},18);
        var numbers=Font.CreateDynamicFontFromOSFont(new[]{"Segoe UI","PingFang SC","Microsoft YaHei UI","Arial"},24);
        Text=Label(font,13,Ink);Caption=Label(font,11,Muted);Heading=Label(font,14,Marine,FontStyle.Bold);
        Title=Label(font,24,Marine,FontStyle.Bold);Score=Label(numbers,36,Marine,FontStyle.Bold);
        White=Label(font,13,Color.white);Name=Label(font,10,Color.white);Name.alignment=TextAnchor.MiddleCenter;
        NameShadow=new GUIStyle(Name);NameShadow.normal.textColor=new Color(0,0,0,.65f);
        Button=Control(font,Paper,Ink,Hex("#E6EFF2"));Primary=Control(font,Marine,Color.white,Hex("#23677C"));
        Selected=Control(font,Hex("#D7E9EE"),Marine,Hex("#C9DFE7"));Selected.fontStyle=FontStyle.Bold;
        Toolbar=Control(font,Paper,Ink,Hex("#E6EFF2"));ToolbarSelected=Control(font,Marine,Color.white,Hex("#23677C"));
        Row=Control(font,Paper,Ink,Hex("#E6EFF2"));Row.alignment=TextAnchor.MiddleLeft;Row.padding=new RectOffset(12,12,3,3);
        RowSelected=new GUIStyle(Selected){alignment=TextAnchor.MiddleLeft,padding=new RectOffset(12,12,3,3)};
        paperPanel=PanelStyle(Paper,12);darkPanel=PanelStyle(Marine,12);softPanel=PanelStyle(Hex("#EDF3F5"),8);
        shadowPanel=PanelStyle(new Color(.05f,.15f,.2f,.12f),12);rule=PanelStyle(Hex("#DEE8EC"),0);
        Slider=new GUIStyle(GUI.skin.horizontalSlider){fixedHeight=7,margin=new RectOffset(0,0,8,8),border=new RectOffset(3,3,3,3)};
        Slider.normal.background=Rounded(Hex("#CDDDE3"),3);Slider.hover.background=Slider.normal.background;
        Thumb=new GUIStyle(GUI.skin.horizontalSliderThumb){fixedWidth=14,fixedHeight=14,border=new RectOffset(6,6,6,6)};
        Thumb.normal.background=Rounded(Marine,6);Thumb.hover.background=Rounded(Hex("#287E99"),6);Thumb.active.background=Thumb.hover.background;Thumb.focused.background=Rounded(Gold,6);
        ScrollTrack=new GUIStyle(GUI.skin.verticalScrollbar){fixedWidth=7};ScrollTrack.normal.background=Texture(new Color(0,0,0,0));
        ScrollThumb=new GUIStyle(GUI.skin.verticalScrollbarThumb){fixedWidth=7,border=new RectOffset(3,3,3,3)};ScrollThumb.normal.background=Rounded(Hex("#C3D6DE"),3);
    }
    static GUIStyle Label(Font font,int size,Color color,FontStyle weight=FontStyle.Normal) {
        var s=new GUIStyle(GUI.skin.label){font=font,fontSize=size,fontStyle=weight,alignment=TextAnchor.MiddleLeft,wordWrap=false,padding=new RectOffset(0,0,0,0)};
        s.normal.textColor=color;return s;
    }
    GUIStyle Control(Font font,Color background,Color text,Color hover) {
        var s=new GUIStyle(GUI.skin.button){font=font,fontSize=12,alignment=TextAnchor.MiddleCenter,padding=new RectOffset(6,6,3,3),border=new RectOffset(6,6,6,6)};
        s.normal.background=Rounded(background,6);s.hover.background=Rounded(hover,6);s.active.background=Rounded(Hex("#BBD5DF"),6);s.focused.background=Rounded(Gold,6);
        s.normal.textColor=text;s.hover.textColor=text;s.active.textColor=Marine;s.focused.textColor=Marine;return s;
    }
    GUIStyle PanelStyle(Color color,int radius)=>new GUIStyle{normal={background=Rounded(color,radius)},border=new RectOffset(radius,radius,radius,radius)};
    Texture2D Rounded(Color color,int radius) {
        int n=radius*2+4;var t=new Texture2D(n,n,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        for(int y=0;y<n;y++)for(int x=0;x<n;x++) {
            float dx=Mathf.Max(radius-(x+.5f),0,x+.5f-(n-radius));float dy=Mathf.Max(radius-(y+.5f),0,y+.5f-(n-radius));
            float alpha=radius==0?1:Mathf.Clamp01(radius+.5f-Mathf.Sqrt(dx*dx+dy*dy));
            t.SetPixel(x,y,new Color(color.r,color.g,color.b,color.a*alpha));
        }t.Apply();return t;
    }
    Texture2D Texture(Color color) {
        if(fills.TryGetValue(color,out var t))return t;
        t=new Texture2D(1,1){hideFlags=HideFlags.HideAndDontSave};t.SetPixel(0,0,color);t.Apply();fills.Add(color,t);return t;
    }
    public void Fill(Rect rect,Color color){if(Event.current.type==EventType.Repaint)GUI.DrawTexture(rect,Texture(color));}
    public void Panel(Rect rect,bool dark=false,bool soft=false) {
        if(Event.current.type!=EventType.Repaint)return;
        if(!soft)shadowPanel.Draw(new Rect(rect.x,rect.y+2,rect.width,rect.height),false,false,false,false);
        (dark?darkPanel:soft?softPanel:paperPanel).Draw(rect,false,false,false,false);
    }
    public void Rule(float x,float y,float width){if(Event.current.type==EventType.Repaint)rule.Draw(new Rect(x,y,width,1),false,false,false,false);}
    public bool Click(Rect rect,string label,GUIStyle style=null)=>GUI.Button(rect,label,style??Button);
    public void Label(Rect rect,string value,GUIStyle style=null)=>GUI.Label(rect,value,style??Text);
    public float Track(Rect rect,float value,float min,float max)=>GUI.HorizontalSlider(rect,value,min,max,Slider,Thumb);
    public static string KitchenName(string name) {
        switch(name){case "cramped_room":return "拥挤厨房";case "asymmetric_advantages":return "非对称厨房";case "coordination_ring":return "协作环岛";case "forced_coordination":return "分区协作";case "counter_circuit_o_1order":return "环形工作台";case "cramped_room_tomato":return "番茄厨房";default:return name;}
    }
}
}
