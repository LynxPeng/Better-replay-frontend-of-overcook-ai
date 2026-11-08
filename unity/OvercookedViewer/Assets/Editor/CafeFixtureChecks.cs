using System;
using OvercookedReplay;
using UnityEngine;

public static class CafeFixtureChecks {
    public static void Run() {
        CheckLayout(5,4);
        CheckLayout(9,5);
        Debug.Log("Cafe fixtures passed: attached assets, shared night materials, shadow budget and anchored motion.");
    }

    static void CheckLayout(int width,int height) {
        var room=new GameObject("Fixture test room");
        try {
            RestaurantBackdrop.Build(room.transform,width,height);
            var lamps=room.GetComponentsInChildren<CafeLamp>();
            if(lamps.Length<3)throw new Exception("Missing wall festoons or street lanterns");
            var softLights=0;
            foreach(var light in room.GetComponentsInChildren<Light>())if(light.shadows!=LightShadows.None)softLights++;
            if(softLights!=1)throw new Exception("Expected just the central point to cast real-time local shadows");
            foreach(var lamp in lamps) {
                var surface=lamp.GetComponentInChildren<MeshRenderer>();
                if(!surface || !surface.sharedMaterial)throw new Exception("Missing daylight fixture material on "+lamp.name);
                var day=surface.sharedMaterial;var map=day.GetTexture("_BaseMap");
                lamp.SetNight(true);
                var evening=surface.sharedMaterial;
                if(day==evening||!map||map!=evening.GetTexture("_BaseMap"))throw new Exception("Night fixture lost its town atlas");
                lamp.SetNight(false);lamp.SetNight(true);
                if(surface.sharedMaterial!=evening)throw new Exception("Switching allocated a new fixture material");
                lamp.SetNight(false);
                if(surface.sharedMaterial!=day)throw new Exception("Daylight fixture was not restored");
            }
            var balloon=Array.Find(room.GetComponentsInChildren<AmbientSway>(),s=>s.name.Contains("Blue_Balloon-Flying"));
            if(!balloon)throw new Exception("Ice cream cart is missing its tethered balloon");
            var tied=balloon.transform.InverseTransformPoint(balloon.transform.parent.TransformPoint(balloon.Pivot));
            var before=balloon.transform.localRotation;
            balloon.Evaluate(1);
            if(Quaternion.Angle(before,balloon.transform.localRotation)<1||
               Vector3.Distance(balloon.transform.TransformPoint(tied),balloon.transform.parent.TransformPoint(balloon.Pivot))>.001f)
                throw new Exception("Balloon does not sway around the end of its tether");
        } finally {UnityEngine.Object.DestroyImmediate(room);}
    }
}
