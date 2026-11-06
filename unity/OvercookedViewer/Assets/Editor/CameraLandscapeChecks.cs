using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using OvercookedReplay;

public static class CameraLandscapeChecks {
    static void Require(bool condition,string message) {if(!condition)throw new Exception(message);}
    static object Call(object target,string method,params object[] arguments) => target.GetType().GetMethod(method).Invoke(target,arguments);
    static bool Near(Vector3 a,Vector3 b) => Vector3.Distance(a,b)<.002f;
    public static void Navigation() {
        var type=typeof(KitchenCamera).Assembly.GetType("OvercookedReplay.CameraNavigation");
        Require(type!=null,"Camera movement lock and navigation state are missing");
        var nav=Activator.CreateInstance(type);var locked=type.GetProperty("Locked");
        Require((bool)locked.GetValue(nav),"Camera must start locked");
        var camera=new GameObject("Navigation regression").AddComponent<Camera>();camera.aspect=16f/9;
        KitchenCamera.Apply(camera,9,5,KitchenView.LongEdge,1,0);
        var position=camera.transform.position;var rotation=camera.transform.rotation;
        var focus=position+camera.transform.forward*25;
        Call(nav,"Scroll",3f);Call(nav,"Move",camera,new Vector2(1,1),1f);Call(nav,"Pitch",50f);Call(nav,"Apply",camera,focus);
        Require(Near(position,camera.transform.position)&&Quaternion.Angle(rotation,camera.transform.rotation)<.01f,"Locked camera accepted movement");
        locked.SetValue(nav,false);Call(nav,"Scroll",2f);Call(nav,"Apply",camera,focus);
        Require(Vector3.Distance(camera.transform.position,focus)<Vector3.Distance(position,focus),"Wheel up must approach the focus");
        Call(nav,"Reset");Call(nav,"Move",camera,new Vector2(1,0),.5f);
        camera.transform.SetPositionAndRotation(position,rotation);Call(nav,"Apply",camera,focus);
        var translated=camera.transform.position-position;
        Require(translated.magnitude>.2f&&Mathf.Abs(translated.y)<.001f,"WASD must pan along the ground");
        Call(nav,"Reset");camera.transform.SetPositionAndRotation(position,rotation);
        var from=new Vector2(camera.pixelWidth*.48f,camera.pixelHeight*.45f);var to=from+new Vector2(70,35);
        var originalHit=Ground(camera,from);Call(nav,"Drag",camera,from,to);Call(nav,"Apply",camera,focus);
        Require(Near(originalHit,Ground(camera,to)),"Middle mouse pan does not preserve its ground anchor");
        Call(nav,"Reset");Call(nav,"Pitch",1000f);camera.transform.SetPositionAndRotation(position,rotation);Call(nav,"Apply",camera,focus);
        float angle=Vector3.Angle(camera.transform.forward,Vector3.up)-90;
        Require(angle<=80.01f&&angle>=14.99f&&Mathf.Abs(camera.transform.right.y)<.0001f,"Pitch must remain bounded without horizon roll");
        locked.SetValue(nav,true);var parked=camera.transform.position;Call(nav,"Scroll",10f);Call(nav,"Move",camera,Vector2.one,1f);Call(nav,"Pitch",-100f);
        camera.transform.SetPositionAndRotation(position,rotation);Call(nav,"Apply",camera,focus);
        Require(Near(parked,camera.transform.position),"Locking must preserve the parked navigation pose");
        var pointer=type.GetMethod("AllowsInput",BindingFlags.Public|BindingFlags.Static);
        Func<Vector2,bool,bool> allows=(point,modal)=>(bool)pointer.Invoke(null,new object[]{point,new Vector2(1920,1080),modal});
        Require(allows(new Vector2(1300,500),false),"Scene input is blocked");
        foreach(var point in new[]{new Vector2(150,500),new Vector2(1300,1030),new Vector2(1300,70),new Vector2(1200,172.5f)})Require(!allows(point,false),"UI or blank viewport margin accepted camera input");
        Require(!allows(new Vector2(1300,500),true),"Settings modal accepted camera input");
        foreach(var view in new[]{KitchenView.LongEdge,KitchenView.Isometric})foreach(int turn in new[]{0,90,180,270}) {
            Call(nav,"Reset");locked.SetValue(nav,false);
            var fitted=KitchenCamera.Apply(camera,5,9,view,1,turn,35);
            Call(nav,"Move",camera,new Vector2(-1,1),.1f);Call(nav,"Pitch",-10f);Call(nav,"Scroll",1f);Call(nav,"Apply",camera,fitted);
            Require(Mathf.Abs(camera.transform.right.y)<.0001f,"Rotated/portrait camera gained horizon roll");
            position=camera.transform.position;rotation=camera.transform.rotation;
            fitted=KitchenCamera.Apply(camera,5,9,view,1,turn,35);Call(nav,"Apply",camera,fitted);
            Require(Near(position,camera.transform.position)&&Quaternion.Angle(rotation,camera.transform.rotation)<.01f,"Reframing accumulated camera navigation");
        }
        Debug.Log("Camera navigation checks passed: default lock, dolly, ground pan, drag anchor, bounded level pitch, parked pose and UI exclusion");
    }
    static Vector3 Ground(Camera camera,Vector2 point) {
        var ray=camera.ScreenPointToRay(point);var plane=new Plane(Vector3.up,Vector3.zero);
        Require(plane.Raycast(ray,out float distance),"Test ray did not meet ground");return ray.GetPoint(distance);
    }
    public static void Gestures() {
        var frameType=typeof(KitchenCamera).Assembly.GetType("OvercookedReplay.CameraInputFrame");
        Require(frameType!=null,"Camera gesture routing needs a testable input frame");
        var nav=new CameraNavigation();nav.Locked=false;
        var camera=new GameObject("Gesture regression").AddComponent<Camera>();camera.aspect=16f/9;
        var focus=KitchenCamera.Apply(camera,9,5,KitchenView.LongEdge,1,0);var start=camera.transform.position;
        Func<Vector2,bool,bool,bool,bool,bool,Vector2,bool> tick=(point,down,held,modal,focused,pitch,axes)=> {
            var frame=Activator.CreateInstance(frameType);
            foreach(var entry in new System.Collections.Generic.Dictionary<string,object>{
                {"Pointer",point},{"ScreenSize",new Vector2(Screen.width,Screen.height)},
                {"GroundPressed",down&&!pitch},{"GroundHeld",held&&!pitch},{"PitchPressed",down&&pitch},{"PitchHeld",held&&pitch},
                {"ModalOpen",modal},{"Focused",focused},{"Axes",axes}})frameType.GetField(entry.Key).SetValue(frame,entry.Value);
            bool changed=(bool)Call(nav,"Step",camera,frame,.016f);
            var fitted=KitchenCamera.Apply(camera,9,5,KitchenView.LongEdge,1,0);nav.Apply(camera,fitted);return changed;
        };
        var scene=new Vector2(Screen.width*.65f,Screen.height*.45f);var ui=new Vector2(Screen.width*.1f,Screen.height*.45f);
        Require(!tick(ui,true,true,false,true,false,Vector2.zero),"UI press accepted pan ownership");
        Require(!tick(scene,false,true,false,true,false,Vector2.zero)&&Near(start,camera.transform.position),"Drag beginning on UI moved camera after entering scene");
        tick(scene,true,true,false,true,false,Vector2.zero);
        Require(tick(scene+new Vector2(30,15),false,true,false,true,false,Vector2.zero),"Owned middle drag was ignored");
        var parked=camera.transform.position;nav.Locked=true;tick(scene+Vector2.one*60,false,true,false,true,false,Vector2.zero);
        nav.Locked=false;Require(!tick(scene+Vector2.one*90,false,true,false,true,false,Vector2.zero)&&Near(parked,camera.transform.position),"Unlock resumed stale drag");
        tick(scene,true,true,false,true,true,Vector2.zero);var rotation=camera.transform.rotation;
        tick(scene+Vector2.up*40,false,true,true,true,true,Vector2.one);
        Require(!tick(scene+Vector2.up*70,false,true,false,true,true,Vector2.zero)&&Quaternion.Angle(rotation,camera.transform.rotation)<.01f,"Closing settings resumed stale pitch drag");
        tick(scene,true,true,false,true,false,Vector2.zero);tick(scene+Vector2.one*30,false,true,false,false,false,Vector2.one);
        Require(!tick(scene+Vector2.one*70,false,true,false,true,false,Vector2.zero),"Regaining focus resumed stale drag");
        tick(scene,true,true,false,true,false,Vector2.zero);tick(ui,false,true,false,true,false,Vector2.zero);
        Require(!tick(scene+Vector2.one*70,false,true,false,true,false,Vector2.zero),"Re-entering scene resumed a cancelled drag");
        Debug.Log("Camera gesture checks passed: UI-origin exclusion, drag ownership, lock, modal, focus and viewport cancellation");
    }
    public static void Sky() {
        var prefab=Resources.Load<GameObject>("Prefabs/ReplaySky");var sky=UnityEngine.Object.Instantiate(prefab);
        var type=typeof(KitchenCamera).Assembly.GetType("OvercookedReplay.LevelSky");
        Require(type!=null&&sky.GetComponent(type)!=null,"Sky still rotates with the camera instead of the world horizon");
        var component=sky.GetComponent(type);var camera=new GameObject("Sky target").transform;
        camera.position=new Vector3(12,25,-18);camera.rotation=Quaternion.Euler(55,37,0);
        Call(component,"Follow",camera);
        Require(Near(sky.transform.position,camera.position)&&Quaternion.Angle(sky.transform.rotation,Quaternion.identity)<.001f,"Sky horizon does not use world up");
        camera.position+=new Vector3(10,2,7);camera.rotation=Quaternion.Euler(22,130,0);Call(component,"UpdatePose");
        Require(Near(sky.transform.position,camera.position)&&Quaternion.Angle(sky.transform.rotation,Quaternion.identity)<.001f,"Sky copied camera orbit or did not follow translation");
        Debug.Log("World-aligned sky checks passed");
    }
    public static void Exterior() {
        var root=TownExterior.Build(new GameObject("Landscaping regression").transform,5,4).transform;
        foreach(string side in new[]{"Left River Garden","Right River Garden"}) {
            var garden=root.Find(side);Require(garden,"Both flanks need a landscaped river garden: "+side);
            foreach(string category in new[]{"Water","Bridge","Flower","GrassStems","Stone","Tree"}) {
                bool found=false;foreach(var node in garden.GetComponentsInChildren<Transform>())if(node.name.Contains(category))found=true;
                Require(found,side+" is missing "+category);
            }
            foreach(var renderer in garden.GetComponentsInChildren<Renderer>())Require(renderer.bounds.max.x<-.6f||renderer.bounds.min.x>4.6f,"Decorative garden enters the playable grid");
        }
        Require(root.Find("Continuous Ground"),"Finite grass plot still ends in a floating sky edge");
        Require(root.GetComponentsInChildren<Collider>().Length==0,"Landscaping may not introduce gameplay colliders");
        Debug.Log("Two river gardens and continuous-ground checks passed");
    }
    public static void InspectAssets() {
        var report=new StringBuilder();
        foreach(string name in new[]{"Floor/Water/Water","Floor/Grass/Floor_Grass_Bright","Floor/Grass/Floor_Green","Floor/Grass/Floor_Grass_Pond","Roads/Bridges/Bridge_Beach","Roads/Bridges/Bridge_Road","Roads/StonePath/StonePath_Straight","Stones/StonesX3","Props/GrassStems","Flowers/Flowers_Mixed"}) {
            var asset=Resources.Load<GameObject>("CartoonTown/Models/"+name);if(!asset){report.AppendLine(name+" MISSING");continue;}
            report.AppendLine(name+" bounds="+KitchenFactory.LocalBounds(asset,asset.transform)+" scale="+asset.transform.localScale+" rotation="+asset.transform.localEulerAngles);
        }
        File.WriteAllText("../../output/landscape-model-bounds.txt",report.ToString());Debug.Log("Landscape model bounds saved");
    }
    public static void Preview() {
        EditorSceneManager.OpenScene("Assets/Scenes/Replay.unity");
        var player=UnityEngine.Object.FindFirstObjectByType<ReplayPlayer>();player.Load("../../output/samples/cramped_room.ocrep.json");player.Seek(30);
        var camera=Camera.main;camera.rect=new Rect(0,0,1,1);camera.aspect=16f/9;
        var sky=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Prefabs/ReplaySky")).GetComponent<LevelSky>();sky.Follow(camera.transform);
        foreach(string name in new[]{"long","oblique","low-pitch","navigated"}) {
            var nav=new CameraNavigation();nav.Locked=false;
            var focus=KitchenCamera.Apply(camera,5,4,name=="oblique"?KitchenView.Isometric:KitchenView.LongEdge,1,0);
            if(name=="low-pitch")nav.Pitch(-33);
            if(name=="navigated"){nav.Scroll(3);nav.Move(camera,new Vector2(1,1),.1f);nav.Pitch(-12);}
            nav.Apply(camera,focus);sky.UpdatePose();
            var target=new RenderTexture(1920,1080,24);target.antiAliasing=4;
            // Warm the first editor render after loading native FBX materials;
            // player captures already wait for forty fully rendered frames.
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});
            var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes("../../output/garden-preview-"+name+".png",image.EncodeToPNG());
            RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);
        }
        Debug.Log("Four landscaped camera preview images saved");
    }
}
