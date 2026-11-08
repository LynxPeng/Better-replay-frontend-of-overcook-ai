using System.Collections.Generic;
using UnityEngine;

namespace OvercookedReplay {
// Only fixtures use the frosted glass treatment; ordinary town models keep
// their imported material even when their meshes share the same atlas.
public sealed class CafeLamp : MonoBehaviour {
    static readonly Dictionary<Texture,Material[]> LampMaterials=new Dictionary<Texture,Material[]>();
    Renderer fixture;
    Material daylight,lit;
    Light pool;

    public void Bind(Renderer surface,Light nightPool=null) {
        fixture=surface;
        pool=nightPool;
        if(fixture && fixture.sharedMaterial) {
            daylight=lit=fixture.sharedMaterial;
            var atlas=daylight.GetTexture("_BaseMap");
            if(atlas) {
                if(!LampMaterials.TryGetValue(atlas,out var variants) || !variants[0] || !variants[1]) {
                    var shader=Resources.Load<Shader>("CafeGlass");
                    variants=new Material[2];
                    for(int i=0;i<2;i++) {
                        variants[i]=new Material(shader){name=i==0?"Cafe glass – daylight":"Cafe glass – evening"};
                        variants[i].SetTexture("_BaseMap",atlas);
                        variants[i].SetFloat("_Night",i);
                    }
                    LampMaterials[atlas]=variants;
                }
                daylight=variants[0];lit=variants[1];
            }
        }
        SetNight(false);
    }

    public void SetNight(bool night) {
        if(fixture)fixture.sharedMaterial=night?lit:daylight;
        if(pool)pool.enabled=night;
    }
}
}
