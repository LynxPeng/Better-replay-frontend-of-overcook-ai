using System;
using UnityEngine;
using OvercookedReplay;

public static class ReplayCharacterChecks {
    public static void PenguinAvailable() {
        var prefab=Resources.Load<GameObject>("LocalCharacters/GuguGaga");
        if(!prefab||prefab.GetComponent<IReplayChefActor>()==null)throw new Exception("Orange penguin needs its own rigged replay actor prefab");
        Debug.Log("Penguin actor available");
    }
    public static void Contract() {
        var contract=typeof(BlueFishActor).Assembly.GetType("OvercookedReplay.IReplayChefActor");
        if(contract==null||!contract.IsAssignableFrom(typeof(BlueFishActor)))throw new Exception("Replay needs a model-independent actor contract before adding the penguin");
        Debug.Log("Replay character actor contract passed");
    }
    public static void Slots() {
        var method=typeof(ReplayPlayer).GetMethod("CharacterResource");
        if(method==null)throw new Exception("Replay has no per-player local character selection");
        string blue=(string)method.Invoke(null,new object[]{0,true}),orange=(string)method.Invoke(null,new object[]{1,true});
        if(blue!="LocalCharacters/BlueFatFish"||orange!="LocalCharacters/GuguGaga")throw new Exception("Blue fish and orange penguin must keep their original player slots");
        if(method.Invoke(null,new object[]{1,false})!=null)throw new Exception("Disabled character must use primitive fallback");
        Debug.Log("Replay local character slot checks passed");
    }
}
