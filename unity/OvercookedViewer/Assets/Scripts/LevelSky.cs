using UnityEngine;

namespace OvercookedReplay {
// Keep the native dome centred, but leave its horizon in world space when the
// viewer pans, changes pitch, or chooses another viewing direction.
public sealed class LevelSky : MonoBehaviour {
    Transform target;
    public void Follow(Transform camera) {target=camera;UpdatePose();}
    public void UpdatePose() {if(target)transform.SetPositionAndRotation(target.position,Quaternion.identity);}
    public void SetNight(bool night) {
        foreach(var node in GetComponentsInChildren<Transform>(true)) {
            if(node.name=="Sun"||node.name=="Sun 1")node.gameObject.SetActive(!night);
            if(node.name=="Moon"||node.name=="Moon 1"||node.name=="Stars")node.gameObject.SetActive(night);
        }
        var block=new MaterialPropertyBlock();
        foreach(var renderer in GetComponentsInChildren<Renderer>(true)) {
            renderer.GetPropertyBlock(block);
            bool dome=renderer.name=="SkyDome";
            block.SetVector("_BaseMap_ST",new Vector4(1,1,dome&&night?.42f:0,0));
            block.SetColor("_BaseColor",!dome&&night?new Color(.45f,.53f,.7f):Color.white);
            renderer.SetPropertyBlock(block);block.Clear();
        }
    }
    void LateUpdate() {UpdatePose();}
}
}
