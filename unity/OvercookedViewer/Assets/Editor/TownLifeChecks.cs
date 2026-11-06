using System;
using UnityEngine;
using OvercookedReplay;

public static class TownLifeChecks {
    static void Require(bool value,string message) {if(!value)throw new Exception(message);}
    public static void Composition() {
        var camera=new GameObject("Kitchen-focused framing check").AddComponent<Camera>();
        foreach(float aspect in new[]{16f/9*.77f/.83f,16f/10*.77f/.83f}) {
            camera.aspect=aspect;KitchenCamera.Apply(camera,9,5,KitchenView.LongEdge,1,0);
            var a=camera.WorldToViewportPoint(new Vector3(-.5f,.5f,-2));var b=camera.WorldToViewportPoint(new Vector3(8.5f,.5f,-2));
            Debug.Log($"Default composition: kitchen width={b.x-a.x:F3}, camera={camera.transform.position}, aspect={aspect:F3}");
            Require(b.x-a.x>.43f,"Default framing still fits the whole garden instead of the reference kitchen view: "+(b.x-a.x).ToString("F3"));
            var street=camera.WorldToViewportPoint(new Vector3(4,.1f,6.2f));
            Require(street.z>0&&street.y>.65f&&street.y<.97f,"Upper street is not visible above the kitchen");
        }
        Debug.Log("Kitchen-focused default composition checks passed");
    }
    public static void Crossings() {
        var root=TownExterior.Build(new GameObject("Street crossings check").transform,9,5).transform;
        var street=root.Find("Rear Street");
        foreach(bool right in new[]{false,true}) {
            var bridge=street.Find(right?"Street Bridge Right":"Street Bridge Left");
            Require(bridge,"Upper road still stops at the river bank");
            var b=KitchenFactory.LocalBounds(bridge.gameObject,root);float river=right?15.9f:-7.9f;
            Require(b.min.x<river-1.3f&&b.max.x>river+1.3f&&b.min.z<5.2f&&b.max.z>7.2f,"Upper bridge does not span the water and road width");
        }
        Debug.Log("Upper street crosses both rivers");
    }
    public static void Rivers() {
        var root=TownExterior.Build(new GameObject("Long river check").transform,9,5).transform;
        var camera=new GameObject("Normal river visibility check").AddComponent<Camera>();
        foreach(string side in new[]{"Left River Garden","Right River Garden"}) {
            var garden=root.Find(side);float min=float.PositiveInfinity,max=float.NegativeInfinity;
            foreach(Transform item in garden)if(item.name=="Floor/Water/Water") {
                var b=KitchenFactory.LocalBounds(item.gameObject,root);min=Mathf.Min(min,b.min.z);max=Mathf.Max(max,b.max.z);
            }
            Require(min<=-80&&max>=80,"River still has nearby visible ends: "+side);
            float x=side.StartsWith("Left")?-7.9f:15.9f;
            foreach(float aspect in new[]{16f/9*.77f/.83f,16f/10*.77f/.83f})foreach(float pitch in new[]{35f,48f,65f}) {
                camera.aspect=aspect;KitchenCamera.Apply(camera,9,5,KitchenView.LongEdge,1,0,pitch);
                foreach(float z in new[]{min,max})foreach(float edge in new[]{-1.3f,1.3f})Require(!Visible(camera,new Vector3(x+edge,-.41f,z)),"River end is visible in normal framing");
                foreach(var boat in garden.GetComponentsInChildren<RiverBoatMotion>()) {
                    float first=float.PositiveInfinity,last=float.NegativeInfinity;
                    for(int i=0;i<256;i++){boat.Evaluate(boat.Period*i/256);float z=boat.transform.localPosition.z;first=Mathf.Min(first,z);last=Mathf.Max(last,z);}
                    bool bridgeEntry=first>4.5f&&first<7.9f;
                    Require((bridgeEntry||!Visible(camera,new Vector3(x,0,first)))&&!Visible(camera,new Vector3(x,0,last)),"Boat wraps outside the hidden bridge entry or far exit");
                }
            }
        }
        Debug.Log("Both rivers continue well beyond normal framing");
    }
    static bool Visible(Camera camera,Vector3 point) {var p=camera.WorldToViewportPoint(point);return p.z>0&&p.x>=0&&p.x<=1&&p.y>=0&&p.y<=1;}
    public static void Motion() {
        var root=TownExterior.Build(new GameObject("Ambient motion check").transform,9,5).transform;
        int boats=0,sway=0;float leftDirection=0,rightDirection=0;
        foreach(var component in root.GetComponentsInChildren<MonoBehaviour>()) {
            var type=component.GetType();
            if(type.Name=="RiverBoatMotion") {
                boats++;var evaluate=type.GetMethod("Evaluate");
                evaluate.Invoke(component,new object[]{0.0});var start=component.transform.localPosition;
                evaluate.Invoke(component,new object[]{4.0});var next=component.transform.localPosition;
                double period=(double)type.GetProperty("Period").GetValue(component);
                float routeLength=(float)period*.9f;
                float distance=Mathf.Repeat(next.z-start.z+routeLength*.5f,routeLength)-routeLength*.5f;
                float direction=Mathf.Sign(distance);
                Require(Mathf.Abs(distance)>.5f&&Mathf.Abs(next.x-start.x)<.05f,"Boat does not move along its channel");
                Require(component.transform.forward.z*direction>.98f,"Boat bow faces opposite its travel");
                if(component.transform.parent.name.StartsWith("Left"))leftDirection=direction;else rightDirection=direction;
                evaluate.Invoke(component,new object[]{period});Require(Mathf.Abs(component.transform.localPosition.z-start.z)<.002f,"Boat route does not loop");
                evaluate.Invoke(component,new object[]{4.0});Require(Vector3.Distance(component.transform.localPosition,next)<.001f,"Boat evaluation accumulates drift");
                for(int i=1;i<=6;i++) {
                    double boundary=i*Math.PI*2/1.7;
                    evaluate.Invoke(component,new object[]{boundary-.0001});var before=component.transform.localRotation;
                    evaluate.Invoke(component,new object[]{boundary+.0001});
                    Require(Quaternion.Angle(before,component.transform.localRotation)<.1f,"Boat pitch snaps at wave wrap");
                }
            }
            if(type.Name=="AmbientSway") {
                sway++;var evaluate=type.GetMethod("Evaluate");var t=component.transform;
                var pivot=(Vector3)type.GetProperty("Pivot").GetValue(component);
                evaluate.Invoke(component,new object[]{0.0});var anchor=t.InverseTransformPoint(t.parent.TransformPoint(pivot));var rotation=t.localRotation;
                evaluate.Invoke(component,new object[]{2.0});
                Require(Quaternion.Angle(rotation,t.localRotation)>.01f,"Plant has no ambient sway");
                Require(Vector3.Distance(t.TransformPoint(anchor),t.parent.TransformPoint(pivot))<.001f,"Sway uproots the plant");
            }
        }
        Require(boats>=2&&leftDirection*rightDirection<0,"Boats need opposite looping motion on the two rivers");
        Require(sway>=6,"Exterior has no default foliage animation");
        Debug.Log("Ambient motion checks passed: "+boats+" boats and "+sway+" anchored plants");
    }
}
