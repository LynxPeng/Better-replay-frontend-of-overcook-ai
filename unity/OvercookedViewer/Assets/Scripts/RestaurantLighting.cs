using UnityEngine;

namespace OvercookedReplay {
// Actual scene illumination follows the room, independent of replay snapshots.
public static class RestaurantLighting {
    public static void Build(Transform parent,int width,int height) {
        // Rear wall modules are 2 m tall. The shallow timber cap gives the native
        // window, door and hood modules a continuous mounting rail for the festoons.
        float centerX=(width-1)*.5f;
        var timber=KitchenFactory.Material("#785448");
        KitchenFactory.Part("Rear timber capping",parent,PrimitiveType.Cube,
            new Vector3(centerX,2.04f,3.375f),new Vector3(width+8.16f,.12f,.45f),timber);
        const float doorX=2.5f;
        HangLights(parent,doorX-3.82f,doorX-.69f);
        HangLights(parent,doorX+.69f,Mathf.Min(width+1.5f,doorX+3.72f));
        var palm=KitchenFactory.Model("CartoonTown/Models/Props/Decoration_PalmPlant",parent,.86f,Vector3.zero);
        if(palm) {
            var bounds=KitchenFactory.LocalBounds(palm,parent);
            palm.transform.localPosition+=new Vector3(1.48f,-bounds.min.y,2.75f);
        }
        var light=new GameObject("Central warm ceiling light").AddComponent<Light>();
        light.transform.SetParent(parent,false);
        light.transform.localPosition=new Vector3(centerX,4.0f,-(height-1)*.5f+.25f);
        light.type=LightType.Point;light.color=new Color(1,.85f,.67f);
        light.intensity=15;light.range=Mathf.Max(15,width+5);
        light.shadows=LightShadows.Soft;light.shadowStrength=.65f;
        light.shadowBias=.04f;light.shadowNormalBias=.2f;
        light.renderMode=LightRenderMode.ForcePixel;
    }

    static void HangLights(Transform parent,float left,float right) {
        float span=right-left;
        if(span<1)return;
        var mount=new GameObject("Wall festoon").transform;
        mount.SetParent(parent,false);
        mount.localPosition=new Vector3((left+right)*.5f,2.035f,3.135f);
        var model=KitchenFactory.Model("CartoonTown/Models/Laterns/LightChain",mount,span,Vector3.zero);
        if(!model){Object.DestroyImmediate(mount.gameObject);return;}
        // Attach the two actual ends to the rail, leaving the native curved wire below it.
        model.transform.localPosition-=Vector3.up*KitchenFactory.LocalBounds(model,mount).max.y;
        var spot=new GameObject("Warm light on service counter").AddComponent<Light>();
        spot.transform.SetParent(mount,false);spot.transform.localPosition=new Vector3(0,-.30f,-.14f);
        spot.transform.localRotation=Quaternion.Euler(57,180,0);
        spot.type=LightType.Spot;spot.spotAngle=105;spot.innerSpotAngle=32;
        spot.color=new Color(1,.76f,.49f);spot.intensity=3.4f;spot.range=4.3f;spot.shadows=LightShadows.None;
        mount.gameObject.AddComponent<CafeLamp>().Bind(model.GetComponentInChildren<Renderer>(),spot);
    }
}
}
