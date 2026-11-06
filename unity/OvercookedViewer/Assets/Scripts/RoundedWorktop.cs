using System.Collections.Generic;
using UnityEngine;

namespace OvercookedReplay {
// Deform the native slab, preserving atlas, height and the body's flat shading.
// Generated meshes live in memory; prefabs retain their licensed source mesh.
public sealed class RoundedWorktop : MonoBehaviour {
    public float SurfaceY=.3f;
    public float SlabDepth=.122f;
    public float BodyScale=1;
    public float BodyCornerRadius;
    const float Radius=CafeSurfaceStyle.CounterCornerRadius,Half=CafeSurfaceStyle.CounterWidth*.5f,Inner=Half-Radius;
    const float CurveEdge=.008f;
    static readonly Dictionary<(Mesh,Matrix4x4,float,float,float,float),Mesh> Cache=new Dictionary<(Mesh,Matrix4x4,float,float,float,float),Mesh>();
    bool applied;
    struct Vertex {
        public Vector3 p,n;public Vector2 uv;
        public static Vertex Mid(Vertex a,Vertex b)=>new Vertex {p=(a.p+b.p)*.5f,n=(a.n+b.n).normalized,uv=(a.uv+b.uv)*.5f};
    }
    static Vector2Int Cell(Vector2 uv)=>new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(uv.x*4),0,3),Mathf.Clamp(Mathf.FloorToInt(uv.y*4),0,3));
    public void Apply() {
        if(applied)return;applied=true;
        foreach(var filter in GetComponentsInChildren<MeshFilter>()) {
            var source=filter.sharedMesh;if(!source)continue;
            if(!source.isReadable) {
                Debug.LogError("Rounded counter requires readable source mesh; run ProjectBootstrap.Setup. Keeping original geometry: "+source.name);
                continue;
            }
            var toLocal=Matrix4x4.identity;
            for(var node=filter.transform;node!=transform;node=node.parent)
                toLocal=Matrix4x4.TRS(node.localPosition,node.localRotation,node.localScale)*toLocal;
            var key=(source,toLocal,SurfaceY,SlabDepth,BodyScale,BodyCornerRadius);
            if(Cache.TryGetValue(key,out var cached)&&cached){filter.sharedMesh=cached;continue;}
            var points=source.vertices;var normals=source.normals;var uv=source.uv;
            var vertices=new List<Vector3>();var resultNormals=new List<Vector3>();var resultUV=new List<Vector2>();
            var inverse=toLocal.inverse;var toNormal=inverse.transpose;var fromNormal=toLocal.transpose;
            Vertex Read(int i)=>new Vertex {p=toLocal.MultiplyPoint3x4(points[i]),n=toNormal.MultiplyVector(normals[i]).normalized,uv=uv[i]};
            // The native cap has its own palette cell. A height band alone also
            // catches cabinet/body vertices and produces triangular gradients.
            var capCells=new HashSet<Vector2Int>();
            for(int part=0;part<source.subMeshCount;part++) {
                var indices=source.GetTriangles(part);
                for(int i=0;i<indices.Length;i+=3) {
                    var a=Read(indices[i]);var b=Read(indices[i+1]);var c=Read(indices[i+2]);
                    if(Mathf.Abs(a.p.y-SurfaceY)<.0002f&&Mathf.Abs(b.p.y-SurfaceY)<.0002f&&Mathf.Abs(c.p.y-SurfaceY)<.0002f)
                        capCells.Add(Cell((a.uv+b.uv+c.uv)/3));
                }
            }
            void Emit(Vertex v,List<int> indices) {
                var centre=new Vector2(Mathf.Clamp(v.p.x,-Inner,Inner),Mathf.Clamp(v.p.z,-Inner,Inner));
                var radial=new Vector2(v.p.x-centre.x,v.p.z-centre.y);
                if(radial.magnitude>Radius) {
                    radial.Normalize();v.p.x=centre.x+radial.x*Radius;v.p.z=centre.y+radial.y*Radius;
                    float horizontal=new Vector2(v.n.x,v.n.z).magnitude;
                    v.n=new Vector3(radial.x*horizontal,v.n.y,radial.y*horizontal).normalized;
                }
                indices.Add(vertices.Count);vertices.Add(inverse.MultiplyPoint3x4(v.p));
                resultNormals.Add(fromNormal.MultiplyVector(v.n).normalized);resultUV.Add(v.uv);
            }
            void Triangle(Vertex a,Vertex b,Vertex c,int depth,List<int> indices) {
                float minX=Mathf.Min(a.p.x,Mathf.Min(b.p.x,c.p.x)),maxX=Mathf.Max(a.p.x,Mathf.Max(b.p.x,c.p.x));
                float minZ=Mathf.Min(a.p.z,Mathf.Min(b.p.z,c.p.z)),maxZ=Mathf.Max(a.p.z,Mathf.Max(b.p.z,c.p.z));
                bool corner=(minX<-Inner||maxX>Inner)&&(minZ<-Inner||maxZ>Inner);
                float ab=new Vector2(a.p.x-b.p.x,a.p.z-b.p.z).sqrMagnitude,bc=new Vector2(b.p.x-c.p.x,b.p.z-c.p.z).sqrMagnitude,ca=new Vector2(c.p.x-a.p.x,c.p.z-a.p.z).sqrMagnitude;
                if(depth<18&&corner&&Mathf.Max(ab,Mathf.Max(bc,ca))>CurveEdge*CurveEdge) {
                    if(ab>=bc&&ab>=ca){var mid=Vertex.Mid(a,b);Triangle(a,mid,c,depth+1,indices);Triangle(mid,b,c,depth+1,indices);}
                    else if(bc>=ca){var mid=Vertex.Mid(b,c);Triangle(a,b,mid,depth+1,indices);Triangle(a,mid,c,depth+1,indices);}
                    else {var mid=Vertex.Mid(c,a);Triangle(a,b,mid,depth+1,indices);Triangle(mid,b,c,depth+1,indices);}
                } else {Emit(a,indices);Emit(b,indices);Emit(c,indices);}
            }
            // The native carcass is shallower than its cap at the front. Its
            // bottom vertices describe the actual rectangular footprint.
            float bottom=999;for(int i=0;i<points.Length;i++)bottom=Mathf.Min(bottom,Read(i).p.y);
            var footprint=new Bounds();bool firstBase=true;
            for(int i=0;i<points.Length;i++) {
                var p=Read(i).p;if(p.y>bottom+.0002f)continue;p.x*=BodyScale;p.z*=BodyScale;
                if(firstBase){footprint=new Bounds(p,Vector3.zero);firstBase=false;}else footprint.Encapsulate(p);
            }
            var bodyCentre=new Vector2(footprint.center.x,footprint.center.z);
            var bodyHalf=new Vector2(footprint.extents.x,footprint.extents.z);
            var bodyInner=bodyHalf-Vector2.one*BodyCornerRadius;
            Vertex Inset(Vertex v){v.p.x*=BodyScale;v.p.z*=BodyScale;return v;}
            void EmitBody(Vertex v,List<int> indices) {
                // Round the carcass rectangle and carry any protruding detail
                // along with its corner, rather than flattening the detail.
                var box=new Vector2(Mathf.Clamp(v.p.x,footprint.min.x,footprint.max.x),Mathf.Clamp(v.p.z,footprint.min.z,footprint.max.z));
                var centre=bodyCentre+new Vector2(Mathf.Clamp(box.x-bodyCentre.x,-bodyInner.x,bodyInner.x),Mathf.Clamp(box.y-bodyCentre.y,-bodyInner.y,bodyInner.y));
                var radial=box-centre;
                if(radial.magnitude>BodyCornerRadius) {
                    var mapped=centre+radial.normalized*BodyCornerRadius;var delta=mapped-box;
                    // Float roundoff can put an unchanged straight edge just
                    // outside the radius. Preserve its authored door normals.
                    if(delta.sqrMagnitude>1e-12f) {
                        v.p.x+=delta.x;v.p.z+=delta.y;
                        float horizontal=new Vector2(v.n.x,v.n.z).magnitude;radial.Normalize();
                        v.n=new Vector3(radial.x*horizontal,v.n.y,radial.y*horizontal).normalized;
                    }
                }
                indices.Add(vertices.Count);vertices.Add(inverse.MultiplyPoint3x4(v.p));
                resultNormals.Add(fromNormal.MultiplyVector(v.n).normalized);resultUV.Add(v.uv);
            }
            void BodyTriangle(Vertex a,Vertex b,Vertex c,int depth,List<int> indices) {
                bool corner=(Mathf.Min(a.p.x,Mathf.Min(b.p.x,c.p.x))<bodyCentre.x-bodyInner.x||Mathf.Max(a.p.x,Mathf.Max(b.p.x,c.p.x))>bodyCentre.x+bodyInner.x)
                    &&(Mathf.Min(a.p.z,Mathf.Min(b.p.z,c.p.z))<bodyCentre.y-bodyInner.y||Mathf.Max(a.p.z,Mathf.Max(b.p.z,c.p.z))>bodyCentre.y+bodyInner.y);
                float ab=new Vector2(a.p.x-b.p.x,a.p.z-b.p.z).sqrMagnitude,bc=new Vector2(b.p.x-c.p.x,b.p.z-c.p.z).sqrMagnitude,ca=new Vector2(c.p.x-a.p.x,c.p.z-a.p.z).sqrMagnitude;
                // Only X/Z curvature needs subdivision; avoid multiplying rows
                // along the cabinet's straight vertical direction.
                if(corner&&depth<18&&Mathf.Max(ab,Mathf.Max(bc,ca))>CurveEdge*CurveEdge) {
                    if(ab>=bc&&ab>=ca){var mid=Vertex.Mid(a,b);BodyTriangle(a,mid,c,depth+1,indices);BodyTriangle(mid,b,c,depth+1,indices);}
                    else if(bc>=ca){var mid=Vertex.Mid(b,c);BodyTriangle(a,b,mid,depth+1,indices);BodyTriangle(a,mid,c,depth+1,indices);}
                    else {var mid=Vertex.Mid(c,a);BodyTriangle(a,b,mid,depth+1,indices);BodyTriangle(mid,b,c,depth+1,indices);}
                } else {EmitBody(a,indices);EmitBody(b,indices);EmitBody(c,indices);}
            }
            var triangles=new List<int>[source.subMeshCount];
            for(int part=0;part<triangles.Length;part++) {
                var original=source.GetTriangles(part);var indices=triangles[part]=new List<int>();
                for(int i=0;i<original.Length;i+=3) {
                    var a=Read(original[i]);var b=Read(original[i+1]);var c=Read(original[i+2]);
                    bool slab=Mathf.Min(a.p.y,Mathf.Min(b.p.y,c.p.y))>=SurfaceY-SlabDepth&&Mathf.Max(a.p.y,Mathf.Max(b.p.y,c.p.y))<=SurfaceY+.0002f
                        &&capCells.Contains(Cell(a.uv))&&capCells.Contains(Cell(b.uv))&&capCells.Contains(Cell(c.uv));
                    if(slab)Triangle(a,b,c,0,indices);
                    else if(BodyCornerRadius>0)BodyTriangle(Inset(a),Inset(b),Inset(c),0,indices);
                    else foreach(int vertex in new[]{original[i],original[i+1],original[i+2]}) {
                        // A uniform inset fits the original cabinet beneath the
                        // rounded cap. Keep native face normals, UVs and winding.
                        var p=toLocal.MultiplyPoint3x4(points[vertex]);p.x*=BodyScale;p.z*=BodyScale;
                        indices.Add(vertices.Count);vertices.Add(BodyScale==1?points[vertex]:inverse.MultiplyPoint3x4(p));
                        resultNormals.Add(normals[vertex]);resultUV.Add(uv[vertex]);
                    }
                }
            }
            var mesh=new Mesh {name="Rounded "+source.name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
            mesh.SetVertices(vertices);mesh.SetNormals(resultNormals);mesh.SetUVs(0,resultUV);mesh.subMeshCount=triangles.Length;
            for(int part=0;part<triangles.Length;part++)mesh.SetTriangles(triangles[part],part);
            mesh.RecalculateBounds();Cache[key]=mesh;filter.sharedMesh=mesh;
        }
    }
    void Start(){Apply();}
}
}
