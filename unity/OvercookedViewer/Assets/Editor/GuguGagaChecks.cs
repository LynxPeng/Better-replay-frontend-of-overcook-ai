using System;
using System.Linq;
using UnityEngine;
using OvercookedReplay;

public static class GuguGagaChecks {
    public static void All() {
        Run();BlueFishChecks.Actor();BlueFishChecks.Motion();BlueFishChecks.ReplayMapping();
        BlueFishChecks.Celebration();BlueFishChecks.PresentationTail();BlueFishWalkChecks.Run();
        BlueFishExpressionChecks.DefaultMood();ReplayChecks.Run();
    }
    static int checks;
    static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    public static void Run() {
        checks=0;ReplayCharacterChecks.Contract();ReplayCharacterChecks.Slots();ReplayCharacterChecks.PenguinAvailable();
        var instance=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("LocalCharacters/GuguGaga"));
        try {
            var actor=instance.GetComponent<GuguGagaActor>();actor.Initialize();
            foreach(var r in instance.GetComponentsInChildren<SkinnedMeshRenderer>())foreach(var m in r.sharedMaterials)
                if(m.name.StartsWith("Hair"))Check(m.GetFloat("_Surface")==0&&m.IsKeywordEnabled("_ALPHATEST_ON"),"Hair must use depth-writing alpha cutout to avoid transparent sorting through the face");
            Check(actor.BoneCount==74,"Expected photon's 74-joint skinned GLB");
            Check(actor.Bone(HumanBodyBones.LeftHand).position.x<0,"Penguin anatomical left must be normalized -X");
            Check(actor.Joint("Eyelid_upper_L").position.z>actor.Bone(HumanBodyBones.Head).position.z,"Penguin must face +Z");
            var rootPosition=instance.transform.position;var rootRotation=instance.transform.rotation;
            foreach(FishAction action in Enum.GetValues(typeof(FishAction))) {
                actor.Sample(action,.27f,1,1.2f,.3f);
                var transforms=actor.AnimatedTransforms.ToArray();var positions=transforms.Select(t=>t.localPosition).ToArray();var rotations=transforms.Select(t=>t.localRotation).ToArray();
                actor.Sample(FishAction.Walk,10,1,2.6f,-1);actor.Sample(action,.27f,1,1.2f,.3f);
                for(int i=0;i<transforms.Length;i++) {
                    Check(Vector3.Distance(positions[i],transforms[i].localPosition)<.00001f,"Seek position changed: "+transforms[i].name);
                    Check(Quaternion.Angle(rotations[i],transforms[i].localRotation)<.05f,"Seek rotation changed: "+transforms[i].name);
                }
                Check(instance.transform.position==rootPosition&&instance.transform.rotation==rootRotation,"Animation changed recorded root");
                Check(float.IsFinite(actor.HeldAnchor.position.y)&&actor.HeldAnchor.position.y>.1f&&actor.HeldAnchor.position.y<1.6f,"Held item anchor invalid");
            }
            actor.Sample(FishAction.Idle,0,1,0,-1);
            float open=Vector3.Distance(actor.Joint("Eyelid_upper_L").localPosition,actor.Joint("Eyelid_lower_L").localPosition);
            var mouth=actor.Joint("Lip_Corner_L").localPosition;float restY=instance.transform.Find("Model").localPosition.y;
            actor.Sample(FishAction.Idle,0,1,2.6f,-1);
            Check(Vector3.Distance(actor.Joint("Eyelid_upper_L").localPosition,actor.Joint("Eyelid_lower_L").localPosition)<open*.01f,"Blink did not close the eyelids");
            actor.Sample(FishAction.Deliver,.225f,1,3.2f,.5f);
            Check(actor.Joint("Lip_Corner_L").localPosition.y>mouth.y,"Delivery must raise mouth corners");
            Check(Mathf.Abs(instance.transform.Find("Model").localPosition.y-restY-.18f)<.00001f,"Delivery jump height is wrong");
            actor.Sample(FishAction.Idle,0,1,0,-1);
            Check(Vector3.Distance(actor.Joint("Lip_Corner_L").localPosition,mouth)<.00001f,"Smile leaked after seek");
            Debug.Log($"GuguGaga checks passed: {checks} assertions; 74-joint skin, normalized axes, eight actions, seek/root/anchor, blink, happy delivery");
        }finally{UnityEngine.Object.DestroyImmediate(instance);}
    }
}
