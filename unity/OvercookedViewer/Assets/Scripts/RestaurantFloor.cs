using System;
using UnityEngine;

namespace OvercookedReplay {
public static class RestaurantFloor {
    public static GameObject Build(Transform parent,int width,int height) {
        var material=Resources.Load<Material>("Materials/RestaurantTile");
        if(!material)throw new InvalidOperationException("Restaurant tile material missing; run ProjectBootstrap.Setup");
        float w=width+8,d=height+6;
        var floor=KitchenFactory.Part("Unified Tile Floor",parent,PrimitiveType.Quad,new Vector3((width-1)*.5f,0,(1-height)*.5f),new Vector3(w,d,1),material);
        floor.transform.localRotation=Quaternion.Euler(90,0,0);
        var properties=new MaterialPropertyBlock();
        properties.SetVector("_BaseMap_ST",new Vector4(w/4,d/4,-1,(-height-2f)/4));
        floor.GetComponent<Renderer>().SetPropertyBlock(properties);return floor;
    }
}
}
