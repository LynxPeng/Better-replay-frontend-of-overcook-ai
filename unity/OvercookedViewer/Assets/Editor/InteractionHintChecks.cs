using System;
using System.IO;
using System.Linq;
using UnityEngine;
using OvercookedReplay;

public static class InteractionHintChecks {
    [Serializable] class Case {public string name;public ReplayHeader header;public Snapshot state;public bool[] expected;}
    [Serializable] class Cases {public Case[] cases;}
    public static void Oracle(){
        var type=typeof(ReplayPlayer).Assembly.GetType("OvercookedReplay.InteractionHints");
        if(type==null)throw new Exception("No interaction target hint implementation");
        var method=type.GetMethod("TryTarget");var cases=JsonUtility.FromJson<Cases>(File.ReadAllText("../../output/interaction-hint-oracle.json"));int count=0;
        foreach(var c in cases.cases)for(int agent=0;agent<2;agent++){
            object[] args={c.header,c.state,agent,default(Vector2Int)};bool actual=(bool)method.Invoke(null,args);
            if(actual!=c.expected[agent])throw new Exception("Python/UI interaction mismatch: "+c.name+" agent="+agent);
            if(actual){var target=(Vector2Int)args[3];var p=c.state.players[agent];if(target!=new Vector2Int(p.position[0]+p.orientation[0],p.position[1]+p.orientation[1]))throw new Exception("Hint is not on faced tile");}count++;
        }Debug.Log("Python interaction oracle passed: "+count);
    }
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    public static void Surfaces(){
        var owner=new GameObject("Hint surface check");var hints=owner.AddComponent<InteractionHints>();
        var station=KitchenFactory.Station('X');station.transform.position=new Vector3(1,0,-1);hints.Register(new Vector2Int(1,1),station);
        var header=new ReplayHeader{terrain=new[]{"   "," X ","   "},dynamics="new"};
        var state=new Snapshot{players=new[]{new ChefState{position=new[]{1,0},orientation=new[]{0,1}},new ChefState{position=new[]{1,2},orientation=new[]{0,-1}}},objects=new[]{new Item{name="onion",position=new[]{1,1}}}};
        hints.Refresh(header,state);var renderers=station.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.Contains("interaction target")).ToArray();
        Require(renderers.Length==2,"Both chefs do not share the target");float area=0;
        foreach(var renderer in renderers){
            var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;var v=mesh.vertices;var t=mesh.triangles;
            for(int i=0;i<t.Length;i+=3)area+=Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).magnitude*.5f;
            Require(renderer.GetComponentsInChildren<Collider>().Length==0&&renderer.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.Off,"Hint changes collisions or shadows");
            Require(renderer.name.StartsWith("Blue")?mesh.bounds.max.x<.0001f:mesh.bounds.min.x>-.0001f,"Shared hints overlap instead of splitting the surface");
            Require(Mathf.Abs(renderer.transform.position.y-KitchenFactory.CounterTopHeight-.003f)<.002f,"Hint floats away from the countertop");
        }
        Require(area/(CafeSurfaceStyle.CounterWidth*CafeSurfaceStyle.CounterWidth)>.795f&&area/(CafeSurfaceStyle.CounterWidth*CafeSurfaceStyle.CounterWidth)<.805f,"Hint does not cover about 80 percent of the top area");
        foreach(var p in state.players)p.held_object=new Item{name="onion",position=p.position};hints.Refresh(header,state);
        Require(!station.GetComponentsInChildren<MeshRenderer>().Any(r=>r.name.Contains("interaction target")),"Invalid targets keep a stale highlight");
        state.players[0].held_object=null;hints.Refresh(header,state);
        Require(station.GetComponentsInChildren<MeshRenderer>().Count(r=>r.name.Contains("interaction target"))==1,"Single chef target is not restored");
        Debug.Log("Interaction surfaces passed: area="+area+", split colours, top height, clear and restore");
    }
    public static void Replay(){
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Replay.unity");
        var player=UnityEngine.Object.FindFirstObjectByType<ReplayPlayer>();
        foreach(var path in Directory.GetFiles(Application.streamingAssetsPath+"/Samples","*.ocrep.json")){
            player.Load(path);int best=0,bestCount=-1;
            foreach(int i in Enumerable.Range(0,player.Data.frames.Length)){
                player.Seek(i);var state=player.Data.frames[i].state;
                int expected=Enumerable.Range(0,2).Count(a=>InteractionHints.TryTarget(player.Data.header,state,a,out _));
                var hints=(InteractionHints)typeof(ReplayPlayer).GetField("interactionHints",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(player);
                int actual=hints.GetComponentsInChildren<MeshRenderer>().Count(r=>r.name.Contains("interaction target"));
                Require(actual==expected,"Recorded target rendering mismatch at "+i+" in "+path);
                if(expected>bestCount){best=i;bestCount=expected;}
            }
            player.Seek(0);player.Seek(player.Data.frames.Length-1);player.Seek(best);
            File.AppendAllText("../../output/interaction-best-frames.txt",Path.GetFileName(path)+" "+best+" "+bestCount+"\n");
        }Debug.Log("Interaction hints survive all sample frames and nonsequential seeks");
    }
}
