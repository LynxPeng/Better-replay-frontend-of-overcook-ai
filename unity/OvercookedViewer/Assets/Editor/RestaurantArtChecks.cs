using System;
using UnityEngine;
using UnityEditor.SceneManagement;
using OvercookedReplay;

public static class RestaurantArtChecks {
    public static void Run() {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);int count=0;
        var station=KitchenFactory.Station('X');
        if(station.transform.Find("Worktop")||station.transform.Find("CounterLip"))throw new Exception("Counter still has a separate handmade top attached");count++;
        var bounds=station.GetComponentsInChildren<Renderer>()[0].bounds;
        foreach(var renderer in station.GetComponentsInChildren<Renderer>())bounds.Encapsulate(renderer.bounds);
        if(bounds.max.y>.65f||bounds.min.y<-.02f)throw new Exception("Counter must be lowered to at most 0.65 units");count++;
        var parent=new GameObject("Item Parent").transform;
        foreach(string name in new[]{"onion","tomato","dish","soup"}) {
            var item=KitchenFactory.Item(name,parent,Vector3.zero);
            bool supplied=false;
            foreach(var r in item.GetComponentsInChildren<Renderer>())foreach(var material in r.sharedMaterials)
                if(material.mainTexture&&material.mainTexture.name=="restaurantbits_extra")supplied=true;
            if(!supplied)throw new Exception("Item did not use supplied Restaurant Bits atlas: "+name);count++;
        }
        var root=RestaurantBackdrop.Build(parent,9,5).transform;
        foreach(string side in new[]{"Left Wall","Right Wall"}) {
            var wall=root.Find(side);if(!wall||wall.GetComponentsInChildren<Renderer>().Length==0)throw new Exception("Solid side wall missing: "+side);count++;
        }
        bool decorated=false;foreach(var node in root.GetComponentsInChildren<Transform>())if(node.name.Contains("wall_decorated"))decorated=true;
        if(!decorated)throw new Exception("Native decorated kitchen wall was not used");count++;
        Debug.Log("Restaurant art checks passed: "+count);
    }
}
