using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using OvercookedReplay;

public static class GuguGagaBootstrap {
    public const string ModelPath="Assets/LocalCharacters/GuguGaga/gugugaga.glb";
    public const string ActorPath="Assets/Resources/LocalCharacters/GuguGaga.prefab";
    public const string ScenePath="Assets/Scenes/GuguGagaPreview.unity";
    [MenuItem("Tools/Overcooked/GuguGaga/Create local animation trial")]
    public static void Setup() {
        Inspect();
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Directory.CreateDirectory(Path.GetDirectoryName(ActorPath));
        string materials="Assets/Resources/LocalCharacters/GuguGagaMaterials";Directory.CreateDirectory(materials);AssetDatabase.Refresh();
        var root=new GameObject("GuguGaga");var model=(GameObject)PrefabUtility.InstantiatePrefab(source);
        model.name="Model";model.transform.SetParent(root.transform,false);
        // UniGLTF's default Z reversal imports the face toward -Z. Turn it toward
        // replay forward (+Z), also bringing anatomical left to normalized -X.
        model.transform.localRotation=Quaternion.Euler(0,180,0);
        var renderers=model.GetComponentsInChildren<SkinnedMeshRenderer>();
        // SkinnedMeshRenderer.bounds includes a padding margin from the importer.
        // Use the bind-pose mesh vertices so the feet really touch the floor.
        var bounds=new Bounds();bool first=true;
        foreach(var r in renderers)foreach(var v in r.sharedMesh.vertices) {
            Vector3 point=r.transform.TransformPoint(v);
            if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
        }
        float scale=1.28f/bounds.size.y;model.transform.localScale=Vector3.one*scale;
        model.transform.localPosition=new Vector3(-bounds.center.x*scale,-bounds.min.y*scale,0);
        foreach(var r in renderers) {
            r.updateWhenOffscreen=true;r.shadowCastingMode=ShadowCastingMode.On;
            r.sharedMaterials=r.sharedMaterials.Select(m=>{
                string path=materials+"/"+m.name+".mat";var copy=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!copy){copy=new Material(m);AssetDatabase.CreateAsset(copy,path);}else copy.CopyPropertiesFromMaterial(m);
                if(copy.HasProperty("_Metallic"))copy.SetFloat("_Metallic",0);
                if(copy.HasProperty("_Smoothness"))copy.SetFloat("_Smoothness",.18f);
                if(m.name=="Hair") {
                    // The source uses BLEND for layered hair cards. Per-renderer
                    // transparent sorting shows the back cards through the face.
                    copy.SetFloat("_Surface",0);copy.SetFloat("_AlphaClip",1);copy.SetFloat("_Cutoff",.35f);
                    copy.SetFloat("_SrcBlend",(int)BlendMode.One);copy.SetFloat("_DstBlend",(int)BlendMode.Zero);copy.SetFloat("_ZWrite",1);
                    copy.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");copy.DisableKeyword("_ALPHAPREMULTIPLY_ON");copy.EnableKeyword("_ALPHATEST_ON");
                    copy.SetOverrideTag("RenderType","TransparentCutout");copy.renderQueue=(int)RenderQueue.AlphaTest;
                    copy.SetShaderPassEnabled("DepthOnly",true);copy.SetShaderPassEnabled("ShadowCaster",true);
                }
                EditorUtility.SetDirty(copy);return copy;
            }).ToArray();
        }
        var actor=root.AddComponent<GuguGagaActor>();actor.Initialize();
        // Persist the bind pose before sampling. Runtime always caches these axes.
        PrefabUtility.SaveAsPrefabAsset(root,ActorPath);Bake(actor);UnityEngine.Object.DestroyImmediate(root);
        EditorSceneManager.OpenScene(BlueFishBootstrap.ScenePath);
        var showcase=UnityEngine.Object.FindFirstObjectByType<BlueFishShowcase>();
        if(showcase.actor)UnityEngine.Object.DestroyImmediate(showcase.actor.gameObject);
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ActorPath));
        showcase.actor=null;showcase.AlternateActor=instance.GetComponent<GuguGagaActor>();
        showcase.DisplayName="咕咕嘎嘎企鹅";showcase.Credit="模型：ilvoris · 绑定：photon\nCC BY 4.0 · 原创回放动作";
        EditorSceneManager.SaveScene(showcase.gameObject.scene,ScenePath);AssetDatabase.SaveAssets();
        Debug.Log("GuguGaga actor, eight clips and preview scene created");
    }
    static void Bake(GuguGagaActor actor) {
        const string folder="Assets/Animations/GuguGaga";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        var bones=actor.BodyTransforms.ToArray();
        foreach(FishAction action in Enum.GetValues(typeof(FishAction))) {
            var curves=new Dictionary<Transform,AnimationCurve[]>();
            foreach(var b in bones)curves[b]=Enumerable.Range(0,7).Select(_=>new AnimationCurve()).ToArray();
            float duration=BlueFishMotion.Duration(action);int count=Mathf.CeilToInt(duration*30);
            for(int i=0;i<=count;i++) {
                float t=duration*i/count;actor.Sample(action,t);
                foreach(var b in bones) {
                    var q=b.localRotation;var p=b.localPosition;var values=new[]{q.x,q.y,q.z,q.w,p.x,p.y,p.z};
                    for(int j=0;j<7;j++)curves[b][j].AddKey(t,values[j]);
                }
            }
            string path=folder+"/"+action+".anim";var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);bool exists=clip;
            if(!clip)clip=new AnimationClip();else clip.ClearCurves();clip.name=action.ToString();clip.frameRate=30;
            foreach(var pair in curves)for(int j=0;j<7;j++) {
                var keys=pair.Value[j].keys;
                if(keys.All(k=>Mathf.Abs(k.value-keys[0].value)<.000001f))pair.Value[j].keys=new[]{keys[0],keys[keys.Length-1]};
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(pair.Key,actor.transform),typeof(Transform),j<4?"localRotation."+"xyzw"[j]:"localPosition."+"xyz"[j-4]),pair.Value[j]);
            }
            clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=BlueFishMotion.Loops(action);AnimationUtility.SetAnimationClipSettings(clip,settings);
            if(!exists)AssetDatabase.CreateAsset(clip,path);else EditorUtility.SetDirty(clip);
        }
    }
    public static void Build() {
        string folder=Path.GetFullPath("../../output/GuguGagaPreview");Directory.CreateDirectory(folder);
        var report=BuildPipeline.BuildPlayer(new[]{ScenePath},Path.Combine(folder,"GuguGagaPreview.exe"),BuildTarget.StandaloneWindows64,BuildOptions.None);
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Penguin preview build failed");
        Debug.Log("GuguGaga preview build succeeded");
    }
    public static void Inspect() {
        var importer=AssetImporter.GetAtPath(ModelPath);if(!importer)throw new Exception("Place the photon rigged GLB at "+ModelPath);
        var settings=new SerializedObject(importer);var pipeline=settings.FindProperty("m_renderPipeline");pipeline.enumValueIndex=Array.IndexOf(pipeline.enumNames,"UniversalRenderPipeline");settings.ApplyModifiedPropertiesWithoutUndo();importer.SaveAndReimport();
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);if(!source)throw new Exception("Penguin GLB import failed");
        var instance=UnityEngine.Object.Instantiate(source);var lines=new System.Collections.Generic.List<string>();
        foreach(var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) {
            lines.Add($"Mesh: {renderer.name}, bones={renderer.bones.Length}, bounds={renderer.bounds}, localScale={renderer.transform.lossyScale}");
            foreach(var m in renderer.sharedMaterials)lines.Add($"Material: {m.name}, shader={m.shader.name}, texture={m.mainTexture?.name}");
        }
        foreach(var t in instance.GetComponentsInChildren<Transform>())lines.Add($"{t.name}: world={t.position}, local={t.localPosition}, rotation={t.rotation}");
        string folder=Path.GetFullPath("../../output/character-research");Directory.CreateDirectory(folder);File.WriteAllLines(Path.Combine(folder,"gugugaga-unity-rig.txt"),lines);
        UnityEngine.Object.DestroyImmediate(instance);Debug.Log("GuguGaga rig imported and inspected");
    }
}
