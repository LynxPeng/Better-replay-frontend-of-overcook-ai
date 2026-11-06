using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using OvercookedReplay;

public static class PerspectiveWallChecks {
    public static void Gallery() {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        string[] names={"wall","wall_window_closed","wall_window_closed_curtains_green","wall_window_closed_curtains_red","wall_orderwindow_decorated","wall_decorated"};
        for(int i=0;i<names.Length;i++) {
            var parent=new GameObject(names[i]).transform;
            KitchenFactory.RestaurantProp(names[i],parent,2,Vector3.zero,2);
            var label=new GameObject("Name").AddComponent<TextMesh>();label.transform.SetParent(parent,false);
            label.transform.localPosition=new Vector3(0,-.25f,.6f);label.transform.rotation=Quaternion.Euler(0,180,0);
            label.text=names[i].Replace("wall_","");label.fontSize=48;label.characterSize=.035f;label.anchor=TextAnchor.MiddleCenter;label.color=Color.black;
            parent.localPosition=new Vector3((i%3-1)*3,0,-(i/3)*4);
        }
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.white*.65f;
        var sun=new GameObject("Gallery Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=.9f;sun.transform.rotation=Quaternion.Euler(55,-40,0);
        var camera=new GameObject("Gallery Camera").AddComponent<Camera>();camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
        camera.transform.position=new Vector3(0,8,13);camera.transform.LookAt(new Vector3(0,1,-1));camera.fieldOfView=45;camera.aspect=1.8f;
        camera.backgroundColor=new Color(.86f,.88f,.84f);camera.clearFlags=CameraClearFlags.SolidColor;
        var target=new RenderTexture(1800,1000,24);target.antiAliasing=4;
        RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});
        var previous=RenderTexture.active;RenderTexture.active=target;var screenshot=new Texture2D(1800,1000,TextureFormat.RGB24,false);
        screenshot.ReadPixels(new Rect(0,0,1800,1000),0,0);screenshot.Apply();File.WriteAllBytes("../../output/native-wall-front-gallery.png",screenshot.EncodeToPNG());
        RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(screenshot);
        Debug.Log("Native wall front gallery saved");
    }
    public static void Inspect() {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var report=new StringBuilder();
        foreach(string name in new[]{"wall","wall_decorated","wall_window_closed","wall_window_closed_curtains_green","wall_orderwindow_decorated"}) {
            var asset=Resources.Load<GameObject>("RestaurantBits/Models/"+name);
            var raw=UnityEngine.Object.Instantiate(asset);var bounds=KitchenFactory.LocalBounds(raw,raw.transform);
            report.AppendLine(name+" bounds="+bounds+" scale="+raw.transform.localScale+" rotation="+raw.transform.localEulerAngles);
            var parent=new GameObject("Anchor").transform;
            var fitted=KitchenFactory.RestaurantProp(name,parent,2,Vector3.zero,2);
            report.AppendLine("fitted pivot="+fitted.transform.GetChild(0).localPosition+" scale="+fitted.transform.GetChild(0).localScale);
            foreach(var filter in raw.GetComponentsInChildren<MeshFilter>()) {
                var histogram=new System.Collections.Generic.SortedDictionary<float,int>();
                foreach(var vertex in filter.sharedMesh.vertices) {
                    float z=(float)Math.Round(raw.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex)).z,2);
                    if(!histogram.ContainsKey(z))histogram[z]=0;histogram[z]++;
                }
                foreach(var pair in histogram)if(pair.Value>8)report.AppendLine("  z="+pair.Key+" vertices="+pair.Value);
            }
            UnityEngine.Object.DestroyImmediate(raw);UnityEngine.Object.DestroyImmediate(parent.gameObject);
        }
        File.WriteAllText("../../output/wall-native-geometry.txt",report.ToString());Debug.Log("Wall geometry inspection saved");
    }
    public static void CameraRegression() {
        var camera=new GameObject("Perspective regression").AddComponent<Camera>();camera.aspect=16f/9;
        KitchenCamera.Apply(camera,9,5,KitchenView.LongEdge,1,0);
        if(camera.orthographic)throw new Exception("Viewer still uses orthographic projection");
        var center=new Vector3(4,.5f,-2);var right=camera.transform.right;
        float separation=Vector3.Distance(camera.transform.position,center)*.12f;
        float near=ProjectedWidth(camera,center-camera.transform.forward*separation,right);
        float far=ProjectedWidth(camera,center+camera.transform.forward*separation,right);
        if(near<=far*1.12f)throw new Exception("Perspective must visibly make nearer objects larger");
        Debug.Log("Perspective regression passed");
    }
    static float ProjectedWidth(Camera camera,Vector3 center,Vector3 right)=>
        Vector3.Distance(camera.WorldToViewportPoint(center-right*.5f),camera.WorldToViewportPoint(center+right*.5f));
    public static void WallRegression() {
        var root=RestaurantBackdrop.Build(new GameObject("Wall regression").transform,9,5);
        foreach(var node in root.GetComponentsInChildren<Transform>())
            if(node.name=="Food Wall Picture")throw new Exception("Invented food reliefs are still hung on walls");
        Debug.Log("Wall decoration regression passed");
    }
    public static void WallAnchorRegression() {
        foreach(int width in new[]{5,8,9})CheckWallAnchors(width);
        Debug.Log("Wall anchor regression passed");
    }
    static void CheckWallAnchors(int width) {
        var root=RestaurantBackdrop.Build(new GameObject("Wall anchors").transform,width,5).transform;
        var rear=root.Find("Rear Wall");float plane=float.NaN,previousEdge=-4.5f;
        foreach(Transform module in rear) {
            if(!module.name.StartsWith("wall"))continue;
            // KayKit wall models share native pivot z=0 and x=0, independent of protruding furniture.
            var native=module.GetChild(0);float current=rear.InverseTransformPoint(native.TransformPoint(Vector3.zero)).z;
            if(float.IsNaN(plane))plane=current;
            if(Mathf.Abs(current-plane)>.001f)throw new Exception("Wall board anchors are recessed relative to neighbours");
            if(Vector3.Dot(module.forward,Vector3.back)<.99f)throw new Exception("Rear wall art faces outside the restaurant");
            var bounds=BoardBounds(module,rear);
            if(Mathf.Abs(bounds.min.x-previousEdge)>.002f)throw new Exception($"Adjacent wall panels leave a gap or overlap: room={width}, module={module.name}, bounds={bounds.min.x}..{bounds.max.x}, expectedStart={previousEdge}");
            previousEdge=bounds.max.x;
        }
        if(Mathf.Abs(previousEdge-(width+3.5f))>.002f)throw new Exception("Rear wall does not meet the side wall");
    }
    static Bounds BoardBounds(Transform module,Transform reference) {
        var native=module.GetChild(0);var result=new Bounds();bool first=true;
        foreach(var filter in native.GetComponentsInChildren<MeshFilter>())foreach(var vertex in filter.sharedMesh.vertices) {
            var point=filter.transform.TransformPoint(vertex);var original=native.InverseTransformPoint(point);
            // Include the back bevel (-0.25..-0.14); the outermost back face
            // alone is inset by 0.1 native units on this rounded wall mesh.
            // Furniture protrudes on +Z and is excluded from the board span.
            if(original.z<-.251f||original.z>-.13f)continue;
            var local=reference.InverseTransformPoint(point);
            if(first){result=new Bounds(local,Vector3.zero);first=false;}else result.Encapsulate(local);
        }
        if(first)throw new Exception("Native wall board face was not found");return result;
    }
    public static void WallFurnitureRegression() {
        foreach(int width in new[]{5,8,9}) {
            var root=RestaurantBackdrop.Build(new GameObject("Furniture clearance").transform,width,5).transform;
            foreach(Transform wall in root.Find("Rear Wall")) {
                if(wall.name!="wall_decorated")continue;
                var occupied=KitchenFactory.LocalBounds(wall.gameObject,root);
                foreach(var item in root.GetComponentsInChildren<Transform>())if(item.name=="fridge_A_decorated")
                    if(occupied.Intersects(KitchenFactory.LocalBounds(item.gameObject,root)))throw new Exception("Fridge intersects decorated wall kitchen in width "+width);
            }
        }
        Debug.Log("Wall furniture clearance regression passed");
    }
}
