using System;
using System.IO;
using System.Linq;
using UnityEngine;
using OvercookedReplay;

// Check the actual posed garment envelope where the palms pass the skirt.
// The guard includes a small palm radius; screenshots remain the visual acceptance check.
public static class BlueFishWalkChecks {
    struct Triangle {public Vector3 A,B,C;}
    static float SurfaceX(System.Collections.Generic.List<Triangle> triangles,Vector3 point,float side) {
        float outer=float.NegativeInfinity;
        foreach(var t in triangles) {
            var b=t.B-t.A;var c=t.C-t.A;var p=point-t.A;float determinant=b.y*c.z-b.z*c.y;
            if(Mathf.Abs(determinant)<1e-9f)continue;
            float u=(p.y*c.z-p.z*c.y)/determinant,v=(b.y*p.z-b.z*p.y)/determinant;
            if(u<-.00001f||v<-.00001f||u+v>1.00001f)continue;
            outer=Mathf.Max(outer,side*(t.A.x+u*b.x+v*c.x));
        }
        return outer;
    }
    public static void Run() {
        var prefab=Resources.Load<GameObject>("LocalCharacters/BlueFatFish");
        if(!prefab)throw new Exception("Local BlueFatFish actor is required");
        var instance=UnityEngine.Object.Instantiate(prefab);var actor=instance.GetComponent<BlueFishActor>();
        var garments=instance.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.sharedMaterials.Any(m=>m&&m.name.IndexOf("clothing",StringComparison.OrdinalIgnoreCase)>=0)).ToArray();
        if(garments.Length==0)throw new Exception("No clothing mesh found for the skirt guard");
        float minimum=float.PositiveInfinity;var report=new System.Text.StringBuilder("phase,side,hand_x,hand_y,hand_z,skirt_width,clearance\n");
        try {
            for(int frame=0;frame<32;frame++) {
                float t=BlueFishMotion.Duration(FishAction.Walk)*frame/32;actor.Sample(FishAction.Walk,t);
                var triangles=new System.Collections.Generic.List<Triangle>();
                foreach(var renderer in garments) {
                    var baked=new Mesh();renderer.BakeMesh(baked,true);
                    var weights=renderer.sharedMesh.boneWeights;var bones=renderer.bones;var points=baked.vertices;
                    var left=actor.Bone(HumanBodyBones.LeftUpperArm);var right=actor.Bone(HumanBodyBones.RightUpperArm);
                    bool IsArm(int b)=>b>=0&&b<bones.Length&&bones[b]&&(bones[b].IsChildOf(left)||bones[b].IsChildOf(right));
                    var included=new bool[points.Length];var posed=new Vector3[points.Length];
                    for(int i=0;i<points.Length;i++) {
                        var w=weights[i];float arm=(IsArm(w.boneIndex0)?w.weight0:0)+(IsArm(w.boneIndex1)?w.weight1:0)+(IsArm(w.boneIndex2)?w.weight2:0)+(IsArm(w.boneIndex3)?w.weight3:0);
                        included[i]=arm<=.1f; // sleeves and cuffs are not part of the skirt envelope
                        posed[i]=actor.transform.InverseTransformPoint(renderer.transform.TransformPoint(points[i]));
                    }
                    var indices=baked.triangles;
                    for(int i=0;i<indices.Length;i+=3){int a=indices[i],b=indices[i+1],c=indices[i+2];if(included[a]&&included[b]&&included[c])triangles.Add(new Triangle {A=posed[a],B=posed[b],C=posed[c]});}
                    UnityEngine.Object.DestroyImmediate(baked);
                }
                if(triangles.Count==0)throw new Exception("No skirt triangles remain after excluding sleeves");
                foreach(var side in new[]{HumanBodyBones.LeftHand,HumanBodyBones.RightHand}) {
                    var hand=actor.transform.InverseTransformPoint(actor.Bone(side).position);
                    float width=SurfaceX(triangles,hand,Mathf.Sign(hand.x)),clearance=Mathf.Abs(hand.x)-width-.035f;
                    foreach(var offset in new[]{new Vector3(0,.025f,0),new Vector3(0,-.025f,0),new Vector3(0,0,.025f),new Vector3(0,0,-.025f)}) {
                        float surface=SurfaceX(triangles,hand+offset,Mathf.Sign(hand.x));
                        clearance=Mathf.Min(clearance,Mathf.Abs(hand.x)-surface-.0245f);
                    }
                    minimum=Mathf.Min(minimum,clearance);
                    report.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},{1},{2:R},{3:R},{4:R},{5:R},{6:R}",frame,side,hand.x,hand.y,hand.z,width,clearance));
                }
            }
            string folder=Path.GetFullPath("../../output/bluefish/walk-clearance");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"clearance.csv"),report.ToString());
            Debug.Log("Walk palm/skirt clearance minimum: "+minimum.ToString("F4"));
            if(minimum<.01f)throw new Exception("Walking palms enter the skirt envelope: "+minimum.ToString("F4"));
            Debug.Log("BlueFish walk clearance passed: 32 poses, both palms, baked garment and 0.035 palm radius");
        }finally {UnityEngine.Object.DestroyImmediate(instance);}
    }
}
