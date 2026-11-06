using System;
using System.Collections.Generic;
using UnityEngine;

namespace OvercookedReplay {
// Native decoration stays outside the recorded game's traversable grid.
public static class RestaurantBackdrop {
    static Transform Group(string name,Transform parent){var g=new GameObject(name);g.transform.SetParent(parent,false);return g.transform;}
    static readonly string[] Menu={"food_burger","food_vegetableburger","food_dinner","food_pizza_cheese_plated","food_pizza_mushroom_plated","food_pizza_pepperoni_plated","food_stew","icecream_bowl_decorated_A","icecream_bowl_decorated_B","food_icecream_cone_vanilla","food_icecream_cone_strawberry","food_icecream_cone_chocolate"};
    public static GameObject Build(Transform parent,int width,int height) {
        var root=Group("Restaurant Surroundings",parent);float cx=(width-1)*.5f,cz=-(height-1)*.5f;
        TownExterior.Build(root,width,height);
        KitchenFactory.Part("Sunlit Foundation",root,PrimitiveType.Cube,new Vector3(cx,-.28f,cz),new Vector3(width+11,.3f,height+9),KitchenFactory.Material("#d5c9ae"));
        RestaurantFloor.Build(root,width,height);
        RestaurantLighting.Build(root,width,height);
        var dining=Group("Dining",root);var rear=Group("Rear Wall",root);
        var alcoves=new List<float>();float doorX=2.5f;
        for(int x=0,index=0;x<width+8;x+=2,index++) {
            bool half=x+1==width+8;float center=-4.5f+x+(half?.5f:1);
            string model=half?"wall_half":index==3?"wall_doorway":index%5==0?"wall_decorated":index%5==2?"wall_window_open":"wall";
            var wall=KitchenFactory.RestaurantWall(model,rear,2,new Vector3(center,0,3.375f),180);
            if(model=="wall_decorated")alcoves.Add(center);
            if(model=="wall_doorway") {
                var asset=Resources.Load<GameObject>("RestaurantBits/Models/door_A");
                var door=KitchenFactory.Model("RestaurantBits/Models/door_A",wall.transform,1,Vector3.zero);
                door.transform.localScale=asset.transform.localScale*.5f;
                door.transform.localPosition=new Vector3(.4f,0,.02f);door.transform.localRotation=Quaternion.Euler(0,16,0);
            }
        }
        for(int side=0;side<2;side++) {
            float wallX=side==0?-4.375f:width+3.375f,yaw=side==0?90:-90;
            var wall=Group(side==0?"Left Wall":"Right Wall",root);
            for(float z=2.5f;z>=-height-1;z-=2)KitchenFactory.RestaurantWall("wall",wall,2,new Vector3(wallX,0,z),yaw);
            KitchenFactory.RestaurantProp(side==0?"kitchencabinet":"kitchencabinet_half_styleB",wall,side==0?.85f:.65f,new Vector3(wallX+(side==0?.35f:-.35f),1.1f,side==0?-height*.5f:-1),-1,yaw);
        }
        // Shuffle once per layout using a local RNG, without touching Unity or RL randomness.
        var random=new System.Random(unchecked(width*73856093^height*19349663^72431));var menu=(string[])Menu.Clone();
        for(int i=menu.Length-1;i>0;i--){int j=random.Next(i+1);string swap=menu[i];menu[i]=menu[j];menu[j]=swap;}
        int dish=0,table=0;
        for(float z=-.9f;z>=-height+.1f;z-=3.1f) {
            DiningTable(dining,new Vector3(-2.35f+(float)(random.NextDouble()-.5)*.22f,0,z),table++%2==0?"table_round_A":"table_round_B_tablecloth_green",menu,ref dish,random,false);
        }
        DiningTable(dining,new Vector3(width+1.1f,0,Mathf.Min(-height+.4f,-4.5f)),"table_round_B_tablecloth_red",menu,ref dish,random,true);
        Fridge(dining,root,"fridge_A_decorated",new Vector3(-3.8f,0,.65f),1.38f,false,width);
        Fridge(dining,root,"fridge_B",new Vector3(width+2.6f,0,1.15f),1.25f,true,width);
        DessertBar(dining,root,width);
        var aisle=Group("Street",root);
        int placed=0;
        for(int x=0;x<width;x+=2) {
            bool overlaps=Mathf.Abs(x-doorX)<1.1f;
            foreach(float alcove in alcoves)if(Mathf.Abs(x-alcove)<1.5f)overlaps=true;
            if(overlaps)continue;
            int kind=placed++%3;
            var service=Group("Service Station",aisle);service.localPosition=new Vector3(x,0,2);
            KitchenFactory.RestaurantProp(kind==0?"kitchencounter_sink":"kitchencounter_straight_B",service,.95f,Vector3.zero,.65f);
            if(kind==1) {
                KitchenFactory.RestaurantProp("food_dinner",service,.42f,new Vector3(-.23f,.65f,0),-1,180);
                KitchenFactory.RestaurantProp("plate_small",service,.42f,new Vector3(.23f,.65f,0));
                KitchenFactory.RestaurantProp("food_vegetableburger",service,.35f,new Vector3(.23f,.675f,0));
            }
            if(kind==2)KitchenFactory.RestaurantProp("pizza_oven",service,.8f,new Vector3(0,.65f,0),-1,180);
            service.localRotation=Quaternion.Euler(0,180,0);Flush(service,root,2,3.225f,true);
        }
        return root.gameObject;
    }
    static void Fridge(Transform parent,Transform room,string model,Vector3 bottom,float size,bool right,int width) {
        var fridge=KitchenFactory.RestaurantProp(model,parent,size,bottom,-1,right?-90:90);
        var native=fridge.transform.GetChild(0);native.localScale=Vector3.Scale(native.localScale,new Vector3(.82f,1,.88f));
        native.localPosition=Vector3.Scale(native.localPosition,new Vector3(.82f,1,.88f));
        Flush(fridge.transform,room,0,right?width+3.225f:-4.225f,right);
    }
    static void Flush(Transform item,Transform room,int axis,float plane,bool maximum) {
        var bounds=KitchenFactory.LocalBounds(item.gameObject,room);
        var delta=Vector3.zero;delta[axis]=plane-(maximum?bounds.max[axis]:bounds.min[axis]);
        item.position+=room.TransformVector(delta);
    }
    static void DiningTable(Transform parent,Vector3 position,string model,string[] menu,ref int dish,System.Random random,bool right) {
        var group=Group("Dining Table",parent);group.localPosition=position;
        var table=KitchenFactory.RestaurantProp(model,group,right?1.35f:1.5f,Vector3.zero);
        float top=KitchenFactory.LocalBounds(table,group).max.y;
        for(int seat=0;seat<2;seat++) {
            float sign=seat==0?-1:1;
            var chair=right?new Vector3(0,0,sign*.83f):new Vector3(sign*.9f,0,seat==0?-.1f:.15f);
            KitchenFactory.RestaurantProp(right?"chair_A":"chair_B",group,.73f,chair,-1,right?(sign<0?0:180):(sign<0?90:270));
            string meal=menu[dish++%menu.Length];var p=new Vector3(sign*.3f,top+.01f,(float)(random.NextDouble()-.5)*.2f);
            bool plate=meal.StartsWith("food_")&&!meal.Contains("plated")&&!meal.Contains("icecream");
            if(plate){KitchenFactory.RestaurantProp("plate_small",group,.48f,p);p.y+=.025f;}
            KitchenFactory.RestaurantProp(meal,group,meal.Contains("plated")?.5f:.38f,p,-1,(float)random.NextDouble()*360);
        }
        KitchenFactory.RestaurantProp("menu",group,.21f,new Vector3(0,top,.32f),-1,right?155:190);
        KitchenFactory.RestaurantProp(right?"mustard":"ketchup",group,.17f,new Vector3(.12f,top,.27f));
    }
    static void DessertBar(Transform parent,Transform room,int width) {
        var bar=Group("Icecream Bar",parent);bar.localPosition=new Vector3(width+1.9f,0,-1.4f);
        KitchenFactory.RestaurantProp("kitchentable_A_large",bar,2.5f,Vector3.zero,.65f);
        KitchenFactory.RestaurantProp("icecream_machine",bar,.76f,new Vector3(-.76f,.65f,0),-1,180);
        string[] flavours={"vanilla","strawberry","chocolate"};
        for(int i=0;i<3;i++) {
            var p=new Vector3(-.05f+i*.41f,.65f,0);
            KitchenFactory.RestaurantProp("icecream_container",bar,.37f,p);
            KitchenFactory.RestaurantProp("icecream_container_icecream_"+flavours[i],bar,.32f,p+Vector3.up*.055f);
            KitchenFactory.RestaurantProp("food_icecream_cone_"+flavours[i],bar,.27f,new Vector3(-.05f+i*.41f,.65f,-.4f));
        }
        for(int i=0;i<2;i++)KitchenFactory.RestaurantProp("chair_stool",bar,.65f,new Vector3(-.45f+i*.9f,0,-1),-1,180);
        bar.localRotation=Quaternion.Euler(0,90,0);Flush(bar,room,0,width+3.225f,true);
        var stand=Group("Sundae Display",parent);stand.localPosition=new Vector3(width+2.6f,0,-3.9f);
        KitchenFactory.RestaurantProp("kitchentable_B",stand,1.1f,Vector3.zero,.6f);
        KitchenFactory.RestaurantProp("icecream_bowl_decorated_A",stand,.38f,new Vector3(-.25f,.6f,0));
        KitchenFactory.RestaurantProp("icecream_bowl_decorated_B",stand,.38f,new Vector3(.25f,.6f,0));
        KitchenFactory.RestaurantProp("icecream_cone_stacked",stand,.28f,new Vector3(.25f,.6f,.32f));
        stand.localRotation=Quaternion.Euler(0,90,0);Flush(stand,room,0,width+3.225f,true);
    }
}
}
