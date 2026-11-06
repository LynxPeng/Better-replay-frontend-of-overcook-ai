using UnityEngine;
using UnityEngine.Rendering;

namespace OvercookedReplay {
// Display mode only: neither game state nor the replay clock is advanced.
public static class SceneAtmosphere {
    public static void Apply(bool night) {
        foreach(var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include,FindObjectsSortMode.None)) {
            if(light.name=="Warm Sun") {
                light.intensity=night?.13f:1.05f;
                light.color=night?new Color(.46f,.61f,1):new Color(1,.97f,.92f);
            } else if(light.name=="Cool Fill")light.intensity=night?.03f:.25f;
            else if(light.name=="Central warm ceiling light")light.enabled=night;
        }
        RenderSettings.ambientMode=AmbientMode.Flat;
        RenderSettings.ambientLight=night?new Color(.1f,.13f,.2f):new Color(.42f,.46f,.5f);
        RenderSettings.fogColor=night?new Color(.055f,.085f,.16f):new Color(.72f,.85f,.85f);
        foreach(var sky in Object.FindObjectsByType<LevelSky>(FindObjectsSortMode.None))sky.SetNight(night);
    }
}
}
