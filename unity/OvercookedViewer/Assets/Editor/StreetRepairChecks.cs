using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor.SceneManagement;
using OvercookedReplay;

public static class StreetRepairChecks {
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static Bounds Bounds(Transform t)=>KitchenFactory.LocalBounds(t.gameObject,t.root);
    public static void Diagnose(){
        var root=TownExterior.Build(new GameObject("Diagnosis").transform,9,5).transform;
        foreach(var item in root.GetComponentsInChildren<Transform>().Where(t=>t.name.Contains("Bridge")&&t.childCount>0&&t.GetComponentInChildren<MeshFilter>())){
            var areas=new Dictionary<int,float>();
            foreach(var f in item.GetComponentsInChildren<MeshFilter>()){
                var v=f.sharedMesh.vertices;var tri=f.sharedMesh.triangles;
                for(int i=0;i<tri.Length;i+=3){var a=f.transform.TransformPoint(v[tri[i]]);var b=f.transform.TransformPoint(v[tri[i+1]]);var c=f.transform.TransformPoint(v[tri[i+2]]);var cross=Vector3.Cross(b-a,c-a);if(cross.normalized.y<.98f)continue;int y=Mathf.RoundToInt((a.y+b.y+c.y)/3*1000);if(!areas.ContainsKey(y))areas[y]=0;areas[y]+=cross.magnitude*.5f;}
            }
            Debug.Log(item.name+" bounds="+Bounds(item)+" horizontal surfaces="+string.Join(";",areas.OrderByDescending(p=>p.Value).Take(6).Select(p=>p.Key+":"+p.Value)));
            var bb=Bounds(item);float first=float.PositiveInfinity,last=float.NegativeInfinity;
            for(int j=0;j<=1000;j++){float x=Mathf.Lerp(bb.min.x,bb.max.x,j/1000f),y=Surface(item,x,bb.center.z);if(Mathf.Abs(y+.04f)<.04f){first=Mathf.Min(first,x);last=Mathf.Max(last,x);}}
            Debug.Log(item.name+" deck end inset="+(first-bb.min.x)+","+(bb.max.x-last));
        }
    }
    public static void Geometry(){
        var root=TownExterior.Build(new GameObject("Road repairs").transform,9,5).transform;
        var roads=root.GetComponentsInChildren<Transform>().Where(t=>t.name=="Roads/StonePath/StonePath_Straight").ToArray();
        for(int i=0;i<roads.Length;i++)for(int j=i+1;j<roads.Length;j++){
            var a=Bounds(roads[i]);var b=Bounds(roads[j]);float x=Mathf.Min(a.max.x,b.max.x)-Mathf.Max(a.min.x,b.min.x),z=Mathf.Min(a.max.z,b.max.z)-Mathf.Max(a.min.z,b.min.z);
            Require(x<.002f||z<.002f,$"Road surfaces overlap: {roads[i].position}, {roads[j].position}, overlap={x},{z}");
        }
        Require(!root.GetComponentsInChildren<Transform>().Any(t=>t.name=="Fences/Fence_Field"),"River posts still share positions with trees");
        Require(!root.GetComponentsInChildren<Transform>().Any(t=>t.name=="Laterns/Latern_Small"),"River lamps still share the narrow bank with rocks and reeds");
        Debug.Log("Road patches meet without overlapping, river fence posts removed");
    }
    static float Surface(Transform root,float x,float z){
        float highest=float.NegativeInfinity;
        foreach(var f in root.GetComponentsInChildren<MeshFilter>()){
            var v=f.sharedMesh.vertices;var tri=f.sharedMesh.triangles;
            for(int i=0;i<tri.Length;i+=3){var a=f.transform.TransformPoint(v[tri[i]]);var b=f.transform.TransformPoint(v[tri[i+1]]);var c=f.transform.TransformPoint(v[tri[i+2]]);
                float d=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(d)<1e-8f)continue;
                float u=((b.z-c.z)*(x-c.x)+(c.x-b.x)*(z-c.z))/d,w=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/d;
                if(u>=0&&w>=0&&u+w<=1)highest=Mathf.Max(highest,u*a.y+w*b.y+(1-u-w)*c.y);
            }
        }return highest;
    }
    public static void BridgeLevels(){
        var root=TownExterior.Build(new GameObject("Bridge level repairs").transform,9,5).transform;
        foreach(var bridge in root.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Street Bridge")||t.name=="Roads/Bridges/Bridge_Beach")){
            var b=Bounds(bridge);foreach(float x in new[]{b.min.x+.01f,b.min.x+.25f,b.center.x,b.max.x-.25f,b.max.x-.01f}){
                float y=Surface(root,x,b.center.z);Debug.Log(bridge.name+" deck/approach at "+x+" = "+y);
                Require(Mathf.Abs(y+.04f)<.035f,"Bridge deck is not aligned to road surface: "+y);
            }
        }Debug.Log("All bridge decks and end planks align to the road surface");
    }
    public static void Turning(){
        EditorSceneManager.OpenScene("Assets/Scenes/Replay.unity");var player=UnityEngine.Object.FindFirstObjectByType<ReplayPlayer>();
        player.Load(Path.Combine(Application.streamingAssetsPath,"Samples/asymmetric_advantages.ocrep.json"));
        var actor=UnityEngine.Object.FindFirstObjectByType<BlueFishActor>();int count=0;
        for(int i=0;i<player.Data.segments.Length;i++){
            var a=player.Data.frames[i];var b=player.Data.frames[i+1];var from=a.state.players[0].orientation;var to=b.state.players[0].orientation;
            if(from.SequenceEqual(to)||player.Data.segments[i].kind!="step"||b.time-a.time<.14f)continue;
            player.Clock.Seek(a.time+.12f);player.SendMessage("Apply");
            var target=Quaternion.Euler(0,Mathf.Atan2(to[0],-to[1])*Mathf.Rad2Deg,0);
            Require(Quaternion.Angle(actor.transform.rotation,target)<1,"Chef takes a whole replay interval to turn");count++;
            if(count==1){float saved=b.time;b.time=a.time;player.Seek(i);
                var selected=Quaternion.Euler(0,Mathf.Atan2(from[0],-from[1])*Mathf.Rad2Deg,0);
                Require(Quaternion.Angle(actor.transform.rotation,selected)<1,"Zero-duration interval changed the selected snapshot facing");b.time=saved;}
        }
        Require(count>4,"Insufficient recorded turns examined");Debug.Log("Fast recorded facing transitions passed: "+count);
    }
    public static void Camera(){
        var cam=new GameObject("Lower default camera").AddComponent<Camera>();cam.aspect=16f/9*.77f/.83f;
        KitchenCamera.Apply(cam,9,5,KitchenView.LongEdge,1,0,48);float oldY=cam.transform.position.y;
        var p=new ViewerPreferences();Require(p.cameraPitch<=43,"Default pitch has not been lowered");
        KitchenCamera.Apply(cam,9,5,KitchenView.LongEdge,.9f,0,p.cameraPitch);Require(cam.transform.position.y<oldY-1,"Default camera is not lower");
        Debug.Log("Lower, closer default camera passed");
    }
}
