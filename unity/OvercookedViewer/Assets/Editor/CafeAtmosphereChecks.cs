using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using OvercookedReplay;

public static class CafeAtmosphereChecks {
    static void Require(bool value,string message) {if(!value)throw new Exception(message);}
    public static void RoundedCounters() {
        foreach(string name in new[]{"kitchencounter_straight_B","stove_single"}) {
            var asset=Resources.Load<GameObject>("RestaurantBits/Models/"+name);
            foreach(var filter in asset.GetComponentsInChildren<MeshFilter>())Require(filter.sharedMesh.isReadable,"Build will strip CPU mesh data required for rounded corners: "+name);
        }
        var station=KitchenFactory.Station('X');int curved=0;
        foreach(var filter in station.GetComponentsInChildren<MeshFilter>())foreach(var vertex in filter.sharedMesh.vertices) {
            var p=station.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
            if(p.y<.595f)continue;
            var corner=new Vector2(Mathf.Abs(p.x)-.433f,Mathf.Abs(p.z)-.433f);
            if(corner.x>0&&corner.y>0) {
                Require(corner.magnitude<=.0555f,"Counter still has sharp slab corners");
                if(corner.x>.004f&&corner.y>.004f)curved++;
            }
        }
        Require(curved>=12,"Counter corner has no curved silhouette segments");
        Debug.Log("Rounded native counter silhouette passed without widening grid seams");
    }
    public static void DayNight() {
        var type=typeof(KitchenCamera).Assembly.GetType("OvercookedReplay.SceneAtmosphere");Require(type!=null,"Day/night atmosphere controller is missing");
        var method=type.GetMethod("Apply");Require(method!=null,"Day/night toggle is missing");
        EditorSceneManager.OpenScene("Assets/Scenes/Replay.unity");
        var room=RestaurantBackdrop.Build(new GameObject("Modes check").transform,9,5);
        var sky=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/ReplaySky"));
        Light point=null;foreach(var light in room.GetComponentsInChildren<Light>())if(light.type==LightType.Point)point=light;
        method.Invoke(null,new object[]{true});Require(point.enabled&&RenderSettings.ambientLight.maxColorComponent<.3f,"Night did not enable point light and reduce ambient");
        var dome=Array.Find(sky.GetComponentsInChildren<Renderer>(true),r=>r.name=="SkyDome");var block=new MaterialPropertyBlock();dome.GetPropertyBlock(block);
        Require(block.GetVector("_BaseMap_ST").z>.3f,"Night sky did not switch atlas column");
        foreach(var node in sky.GetComponentsInChildren<Transform>(true))if(node.name=="Sun"||node.name=="Sun 1")Require(!node.gameObject.activeSelf,"Sun remains visible at night");
        method.Invoke(null,new object[]{false});Require(!point.enabled&&RenderSettings.ambientLight.maxColorComponent>.4f,"Day did not restore even bright illumination");
        dome.GetPropertyBlock(block);Require(Mathf.Abs(block.GetVector("_BaseMap_ST").z)<.001f,"Day sky did not restore noon");
        var night=typeof(ViewerPreferences).GetField("night");Require(night!=null,"Atmosphere preference is not persisted");
        var prefs=new ViewerPreferences();night.SetValue(prefs,true);
        var path="../../output/day-night-preferences.json";prefs.Save(path);Require((bool)night.GetValue(ViewerPreferences.Load(path)),"Night preference did not round-trip");
        Debug.Log("Day/night lighting, native sky, sun visibility and saved mode passed");
    }
    public static void Surfaces() {
        var material=Resources.Load<Material>("Materials/RestaurantTile");
        Require(material&&material.GetTexture("_BumpMap"),"Tile bevel normals are missing");
        var texture=material.mainTexture as Texture2D;
        Require(texture&&texture.width>=1024&&texture.wrapMode==TextureWrapMode.Repeat,"Tile asset is too small or does not repeat");
        foreach(var size in new[]{new Vector2Int(5,4),new Vector2Int(9,5)}) {
            var floor=RestaurantFloor.Build(new GameObject("Tile UV check").transform,size.x,size.y);
            var block=new MaterialPropertyBlock();floor.GetComponent<Renderer>().GetPropertyBlock(block);
            var uv=block.GetVector("_BaseMap_ST");
            Require(Mathf.Abs(uv.x*4-floor.transform.localScale.x)<.001f&&Mathf.Abs(uv.y*4-floor.transform.localScale.y)<.001f,"Tiles change world size between layouts");
        }
        var counter=KitchenFactory.Station('X');
        foreach(var renderer in counter.GetComponentsInChildren<Renderer>())Require(renderer.sharedMaterial==Resources.Load<Material>("Materials/CafeCounter"),"Counter honey palette was not applied");
        var food=KitchenFactory.Item("tomato",new GameObject("Food palette check").transform,Vector3.zero);
        foreach(var renderer in food.GetComponentsInChildren<Renderer>())Require(renderer.sharedMaterial.mainTexture.name=="restaurantbits_extra","Counter palette leaked into food atlas");
        Debug.Log("Tile scale, bevel normals and isolated counter/food palettes passed");
    }
    public static void Lighting() {
        EditorSceneManager.OpenScene("Assets/Scenes/Replay.unity");
        var root=RestaurantBackdrop.Build(new GameObject("Lighting check").transform,9,5);
        SceneAtmosphere.Apply(true);
        var lights=root.GetComponentsInChildren<Light>();Light centre=null;
        foreach(var light in lights)if(light.type==LightType.Point)centre=light;
        Require(centre,"Restaurant has no central point light");
        Require(Vector2.Distance(new Vector2(centre.transform.localPosition.x,centre.transform.localPosition.z),new Vector2(4,-2))<.6f,"Point light is not centred above the room");
        Require(centre.shadows==LightShadows.Soft&&centre.range>12,"Central light lacks soft shadows or room coverage");
        Require(RenderSettings.ambientLight.maxColorComponent<.3f,"Flat ambient illumination still dominates the room");
        var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        Require(pipeline&&pipeline.supportsAdditionalLightShadows,"Point-light shadow support is disabled");
        Debug.Log("Central warm point light, soft shadows and reduced ambient checks passed");
    }
    public static void CameraHeight() {
        var shift=typeof(CameraInputFrame).GetField("ShiftHeld");Require(shift!=null,"Shift-wheel height routing is missing");
        var camera=new GameObject("Height input check").AddComponent<Camera>();camera.aspect=16f/9;
        var nav=new CameraNavigation();
        var input=new CameraInputFrame {Pointer=new Vector2(1250,500),ScreenSize=new Vector2(1920,1080),Focused=true,Wheel=-2};
        object boxed=input;shift.SetValue(boxed,true);input=(CameraInputFrame)boxed;
        Action apply=()=>nav.Apply(camera,KitchenCamera.Apply(camera,9,5,KitchenView.LongEdge,1,0));
        apply();var initial=camera.transform.position;var rotation=camera.transform.rotation;
        nav.Step(camera,input,.016f);apply();Require(Vector3.Distance(initial,camera.transform.position)<.001f,"Locked camera accepted height input");
        nav.Locked=false;Require(nav.Step(camera,input,.016f),"Shift wheel ignored in scene");apply();
        var lower=camera.transform.position;
        Require(lower.y<initial.y-.5f&&Mathf.Abs(lower.x-initial.x)<.001f&&Mathf.Abs(lower.z-initial.z)<.001f,"Height gesture changes zoom or ground position");
        Require(Quaternion.Angle(rotation,camera.transform.rotation)<.001f,"Height gesture changes camera pitch");
        input.ModalOpen=true;Require(!nav.Step(camera,input,.016f),"Settings accepted height input");
        input.ModalOpen=false;input.Focused=false;Require(!nav.Step(camera,input,.016f),"Unfocused window accepted height input");
        input.Focused=true;input.Pointer=new Vector2(100,500);Require(!nav.Step(camera,input,.016f),"UI accepted height input");
        apply();Require(Vector3.Distance(lower,camera.transform.position)<.001f,"Blocked height input changed offsets");
        nav.Reset();apply();Require(Vector3.Distance(initial,camera.transform.position)<.001f,"Camera reset does not clear elevation");
        input.Pointer=new Vector2(1250,500);input.Wheel=2;boxed=input;shift.SetValue(boxed,false);input=(CameraInputFrame)boxed;
        nav.Step(camera,input,.016f);apply();Require(camera.transform.position.y<initial.y&&Mathf.Abs(camera.transform.position.z-initial.z)>.1f,"Ordinary wheel lost its dolly zoom");
        Debug.Log("Shift-wheel elevation passed: independent height, preserved pitch/zoom, lock, UI, focus and reset");
    }
    public static void WallEquipment() {
        foreach(var size in new[]{new Vector2Int(5,4),new Vector2Int(5,5),new Vector2Int(9,5)}) {
            var room=RestaurantBackdrop.Build(new GameObject("Wall equipment check").transform,size.x,size.y).transform;
            var grid=new Bounds(new Vector3((size.x-1)*.5f,.5f,-(size.y-1)*.5f),new Vector3(size.x,1,size.y));
            int fridges=0,stations=0;
            foreach(Transform item in room.Find("Dining"))if(item.name.StartsWith("fridge_")||item.name=="Icecream Bar"||item.name=="Sundae Display") {
                var bounds=KitchenFactory.LocalBounds(item.gameObject,room);
                float gap=Mathf.Min(Mathf.Abs(bounds.min.x+4.25f),Mathf.Abs(bounds.max.x-(size.x+3.25f)));
                Require(gap<=.065f,"Decorative equipment is floating away from side walls: "+item.name+" "+size+" gap="+gap);
                Require(!bounds.Intersects(grid),"Wall equipment enters the recorded game grid: "+item.name);
                if(item.name.StartsWith("fridge_")) {
                    fridges++;Require((item.localPosition.x<0?item.forward.x:-item.forward.x)>.99f,"Fridge door faces the wall");
                }
            }
            foreach(Transform item in room.Find("Street")) {
                var bounds=KitchenFactory.LocalBounds(item.gameObject,room);
                Require(Mathf.Abs(bounds.max.z-3.25f)<=.065f,"Service equipment floats away from the rear wall: "+item.name);
                Require(!bounds.Intersects(grid),"Service equipment enters the recorded grid");stations++;
            }
            Require(fridges==2&&stations>0,"Wall alignment lost equipment");
        }
        Debug.Log("Fridges, dessert equipment and rear service stations sit against walls without entering the game grid");
    }
    public static void Traffic() {
        foreach(var size in new[]{new Vector2Int(5,4),new Vector2Int(5,5),new Vector2Int(9,5)}) {
            var town=TownExterior.Build(new GameObject("Traffic coverage").transform,size.x,size.y);
            var boats=town.GetComponentsInChildren<RiverBoatMotion>();
            Require(boats.Length<=3,"Fleet exceeds the requested maximum of three boats");
            var camera=new GameObject("Traffic camera").AddComponent<Camera>();
            foreach(float aspect in new[]{16f/9*.77f/.83f,16f/10*.77f/.83f})foreach(float pitch in new[]{35f,48f,65f})foreach(var mode in new[]{KitchenView.LongEdge,KitchenView.Isometric}) {
                camera.aspect=aspect;KitchenCamera.Apply(camera,size.x,size.y,mode,1,0,pitch);
                town.GetComponent<RiverTraffic>().Fit(camera);
                foreach(var boat in boats) {
                    float end=(float)typeof(RiverBoatMotion).GetField("end",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(boat);
                    var bounds=KitchenFactory.LocalBounds(boat.gameObject,town.transform);bounds.center+=Vector3.forward*(end-boat.transform.position.z);
                    float minX=999,maxX=-999,minY=999,maxY=-999;
                    for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2) {
                        var v=camera.WorldToViewportPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));
                        minX=Mathf.Min(minX,v.x);maxX=Mathf.Max(maxX,v.x);minY=Mathf.Min(minY,v.y);maxY=Mathf.Max(maxY,v.y);
                    }
                    Require(maxX<0||minX>1||maxY<0||minY>1,$"Boat far exit pops inside {mode}: x=[{minX},{maxX}], y=[{minY},{maxY}], pitch={pitch}, layout={size}");
                }
                for(double time=0;time<100;time+=.25) {
                    bool visible=false;
                    foreach(var boat in boats) {
                        boat.Evaluate(time);var p=boat.transform.position;
                        // Conservative visible river segment: clear of the rear
                        // room walls and of the upper crossing's deck/rails.
                        if(p.z<8.4f)continue;
                        var v=camera.WorldToViewportPoint(p);
                        if(v.z>0&&v.x>.01f&&v.x<.99f&&v.y>.01f&&v.y<.99f){visible=true;break;}
                    }
                    if(!visible) {
                        float first=999,last=-999;
                        for(float z=8.5f;z<35;z+=.05f)foreach(float x in new[]{-7.9f,size.x+6.9f}) {
                            var v=camera.WorldToViewportPoint(new Vector3(x,-.35f,z));
                            if(v.z>0&&v.x>.025f&&v.x<.975f&&v.y>.04f&&v.y<.96f){first=Mathf.Min(first,z);last=Mathf.Max(last,z);}
                        }
                        throw new Exception($"Both rivers have no clearly visible boat: size={size}, mode={mode}, pitch={pitch}, aspect={aspect}, time={time}, visible river Z=[{first},{last}]");
                    }
                }
            }
            UnityEngine.Object.DestroyImmediate(town);
        }
        Debug.Log("Full-loop boat coverage passed across normal layouts, pitches and aspect ratios");
    }
}
