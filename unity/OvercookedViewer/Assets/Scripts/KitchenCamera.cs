using UnityEngine;

namespace OvercookedReplay {
public enum KitchenView {LongEdge,Isometric}
public static class KitchenCamera {
    public static Vector3 Apply(Camera camera,int width,int height,KitchenView mode,float zoom,float turn,float pitch=42) {
        Vector3 target=new Vector3((width-1)*.5f,.45f,-(height-1)*.5f);
        float yaw=(width>=height?0:90)+turn;
        pitch=Mathf.Clamp(pitch,35,65);
        float verticalDistance=Mathf.Sin(pitch*Mathf.Deg2Rad),horizontalDistance=Mathf.Cos(pitch*Mathf.Deg2Rad);
        Vector3 offset=mode==KitchenView.LongEdge?new Vector3(0,verticalDistance,-horizontalDistance):new Vector3(horizontalDistance*.633f,verticalDistance,-horizontalDistance*.774f);
        target+=Quaternion.Euler(0,yaw,0)*new Vector3(0,0,.6f);
        offset=Quaternion.Euler(0,yaw,0)*offset;
        camera.orthographic=false;camera.fieldOfView=50;camera.nearClipPlane=.1f;
        camera.transform.rotation=Quaternion.LookRotation(-offset.normalized,Vector3.up);
        float tanY=Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad),tanX=tanY*Mathf.Max(.2f,camera.aspect),distance=0;
        // Fit in camera space: every point needs positive depth and must lie in
        // both side planes of the perspective frustum. Frame the restaurant and
        // the upper street; distant gardens may crop like the user's reference.
        var points=new Vector3[16];int index=0;
        for(int box=0;box<2;box++)for(int x=0;x<2;x++)for(int z=0;z<2;z++)for(int y=0;y<2;y++) {
            float minX=box==0?-.5f:-3.8f,maxX=box==0?width-.5f:width+2.8f;
            float minZ=box==0?-height+.5f:-height-.6f,maxZ=box==0?.5f:8.2f;
            // The near edge is floor, not a tall front wall. Keeping its top
            // at floor height avoids zooming out for an imaginary wall.
            float top=box==0?1.5f:z==0?.15f:2.3f;
            Vector3 corner=new Vector3(x==0?minX:maxX,y*top,z==0?minZ:maxZ);
            Vector3 local=Quaternion.Inverse(camera.transform.rotation)*(corner-target);
            points[index++]=local;
            distance=Mathf.Max(distance,Mathf.Abs(local.x)/(tanX*.92f)-local.z);
            distance=Mathf.Max(distance,Mathf.Abs(local.y)/(tanY*.92f)-local.z);
            distance=Mathf.Max(distance,camera.nearClipPlane+.2f-local.z);
        }
        // Allow the camera to slide in its image plane while fitting. This avoids
        // a large empty upper half for the diagonal view of a perspective room.
        float low=0,high=distance;Vector2 center;
        for(int i=0;i<28;i++) {
            float middle=(low+high)*.5f;
            if(FitAtDepth(points,middle,tanX*.92f,tanY*.92f,camera.nearClipPlane+.1f,out center))high=middle;else low=middle;
        }
        distance=high+.001f;FitAtDepth(points,distance,tanX*.92f,tanY*.92f,camera.nearClipPlane+.1f,out center);
        target+=camera.transform.right*center.x+camera.transform.up*center.y;
        distance*=Mathf.Clamp(zoom,.6f,1.8f);camera.transform.position=target+offset.normalized*distance;
        camera.farClipPlane=Mathf.Max(600,distance+width+height+30);
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.86f,.9f,.87f);
        return target;
    }
    static bool FitAtDepth(Vector3[] points,float depth,float tanX,float tanY,float near,out Vector2 center) {
        float left=float.NegativeInfinity,right=float.PositiveInfinity,bottom=float.NegativeInfinity,top=float.PositiveInfinity;
        center=Vector2.zero;
        foreach(var point in points) {
            float z=depth+point.z;if(z<near)return false;
            left=Mathf.Max(left,point.x-tanX*z);right=Mathf.Min(right,point.x+tanX*z);
            bottom=Mathf.Max(bottom,point.y-tanY*z);top=Mathf.Min(top,point.y+tanY*z);
        }
        if(left>right||bottom>top)return false;
        center=new Vector2((left+right)*.5f,(bottom+top)*.5f);return true;
    }
}
}
