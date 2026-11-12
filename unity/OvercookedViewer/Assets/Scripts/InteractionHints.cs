using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OvercookedReplay {
// Read-only affordance derived from snapshots, checked against Python INTERACT.
// It never executes actions, advances cooking or assigns rewards.
public sealed class InteractionHints : MonoBehaviour {
    [Serializable] class RecipeParameters {public int max_num_ingredients=3;}
    sealed class Patch {public MeshRenderer[] renderers;public int mask;}
    readonly Dictionary<Vector2Int,Patch> patches=new Dictionary<Vector2Int,Patch>();
    readonly Dictionary<Vector2Int,int> targets=new Dictionary<Vector2Int,int>();
    static Mesh[][] slices;
    static Material blue,orange,green;
    public int PlayerCount {get;set;}=2;
    public static bool TryTarget(ReplayHeader header,Snapshot state,int agent,out Vector2Int target){
        target=default;
        if(state?.players==null||agent<0||agent>=state.players.Length||header?.terrain==null)return false;
        var p=state.players[agent];target=new Vector2Int(p.position[0]+p.orientation[0],p.position[1]+p.orientation[1]);
        if(target.y<0||target.y>=header.terrain.Length||target.x<0||target.x>=header.terrain[target.y].Length)return false;
        Item item=null;if(state.objects!=null)foreach(var o in state.objects)if(o.position[0]==target.x&&o.position[1]==target.y){item=o;break;}
        string held=p.held_object?.name;bool empty=string.IsNullOrEmpty(held);
        switch(header.terrain[target.y][target.x]){
            case 'X':return empty?item!=null:item==null;
            case 'O':case 'T':case 'D':return empty;
            case 'S':return held=="soup"&&p.held_object.is_ready;
            case 'P':
                if(empty)return header.dynamics=="new"&&item!=null&&item.is_idle&&(item._ingredients?.Length??0)>0;
                if(held=="dish")return item!=null&&item.is_ready;
                if(held!="onion"&&held!="tomato")return false;
                int capacity=3;
                if(!string.IsNullOrEmpty(header.mdp_params_json))try{capacity=JsonUtility.FromJson<RecipeParameters>(header.mdp_params_json)?.max_num_ingredients??3;}catch(ArgumentException){return false;}
                return item==null||(item.is_idle&&(item._ingredients?.Length??0)<capacity);
            default:return false;
        }
    }
    public static Material Tint(Color colour){
        var mat=new Material(Resources.Load<Shader>("InteractionSurface"));mat.name="Interaction target tint";
        mat.SetColor("_BaseColor",colour);
        mat.renderQueue=(int)RenderQueue.Transparent+5;return mat;
    }
    static List<Vector3> Clip(List<Vector3> polygon,float edge,int side){
        var clipped=new List<Vector3>();var previous=polygon[polygon.Count-1];bool was=(previous.x-edge)*side>=0;
        foreach(var point in polygon){
            bool inside=(point.x-edge)*side>=0;
            if(inside!=was)clipped.Add(Vector3.Lerp(previous,point,(edge-previous.x)/(point.x-previous.x)));
            if(inside)clipped.Add(point);
            previous=point;was=inside;
        }
        return clipped;
    }
    static Mesh Shape(int slice,int count){
        float half=CafeSurfaceStyle.CounterWidth*Mathf.Sqrt(.8f)*.5f,radius=.035f;var polygon=new List<Vector3>();
        for(int corner=0;corner<4;corner++)for(int i=0;i<=8;i++){
            float angle=(corner+i/8f)*Mathf.PI*.5f;
            var centre=new Vector2(corner==0||corner==3?half-radius:-half+radius,corner<2?half-radius:-half+radius);
            polygon.Add(new Vector3(centre.x+Mathf.Cos(angle)*radius,0,centre.y+Mathf.Sin(angle)*radius));
        }
        float width=2*half/count;
        if(slice>0)polygon=Clip(polygon,-half+slice*width,1);
        if(slice<count-1)polygon=Clip(polygon,-half+(slice+1)*width,-1);
        var centrePoint=new Vector3(-half+(slice+.5f)*width,0,0);
        var vertices=new List<Vector3>{centrePoint};vertices.AddRange(polygon);var triangles=new List<int>();
        for(int i=0;i<polygon.Count;i++){triangles.Add(0);triangles.Add(1+(i+1)%polygon.Count);triangles.Add(1+i);}
        var mesh=new Mesh{name="Central 80 percent interaction target"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    public void Register(Vector2Int tile,GameObject station){
        if(slices==null)slices=new[]{new[]{Shape(0,1)},new[]{Shape(0,2),Shape(1,2)},
                                    new[]{Shape(0,3),Shape(1,3),Shape(2,3)}};
        if(!blue){blue=Resources.Load<Material>("Materials/InteractionHintBlue");orange=Resources.Load<Material>("Materials/InteractionHintOrange");
            if(!blue||!orange)throw new InvalidOperationException("Run InteriorMaterialAssets.Prepare to create interaction hint materials");}
        var surface=station.GetComponentInChildren<RoundedWorktop>();if(!surface)return;
        var point=surface.transform.TransformPoint(new Vector3(0,surface.SurfaceY,0))+Vector3.up*.003f;
        MeshRenderer Create(string name,Material material){
            var g=new GameObject(name);g.transform.SetParent(station.transform,false);g.transform.position=point;
            g.AddComponent<MeshFilter>().sharedMesh=slices[0][0];var renderer=g.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;g.SetActive(false);return renderer;
        }
        var renderers=new MeshRenderer[PlayerCount];
        renderers[0]=Create("Blue interaction target",blue);
        renderers[1]=Create("Orange interaction target",orange);
        if(PlayerCount==3){
            if(!green){green=new Material(blue);green.SetColor("_BaseColor",new Color(.30f,.89f,.46f,.52f));}
            renderers[2]=Create("Green interaction target",green);
        }
        patches[tile]=new Patch{renderers=renderers};
    }
    public void Refresh(ReplayHeader header,Snapshot snapshot){
        targets.Clear();for(int agent=0;agent<PlayerCount;agent++)if(TryTarget(header,snapshot,agent,out var tile))targets[tile]=(targets.TryGetValue(tile,out int mask)?mask:0)|(1<<agent);
        foreach(var pair in patches){
            var p=pair.Value;int mask=targets.TryGetValue(pair.Key,out int active)?active:0;if(mask==p.mask)continue;p.mask=mask;
            int count=0,slice=0;
            for(int agent=0;agent<p.renderers.Length;agent++)if((mask&(1<<agent))!=0)count++;
            for(int agent=0;agent<p.renderers.Length;agent++){
                var renderer=p.renderers[agent];bool visible=(mask&(1<<agent))!=0;
                if(visible)renderer.GetComponent<MeshFilter>().sharedMesh=slices[count-1][slice++];
                renderer.gameObject.SetActive(visible);
            }
        }
    }
}
}
