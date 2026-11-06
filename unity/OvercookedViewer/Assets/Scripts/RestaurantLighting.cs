using UnityEngine;

namespace OvercookedReplay {
// Actual scene illumination follows the room, independent of replay snapshots.
public static class RestaurantLighting {
    public static void Build(Transform parent,int width,int height) {
        var light=new GameObject("Central warm ceiling light").AddComponent<Light>();
        light.transform.SetParent(parent,false);
        light.transform.localPosition=new Vector3((width-1)*.5f,4.6f,-(height-1)*.5f+.25f);
        light.type=LightType.Point;light.color=new Color(1,.89f,.72f);
        light.intensity=18;light.range=Mathf.Max(16,width+5);
        light.shadows=LightShadows.Soft;light.shadowStrength=.65f;
        light.shadowBias=.04f;light.shadowNormalBias=.2f;
        light.renderMode=LightRenderMode.ForcePixel;
    }
}
}
