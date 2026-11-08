using UnityEngine;
using UnityEngine.Rendering;

namespace OvercookedReplay {
// Display mode only: neither game state nor the replay clock is advanced.
public static class SceneAtmosphere {
    public static void Apply(bool night) {
        foreach(var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None)) {
            if(light.name=="Warm Sun") {
                // A higher, warm afternoon key leaves short shadows behind the cooks.
                // Moonlight enters from the opposite bank; the island keeps its warm pool.
                light.transform.rotation=night?Quaternion.Euler(34,145,0):Quaternion.Euler(50,-29,0);
                light.intensity=night?.34f:1.04f;
                light.shadowStrength=night?.40f:.61f;
                light.color=night?new Color(.54f,.69f,1):new Color(1,.94f,.84f);
            } else if(light.name=="Cool Fill") {
                light.intensity=.16f;
                // At night this soft fill comes over the viewer's shoulder, reaching
                // the near cupboard faces and the dark penguin opposite the moon.
                light.transform.rotation=night?Quaternion.Euler(38,-20,0):Quaternion.Euler(40,145,0);
                light.color=night?new Color(.60f,.76f,1):new Color(.75f,.87f,1);
            }
            else if(light.name=="Central warm ceiling light")light.enabled=night;
        }
        // Sky bounce brightens hat tops; the lower hemisphere gives cupboard bases weight.
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=night?new Color(.19f,.24f,.29f):new Color(.57f,.65f,.72f);
        RenderSettings.ambientEquatorColor=night?new Color(.19f,.20f,.24f):new Color(.44f,.46f,.45f);
        RenderSettings.ambientGroundColor=night?new Color(.15f,.13f,.14f):new Color(.35f,.29f,.23f);
        RenderSettings.fogColor=night?new Color(.055f,.085f,.16f):new Color(.72f,.85f,.85f);
        foreach(var lamp in Object.FindObjectsByType<CafeLamp>(FindObjectsSortMode.None))lamp.SetNight(night);
        foreach(var sky in Object.FindObjectsByType<LevelSky>(FindObjectsSortMode.None))sky.SetNight(night);
    }
}
}
