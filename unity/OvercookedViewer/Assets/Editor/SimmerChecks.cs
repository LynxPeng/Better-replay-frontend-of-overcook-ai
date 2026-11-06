using System;
using System.IO;
using OvercookedReplay;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SimmerChecks {
    public static void Run() {
        EditorSceneManager.OpenScene("Assets/Scenes/Replay.unity");
        var player = UnityEngine.Object.FindAnyObjectByType<ReplayPlayer>();
        player.Load(Path.Combine(Application.streamingAssetsPath, "Samples/cramped_room.ocrep.json"));
        int cooking = Array.FindIndex(player.Data.frames, f => Array.Exists(f.state.objects, o => o.is_cooking));
        int ready = Array.FindIndex(player.Data.frames, f => Array.Exists(f.state.objects, o => o.is_ready && o.name == "soup"));
        if (cooking < 0 || ready < 0) throw new Exception("Sample needs cooking and ready snapshots");
        player.Seek(cooking);
        var pot = GameObject.Find("Simmering pot");
        if (!pot || !pot.transform.Find("stew_pot").gameObject.activeInHierarchy)
            throw new Exception("Cooking soup must use the Restaurant Bits broth insert");
        var puff = pot.transform.Find("Steam");
        if (!puff || !puff.gameObject.activeInHierarchy || puff.GetComponent<Renderer>().sharedMaterial.color != Color.white)
            throw new Exception("Active soup needs white steam");
        var firstPosition = puff.localPosition;
        player.Seek(cooking + 1);
        if (Vector3.Distance(firstPosition, puff.localPosition) < .001f)
            throw new Exception("Steam should drift with confirmed replay time");
        player.Seek(cooking);
        if (Vector3.Distance(firstPosition, puff.localPosition) > .0001f)
            throw new Exception("Seeking to the same time should restore the same steam");
        player.Seek(ready);
        if (!puff.gameObject.activeInHierarchy) throw new Exception("Ready soup must remain visibly hot");
        player.Seek(0);
        if (puff.gameObject.activeInHierarchy || pot.transform.Find("stew_pot").gameObject.activeInHierarchy)
            throw new Exception("Cold empty stove retained effects");
        Debug.Log("Simmer checks passed: hot broth, white steam, scrub and empty stove");
    }
}
