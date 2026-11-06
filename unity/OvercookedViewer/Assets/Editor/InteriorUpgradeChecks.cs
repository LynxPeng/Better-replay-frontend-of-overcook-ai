using System;
using System.IO;
using System.Text;
using UnityEngine;
using OvercookedReplay;

public static class InteriorUpgradeChecks {
    static void Require(bool value,string message) {if(!value)throw new Exception(message);}
    public static void Inspect() {
        var text=new StringBuilder();
        foreach(string name in new[]{"kitchencounter_straight_B","stove_single","fridge_A_decorated","fridge_B","icecream_machine","kitchentable_B_decorated","kitchentable_A_large_decorated_A","wall_doorway","door_A","door_B","table_round_A","table_round_B_tablecloth_green","food_burger","food_dinner","icecream_bowl_decorated_A"}) {
            var asset=Resources.Load<GameObject>("RestaurantBits/Models/"+name);
            text.AppendLine(name+" raw="+KitchenFactory.LocalBounds(asset,asset.transform));
        }
        foreach(char tile in new[]{'X','P'}) {
            var station=KitchenFactory.Station(tile);var cabinet=station.transform.GetChild(0);
            text.AppendLine(tile+" fitted="+KitchenFactory.LocalBounds(cabinet.gameObject,station.transform));
            var slices=new System.Collections.Generic.SortedDictionary<float,Bounds>();
            foreach(var filter in cabinet.GetComponentsInChildren<MeshFilter>())foreach(var vertex in filter.sharedMesh.vertices) {
                var p=station.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                float y=(float)Math.Round(p.y,3);if(y<.4f)continue;
                if(!slices.TryGetValue(y,out var b))b=new Bounds(p,Vector3.zero);else b.Encapsulate(p);slices[y]=b;
            }
            foreach(var slice in slices)text.AppendLine("  y="+slice.Key+" x/z="+slice.Value.size.x+"/"+slice.Value.size.z+" centre="+slice.Value.center);
        }
        File.WriteAllText("../../output/interior-native-geometry.txt",text.ToString());Debug.Log("Interior native geometry saved");
    }
    public static void Counters() {
        foreach(char tile in new[]{'X','P'}) {
            var station=KitchenFactory.Station(tile);var body=station.transform.GetChild(0);
            var bounds=TopBounds(body,station.transform,tile=='P');
            Require(Mathf.Abs(bounds.size.x-.976f)<.002f&&Mathf.Abs(bounds.size.z-.976f)<.002f,"Counter worktop does not preserve the shared 24mm joint: "+tile+" "+bounds.size.ToString("F6"));
            Require(Mathf.Abs(bounds.center.x)<.005f&&Mathf.Abs(bounds.center.z)<.005f,"Counter top is not centred on its grid cell");
            for(int angle=90;angle<=270;angle+=90) {
                body.localRotation=Quaternion.Euler(0,angle,0);var turned=TopBounds(body,station.transform,tile=='P');
                Require(Mathf.Abs(turned.size.x-.976f)<.002f&&Mathf.Abs(turned.size.z-.976f)<.002f&&Mathf.Abs(turned.center.x)<.005f&&Mathf.Abs(turned.center.z)<.005f,"Turning a cabinet changes its dividing seam");
            }
        }
        Debug.Log("Counter worktops preserve shared 24mm grid seams at every orientation");
    }
    static Bounds TopBounds(Transform body,Transform reference,bool stove) {
        var all=KitchenFactory.LocalBounds(body.gameObject,reference);var b=new Bounds();bool first=true;
        foreach(var filter in body.GetComponentsInChildren<MeshFilter>())foreach(var vertex in filter.sharedMesh.vertices) {
            var p=reference.InverseTransformPoint(filter.transform.TransformPoint(vertex));
            // Native stove slab is below the raised burner rings.
            if(stove?(p.y<.485f||p.y>.505f):p.y<all.max.y-.005f)continue;
            if(first){b=new Bounds(p,Vector3.zero);first=false;}else b.Encapsulate(p);
        }
        return b;
    }
    public static void Interior() {
        var root=RestaurantBackdrop.Build(new GameObject("Interior refresh regression").transform,9,5).transform;
        bool machine=false,door=false;var meals=new System.Collections.Generic.HashSet<string>();
        foreach(var t in root.GetComponentsInChildren<Transform>()) {
            if(t.name=="icecream_machine")machine=true;if(t.name=="wall_doorway")door=true;
            if(t.name.StartsWith("food_")||t.name.StartsWith("icecream_bowl_decorated"))meals.Add(t.name);
            if(t.name.StartsWith("fridge_"))Require(KitchenFactory.LocalBounds(t.gameObject,root).size.x<1.1f,"Fridge is still too broad");
        }
        Require(machine&&meals.Count>=5,"Native dessert equipment and varied meals are missing");
        Require(door,"Room has no native doorway");
        Require(root.Find("Unified Tile Floor"),"Kitchen and dining still use different floors");
        var floor=root.Find("Unified Tile Floor").GetComponent<Renderer>();
        Require(floor.sharedMaterial.mainTexture.name=="CafeTile_BaseColor","Unified floor is not using the hand-authored tile texture");
        var second=RestaurantBackdrop.Build(new GameObject("Same layout").transform,9,5).transform;
        Require(MenuSignature(root)==MenuSignature(second),"Decorative meals change when rebuilding the same layout");
        Debug.Log("Varied meals, dessert equipment, narrow fridges, doorway and unified floor checks passed");
    }
    static string MenuSignature(Transform root) {
        var names=new System.Collections.Generic.List<string>();
        foreach(var t in root.Find("Dining").GetComponentsInChildren<Transform>())if(t.name.StartsWith("food_")||t.name.StartsWith("icecream_bowl_decorated"))names.Add(t.name);
        return string.Join("|",names);
    }
    public static void DisplayClearance() {
        foreach(var dimensions in new[]{new Vector2Int(5,4),new Vector2Int(5,5),new Vector2Int(9,5)}) {
            var root=RestaurantBackdrop.Build(new GameObject("Display clearance").transform,dimensions.x,dimensions.y).transform;
            var display=root.Find("Dining").Find("Sundae Display");var bounds=KitchenFactory.LocalBounds(display.gameObject,root);
            foreach(Transform wall in root.Find("Rear Wall"))if(wall.name=="wall_decorated")
                Require(!bounds.Intersects(KitchenFactory.LocalBounds(wall.gameObject,root)),"Sundae display intersects native rear furniture: "+dimensions);
        }
        Debug.Log("Sundae display clears native furniture in small and wide layouts");
    }
    public static void SeatingClearance() {
        var root=RestaurantBackdrop.Build(new GameObject("Seating clearance").transform,5,4).transform;var dining=root.Find("Dining");
        Transform table=null;foreach(Transform child in dining)if(child.name=="Dining Table"&&child.localPosition.x>5)table=child;
        var bounds=KitchenFactory.LocalBounds(table.GetChild(0).gameObject,root);
        foreach(Transform stool in dining.Find("Icecream Bar"))if(stool.name=="chair_stool")
            Require(!bounds.Intersects(KitchenFactory.LocalBounds(stool.gameObject,root)),"Right dining table intersects dessert bar stools");
        Debug.Log("Small layout dining table clears the dessert stools");
    }
    public static void Road() {
        var root=TownExterior.Build(new GameObject("Wider road regression").transform,9,5).transform;
        var street=root.Find("Rear Street");
        foreach(var name in new[]{"Street Bridge Left","Street Bridge Right"}) {
            var b=KitchenFactory.LocalBounds(street.Find(name).gameObject,root);
            Require(b.size.z>=3.35f,"Exterior bridge/road is still too narrow");
        }
        Debug.Log("Wider upper street and bridge checks passed");
    }
}
