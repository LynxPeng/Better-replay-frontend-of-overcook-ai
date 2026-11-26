using UnityEngine;

namespace OvercookedReplay {
// Static geometry and snapshot projection only. No action outcomes are computed here.
public static class PartitionKitchenView {
    public static GameObject Station(char tile) {
        if(tile=='#'){
            var wall=new GameObject("Station_#");
            KitchenFactory.Part("Wall",wall.transform,PrimitiveType.Cube,new Vector3(0,.49f,0),
                new Vector3(.96f,.98f,.96f),KitchenFactory.Material("#66786A"));return wall;
        }
        char basis=tile=='K'?'P':tile=='C'||tile=='P'||tile=='E'||tile=='B'?'X':tile;
        var root=KitchenFactory.Station(basis);root.name="Station_"+tile;
        var top=Vector3.up*(KitchenFactory.CounterTopHeight+.02f);
        if(tile=='C'){
            KitchenFactory.RestaurantProp("cuttingboard",root.transform,.66f,top);
            KitchenFactory.RestaurantProp("knife",root.transform,.31f,top+new Vector3(.25f,.04f,.18f));
        }
        if(tile=='P')KitchenFactory.RestaurantProp("crate_potatoes",root.transform,.77f,top);
        if(tile=='E'||tile=='B')for(int i=0;i<4;i++)
            Item(tile=='E'?"egg":"beef",root.transform,top+new Vector3((i%2-.5f)*.29f,.07f,(i/2-.5f)*.27f),.23f);
        return root;
    }
    public static GameObject Item(string name,Transform parent,Vector3 position,float size) {
        var root=new GameObject(name);root.transform.SetParent(parent,false);root.transform.localPosition=position;
        if(name=="egg"){
            KitchenFactory.Part("Egg",root.transform,PrimitiveType.Sphere,Vector3.zero,
                new Vector3(size*.72f,size,size*.72f),KitchenFactory.Material("#FFF1D2"));
        }else if(name=="beef"){
            KitchenFactory.Part("Beef",root.transform,PrimitiveType.Cube,Vector3.zero,
                new Vector3(size,size*.43f,size*.74f),KitchenFactory.Material("#A64B42"));
            KitchenFactory.Part("Marbling",root.transform,PrimitiveType.Cube,new Vector3(0,size*.23f,0),
                new Vector3(size*.72f,size*.03f,size*.09f),KitchenFactory.Material("#F1C8A6"));
        }else if(name.StartsWith("cut_",System.StringComparison.Ordinal)){
            string raw=name.Substring(4);var color=KitchenFactory.Material(raw=="tomato"?"#E96744":"#E6C975");
            for(int i=0;i<3;i++)KitchenFactory.Part("Cut piece",root.transform,PrimitiveType.Cube,
                new Vector3((i-1)*size*.24f,0,(i%2)*size*.16f),new Vector3(size*.28f,size*.22f,size*.32f),color);
        }else if(name.StartsWith("meal_",System.StringComparison.Ordinal)){
            // The presentation bowl is not an environment dish requirement.
            if(!KitchenFactory.RestaurantProp("bowl_small",root.transform,size,Vector3.zero))
                KitchenFactory.Part("Meal bowl",root.transform,PrimitiveType.Cylinder,Vector3.zero,
                    new Vector3(size,size*.22f,size),KitchenFactory.Material("#F4E9D4"));
            bool potato=name!="meal_tomato_egg",beef=name=="meal_potato_tomato_beef";
            for(int i=0;i<3;i++){
                var color=KitchenFactory.Material(beef&&i==2?"#A64B42":name=="meal_potato"||(potato&&i==0)||(!potato&&i==2)?"#E8CE76":"#E96744");
                KitchenFactory.Part("Meal ingredient",root.transform,PrimitiveType.Sphere,
                    new Vector3((i-1)*size*.18f,size*.19f,(i%2)*size*.14f),Vector3.one*size*.27f,color);
            }
        }
        return root;
    }
    static Vector3 Top(int cell)=>ReplayPlayer.World(PartitionReplay.XY(cell))+Vector3.up*(KitchenFactory.CounterTopHeight+.12f);
    static void Progress(Transform root,int cell,float value,string name){
        var p=Top(cell)+new Vector3(0,.19f,.34f);
        KitchenFactory.Part(name+" track",root,PrimitiveType.Cube,p,new Vector3(.65f,.035f,.065f),KitchenFactory.Material("#57645B"));
        if(value>0)KitchenFactory.Part(name,root,PrimitiveType.Cube,p+new Vector3((value-1)*.325f,.015f,0),
            new Vector3(value*.65f,.038f,.07f),KitchenFactory.Material(value>=1?"#78C994":"#EEBD64"));
    }
    public static void Draw(PartitionState state,Transform root){
        for(int j=0;j<4;j++)Progress(root,PartitionReplay.Boards[j],state.cuts[j]/2f,"ChoppingProgress");
        for(int j=0;j<2;j++){
            var p=Top(PartitionReplay.Pots[j])+Vector3.up*.1f;
            int[] bits={1,2,4,8};string[] items={"cut_tomato","cut_potato","egg","beef"};int count=0;
            for(int k=0;k<4;k++)if((state.mix[j]&bits[k])!=0)
                KitchenFactory.Item(items[k],root,p+new Vector3((count++-1)*.17f,0,0),.18f);
            Progress(root,PartitionReplay.Pots[j],state.heat[j]/4f,"TendingProgress");
        }
    }
    public static string Label(char tile,int x,int y,PartitionState s){
        if(tile=='T')return "柿子";if(tile=='P')return "马铃薯";if(tile=='E')return "鸡蛋";if(tile=='B')return "牛肉";
        if(tile=='S')return x==0?"左侧交付":"右侧交付";
        if(tile=='H')return "同步口";
        if(tile=='C'){int j=System.Array.IndexOf(PartitionReplay.Boards,y*13+x);return "切配 "+s.cuts[j]+"/2";}
        if(tile=='K'){
            int j=System.Array.IndexOf(PartitionReplay.Pots,y*13+x);
            string recipe=s.locked?.Length==2&&s.locked[j]>=0?PartitionReplay.Recipes[s.locked[j]]:"";
            return s.heat[j]==4?recipe+" · 已熟":(recipe==""?"看锅":recipe)+" "+s.heat[j]+"/4";
        }
        return null;
    }
}
}
