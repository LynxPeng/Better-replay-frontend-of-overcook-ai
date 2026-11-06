using UnityEngine;
using System.Collections.Generic;

namespace OvercookedReplay {
public static class KitchenFactory {
    public const float CounterTopHeight=.60f;
    static readonly Dictionary<string,Material> palette=new Dictionary<string,Material>();
    static readonly Dictionary<Material,Material> imported=new Dictionary<Material,Material>();
    public static Material Material(string hex) {
        if(palette.TryGetValue(hex,out var cached)&&cached)return cached;
        ColorUtility.TryParseHtmlString(hex,out var color);
        var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name="Palette "+hex;m.color=color;
        m.SetFloat("_Smoothness",.10f);
        m.SetFloat("_SpecularHighlights",0);m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.SetFloat("_EnvironmentReflections",0);m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");palette[hex]=m;return m;
    }
    public static GameObject Part(string name,Transform parent,PrimitiveType type,Vector3 pos,Vector3 scale,Material material) {
        var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.SetParent(parent,false);
        g.transform.localPosition=pos;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=material;
        Object.DestroyImmediate(g.GetComponent<Collider>());return g;
    }
    public static GameObject Model(string path,Transform parent,float size,Vector3 position,Material tint=null,float verticalScale=1) {
        var asset=Resources.Load<GameObject>(path);if(!asset)return null;
        var g=Object.Instantiate(asset);g.transform.SetParent(parent,false);g.transform.localPosition=Vector3.zero;
        var renderers=g.GetComponentsInChildren<Renderer>();if(renderers.Length==0)return g;
        var b=LocalBounds(g,parent);
        float scale=size/Mathf.Max(.001f,Mathf.Max(b.size.x,Mathf.Max(b.size.y,b.size.z)));
        var factors=new Vector3(scale,scale*verticalScale,scale);
        g.transform.localScale=Vector3.Scale(g.transform.localScale,factors);g.transform.localPosition=position-Vector3.Scale(b.center,factors);
        foreach(var r in renderers) {
            if(tint){r.sharedMaterial=tint;continue;}
            var materials=r.sharedMaterials;
            for(int i=0;i<materials.Length;i++) {
                var original=materials[i];if(!original)continue;
                if(!imported.TryGetValue(original,out var replacement)||!replacement) {
                    Color color=original.HasProperty("_BaseColor")?original.GetColor("_BaseColor"):original.HasProperty("_Color")?original.color:Color.white;
                    if(path.StartsWith("Models/Nature/")&&original.name.Contains("leafs"))ColorUtility.TryParseHtmlString("#8eb761",out color);
                    replacement=new Material(Material("#"+ColorUtility.ToHtmlStringRGBA(color)));
                    replacement.name="URP "+original.name;
                    var texture=original.mainTexture;
                    if(!texture&&path.StartsWith("Models/City/"))texture=Resources.Load<Texture2D>("Models/City/Textures/colormap");
                    if(path.StartsWith("Models/KayKit/"))texture=Resources.Load<Texture2D>("Models/KayKit/restaurantbits_texture");
                    if(path.StartsWith("RestaurantBits/"))texture=Resources.Load<Texture2D>("RestaurantBits/Textures/restaurantbits_extra");
                    if(path.StartsWith("CartoonTown/")) {
                        string atlas=original.name.Contains("Floor")?"Floor":original.name.Contains("Glossy")?"GlossyColors":"Colors";
                        texture=Resources.Load<Texture2D>("CartoonTown/Textures/"+atlas);
                    }
                    if(texture)replacement.SetTexture("_BaseMap",texture);
                    imported[original]=replacement;
                }
                materials[i]=replacement;
            }
            r.sharedMaterials=materials;
        }
        return g;
    }
    public static Bounds LocalBounds(GameObject root,Transform relativeTo) {
        var bounds=new Bounds();bool first=true;
        foreach(var renderer in root.GetComponentsInChildren<Renderer>()) {
            var local=renderer.localBounds;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2) {
                var corner=local.center+Vector3.Scale(local.extents,new Vector3(x,y,z));
                var point=relativeTo.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
            }
        }
        return bounds;
    }
    public static GameObject RestaurantProp(string name,Transform parent,float size,Vector3 bottom,float height=-1,float yaw=0) {
        var root=new GameObject(name);root.transform.SetParent(parent,false);
        string path=Resources.Load<GameObject>("RestaurantBits/Models/"+name)?"RestaurantBits/Models/"+name:"Models/KayKit/"+name;
        // Isolate worktops from food and props sharing the source atlas.
        var tint=name.StartsWith("kitchencounter_")?Resources.Load<Material>("Materials/CafeCounter"):null;
        var model=Model(path,root.transform,size,Vector3.zero,tint);
        if(!model){Object.DestroyImmediate(root);return null;}
        var bounds=LocalBounds(model,root.transform);float actualHeight=bounds.size.y;
        if(height>0&&actualHeight>.0001f) {
            float factor=height/actualHeight;var scale=model.transform.localScale;scale.y*=factor;model.transform.localScale=scale;
            var position=model.transform.localPosition;position.y*=factor;model.transform.localPosition=position;actualHeight=height;
        }
        root.transform.localPosition=bottom+Vector3.up*(actualHeight*.5f);
        root.transform.localRotation=Quaternion.Euler(0,yaw,0);return root;
    }
    public static GameObject RestaurantWall(string name,Transform parent,float span,Vector3 anchor,float yaw) {
        var root=new GameObject(name);root.transform.SetParent(parent,false);
        string path="RestaurantBits/Models/"+name;var asset=Resources.Load<GameObject>(path);
        if(!asset){Object.DestroyImmediate(root);return null;}
        var model=Model(path,root.transform,span,Vector3.zero);
        // These modules share a 4-unit native wall panel, with its ground pivot
        // at (0,0,0). Protruding cookers/sills must not recenter the wall board.
        model.transform.localScale=asset.transform.localScale*(span/4f);
        // The half panel is authored from one edge. Use imported coordinates
        // (FBX axis conversion can reflect X), preserving the wall plane z=0.
        float centerX=name=="wall_half"?LocalBounds(asset,asset.transform).center.x:0;
        model.transform.localPosition=new Vector3(-centerX*model.transform.localScale.x,0,0);
        root.transform.localPosition=anchor;root.transform.localRotation=Quaternion.Euler(0,yaw,0);return root;
    }
    public static GameObject Chef(Color color) {
        var root=new GameObject("Chef");var t=root.transform;
        var cloth=Material("#"+ColorUtility.ToHtmlStringRGBA(color));
        var cream=Material("#fff4dc");var skin=Material("#edbc91");var dark=Material("#293744");
        Part("Body",t,PrimitiveType.Capsule,new Vector3(0,.46f,0),new Vector3(.46f,.34f,.34f),cloth);
        Part("Apron",t,PrimitiveType.Cube,new Vector3(0,.45f,.175f),new Vector3(.3f,.37f,.045f),cream);
        Part("Head",t,PrimitiveType.Sphere,new Vector3(0,.89f,0),Vector3.one*.36f,skin);
        Part("HatBrim",t,PrimitiveType.Cylinder,new Vector3(0,1.07f,0),new Vector3(.4f,.045f,.4f),cream);
        for(int i=-1;i<=1;i++)Part("Hat",t,PrimitiveType.Sphere,new Vector3(i*.095f,1.21f,0),new Vector3(.23f,.27f,.29f),cream);
        for(int i=-1;i<=1;i+=2) {
            Part("Eye",t,PrimitiveType.Sphere,new Vector3(i*.065f,.93f,.158f),Vector3.one*.035f,dark);
            Part("Moustache",t,PrimitiveType.Sphere,new Vector3(i*.035f,.835f,.166f),new Vector3(.085f,.035f,.035f),dark);
            var leg=new GameObject(i<0?"LeftLeg":"RightLeg");leg.transform.SetParent(t,false);leg.transform.localPosition=new Vector3(i*.12f,.24f,0);
            Part("Boot",leg.transform,PrimitiveType.Capsule,new Vector3(0,-.13f,.035f),new Vector3(.17f,.12f,.23f),dark);
            var arm=new GameObject(i<0?"LeftArm":"RightArm");arm.transform.SetParent(t,false);arm.transform.localPosition=new Vector3(i*.28f,.65f,0);
            Part("Sleeve",arm.transform,PrimitiveType.Capsule,new Vector3(0,-.12f,0),new Vector3(.16f,.13f,.16f),cream);
            Part("Hand",arm.transform,PrimitiveType.Sphere,new Vector3(0,-.26f,.02f),Vector3.one*.14f,skin);
        }
        var hand=new GameObject("HeldAnchor");hand.transform.SetParent(t,false);hand.transform.localPosition=new Vector3(0,.59f,.4f);
        return root;
    }
    public static GameObject Item(string name,Transform parent,Vector3 pos,float size=.3f) {
        var color=name=="tomato"?"#ef654b":name=="onion"?"#e9c471":name=="dish"?"#f4f2df":"#e7a653";
        var root=new GameObject(name);root.transform.SetParent(parent,false);root.transform.localPosition=pos;
        string model=name=="dish"?"plate":name=="soup"?"bowl_small":"food_ingredient_"+name;
        bool textured=RestaurantProp(model,root.transform,size,Vector3.zero);
        if(name=="soup"&&textured)RestaurantProp("stew_bowl",root.transform,size*.72f,Vector3.up*(size*.18f));
        model=name=="dish"?"plate":name=="soup"?"bowl-soup":name;
        if(!textured&&!Model("Models/Food/"+model,root.transform,size,Vector3.zero,Material(color)))
            Part("Ingredient",root.transform,PrimitiveType.Sphere,Vector3.zero,Vector3.one*size,Material(color));
        return root;
    }
    sealed class Surface {public float area;public Bounds bounds;public bool initialized;}
    static void FitCounterWorktop(GameObject counter) {
        var reference=counter.transform;var full=LocalBounds(counter,reference);
        var levels=new Dictionary<int,Surface>();
        foreach(var filter in counter.GetComponentsInChildren<MeshFilter>()) {
            var vertices=filter.sharedMesh.vertices;var triangles=filter.sharedMesh.triangles;
            for(int i=0;i<triangles.Length;i+=3) {
                var a=reference.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[i]]));
                var b=reference.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[i+1]]));
                var c=reference.InverseTransformPoint(filter.transform.TransformPoint(vertices[triangles[i+2]]));
                var normal=Vector3.Cross(b-a,c-a);float y=(a.y+b.y+c.y)/3;
                if(Mathf.Abs(normal.normalized.y)<.98f||y<full.min.y+full.size.y*.75f)continue;
                int level=Mathf.RoundToInt(y*10000);
                if(!levels.TryGetValue(level,out var surface)){surface=new Surface();levels[level]=surface;}
                surface.area+=Mathf.Abs(normal.y)*.5f;
                foreach(var p in new[]{a,b,c}) {if(!surface.initialized){surface.bounds=new Bounds(p,Vector3.zero);surface.initialized=true;}else surface.bounds.Encapsulate(p);}
            }
        }
        Surface best=null;int bestLevel=int.MinValue;
        foreach(var pair in levels)if(best==null||pair.Value.area>best.area+.0001f||(Mathf.Abs(pair.Value.area-best.area)<.0001f&&pair.Key>bestLevel)){best=pair.Value;bestLevel=pair.Key;}
        if(best==null)throw new System.InvalidOperationException("Native counter has no horizontal working surface");
        var bounds=best.bounds;
        // Include the slab's bevel beside the large horizontal face, while
        // excluding lower handles and upper burner rings from the footprint.
        float surfaceY=bestLevel/10000f;
        foreach(var filter in counter.GetComponentsInChildren<MeshFilter>())foreach(var vertex in filter.sharedMesh.vertices) {
            var p=reference.InverseTransformPoint(filter.transform.TransformPoint(vertex));
            if(Mathf.Abs(p.y-surfaceY)<=.025f)bounds.Encapsulate(p);
        }
        var factor=new Vector3(CafeSurfaceStyle.CounterWidth/bounds.size.x,1,CafeSurfaceStyle.CounterWidth/bounds.size.z);
        var model=reference.GetChild(0);model.localScale=Vector3.Scale(model.localScale,factor);
        model.localPosition=Vector3.Scale(model.localPosition-new Vector3(bounds.center.x,0,bounds.center.z),factor);
        var rounded=counter.AddComponent<RoundedWorktop>();rounded.SurfaceY=surfaceY;
        rounded.SlabDepth=counter.name.StartsWith("stove_")?.022f:.122f;
        rounded.BodyScale=counter.name.StartsWith("stove_")?1:CafeSurfaceStyle.CabinetBodyScale;
        rounded.BodyCornerRadius=counter.name.StartsWith("stove_")?0:CafeSurfaceStyle.CabinetCornerRadius;
    }
    public static GameObject Station(char tile,bool round=true) {
        var root=new GameObject("Station_"+tile);var t=root.transform;
        string cabinet=tile=='P'?"stove_single":"kitchencounter_straight_B";
        var counter=RestaurantProp(cabinet,t,.98f,Vector3.zero,CounterTopHeight);
        if(!counter)counter=RestaurantProp("kitchencounter_straight_A",t,.98f,Vector3.zero,CounterTopHeight);
        if(counter)FitCounterWorktop(counter);
        if(counter&&round)counter.GetComponent<RoundedWorktop>().Apply();
        if(tile=='P') {
            RestaurantProp("pot_A",t,.7f,Vector3.up*(CounterTopHeight+.015f));
        }
        if(tile=='O'||tile=='T') {
            if(!RestaurantProp(tile=='O'?"crate_onions":"crate_tomatoes",t,.78f,Vector3.up*CounterTopHeight))
                for(int i=0;i<4;i++)Item(tile=='O'?"onion":"tomato",t,new Vector3((i%2-.5f)*.3f,CounterTopHeight,(i/2-.5f)*.3f),.27f);
        }
        if(tile=='D')RestaurantProp("dishrack_plates",t,.67f,Vector3.up*CounterTopHeight);
        if(tile=='S') {
            RestaurantProp("cuttingboard",t,.60f,Vector3.up*CounterTopHeight);
            RestaurantProp("menu",t,.28f,new Vector3(.30f,CounterTopHeight,.28f),-1,180);
        }
        return root;
    }
}}
