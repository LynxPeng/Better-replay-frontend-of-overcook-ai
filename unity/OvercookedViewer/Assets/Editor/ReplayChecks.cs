using System;
using System.IO;
using UnityEngine;
using OvercookedReplay;

public static class ReplayChecks
{
    public static void ScoreMarkerRegression() {
        var collect=typeof(ReplayFile).GetMethod("ScoreMarkerTimes",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
        if(collect==null)throw new Exception("Timeline has no score-based marker collector; score-only replays lose markers");
        var data=new ReplayFile{frames=new[]{new Frame{time=0,score=0},new Frame{time=1,score=10},new Frame{time=2,score=60}},
            segments=new[]{new Segment{kind="step",events=new ReplayEvent[0]},new Segment{kind="step",events=new ReplayEvent[0]}}};
        var marks=(System.Collections.Generic.List<float>)collect.Invoke(null,new object[]{data});
        if(marks.Count!=2||marks[0]!=1||marks[1]!=2)throw new Exception("Missing score markers when event lists are empty");
        data.segments[0].events=new[]{new ReplayEvent{kind="meal_delivery",agent_index=1}};
        marks=(System.Collections.Generic.List<float>)collect.Invoke(null,new object[]{data});
        if(marks.Count!=2)throw new Exception("One scoring step produced duplicate markers");
        data.frames[1].score=data.frames[2].score=0;data.segments[1].kind="gap";
        marks=(System.Collections.Generic.List<float>)collect.Invoke(null,new object[]{data});
        if(marks.Count!=1||marks[0]!=1)throw new Exception("Confirmed zero-reward delivery marker lost");
        data.segments[0].events=new ReplayEvent[0];
        marks=(System.Collections.Generic.List<float>)collect.Invoke(null,new object[]{data});
        if(marks.Count!=0)throw new Exception("Non-scoring actions produced score markers");
        var path=Path.GetFullPath("../../tests/fixtures/native-score-projection.ocrep.json");
        var projection=ReplayFile.Load(path);
        if(!projection.header.IsPartition||projection.frames[0].state.players[1].held_object?.name!="meal_potato")
            throw new Exception("native_state projection was not recognized as a v3 kitchen");
        if(!Array.Exists(projection.segments[0].events,e=>e.kind=="meal_delivery"&&e.agent_index==1&&e.order_id==0))
            throw new Exception("native_info delivery not recovered");
        marks=(System.Collections.Generic.List<float>)collect.Invoke(null,new object[]{projection});
        if(marks.Count!=1||marks[0]!=projection.frames[1].time)throw new Exception("Projection's 0 to 10 score marker missing");
        Debug.Log("Score marker regression passed: event-free scoring, deduplication, zero-reward delivery, native projection");
    }
    public static void VisualRegression() {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Replay.unity");
        bool background=false;
        foreach(var camera in Camera.allCameras)if(camera.rect==new Rect(0,0,1,1) && camera.cullingMask==0 && camera.clearFlags==CameraClearFlags.SolidColor && camera.depth<Camera.main.depth)background=true;
        if(!background)throw new Exception("UI viewport has no full-screen background clear camera");
        Debug.Log("Full-screen clear regression passed");
    }
    public static void SerializerRegression() {
        int warnings=0;
        Application.LogCallback callback=(message,trace,type)=>{if(message.Contains("Serialization depth limit"))warnings++;};
        Application.logMessageReceived+=callback;
        try {var source=Directory.GetFiles(Application.streamingAssetsPath+"/Samples","cramped_room.ocrep.json")[0];
            var data=ReplayFile.Load(source);JsonUtility.ToJson(data);
        } finally {Application.logMessageReceived-=callback;}
        if(warnings>0)throw new Exception("Replay DTO emitted recursive serialization warnings: "+warnings);
        Debug.Log("Replay serialization regression passed");
    }
    public static void Run()
    {
        int assertions = 0;
        foreach (string path in Directory.GetFiles(Application.streamingAssetsPath + "/Samples", "*.ocrep.json"))
        {
            var replay = ReplayFile.Load(path);
            var clock = new ReplayClock(replay);
            for (int i = 0; i < replay.frames.Length; i++)
            {
                clock.SeekFrame(i);
                if (clock.Index != i) throw new Exception("Seek mismatch " + i);
                assertions++;
            }
            clock.SeekFrame(0);
            clock.Playing = true;
            clock.Advance(replay.frames[replay.frames.Length - 1].time + 1);
            if (clock.Index != replay.frames.Length - 1 || clock.Playing) throw new Exception("Terminal clock mismatch");
            assertions++;
            // Only the scripted one-delivery fixture ends at 20; the same layout
            // also has a real upstream policy recording ending at 220.
            if (Path.GetFileName(path) == "cramped_room.ocrep.json" && replay.frames[replay.frames.Length-1].score != 20)
                throw new Exception("Last delivery lost");
        }
        string bad = "{\"header\":{\"schema_version\":77}}";
        try { ReplayFile.Parse(bad); throw new Exception("Bad schema accepted"); }
        catch (ArgumentException) { assertions++; }
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        var camera=new GameObject("Test Camera").AddComponent<Camera>();camera.tag="MainCamera";
        var source=Directory.GetFiles(Application.streamingAssetsPath+"/Samples","cramped_room.ocrep.json")[0];
        var counter=ReplayFile.Load(source);
        counter.frames[0].state.objects=new[]{new Item {name="soup",position=new[]{0,0},cook_time=20,cooking_tick=20,is_ready=true,
            _ingredients=new[]{new Ingredient{name="onion"},new Ingredient{name="onion"},new Ingredient{name="onion"}}}};
        string testFile=Path.GetFullPath("../../output/counter-soup-test.ocrep.json");File.WriteAllText(testFile,JsonUtility.ToJson(counter));
        var player=new GameObject("Test Player").AddComponent<ReplayPlayer>();player.Load(testFile);
        if(GameObject.Find("Kitchen/Objects/soup")==null)throw new Exception("Counter soup must be rendered as a plated bowl");
        assertions++;
        Directory.CreateDirectory("../../output");
        File.WriteAllText("../../output/unity-checks.json", "{\"passed\":true,\"assertions\":"+assertions+"}");
        Debug.Log("Replay checks passed: " + assertions);
    }
}
