using System.Collections.Generic;
using UnityEngine;

namespace OvercookedReplay {
// The table slots and all work-in-progress items are projections of the recorded frame.
public sealed class SharedKitchenView {
    const float Top=KitchenFactory.CounterTopHeight;
    static readonly Vector3 MainSlot=new Vector3(-.23f,.045f,.13f);
    static readonly Vector3 SideSlot=new Vector3(.23f,.045f,.13f);
    static readonly Vector3 PlateSlot=new Vector3(0,.045f,-.24f);
    public readonly Dictionary<char,Vector3> Stations=new Dictionary<char,Vector3>();
    readonly int[] latestEvent,jointUnloads;
    ItemSlot cooker,cutting,main,side,plate,buffer;
    Renderer[] handles;

    public SharedKitchenView(ReplayFile replay) {
        latestEvent=new int[replay.frames.Length];
        jointUnloads=new int[replay.frames.Length];
        if(replay.frames[0].state.timestep!=0)jointUnloads[0]=-1;
        for(int i=1;i<latestEvent.Length;i++) {
            if(replay.segments[i-1].kind=="gap") {
                jointUnloads[i]=-1;
                continue;
            }
            latestEvent[i]=latestEvent[i-1];
            jointUnloads[i]=jointUnloads[i-1];
            var events=replay.segments[i-1].events;
            if(events==null)continue;
            foreach(var entry in events) {
                if(EventName(entry.kind)!=null)latestEvent[i]=i;
                if(entry.kind=="joint_unload"&&jointUnloads[i]>=0)jointUnloads[i]++;
            }
        }
    }
    public int? WaitSteps(SharedFrame frame,int index)=>jointUnloads[index]<0?(int?)null:frame.joint_wait_steps-jointUnloads[index];
    public int LatestEventFrame(int index)=>latestEvent[index];
    public static string EventName(string kind)=>kind switch {
        "joint_unload"=>"双人同步出锅","normal_unload"=>"普通单出锅","assemble"=>"装盘完成","meal_delivery"=>"出餐成功",_=>null
    };
    public static string ItemName(string code)=>code switch {
        "RM"=>"生主菜","CM"=>"熟主菜","RS"=>"生配菜","CS"=>"切好配菜","PL"=>"空盘","MEAL"=>"成品餐",_=>"空"
    };
    public static string OrderStatus(string status)=>status switch {
        "queued"=>"待发布","active"=>"待开工","started"=>"制作中","served"=>"已交付",_=>status
    };
    public static string StationLabel(char tile,SharedFrame s) {
        switch(tile) {
            case 'M':return "主菜食材";
            case 'L':return "配菜食材";
            case 'D':return "取盘架";
            case 'S':return "出餐口";
            case 'B':return string.IsNullOrEmpty(s.buf)?"暂存台":"暂存 · "+ItemName(s.buf);
            case 'K':return string.IsNullOrEmpty(s.cutting.item)?"切配台":
                s.cutting.item=="CS"?"切配 · 已切好":"切配 · 已切 "+s.cutting.prog+" 次";
            case 'G':
                if(string.IsNullOrEmpty(s.cooker.item))return "烹饪锅";
                if(s.cooker.rem>0)return (s.cooker.kind=="joint"?"联合锅":"普通锅")+" · 剩 "+s.cooker.rem+" 步";
                if(!string.IsNullOrEmpty(s.asm.main))return "已熟 · 主菜位已占";
                return s.cooker.kind=="joint"?"已熟 · 待双人出锅":"已熟 · 待出锅";
            case 'A':
                if(s.asm.plate=="MEAL")return "装盘台 · 成品餐";
                int count=(string.IsNullOrEmpty(s.asm.plate)?0:1)+(string.IsNullOrEmpty(s.asm.side)?0:1)+(string.IsNullOrEmpty(s.asm.main)?0:1);
                return "装盘台 · "+count+"/3";
            default:return null;
        }
    }

