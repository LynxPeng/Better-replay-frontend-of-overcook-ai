using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using OvercookedReplay;

public static class SurfaceRefinementChecks {
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static Vector2Int Cell(Vector2 uv)=>new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(uv.x*4),0,3),Mathf.Clamp(Mathf.FloorToInt(uv.y*4),0,3));
    static string VertexKey(Vector3 p,Vector3 n,Vector2 uv)=>$"{Mathf.RoundToInt(p.x*100000)},{Mathf.RoundToInt(p.y*100000)},{Mathf.RoundToInt(p.z*100000)}|{Mathf.RoundToInt(n.x*10000)},{Mathf.RoundToInt(n.y*10000)},{Mathf.RoundToInt(n.z*10000)}|{Mathf.RoundToInt(uv.x*100000)},{Mathf.RoundToInt(uv.y*100000)}";
    static Bounds BottomFootprint(RoundedWorktop body) {
        float bottom=999;var points=new System.Collections.Generic.List<Vector3>();
        foreach(var filter in body.GetComponentsInChildren<MeshFilter>())foreach(var v in filter.sharedMesh.vertices) {
            var p=body.transform.InverseTransformPoint(filter.transform.TransformPoint(v));points.Add(p);bottom=Mathf.Min(bottom,p.y);
        }
        var bounds=new Bounds();bool first=true;
        foreach(var point in points)if(point.y<bottom+.0002f) {
            var p=point;p.x*=body.BodyScale;p.z*=body.BodyScale;
            if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);
        }
        return bounds;
    }
    public static void MeshBudget() {
        var text=new StringBuilder();
        foreach(char type in new[]{'X','P'}) {
            var station=KitchenFactory.Station(type);int vertices=0,triangles=0;
            foreach(var filter in station.GetComponentInChildren<RoundedWorktop>().GetComponentsInChildren<MeshFilter>()){vertices+=filter.sharedMesh.vertexCount;triangles+=filter.sharedMesh.triangles.Length/3;}
            text.AppendLine(type+": vertices="+vertices+", triangles="+triangles);File.WriteAllText("../../output/surface-mesh-budget.txt",text.ToString());
            Require(vertices<30000&&triangles<10000,"Rounded worktop is excessively tessellated: "+text.ToString());
        }
        Debug.Log("Rounded mesh budget passed: "+text.ToString());
    }
    public static void CounterHighlightsRemoved() {
        foreach(char type in "XOTDPS") {
            var station=KitchenFactory.Station(type);
            Require(!station.transform.Find("CounterEdgeHighlight"),"Removed painted counter highlight reappeared: "+type);
            UnityEngine.Object.DestroyImmediate(station);
        }
        Debug.Log("All kitchen stations retain their native palette without a painted white perimeter");
    }
    public static void Compare() {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var light=new GameObject("Surface comparison sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.05f;light.transform.rotation=Quaternion.Euler(48,-35,0);
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.42f,.46f,.5f);
        var camera=new GameObject("Surface comparison camera").AddComponent<Camera>();camera.gameObject.AddComponent<UniversalAdditionalCameraData>();camera.fieldOfView=35;camera.aspect=4f/3;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.3f,.34f,.37f);
        foreach(string variant in new[]{"original","tinted","rounded"}) {
            var station=KitchenFactory.Station('X',variant=="rounded");
            if(variant=="original")foreach(var renderer in station.GetComponentsInChildren<Renderer>()) {
                var mat=new Material(renderer.sharedMaterial);mat.SetTexture("_BaseMap",Resources.Load<Texture2D>("RestaurantBits/Textures/restaurantbits_extra"));renderer.sharedMaterial=mat;
            }
            foreach(bool rear in new[]{false,true}) {
                camera.transform.position=new Vector3(1.3f,1.1f,rear?-1.8f:1.8f);camera.transform.LookAt(new Vector3(0,.32f,0));
                Save(camera,"../../output/surface-before-"+variant+(rear?"-rear":"-front")+".png",1280,960);
            }
            UnityEngine.Object.DestroyImmediate(station);
        }
        Debug.Log("Native, tinted and rounded worktops rendered in identical lighting");
    }
    static void Save(Camera camera,string path,int width,int height) {
        var rt=new RenderTexture(width,height,24){antiAliasing=4};
        var request=new RenderPipeline.StandardRequest{destination=rt};RenderPipeline.SubmitRenderRequest(camera,request);RenderPipeline.SubmitRenderRequest(camera,request);
        var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());RenderTexture.active=old;
        rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);
    }
    public static void CabinetSides() {
        foreach(char type in new[]{'X','P'}) {
            var raw=KitchenFactory.Station(type,false).GetComponentInChildren<RoundedWorktop>();
            var rounded=KitchenFactory.Station(type).GetComponentInChildren<RoundedWorktop>();
            var original=raw.GetComponentInChildren<MeshFilter>().sharedMesh;var result=rounded.GetComponentInChildren<MeshFilter>().sharedMesh;
            var p=result.vertices;var n=result.normals;var uv=result.uv;
            var actual=new System.Collections.Generic.HashSet<string>();for(int i=0;i<p.Length;i++)actual.Add(VertexKey(p[i],n[i],uv[i]));
            var actualTriangles=new System.Collections.Generic.HashSet<string>();var indices=result.triangles;
            for(int i=0;i<indices.Length;i+=3)actualTriangles.Add(VertexKey(p[indices[i]],n[indices[i]],uv[indices[i]])+";"+VertexKey(p[indices[i+1]],n[indices[i+1]],uv[indices[i+1]])+";"+VertexKey(p[indices[i+2]],n[indices[i+2]],uv[indices[i+2]]));
            p=original.vertices;n=original.normals;uv=original.uv;
            var filter=raw.GetComponentInChildren<MeshFilter>();int missing=0,body=0;var footprint=BottomFootprint(raw);
            for(int i=0;i<p.Length;i++) {
                var local=raw.transform.InverseTransformPoint(filter.transform.TransformPoint(p[i]));
                bool slab=type=='X'?Cell(uv[i])==new Vector2Int(3,0)&&local.y>=raw.SurfaceY-.122f:local.y>=raw.SurfaceY-.022f;
                if(slab)continue;
                var inner=footprint.extents-Vector3.one*raw.BodyCornerRadius;
                if(raw.BodyCornerRadius>0&&Mathf.Abs(local.x*raw.BodyScale-footprint.center.x)>inner.x&&Mathf.Abs(local.z*raw.BodyScale-footprint.center.z)>inner.z)continue;
                body++;
                local.x*=raw.BodyScale;local.z*=raw.BodyScale;
                var expected=filter.transform.InverseTransformPoint(raw.transform.TransformPoint(local));
                if(!actual.Contains(VertexKey(expected,n[i],uv[i])))missing++;
            }
            Require(body>50&&missing==0,$"Native cabinet body normals/UVs or uniform inset changed: {type}, missing={missing}/{body}");
            string Expected(int i) {
                if(raw.BodyScale==1)return VertexKey(p[i],n[i],uv[i]);
                var local=raw.transform.InverseTransformPoint(filter.transform.TransformPoint(p[i]));local.x*=raw.BodyScale;local.z*=raw.BodyScale;
                return VertexKey(filter.transform.InverseTransformPoint(raw.transform.TransformPoint(local)),n[i],uv[i]);
            }
            indices=original.triangles;int checkedFaces=0;
            for(int i=0;i<indices.Length;i+=3) {
                bool slab=true;
                for(int j=0;j<3;j++) {
                    int v=indices[i+j];var local=raw.transform.InverseTransformPoint(filter.transform.TransformPoint(p[v]));
                    if(local.y<raw.SurfaceY-raw.SlabDepth||local.y>raw.SurfaceY+.0002f||Cell(uv[v])!=(type=='X'?new Vector2Int(3,0):new Vector2Int(1,3)))slab=false;
                }
                if(slab)continue;
                if(raw.BodyCornerRadius>0) {
                    var inner=footprint.extents-Vector3.one*raw.BodyCornerRadius;var b=new Bounds();bool first=true;
                    for(int j=0;j<3;j++){var v=raw.transform.InverseTransformPoint(filter.transform.TransformPoint(p[indices[i+j]]));v.x*=raw.BodyScale;v.z*=raw.BodyScale;if(first){b=new Bounds(v,Vector3.zero);first=false;}else b.Encapsulate(v);}
                    if((b.min.x<footprint.center.x-inner.x||b.max.x>footprint.center.x+inner.x)&&(b.min.z<footprint.center.z-inner.z||b.max.z>footprint.center.z+inner.z))continue;
                }
                checkedFaces++;
                Require(actualTriangles.Contains(Expected(indices[i])+";"+Expected(indices[i+1])+";"+Expected(indices[i+2])),"Native body triangle shading or winding changed: "+type);
            }
            Require(checkedFaces>50,"Body shading regression did not examine enough original faces");
        }
        Debug.Log("Native cabinet bodies preserve UVs and flat shading with a uniform inset under the rounded cap");
    }
    public static void CornerContours() {
        var station=KitchenFactory.Station('X');var reference=station.GetComponentInChildren<RoundedWorktop>().transform;
        int arcEdges=0;float largestAngle=0;
        foreach(var filter in reference.GetComponentsInChildren<MeshFilter>()) {
            var mesh=filter.sharedMesh;var points=mesh.vertices;var uv=mesh.uv;var triangles=mesh.triangles;
            for(int i=0;i<triangles.Length;i+=3)for(int edge=0;edge<3;edge++) {
                int ia=triangles[i+edge],ib=triangles[i+(edge+1)%3];if(Cell(uv[ia])!=new Vector2Int(3,0)||Cell(uv[ib])!=new Vector2Int(3,0))continue;
                var a=reference.InverseTransformPoint(filter.transform.TransformPoint(points[ia]));var b=reference.InverseTransformPoint(filter.transform.TransformPoint(points[ib]));
                if(Mathf.Abs(a.y-b.y)>.0001f||a.x*b.x<=0||a.z*b.z<=0)continue;
                var da=new Vector2(Mathf.Abs(a.x)-.433f,Mathf.Abs(a.z)-.433f);var db=new Vector2(Mathf.Abs(b.x)-.433f,Mathf.Abs(b.z)-.433f);
                if(da.x<=0||da.y<=0||db.x<=0||db.y<=0)continue;
                Require(da.magnitude<=.0555f&&db.magnitude<=.0555f,"Slab corner is not the requested larger continuous roundover");
                if(Mathf.Abs(da.magnitude-.055f)<.0005f&&Mathf.Abs(db.magnitude-.055f)<.0005f&&Vector3.Distance(a,b)>.00001f){arcEdges++;largestAngle=Mathf.Max(largestAngle,Vector2.Angle(da,db));}
            }
        }
        Require(arcEdges>24&&largestAngle<15,$"Corner contour has coarse saw-cut chords: edges={arcEdges}, largest={largestAngle}");
        Debug.Log("Larger slab corners have smooth top and bottom contours without coarse cut faces");
    }
    public static void JointsAndTileEdges() {
        var style=typeof(KitchenFactory).Assembly.GetType("OvercookedReplay.CafeSurfaceStyle");Require(style!=null,"Counter and floor joint widths have no shared physical definition");
        float joint=(float)style.GetField("JointWidth").GetRawConstantValue();
        var station=KitchenFactory.Station('X');var body=station.GetComponentInChildren<RoundedWorktop>();
        float min=999,max=-999;
        foreach(var filter in body.GetComponentsInChildren<MeshFilter>())foreach(var v in filter.sharedMesh.vertices) {
            var p=body.transform.InverseTransformPoint(filter.transform.TransformPoint(v));
            if(p.y<body.SurfaceY-.001f)continue;min=Mathf.Min(min,p.x);max=Mathf.Max(max,p.x);
        }
        Require(Mathf.Abs(1-(max-min)-joint)<.001f,"Counter gap does not match the shared tile joint");
        var texture=new Texture2D(2,2);ImageConversion.LoadImage(texture,File.ReadAllBytes("Assets/Art/CafeSurfaces/CafeTile_BaseColor.png"));
        float L(int x,int y)=>texture.GetPixel(x,y).grayscale;
        int dark=0;for(int x=0;x<16;x++){if(L(x,128)<.5f)dark++;if(L(255-x,128)<.5f)dark++;}
        Require(Mathf.Abs(dark/256f-joint)<.012f,"Actual dark grout width differs from the counter gap");
        int rim=Mathf.RoundToInt((joint*.5f+.012f)*256-.5f);
        Require(L(rim,128)>L(20,128)+.025f,"Tile edge lacks the pale rounded highlight");
        Require(L(4,4)<L(4,128)-.04f,"Tile corners remain square and rigid");
        var c=texture.GetPixel(128,128);
        Require(c.r-c.g>.009f&&c.g-c.b>.035f&&c.r-c.b<.11f,"Tile warmth is not a restrained yellow greige");
        UnityEngine.Object.DestroyImmediate(texture);
        Debug.Log("Counter/floor joint width, pale bevel rim, rounded tile corners and slight warmth passed");
    }
    public static void WhiteSteam() {
        EditorSceneManager.OpenScene("Assets/Scenes/Replay.unity");
        var player=UnityEngine.Object.FindFirstObjectByType<ReplayPlayer>();player.Load("../../output/samples/asymmetric_advantages.ocrep.json");
        int frame=Array.FindIndex(player.Data.frames,f=>Array.Exists(f.state.objects,o=>o.name=="soup"&&player.Data.header.terrain[o.position[1]][o.position[0]]=='P'));
        Require(frame>=0,"Steam fixture has no pot soup");
        var soup=Array.Find(player.Data.frames[frame].state.objects,o=>o.name=="soup"&&player.Data.header.terrain[o.position[1]][o.position[0]]=='P');
        foreach(bool ready in new[]{true,false}) {
            soup.is_ready=ready;soup.is_cooking=!ready;player.Seek(frame);
            var bubbles=(System.Collections.Generic.List<Transform>)typeof(ReplayPlayer).GetField("steam",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(player);
            Require(bubbles.Count>0,"Pot has no steam");
            foreach(var bubble in bubbles) {
                var c=bubble.GetComponent<Renderer>().sharedMaterial.color;
                Require(c.r>.98f&&c.g>.98f&&c.b>.98f,"Pot steam is tinted instead of white: "+ColorUtility.ToHtmlStringRGB(c)+", ready="+ready);
            }
        }
        Debug.Log("Cooking and ready pot bubbles both use white steam");
    }
    public static void CabinetCorners() {
        var station=KitchenFactory.Station('X');var body=station.GetComponentInChildren<RoundedWorktop>();
        var footprint=BottomFootprint(KitchenFactory.Station('X',false).GetComponentInChildren<RoundedWorktop>());
        const float radius=.03548f;int curved=0,flat=0;var signedCorners=new int[4];
        foreach(var filter in body.GetComponentsInChildren<MeshFilter>()) {
            var mesh=filter.sharedMesh;var points=mesh.vertices;var normals=mesh.normals;var uv=mesh.uv;var triangles=mesh.triangles;
            for(int i=0;i<points.Length;i++) {
                var p=body.transform.InverseTransformPoint(filter.transform.TransformPoint(points[i]));
                if(p.y>=body.SurfaceY-body.SlabDepth-.01f||p.x<footprint.min.x-.001f||p.x>footprint.max.x+.001f||p.z<footprint.min.z-.001f||p.z>footprint.max.z+.001f)continue;
                var q=new Vector2(Mathf.Abs(p.x-footprint.center.x)-(footprint.extents.x-radius),Mathf.Abs(p.z-footprint.center.z)-(footprint.extents.z-radius));
                if(q.x<=0||q.y<=0)continue;
                Require(q.magnitude<=radius+.0005f,"Lower cabinet still has a square corner");
                if(q.x>.003f&&q.y>.003f&&Mathf.Abs(q.magnitude-radius)<.0005f){curved++;signedCorners[(p.x>footprint.center.x?1:0)+(p.z>footprint.center.z?2:0)]++;}
            }
            for(int i=0;i<triangles.Length;i+=3) {
                int ia=triangles[i],ib=triangles[i+1],ic=triangles[i+2];
                var a=body.transform.InverseTransformPoint(filter.transform.TransformPoint(points[ia]));var b=body.transform.InverseTransformPoint(filter.transform.TransformPoint(points[ib]));var c=body.transform.InverseTransformPoint(filter.transform.TransformPoint(points[ic]));
                if(Mathf.Max(a.y,Mathf.Max(b.y,c.y))>=body.SurfaceY-body.SlabDepth)continue;
                if(Vector3.Cross(b-a,c-a).magnitude<.02f)continue;
                bool planarX=Mathf.Max(a.x,Mathf.Max(b.x,c.x))-Mathf.Min(a.x,Mathf.Min(b.x,c.x))<.00001f;
                bool planarZ=Mathf.Max(a.z,Mathf.Max(b.z,c.z))-Mathf.Min(a.z,Mathf.Min(b.z,c.z))<.00001f;
                // Front doors intentionally have authored soft edge normals.
                // Check the original plain carcass back and outer side planes.
                if(!(planarZ&&Mathf.Min(Mathf.Abs(a.z-footprint.min.z),Mathf.Abs(a.z-footprint.max.z))<.0005f)&&!(planarX&&Mathf.Min(Mathf.Abs(a.x-footprint.min.x),Mathf.Abs(a.x-footprint.max.x))<.0005f))continue;flat++;
                Require(Vector3.Angle(normals[ia],normals[ib])<.1f&&Vector3.Angle(normals[ia],normals[ic])<.1f,$"Lower corner smoothing leaks gradients onto a flat side: a={a:F6}, b={b:F6}, c={c:F6}, n={normals[ia]:F6}/{normals[ib]:F6}/{normals[ic]:F6}, uv={uv[ia]:F4}/{uv[ib]:F4}/{uv[ic]:F4}");
            }
        }
        Require(curved>24&&flat>0,"Lower cabinet does not have curved corners and preserved planar sides");
        foreach(int count in signedCorners)Require(count>6,"A front or rear lower cabinet corner was not rounded");
        Debug.Log("Lower cabinet has rounded vertical corners and a curved base outline; broad sides retain flat shading");
    }
    public static void Inspect() {
        var report=new StringBuilder();
        foreach(char type in new[]{'X','P'}) {
            var station=KitchenFactory.Station(type,false);var round=station.GetComponentInChildren<RoundedWorktop>();
            var reference=round.transform;report.AppendLine("Station="+type+" SurfaceY="+round.SurfaceY);
            foreach(var filter in round.GetComponentsInChildren<MeshFilter>()) {
                var mesh=filter.sharedMesh;var vertices=mesh.vertices;var normals=mesh.normals;var uv=mesh.uv;var triangles=mesh.triangles;
                var cells=new System.Collections.Generic.Dictionary<string,int>();int mixed=0,rawSideGradients=0;
                for(int i=0;i<triangles.Length;i+=3) {
                    int ia=triangles[i],ib=triangles[i+1],ic=triangles[i+2];
                    var a=reference.InverseTransformPoint(filter.transform.TransformPoint(vertices[ia]));var b=reference.InverseTransformPoint(filter.transform.TransformPoint(vertices[ib]));var c=reference.InverseTransformPoint(filter.transform.TransformPoint(vertices[ic]));
                    var average=(uv[ia]+uv[ib]+uv[ic])/3;string cell=Mathf.FloorToInt(average.x*4)+","+Mathf.FloorToInt(average.y*4);
                    if(Mathf.Min(a.y,Mathf.Min(b.y,c.y))>round.SurfaceY-.14f) {
                        string key=cell+" y=["+Mathf.Min(a.y,Mathf.Min(b.y,c.y)).ToString("F3")+","+Mathf.Max(a.y,Mathf.Max(b.y,c.y)).ToString("F3")+"]";
                        if(!cells.ContainsKey(key))cells[key]=0;cells[key]++;
                    }
                    bool changed=false,unchanged=false;
                    foreach(var p in new[]{a,b,c}) {
                        var radial=new Vector2(p.x-Mathf.Clamp(p.x,-.459f,.459f),p.z-Mathf.Clamp(p.z,-.459f,.459f));
                        bool deform=p.y>=round.SurfaceY-.131f&&p.y<=round.SurfaceY+.026f&&radial.magnitude>.03501f;
                        if(deform)changed=true;else unchanged=true;
                    }
                    if(changed&&unchanged&&Mathf.Min(a.y,Mathf.Min(b.y,c.y))<round.SurfaceY-.131f)mixed++;
                    if(Mathf.Min(a.y,Mathf.Min(b.y,c.y))<-.1f&&Mathf.Max(a.y,Mathf.Max(b.y,c.y))>.15f&&Vector3.Angle(normals[ia],normals[ib])>1)rawSideGradients++;
                }
                report.AppendLine("Native vertices="+vertices.Length+", triangles="+triangles.Length/3+", body faces partly deformed by previous band="+mixed+", native body gradient faces="+rawSideGradients);
                foreach(var cell in cells)report.AppendLine("UV cell "+cell.Key+" triangles="+cell.Value);
            }
        }
        File.WriteAllText("../../output/surface-native-diagnosis.txt",report.ToString());Debug.Log(report.ToString());
    }
}
