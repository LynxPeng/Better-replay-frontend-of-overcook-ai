using System;
using System.IO;
using UnityEngine;
using OvercookedReplay;

public static class ReplayChecks
{
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
            if (replay.header.layout_name == "cramped_room" && replay.frames[replay.frames.Length-1].score != 20)
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