    sealed class ItemSlot {
        readonly Transform parent;
        readonly Vector3 position;
        readonly float size;
        readonly Dictionary<string,GameObject> models=new Dictionary<string,GameObject>();
        string current="";
        public ItemSlot(Transform parent,Vector3 offset,float size) {
            this.parent=parent;position=Vector3.up*Top+offset;this.size=size;
        }
        public void Show(string code) {
            code=code??"";
            if(current==code)return;
            if(current!="")models[current].SetActive(false);
            current=code;
            if(code=="")return;
            if(!models.TryGetValue(code,out var model))models[code]=model=Item(code,parent,position,size);
            model.SetActive(true);
        }
    }
    static GameObject Shape(string name,Transform parent,PrimitiveType shape,Vector3 pos,Vector3 scale,string color) {
        return KitchenFactory.Part(name,parent,shape,pos,scale,KitchenFactory.Material(color));
    }
    static GameObject Box(string name,Transform parent,Vector3 pos,Vector3 size,string color) {
        return Shape(name,parent,PrimitiveType.Cube,pos,size,color);
    }
    static GameObject Disc(string name,Transform parent,Vector3 pos,float width,float thickness,string color) {
        return Shape(name,parent,PrimitiveType.Cylinder,pos,new Vector3(width,thickness*.5f,width),color);
    }
    // All item origins sit on their supporting surface. A plated meal uses the same
    // cooked main, chopped side and ceramic plate that appear in the assembly slots.
    public static GameObject Item(string code,Transform parent,Vector3 pos,float size) {
        var item=new GameObject("SharedItem_"+code);
        var t=item.transform;t.SetParent(parent,false);t.localPosition=pos;t.localScale=Vector3.one*size;
        switch(code) {
            case "RM":
            case "CM":
                bool raw=code=="RM";
                Shape("Main portion",t,PrimitiveType.Sphere,new Vector3(0,.14f,0),new Vector3(.98f,.28f,.69f),raw?"#D56B64":"#A85C35");
                for(int i=-1;i<=1;i++) {
                    var mark=Box(raw?"Marbling":"Sear mark",t,new Vector3(i*.23f,.26f,0),
                        new Vector3(.044f,.015f,.40f),raw?"#FADEC4":"#573424");
                    mark.transform.localRotation=Quaternion.Euler(0,raw?-22:20,0);
                }
                break;
            case "RS":
                for(int i=0;i<3;i++) {
                    var leaf=Shape("Leaf",t,PrimitiveType.Sphere,new Vector3((i-1)*.20f,.23f,0),
                        new Vector3(.38f,.56f,.68f),i==1?"#9BC76A":"#5B9659");
                    leaf.transform.localRotation=Quaternion.Euler(0,0,(i-1)*-29);
                }
                break;
            case "CS":
                for(int i=0;i<5;i++) {
                    var piece=Box("Chopped side",t,new Vector3((i%3-1)*.23f,.10f,(i/3-.5f)*.27f),
                        new Vector3(.20f,.20f,.24f),i%2==0?"#9BBE65":"#4D905C");
                    piece.transform.localRotation=Quaternion.Euler(0,i*18,0);
                }
                break;
            case "PL":
            case "MEAL":
                Disc("Ceramic rim",t,new Vector3(0,.06f,0),1,.10f,"#E3E9DD");
                Disc("Glazed border",t,new Vector3(0,.12f,0),.88f,.035f,"#52929B");
                Disc("Plate well",t,new Vector3(0,.141f,0),.77f,.03f,"#FFF1D7");
                if(code=="MEAL") {
                    Item("CM",t,new Vector3(-.10f,.16f,.035f),.65f);
                    Item("CS",t,new Vector3(.19f,.16f,-.16f),.40f);
                }
                break;
        }
        return item;
    }
    static void IngredientTray(Transform t,string code) {
        var center=new Vector3(0,Top+.035f,0);
        Box("Crate bottom",t,center,new Vector3(.79f,.06f,.69f),"#A57146");
        foreach(int edge in new[]{-1,1}) {
            Box("Crate end",t,center+new Vector3(edge*.37f,.06f,0),new Vector3(.045f,.15f,.69f),"#C68F58");
            Box("Crate rail",t,center+new Vector3(0,.06f,edge*.325f),new Vector3(.75f,.15f,.045f),"#C68F58");
        }
        for(int i=0;i<4;i++)
            Item(code,t,center+new Vector3((i%2-.5f)*.33f,.08f,(i/2-.5f)*.28f),.32f);
    }
    public GameObject Station(char tile,int x,int y) {
        if(tile=='X') {
            var wall=new GameObject("SharedWall");
            Box("Stone",wall.transform,Vector3.up*.21f,new Vector3(.99f,.42f,.99f),"#61756B");
            Box("Coping",wall.transform,Vector3.up*.425f,new Vector3(.99f,.04f,.99f),"#93A194");
            return wall;
        }
        Stations.Add(tile,new Vector3(x,Top,-y));
        var station=KitchenFactory.Station(tile=='D'?'D':'X');
        station.name="SharedStation_"+tile;
        var t=station.transform;var top=Vector3.up*Top;
        switch(tile) {
            case 'M':IngredientTray(t,"RM");break;
            case 'L':IngredientTray(t,"RS");break;
            case 'G':
                Disc("Burner",t,top+Vector3.up*.048f,.76f,.095f,"#383D3B");
                Disc("Pan",t,top+Vector3.up*.123f,.74f,.085f,"#727C73");
                Disc("Cooking surface",t,top+Vector3.up*.168f,.64f,.025f,"#3C463F");
                handles=new Renderer[2];
                for(int i=0;i<2;i++)handles[i]=Box("Unloading handle",t,top+new Vector3((i-.5f)*.83f,.145f,0),
                    new Vector3(.16f,.08f,.16f),"#806243").GetComponent<Renderer>();
                cooker=new ItemSlot(t,Vector3.up*.188f,.51f);break;
            case 'K':
                Box("Cutting board",t,top+Vector3.up*.04f,new Vector3(.74f,.08f,.55f),"#C99355");
                Box("Knife blade",t,top+new Vector3(.22f,.15f,.10f),new Vector3(.04f,.14f,.26f),"#B8CDCF");
                Box("Knife handle",t,top+new Vector3(.22f,.19f,-.10f),new Vector3(.067f,.071f,.16f),"#69503D");
                cutting=new ItemSlot(t,new Vector3(-.12f,.085f,0),.40f);break;
            case 'A':
                Box("Main slot",t,top+MainSlot,new Vector3(.41f,.05f,.4f),"#B57B64");
                Box("Side slot",t,top+SideSlot,new Vector3(.41f,.05f,.4f),"#8DA778");
                Box("Plate slot",t,top+PlateSlot,new Vector3(.60f,.05f,.33f),"#81A0AC");
                main=new ItemSlot(t,MainSlot+Vector3.up*.03f,.39f);
                side=new ItemSlot(t,SideSlot+Vector3.up*.03f,.39f);
                plate=new ItemSlot(t,PlateSlot+Vector3.up*.03f,.41f);break;
            case 'B':
                Box("Storage tray",t,top+Vector3.up*.03f,new Vector3(.75f,.06f,.66f),"#739195");
                foreach(int edge in new[]{-1,1})
                    Box("Tray lip",t,top+new Vector3(edge*.35f,.075f,0),new Vector3(.035f,.085f,.66f),"#A5BDB5");
                buffer=new ItemSlot(t,Vector3.up*.065f,.50f);break;
            case 'D':
                if(!t.Find("dishrack_plates"))
                    for(int i=0;i<4;i++)Item("PL",t,top+Vector3.up*(.035f+i*.055f),.6f);
                break;
            case 'S':
                Box("Serving pass",t,top+Vector3.up*.035f,new Vector3(.82f,.055f,.60f),"#55897C");
                Disc("Bell base",t,top+new Vector3(.18f,.11f,.10f),.37f,.06f,"#4F544A");
                Shape("Service bell",t,PrimitiveType.Sphere,top+new Vector3(.18f,.15f,.10f),new Vector3(.32f,.20f,.32f),"#E7B155");
                Disc("Bell button",t,top+new Vector3(.18f,.26f,.10f),.08f,.04f,"#805C37");
                break;
        }
        return station;
    }
    public void ShowFrame(SharedFrame frame) {
        cooker.Show(frame.cooker.item);cutting.Show(frame.cutting.item);buffer.Show(frame.buf);
        main.Show(frame.asm.main);side.Show(frame.asm.side);plate.Show(frame.asm.plate);
        bool ready=frame.cooker.item=="CM"&&frame.cooker.kind=="joint"&&string.IsNullOrEmpty(frame.asm.main);
        foreach(var handle in handles)handle.sharedMaterial=KitchenFactory.Material(ready?"#FFB64C":"#806243");
    }
}
}
