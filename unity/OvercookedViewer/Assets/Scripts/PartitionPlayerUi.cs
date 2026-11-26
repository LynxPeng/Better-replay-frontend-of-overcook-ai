using UnityEngine;

namespace OvercookedReplay {
public partial class ReplayPlayer {
    void PartitionOrder(Order order,float y){
        ui.Panel(new Rect(0,y,224,60),soft:true);
        ui.Label(new Rect(10,y+4,206,22),PartitionReplay.Recipes[order.recipe],ui.Heading);
        int remaining=Mathf.Max(0,order.due-Data.frames[Clock.Index].state.timestep);
        string id=order.id<0?"编号未记录":"#"+order.id;
        ui.Label(new Rect(10,y+26,206,22),$"{id} · {(order.side==0?"左":"右")}侧 · 剩 {remaining} 步",ui.Caption);
        if(order.issued>=0&&order.due>order.issued){
            float ratio=Mathf.Clamp01(remaining/(float)(order.due-order.issued));
            ui.Fill(new Rect(10,y+53,204,3),ui.Line);
            ui.Fill(new Rect(10,y+53,204*ratio,3),remaining<=10?ui.OrangeSeat:ui.Marine);
        }
    }
    void PartitionEventRows(ref float y){
        var raw=Data.frames[Clock.Index].state.partition;
        if(Data.header.environment=="partition_v3_legacy"&&
           (raw.ids?.Length!=3||raw.issued?.Length!=3||raw.locked?.Length!=2||raw.deposited?.Length!=5)){
            ui.Label(new Rect(2,y,222,22),"旧录制：部分工位信息未记录",ui.Caption);y+=26;
        }
        if(Clock.Index>0){
            var events=Data.segments[Clock.Index-1].events;
            if(events!=null)foreach(var e in events){
                string text=null;
                if(e.kind=="meal_delivery")text="交付成功"+(e.order_id>=0&&Data.header.schema_version==2?" · #"+e.order_id:"");
                if(e.kind=="order_expired")text="订单超时 · #"+e.order_id;
                if(e.kind=="counter_pickup")text=e.wait_steps<0?"台面取货 · 等待时长未记录":"台面取货 · 等待 "+e.wait_steps+" 步";
                if(text!=null){ui.Label(new Rect(2,y,222,22),text,ui.Caption);y+=25;}
            }
        }
        ui.Label(new Rect(2,y,222,22),"工位高亮表示当前朝向",ui.Caption);y+=28;
    }
    void PartitionLabels(float scale){
        var state=Data.frames[Clock.Index].state.partition;
        for(int y=0;y<Data.header.terrain.Length;y++)for(int x=0;x<Data.header.terrain[y].Length;x++){
            string label=PartitionKitchenView.Label(Data.header.terrain[y][x],x,y,state);if(label==null)continue;
            var point=Camera.main.WorldToScreenPoint(new Vector3(x,1.2f,-y));if(point.z<=0)continue;
            var rect=new Rect(point.x/scale-70,(Screen.height-point.y)/scale-10,140,20);
            if(rect.xMin<300||rect.xMax>1270||rect.yMin<79||rect.yMax>Screen.height/scale*.83f)continue;
            ui.Label(new Rect(rect.x+1,rect.y+1,rect.width,rect.height),label,ui.NameShadow);
            ui.Label(rect,label,ui.Name);
        }
    }
}
}
