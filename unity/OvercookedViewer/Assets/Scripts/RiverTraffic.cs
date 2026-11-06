using UnityEngine;

namespace OvercookedReplay {
// A small fixed fleet, entering under a bridge or off-screen and leaving off-screen.
// Fit once at load; moving the camera later never resets or teleports a ship.
public sealed class RiverTraffic : MonoBehaviour {
    bool fitted;int warmFrames;
    public void Fit(Camera camera) {
        var boats=GetComponentsInChildren<RiverBoatMotion>();if(boats.Length==0)return;
        var planes=GeometryUtility.CalculateFrustumPlanes(camera);
        float leftX=-7.9f,rightX=0;var envelope=new Bounds();bool first=true;
        foreach(var boat in boats) {
            if(boat.transform.parent.name.StartsWith("Right"))rightX=boat.transform.position.x;
            foreach(var renderer in boat.GetComponentsInChildren<Renderer>()) {
                var b=renderer.bounds;b.center-=new Vector3(boat.transform.position.x,0,boat.transform.position.z);
                if(first){envelope=b;first=false;}else envelope.Encapsulate(b);
            }
        }
        // One common route per bank keeps its pair exactly half a cycle apart,
        // despite different mesh sizes. Fit the whole fleet envelope, not a root.
        (float start,float end,float coverage) Route(float x) {
            float entry=float.NegativeInfinity,exit=float.PositiveInfinity,low=entry,high=exit;
            foreach(var plane in planes) {
                var n=plane.normal;float centre=n.x*x+n.y*boats[0].transform.position.y+plane.distance;
                float support=Vector3.Dot(n,envelope.center)+Vector3.Dot(new Vector3(Mathf.Abs(n.x),Mathf.Abs(n.y),Mathf.Abs(n.z)),envelope.extents);
                if(n.z<-.00001f){exit=Mathf.Min(exit,-(n.x*x+plane.distance+support)/n.z);high=Mathf.Min(high,-centre/n.z);}
                if(n.z>.00001f){entry=Mathf.Max(entry,-(n.x*x+plane.distance+support)/n.z);low=Mathf.Max(low,-centre/n.z);}
            }
            float end=Mathf.Max(9.5f,exit+.3f),start=Mathf.Max(7.1f,entry-.3f);
            start=Mathf.Min(start,end-2);
            return (start,end,Mathf.Max(0,high-Mathf.Max(8.4f,low))/(end-start));
        }
        var left=Route(leftX);var right=Route(rightX);bool pairRight=right.coverage>left.coverage;
        for(int i=0;i<boats.Length;i++) {
            bool onRight=i<2?pairRight:!pairRight;var route=onRight?right:left;
            var boat=boats[i];boat.transform.SetParent(transform.Find(onRight?"Right River Garden":"Left River Garden"),true);
            var p=boat.transform.localPosition;p.x=onRight?rightX:leftX;boat.transform.localPosition=p;
            boat.Configure(route.start,route.end,.9f,onRight?-1:1,i<2?i*.5f:.25f);
        }
        fitted=true;
    }
    void LateUpdate() {if(!fitted&&++warmFrames>=3&&Camera.main)Fit(Camera.main);}
}
}
