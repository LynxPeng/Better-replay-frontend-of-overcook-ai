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
    public static Rect Settings(float height)=>new Rect(232,(height-610)*.5f,816,610);
    public static readonly Rect NavigationHint=new Rect(310,72,650,28);
    public const float NameHeight=1.95f;
}

// A shared, cached IMGUI skin. Nine-sliced corners remain round at every size.
public sealed class ReplayUiTheme : System.IDisposable {
    public static Color Hex(string value){ColorUtility.TryParseHtmlString(value,out var c);return c;}
    public readonly Color Paper=Hex("#FFF9EC"),Mist=Hex("#DCE7DC"),Marine=Hex("#194B3C"),Ink=Hex("#233B32"),Muted=Hex("#53665D"),Gold=Hex("#EEAB54");
    public readonly Color Mint=Hex("#D7E7D8"),Line=Hex("#D8D7C6"),Honey=Hex("#FBE4B4"),BlueSeat=Hex("#4C93AD"),OrangeSeat=Hex("#CA7344");
    readonly Color trackColor,progressColor;
    public readonly GUIStyle Text,Caption,Heading,Title,Score,White,Name,NameShadow,Button,Primary,Selected,Toolbar,ToolbarSelected,Row,RowSelected;
    public readonly GUIStyle Slider,Thumb,ScrollTrack,ScrollThumb;
    public readonly GUIStyle OnDark,OnDarkTitle,OnDarkScore,OnDarkCaption,Numeric;
    readonly GUIStyle paperPanel,darkPanel,softPanel,shadowPanel,rule,ticket;
    readonly RecipeArtwork food=new RecipeArtwork();
    readonly Texture2D bell;
    readonly List<Texture2D> generated=new List<Texture2D>();
    readonly Font font,numbers;
    readonly Dictionary<Color,Texture2D> fills=new Dictionary<Color,Texture2D>();
    public ReplayUiTheme(bool night=false) {
        if(night) {
            Paper=Hex("#33372F");Mist=Hex("#252A25");Marine=Hex("#243C31");
            Ink=Hex("#F6EBD8");Muted=Hex("#D9CDB6");Gold=Hex("#E8AC61");
            Mint=Hex("#4B5D4D");Line=Hex("#566050");Honey=Hex("#594B34");
        }
        var ivory=Hex("#FFF8E6");
        var heading=night?ivory:Marine;
        var button=night?Hex("#44483D"):Hex("#F0EFE2");
        var chosen=night?Hex("#CA9A62"):Marine;
        var chosenInk=night?Hex("#25291E"):ivory;
        var chosenHover=night?Hex("#E0B480"):Hex("#32664F");
        trackColor=night?Hex("#647160"):Hex("#C1CDC1");progressColor=night?Gold:Marine;
        font=ViewerFont.Create();
        numbers=ViewerFont.Create();
        Text=Label(font,13,Ink);Caption=Label(font,12,Muted);Heading=Label(font,14,heading,FontStyle.Bold);
        Title=Label(font,24,heading,FontStyle.Bold);Score=Label(numbers,36,heading,FontStyle.Bold);
        White=Label(font,13,Color.white);Name=Label(font,10,Color.white);Name.alignment=TextAnchor.MiddleCenter;
        OnDark=Label(font,14,ivory,FontStyle.Bold);OnDarkTitle=Label(font,25,ivory,FontStyle.Bold);
        OnDarkScore=Label(numbers,38,ivory,FontStyle.Bold);OnDarkCaption=Label(font,13,Hex("#F0E9D9"));
        Numeric=Label(numbers,20,heading,FontStyle.Bold);
        bell=MakeBell();
        NameShadow=new GUIStyle(Name);NameShadow.normal.textColor=new Color(0,0,0,.65f);
        Button=Control(font,button,Ink,Mint);Primary=Control(font,chosen,chosenInk,chosenHover);
        Selected=Control(font,Mint,heading,night?Hex("#566B59"):Hex("#C1D9C8"));Selected.fontStyle=FontStyle.Bold;
        Toolbar=Control(font,Paper,Ink,Mint);ToolbarSelected=Control(font,chosen,chosenInk,chosenHover);
        Row=Control(font,Paper,Ink,Mint);Row.alignment=TextAnchor.MiddleLeft;Row.padding=new RectOffset(12,12,3,3);
        RowSelected=new GUIStyle(Selected){alignment=TextAnchor.MiddleLeft,padding=new RectOffset(12,12,3,3)};
        paperPanel=PanelStyle(Paper,10);darkPanel=PanelStyle(Marine,10);softPanel=PanelStyle(night?Hex("#3C4337"):Hex("#F2F0DD"),6);
        shadowPanel=PanelStyle(Hex("#253B321C"),10);rule=PanelStyle(Line,0);ticket=PanelStyle(Honey,5);
        Slider=new GUIStyle(GUI.skin.horizontalSlider){fixedHeight=7,margin=new RectOffset(0,0,8,8),border=new RectOffset(3,3,3,3)};
        Slider.normal.background=Rounded(trackColor,3);Slider.hover.background=Slider.normal.background;
        Thumb=new GUIStyle(GUI.skin.horizontalSliderThumb){fixedWidth=14,fixedHeight=14,border=new RectOffset(6,6,6,6)};
        Thumb.normal.background=Rounded(progressColor,6);Thumb.hover.background=Rounded(night?ivory:Hex("#417254"),6);Thumb.active.background=Thumb.hover.background;Thumb.focused.background=Rounded(Gold,6);
        ScrollTrack=new GUIStyle(GUI.skin.verticalScrollbar){fixedWidth=7};ScrollTrack.normal.background=Texture(new Color(0,0,0,0));
        ScrollThumb=new GUIStyle(GUI.skin.verticalScrollbarThumb){fixedWidth=7,border=new RectOffset(3,3,3,3)};ScrollThumb.normal.background=Rounded(Hex("#BECBB9"),3);
    }
    static GUIStyle Label(Font font,int size,Color color,FontStyle weight=FontStyle.Normal) {
        var s=new GUIStyle(GUI.skin.label){font=font,fontSize=size,fontStyle=weight,alignment=TextAnchor.MiddleLeft,wordWrap=false,padding=new RectOffset(0,0,0,0)};
        s.normal.textColor=color;return s;
    }
    GUIStyle Control(Font font,Color background,Color text,Color hover) {
        var s=new GUIStyle(GUI.skin.button){font=font,fontSize=13,alignment=TextAnchor.MiddleCenter,padding=new RectOffset(5,5,2,2),border=new RectOffset(5,5,5,5)};
        s.normal.background=Rounded(background,5);s.hover.background=Rounded(hover,5);s.active.background=Rounded(Mint,5);s.focused.background=Rounded(Gold,5);
        s.normal.textColor=text;s.hover.textColor=text;s.active.textColor=Ink;s.focused.textColor=Hex("#1B3527");return s;
    }
    GUIStyle PanelStyle(Color color,int radius)=>new GUIStyle{normal={background=Rounded(color,radius)},border=new RectOffset(radius,radius,radius,radius)};
    Texture2D Rounded(Color color,int radius) {
        int n=radius*2+4;var t=new Texture2D(n,n,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        for(int y=0;y<n;y++)for(int x=0;x<n;x++) {
            float dx=Mathf.Max(radius-(x+.5f),0,x+.5f-(n-radius));float dy=Mathf.Max(radius-(y+.5f),0,y+.5f-(n-radius));
            float alpha=radius==0?1:Mathf.Clamp01(radius+.5f-Mathf.Sqrt(dx*dx+dy*dy));
            t.SetPixel(x,y,new Color(color.r,color.g,color.b,color.a*alpha));
        }t.Apply(false,true);generated.Add(t);return t;
    }
    Texture2D Texture(Color color) {
        if(fills.TryGetValue(color,out var t))return t;
        t=new Texture2D(1,1){hideFlags=HideFlags.HideAndDontSave};t.SetPixel(0,0,color);t.Apply(false,true);fills.Add(color,t);generated.Add(t);return t;
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
    Texture2D MakeBell() {
        const int n=64;
        var tex=new Texture2D(n,n,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        for(int y=0;y<n;y++)for(int x=0;x<n;x++) {
            var p=new Vector2(x+.5f,y+.5f);
            float dome=Mathf.Max(Mathf.Abs(Vector2.Distance(p,new Vector2(32,20))-22)-2,20-p.y);
            float handle=Vector2.Distance(p,new Vector2(32,48))-3;
            float tray=Mathf.Max(Mathf.Abs(p.x-32)-28,Mathf.Abs(p.y-13)-2);
            float rim=Mathf.Max(Mathf.Abs(p.x-32)-23,Mathf.Abs(p.y-20)-2);
            float dist=Mathf.Min(Mathf.Min(dome,handle),Mathf.Min(tray,rim));
            tex.SetPixel(x,y,new Color(Gold.r,Gold.g,Gold.b,Mathf.Clamp01(.5f-dist)));
        }
        tex.Apply(false,true);generated.Add(tex);return tex;
    }
    public void ServiceBell(Rect r){if(Event.current.type==EventType.Repaint)GUI.DrawTexture(r,bell,ScaleMode.ScaleToFit,true);}
    public float Timeline(Rect r,float value,float duration,List<float> deliveries) {
        float limit=Mathf.Max(.001f,duration);
        GUI.SetNextControlName("Replay seek");
        float next=GUI.HorizontalSlider(r,value,0,limit,Slider,Thumb);
        if(Event.current.type!=EventType.Repaint)return next;
        float x=r.x+Thumb.fixedWidth*.5f, span=r.width-Thumb.fixedWidth;
        float head=x+span*Mathf.Clamp01(value/limit),y=r.center.y;
        // Keep Unity's keyboard and drag behavior, then draw a single aligned rail and handle.
        Fill(r,Paper);
        Fill(new Rect(x,y-2,span,4),trackColor);
        Fill(new Rect(x,y-2,Mathf.Max(0,head-x),4),progressColor);
        float previous=float.NegativeInfinity;
        foreach(float stamp in deliveries) {
            float marker=x+span*Mathf.Clamp01(stamp/limit);
            if(marker-previous<5)continue;
            Fill(new Rect(marker-2.5f,y+4,5,7),Gold);
            previous=marker;
        }
        var knob=new Rect(head-7,y-7,14,14);
        Thumb.Draw(knob,knob.Contains(Event.current.mousePosition),GUIUtility.hotControl!=0 && r.Contains(Event.current.mousePosition),
                  false,GUI.GetNameOfFocusedControl()=="Replay seek");
        return next;
    }
    public void OrderTicket(Rect rect,string[] ingredients) {
        if(Event.current.type==EventType.Repaint)ticket.Draw(rect,false,false,false,false);
        float left=rect.x+12;
        int onions=0,tomatoes=0;string other=null;
        if(ingredients!=null)foreach(string item in ingredients) {
            if(item=="onion")onions++;
            else if(item=="tomato")tomatoes++;
            else other=other==null?item:other+" + "+item;
        }
        if(other!=null){Label(new Rect(left,rect.y,rect.width-24,rect.height),string.Join(" + ",ingredients));return;}
        if(onions>0)RecipeEntry(rect,left,"onion","洋葱",onions);
        if(tomatoes>0)RecipeEntry(rect,onions==0?left:left+rect.width*.48f,"tomato","番茄",tomatoes);
        if(onions+tomatoes==0)Label(new Rect(left,rect.y,rect.width-24,rect.height),"无配料",Caption);
    }
    void RecipeEntry(Rect rect,float x,string id,string name,int count) {
        float size=Mathf.Min(rect.height-8,36);
        if(Event.current.type==EventType.Repaint) {
            var texture=food.Find(id);
            if(texture)GUI.DrawTexture(new Rect(x,rect.center.y-size*.5f,size,size),texture,ScaleMode.ScaleToFit,true);
        }
        Label(new Rect(x+size+5,rect.y,66,rect.height),name+" ×"+count,Text);
    }
    public void Perforation(float x,float y,float width) {
        if(Event.current.type!=EventType.Repaint)return;
        for(int i=0;i+5<=width;i+=9)Fill(new Rect(x+i,y,5,1),Line);
    }
    public void Dispose() {
        food.Dispose();
        foreach(var t in generated)UnityEngine.Object.Destroy(t);
        if(font)UnityEngine.Object.Destroy(font);
        if(numbers)UnityEngine.Object.Destroy(numbers);
    }
    public static string KitchenName(string name) {
        switch(name){case "cramped_room":return "拥挤厨房";case "asymmetric_advantages":return "非对称厨房";case "coordination_ring":return "协作环岛";case "forced_coordination":return "分区协作";case "counter_circuit_o_1order":return "环形工作台";case "cramped_room_tomato":return "番茄厨房";default:return name;}
    }
}
}
