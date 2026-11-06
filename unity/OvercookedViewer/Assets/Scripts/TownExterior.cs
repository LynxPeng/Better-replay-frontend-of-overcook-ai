using UnityEngine;

namespace OvercookedReplay {
public static class TownExterior {
    static Material grassBacking;
    public static readonly string[] RequiredModels={
        "Floor/Grass/Floor_Grass_Bright","Floor/Water/Water",
        "Buildings/Shops/Coffee_Shop_TopBlue","Buildings/Shops/Candy_Shop","Buildings/Houses/House_1Room_Pink","Buildings/Houses/House_2Rooms_Purple",
        "Roads/StonePath/StonePath_Straight","Roads/Bridges/Bridge_Beach",
        "Trees/Tree_Simple_Green","Trees/Tree_Fruits_Lemon","Trees/Tree_Green_x3Crowns","Trees/PineTree_Bright",
        "Flowers/Flowers_Mixed","Flowers/Flowers_Pink","Flowers/Flowers_Yellow","Flowers/Flowers_BlueLight",
        "Buildings/Shops/IceCreamCart","Props/Balloons/Blue_Balloon-Flying","Props/Decoration_PalmPlant","Laterns/LightChain",
        "Props/GrassStems","Props/GrassStemsHigh","Props/Bank","Props/Boat","Props/Decoration_FlowerBox","Props/Clock",
        "Stones/StonesX3","Stones/Stone_LowFlat_1","Fences/Fence_Field","Laterns/Latern_Path","Laterns/Latern_Small"};
    public static GameObject Build(Transform parent,int width,int height) {
        var root=new GameObject("Town Exterior");root.transform.SetParent(parent,false);var t=root.transform;
        float cx=(width-1)*.5f;
        // A continuous backing removes the apparent sloping "horizon" that
        // was actually the silhouette of a small, floating rectangular plot.
        float front=-height-8,back=21,riverFront=-180,riverBack=180;
        ContinuousGround(t,width,riverFront,riverBack);
        Lawn(t,-6.6f,width+5.6f,front,back);
        Lawn(t,-14.4f,-9.2f,front,back);Lawn(t,width+8.2f,width+13.4f,front,back);
        var street=new GameObject("Rear Street").transform;street.SetParent(t,false);
        Prop("Buildings/Shops/Coffee_Shop_TopBlue",street,3.5f,new Vector3(cx-2.8f,-.2f,11.1f),-1,180);
        Prop("Buildings/Shops/Candy_Shop",street,3.4f,new Vector3(cx+2.8f,-.2f,11.1f),-1,180);
        Prop("Buildings/Houses/House_1Room_Pink",street,3.1f,new Vector3(cx-6.5f,-.2f,12.3f),-1,180);
        Prop("Buildings/Houses/House_2Rooms_Purple",street,3.2f,new Vector3(cx+6.5f,-.2f,12.3f),-1,180);
        foreach(bool right in new[]{false,true}) {
            float riverX=right?width+6.9f:-7.9f;
            var bridge=Footprint("Roads/Bridges/Bridge_Beach",street,4.1f,3.4f,new Vector3(riverX,-.7645f,6.2f),1.05f,90);
            bridge.name=right?"Street Bridge Right":"Street Bridge Left";
            Prop("Flowers/Flowers_Mixed",street,1.25f,new Vector3(riverX+(right?2.3f:-2.3f),-.2f,9.1f));
        }
        Road(street,-18,width+17);
        for(float x=-4.8f;x<width+5;x+=2.8f) {
            Prop("Trees/Tree_Simple_Green",street,2.3f,new Vector3(x,-.2f,14));
            Prop("Flowers/Flowers_Mixed",street,1.1f,new Vector3(x,-.2f,12.5f));
            if(!NearCrossing(x,width)) {
                var lamp=Prop("Laterns/Latern_Path",street,1.85f,new Vector3(x,-.2f,4.2f));
                if(lamp)lamp.AddComponent<CafeLamp>().Bind(lamp.GetComponentInChildren<Renderer>());
            }
            if(!NearCrossing(x+1,width))Prop("Props/Decoration_FlowerBox",street,.95f,new Vector3(x+1,-.2f,4.3f),-1,180);
            Prop("Flowers/Flowers_Pink",street,.75f,new Vector3(x+.4f,-.2f,9.4f),-1,30);
        }
        foreach(float x in new[]{cx-4.5f,cx+4.5f}) {
            Prop("Props/Bank",street,1.2f,new Vector3(x,-.2f,9.2f),-1,180);
            Prop("Props/Decoration_FlowerBox",street,.85f,new Vector3(x+1,-.2f,9.2f));
        }
        Prop("Props/Clock",street,1.4f,new Vector3(cx,-.2f,9.6f),-1,180);
        // A single vendor at the far pavement, between the cafe and the clock.
        // The pack's balloon includes its tether; sway around the tied end.
        var cart=Prop("Buildings/Shops/IceCreamCart",street,1.65f,new Vector3(cx-1.6f,-.2f,8.68f),-1,160);
        if(cart) {
            var tether=new Vector3(cx-2.23f,.32f,8.61f);
            var balloon=Prop("Props/Balloons/Blue_Balloon-Flying",street,1.65f,tether);
            if(balloon)balloon.AddComponent<AmbientSway>().Configure(tether,8,.24f,.31f);
        }
        RiverGarden(t,width,height,false,riverFront,riverBack);
        RiverGarden(t,width,height,true,riverFront,riverBack);
        root.AddComponent<RiverTraffic>();
        return root;
    }
    static void Road(Transform parent,float left,float right) {
        for(float x=left;x<right-.01f;x+=2.8f) {
            float width=Mathf.Min(2.8f,right-x);
            PavedRect(parent,new Rect(x,4.5f,width,3.4f),90);
        }
    }
    static bool NearCrossing(float x,int width)=>Mathf.Abs(x+7.9f)<2.9f||Mathf.Abs(x-width-6.9f)<2.9f;
    // Form a union of paved rectangles. Existing roads and bridge footprints
    // own their area, so junctions never stack two coplanar native meshes.
    static void PavedRect(Transform parent,Rect desired,float yaw=0) {
        var pieces=new System.Collections.Generic.List<Rect>{desired};
        foreach(Transform existing in parent) {
            if(existing.name!="Roads/StonePath/StonePath_Straight"&&!existing.name.Contains("Bridge"))continue;
            var b=KitchenFactory.LocalBounds(existing.gameObject,parent);
            // The native bridge's leading posts protrude 15 mm beyond its
            // first plank after scaling. Paving reaches the plank, not the post.
            if(existing.name.Contains("Bridge"))b.min=new Vector3(b.min.x+.015f,b.min.y,b.min.z);
            var cut=Rect.MinMaxRect(b.min.x,b.min.z,b.max.x,b.max.z);
            var next=new System.Collections.Generic.List<Rect>();
            foreach(var r in pieces) {
                float left=Mathf.Max(r.xMin,cut.xMin),right=Mathf.Min(r.xMax,cut.xMax),front=Mathf.Max(r.yMin,cut.yMin),back=Mathf.Min(r.yMax,cut.yMax);
                if(right-left<.0001f||back-front<.0001f){next.Add(r);continue;}
                next.Add(Rect.MinMaxRect(r.xMin,r.yMin,left,r.yMax));next.Add(Rect.MinMaxRect(right,r.yMin,r.xMax,r.yMax));
                next.Add(Rect.MinMaxRect(left,r.yMin,right,front));next.Add(Rect.MinMaxRect(left,back,right,r.yMax));
            }pieces=next;
        }
        foreach(var r in pieces)if(r.width>.003f&&r.height>.003f)
            Footprint("Roads/StonePath/StonePath_Straight",parent,r.width,r.height,new Vector3(r.center.x,-.18f,r.center.y),.14f,yaw);
    }
    static void Lawn(Transform parent,float left,float right,float front,float back) {
        for(float x=left;x<right-.01f;x+=3)for(float z=front;z<back-.01f;z+=3) {
            float w=Mathf.Min(3,right-x),d=Mathf.Min(3,back-z);
            Footprint("Floor/Grass/Floor_Grass_Bright",parent,w,d,new Vector3(x+w*.5f,-.55f,z+d*.5f),.35f);
        }
    }
    static void ContinuousGround(Transform parent,int width,float riverFront,float riverBack) {
        if(!grassBacking) {
            var sample=KitchenFactory.Model("CartoonTown/Models/Floor/Grass/Floor_Grass_Bright",parent,1,Vector3.zero);
            var filter=sample.GetComponentInChildren<MeshFilter>();var renderer=filter.GetComponent<Renderer>();
            var vertices=filter.sharedMesh.vertices;var triangles=filter.sharedMesh.triangles;var uvs=filter.sharedMesh.uv;
            float highest=float.NegativeInfinity;Vector2 grassUV=Vector2.zero;
            // Sample an upward-facing grass triangle, never the brown tile
            // sides. Reuse the pack's atlas colour for a seamless flat backing.
            for(int i=0;i<triangles.Length;i+=3) {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                var va=filter.transform.TransformPoint(vertices[a]);var vb=filter.transform.TransformPoint(vertices[b]);var vc=filter.transform.TransformPoint(vertices[c]);
                if(Vector3.Dot(Vector3.Cross(vb-va,vc-va).normalized,Vector3.up)<.9f)continue;
                float y=(va.y+vb.y+vc.y)/3;if(y<=highest)continue;
                highest=y;grassUV=(uvs[a]+uvs[b]+uvs[c])/3;
            }
            grassBacking=new Material(renderer.sharedMaterial);grassBacking.name="CartoonTown grass ground backing";
            grassBacking.SetTextureScale("_BaseMap",Vector2.zero);grassBacking.SetTextureOffset("_BaseMap",grassUV);
            Object.DestroyImmediate(sample);
        }
        var root=new GameObject("Continuous Ground").transform;root.SetParent(parent,false);
        GroundRect(root,-256,-9.2f,-256,256);GroundRect(root,-6.6f,width+5.6f,-256,256);GroundRect(root,width+8.2f,256,-256,256);
        foreach(float x in new[]{-7.9f,width+6.9f}) {
            GroundRect(root,x-1.3f,x+1.3f,-256,riverFront);GroundRect(root,x-1.3f,x+1.3f,riverBack,256);
        }
    }
    static void GroundRect(Transform parent,float left,float right,float front,float back) {
        var plane=KitchenFactory.Part("Native grass atlas ground",parent,PrimitiveType.Quad,new Vector3((left+right)*.5f,-.205f,(front+back)*.5f),new Vector3(right-left,back-front,1),grassBacking);
        plane.transform.localRotation=Quaternion.Euler(90,0,0);plane.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
    }
    static void RiverGarden(Transform parent,int width,int height,bool right,float front,float back) {
        var garden=new GameObject(right?"Right River Garden":"Left River Garden").transform;garden.SetParent(parent,false);
        float sign=right?1:-1,riverX=right?width+6.9f:-7.9f,innerX=riverX-sign*2.15f,outerX=riverX+sign*3;
        float bridgeZ=-height*.5f+(right?2.2f:-.5f);
        for(float z=front;z<back-.01f;z+=12) {
            float length=Mathf.Min(12,back-z);
            Footprint("Floor/Water/Water",garden,2.62f,length+.02f,new Vector3(riverX,-.55f,z+length*.5f),.14f);
        }
        front=-height-6;back=16;
        Footprint("Roads/Bridges/Bridge_Beach",garden,4.1f,1.6f,new Vector3(riverX,-.7645f,bridgeZ),1.05f,90);
        // Footpaths join the bridges to the rear street; cross paths stay short
        // so the river gardens remain readable from the default kitchen view.
        for(float z=front;z<4.5f;z+=2) {
            PavedRect(garden,new Rect(innerX-.6f,z,1.2f,Mathf.Min(2,4.5f-z)));
        }
        PavedRect(garden,new Rect(outerX-sign*.8f-.8f,bridgeZ-.625f,1.6f,1.25f),90);
        string[] flowers={"Flowers_Mixed","Flowers_Pink","Flowers_Yellow","Flowers_BlueLight"};
        string[] trees={"Tree_Fruits_Lemon","Tree_Green_x3Crowns","PineTree_Bright","Tree_Simple_Green"};
        for(int i=0;i<7;i++) {
            float z=Mathf.Lerp(front+1.2f,back-1.2f,i/6f);
            if(Mathf.Abs(z-bridgeZ)<2.4f||(z>2.7f&&z<9.6f))continue;
            float stagger=i%2==0?.35f:-.25f;
            Prop("Trees/"+trees[(i+(right?1:0))%trees.Length],garden,1.9f+(i%3)*.25f,new Vector3(outerX+sign*stagger,-.2f,z));
            Prop("Flowers/"+flowers[i%flowers.Length],garden,1.05f,new Vector3(outerX-sign*.9f,-.2f,z+.7f),-1,i*37);
            Prop("Flowers/"+flowers[(i+2)%flowers.Length],garden,.85f,new Vector3(innerX-sign*.8f,-.2f,z+.4f),-1,i*53);
            Prop("Stones/StonesX3",garden,.75f,new Vector3(riverX+sign*1.6f,-.21f,z-.6f),-1,i*61);
            Prop("Stones/Stone_LowFlat_1",garden,.48f,new Vector3(riverX-sign*1.55f,-.21f,z-.9f),-1,i*41);
            for(int j=0;j<2;j++) {
                float reedX=riverX+(j==0?1:-1)*1.48f;
                if(Mathf.Abs(z-bridgeZ)>.9f)Prop("Props/"+(i%2==0?"GrassStemsHigh":"GrassStems"),garden,.5f,new Vector3(reedX,-.2f,z),-1,i*47);
            }
        }
        // Three ships total: two staggered on the left and one counterflowing.
        // RiverTraffic fits their far exit once the normal camera is framed.
        int boatCount=right?1:2;
        for(int i=0;i<boatCount;i++) {
            float from=7.1f,to=55,phase=right?.25f:i*.5f;
            var boat=Prop("Props/Boat",garden,i==0?1.05f:.95f,new Vector3(riverX,-.48f,0));
            boat.AddComponent<RiverBoatMotion>().Configure(from,to,.9f,right?-1:1,phase);
        }
        Prop("Props/Bank",garden,1.25f,new Vector3(outerX-sign*.8f,-.2f,bridgeZ-2.3f),-1,right?270:90);
        Prop("Props/Decoration_FlowerBox",garden,.85f,new Vector3(outerX-sign*.8f,-.2f,bridgeZ-3.3f));
    }
    static GameObject Footprint(string name,Transform parent,float width,float depth,Vector3 bottom,float height,float yaw=0) {
        // Scale footprint independently from height: native town ground/road
        // meshes are thick tiles, unlike the one-unit kitchen grid.
        var root=Prop(name,parent,1,bottom,height,yaw);if(!root)return null;
        var b=KitchenFactory.LocalBounds(root,root.transform);var model=root.transform.GetChild(0);
        if(Mathf.Abs(Mathf.Sin(yaw*Mathf.Deg2Rad))>.99f){float swap=width;width=depth;depth=swap;}
        var scale=model.localScale;scale.x*=width/Mathf.Max(.001f,b.size.x);scale.z*=depth/Mathf.Max(.001f,b.size.z);model.localScale=scale;
        return root;
    }
    static GameObject Prop(string name,Transform parent,float size,Vector3 bottom,float height=-1,float yaw=0) {
        var root=new GameObject(name);root.transform.SetParent(parent,false);
        var model=KitchenFactory.Model("CartoonTown/Models/"+name,root.transform,size,Vector3.zero);
        if(!model){Object.DestroyImmediate(root);return null;}
        var b=KitchenFactory.LocalBounds(model,root.transform);float actualHeight=b.size.y;
        if(height>0&&actualHeight>.0001f) {
            float factor=height/actualHeight;var s=model.transform.localScale;s.y*=factor;model.transform.localScale=s;
            var p=model.transform.localPosition;p.y*=factor;model.transform.localPosition=p;actualHeight=height;
        }
        root.transform.localPosition=bottom+Vector3.up*(actualHeight*.5f);root.transform.localRotation=Quaternion.Euler(0,yaw,0);
        if(name.StartsWith("Trees/")||name.StartsWith("Flowers/")||name.StartsWith("Props/GrassStems")) {
            float amplitude=name.StartsWith("Trees/")?2.2f:name.StartsWith("Flowers/")?4.5f:6.5f;
            root.AddComponent<AmbientSway>().Configure(bottom,amplitude,.33f,Mathf.Repeat(bottom.x*.11f+bottom.z*.073f,1));
        }
        return root;
    }
}
}
