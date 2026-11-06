using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UniVRM10;
using UniGLTF;
using OvercookedReplay;

public static class BlueFishBootstrap {
    public const string ModelPath="Assets/LocalCharacters/BlueFatFish/BlueFatFish.vrm";
    public const string ActorPath="Assets/Resources/LocalCharacters/BlueFatFish.prefab";
    public const string ScenePath="Assets/Scenes/BlueFishPreview.unity";
    public const string AnimationPath="Assets/Animations/BlueFatFish";
    [MenuItem("Tools/Overcooked/Blue fish/Create local animation trial")]
    public static void Setup() {
        if(!File.Exists(ModelPath))throw new Exception("Place the locally authorized BlueFatFish.vrm in "+ModelPath);
        // Pin URP explicitly: Auto can resolve before the pipeline is initialized on a fresh import.
        var importer=AssetImporter.GetAtPath(ModelPath);var importerSettings=new SerializedObject(importer);
        var pipelineProperty=importerSettings.FindProperty("RenderPipeline");
        int urp=Array.IndexOf(pipelineProperty.enumNames,"UniversalRenderPipeline");
        if(urp<0)throw new Exception("UniVRM URP import option is missing");
        pipelineProperty.enumValueIndex=urp;importerSettings.ApplyModifiedPropertiesWithoutUndo();importer.SaveAndReimport();
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if(!source)throw new Exception("VRM import failed; inspect the Unity import log");
        Directory.CreateDirectory(Path.GetDirectoryName(ActorPath));Directory.CreateDirectory(AnimationPath);AssetDatabase.Refresh();
        var wrapper=new GameObject("BlueFatFish");var model=(GameObject)PrefabUtility.InstantiatePrefab(source);
        model.name="Model";model.transform.SetParent(wrapper.transform,false);
        var renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>();
        var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
        model.transform.localScale=Vector3.one*(1.32f/bounds.size.y);
        model.transform.localPosition=Vector3.up*(-bounds.min.y*model.transform.localScale.y);
        foreach(var r in renderers){r.updateWhenOffscreen=true;r.shadowCastingMode=ShadowCastingMode.On;}
        var actor=wrapper.AddComponent<BlueFishActor>();actor.Initialize();
        var vrm=model.GetComponent<Vrm10Instance>();vrm.UpdateType=Vrm10Instance.UpdateTypes.None;
        var animator=model.GetComponent<Animator>();if(animator){animator.applyRootMotion=false;animator.enabled=false;}
        // Save the bind pose, never a sampled pose: subsequent instances must cache the original axes.
        PrefabUtility.SaveAsPrefabAsset(wrapper,ActorPath);
        var description=new List<string>{"Model: 蓝色小肥鱼Deepseek / 木子恶魔",$"Height: 1.32; humanoid mappings: {actor.BoneCount}"};
        foreach(HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))if(b!=HumanBodyBones.LastBone&&actor.Bone(b))description.Add($"{b}: {actor.Bone(b).name} {actor.Bone(b).position}");
        foreach(var r in renderers)foreach(var m in r.sharedMaterials)description.Add($"Material: {m.name}, shader: {m.shader.name}");
        var output=Path.GetFullPath("../../output/bluefish");Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"rig-inspection.txt"),description);
        Bake(actor);UnityEngine.Object.DestroyImmediate(wrapper);
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.fieldOfView=34;
        camera.nearClipPlane=.03f;camera.farClipPlane=100;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.78f,.88f,.90f);
        camera.gameObject.AddComponent<UniversalAdditionalCameraData>();camera.gameObject.AddComponent<AudioListener>();
        camera.transform.position=new Vector3(1.02f,1.6f,3.56f);camera.transform.LookAt(new Vector3(0,.7f,0));
        var sun=new GameObject("Soft studio key").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1;sun.shadows=LightShadows.Soft;sun.shadowStrength=.4f;sun.transform.rotation=Quaternion.Euler(45,-30,0);
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.68f,.72f,.78f);RenderSettings.fog=false;
        KitchenFactory.Part("Studio floor",null,PrimitiveType.Cylinder,new Vector3(0,-.055f,0),new Vector3(4,.05f,4),KitchenFactory.Material("#c8ddda"));
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ActorPath));
        var table=KitchenFactory.Part("Interaction counter",null,PrimitiveType.Cube,new Vector3(0,.18f,.65f),new Vector3(.74f,.36f,.42f),KitchenFactory.Material("#528e8d"));
        var showcase=new GameObject("Animation showcase").AddComponent<BlueFishShowcase>();showcase.actor=instance.GetComponent<BlueFishActor>();showcase.counter=table.transform;
        EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
        Debug.Log("BlueFatFish local actor, eight clips and preview scene created");
    }
    static void Bake(BlueFishActor actor) {
        var bones=actor.AnimatedTransforms.ToList();
        foreach(FishAction action in Enum.GetValues(typeof(FishAction))) {
            var curves=new Dictionary<string,AnimationCurve[]>();
            foreach(var bone in bones)curves[AnimationUtility.CalculateTransformPath(bone,actor.transform)]=Enumerable.Range(0,7).Select(_=>new AnimationCurve()).ToArray();
            float duration=BlueFishMotion.Duration(action);int count=Mathf.CeilToInt(duration*30);
            for(int frame=0;frame<=count;frame++) {
                float t=duration*frame/count;actor.Sample(action,t);
                foreach(var bone in bones) {
                    var q=bone.localRotation;var p=bone.localPosition;
                    var values=new[]{q.x,q.y,q.z,q.w,p.x,p.y,p.z};var channels=curves[AnimationUtility.CalculateTransformPath(bone,actor.transform)];
                    for(int channel=0;channel<7;channel++)channels[channel].AddKey(t,values[channel]);
                }
            }
            string path=AnimationPath+"/"+action+".anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);bool exists=clip;
            if(!clip)clip=new AnimationClip();else clip.ClearCurves();clip.name=action.ToString();clip.frameRate=30;
            foreach(var pair in curves)for(int i=0;i<7;i++) {
                var keys=pair.Value[i].keys;
                if(keys.All(k=>Mathf.Abs(k.value-keys[0].value)<.000001f))pair.Value[i].keys=new[]{keys[0],keys[keys.Length-1]};
                string property=i<4?"localRotation."+"xyzw"[i]:"localPosition."+"xyz"[i-4];
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(pair.Key,typeof(Transform),property),pair.Value[i]);
            }
            clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=BlueFishMotion.Loops(action);AnimationUtility.SetAnimationClipSettings(clip,settings);
            if(!exists)AssetDatabase.CreateAsset(clip,path);else EditorUtility.SetDirty(clip);
        }
    }
    [MenuItem("Tools/Overcooked/Blue fish/Build local trial")]
    public static void Build() {
        string folder=Path.GetFullPath("../../output/BlueFishPreview");Directory.CreateDirectory(folder);
        var report=BuildPipeline.BuildPlayer(new[]{ScenePath},Path.Combine(folder,"BlueFishPreview.exe"),BuildTarget.StandaloneWindows64,BuildOptions.None);
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("BlueFish preview build failed");
        Debug.Log("BlueFish preview build succeeded: "+folder);
    }
    public static void BuildReplay() {
        string folder=Path.GetFullPath("../../output/BlueFishReplay");Directory.CreateDirectory(folder);
        var report=BuildPipeline.BuildPlayer(new[]{"Assets/Scenes/Replay.unity"},Path.Combine(folder,"OvercookedReplay.exe"),BuildTarget.StandaloneWindows64,BuildOptions.None);
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("BlueFish replay build failed");
    }
}
