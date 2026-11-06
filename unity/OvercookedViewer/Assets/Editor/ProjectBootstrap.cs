using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using OvercookedReplay;

public static class ProjectBootstrap {
    [MenuItem("Tools/Overcooked/Initialize Replay Scene")]
    public static void Setup() {
        Directory.CreateDirectory("Assets/Resources/Prefabs");Directory.CreateDirectory("Assets/Scenes");
        var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        renderer.renderingMode=RenderingMode.Forward;pipeline.msaaSampleCount=4;pipeline.renderScale=1.25f;
        pipeline.mainLightShadowmapResolution=4096;pipeline.shadowDistance=35;
        GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
        EditorUtility.SetDirty(pipeline);EditorUtility.SetDirty(renderer);
        ViewerBackgroundAssets.UpgradeTownMaterials();ViewerBackgroundAssets.BakeSky();
        InteriorMaterialAssets.Prepare();
        // Editor mesh reads work without this flag; standalone players do not.
        foreach(string name in new[]{"kitchencounter_straight_B","stove_single"}) {
            var importer=AssetImporter.GetAtPath("Assets/Resources/RestaurantBits/Models/"+name+".fbx") as ModelImporter;
            if(!importer)throw new InvalidOperationException("Missing native counter mesh: "+name);
            if(!importer.isReadable){importer.isReadable=true;importer.SaveAndReimport();}
        }
        // Upgrade the locally installed Store package copies; original archive is untouched.
        var restaurantTexture=Resources.Load<Texture2D>("RestaurantBits/Textures/restaurantbits_extra");
        foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{"Assets/Resources/RestaurantBits"})) {
            var material=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            material.shader=Shader.Find("Universal Render Pipeline/Lit");material.SetTexture("_BaseMap",restaurantTexture);
            material.SetColor("_BaseColor",Color.white);material.SetFloat("_Smoothness",.12f);
            material.SetFloat("_SpecularHighlights",0);material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.SetFloat("_EnvironmentReflections",0);material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");EditorUtility.SetDirty(material);
        }
        foreach(char c in "XOTDPS")Save(KitchenFactory.Station(c,false),"Station_"+c);
        Save(KitchenFactory.Chef(new Color(.22f,.52f,.7f)),"Chef0");Save(KitchenFactory.Chef(new Color(.86f,.43f,.25f)),"Chef1");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        // Main camera reserves a partial viewport. Clear the entire back buffer
        // first so the IMGUI regions never reuse undefined swap-chain contents.
        var background=new GameObject("UI Background Clear").AddComponent<Camera>();
        background.depth=-100;background.cullingMask=0;background.clearFlags=CameraClearFlags.SolidColor;
        background.backgroundColor=new Color(.88f,.91f,.86f);background.rect=new Rect(0,0,1,1);
        background.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
        var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.nearClipPlane=.05f;camera.farClipPlane=100;
        camera.gameObject.AddComponent<AudioListener>();camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        var sun=new GameObject("Warm Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=.52f;sun.color=new Color(1,.95f,.85f);sun.shadows=LightShadows.Soft;
        sun.shadowStrength=.5f;
        sun.transform.rotation=Quaternion.Euler(48,-35,0);
        var fill=new GameObject("Cool Fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.08f;fill.color=new Color(.72f,.82f,1);fill.transform.rotation=Quaternion.Euler(40,145,0);
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.19f,.205f,.225f);
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.72f,.85f,.85f);
        RenderSettings.fogStartDistance=70;RenderSettings.fogEndDistance=220;
        SceneAtmosphere.Apply(false);
        new GameObject("Replay Player").AddComponent<ReplayPlayer>();
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/Replay.unity");
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Replay.unity",true)};
        PlayerSettings.companyName="Overcooked Research";PlayerSettings.productName="Overcooked Replay";
        PlayerSettings.defaultScreenWidth=1920;PlayerSettings.defaultScreenHeight=1080;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
        PlayerSettings.runInBackground=true;PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
        var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        var input=settings.FindProperty("activeInputHandler");if(input!=null){input.intValue=2;settings.ApplyModifiedPropertiesWithoutUndo();}
        AssetDatabase.SaveAssets();AssetDatabase.Refresh();Debug.Log("Overcooked bootstrap complete");
    }
    static void Save(GameObject root,string name) {
        // Persist custom materials before prefab creation; idempotent paths keep references stable.
        Directory.CreateDirectory("Assets/GeneratedMaterials");int index=0;
        foreach(var renderer in root.GetComponentsInChildren<Renderer>()) {
            var materials=renderer.sharedMaterials;
            for(int j=0;j<materials.Length;j++)if(materials[j] && !AssetDatabase.Contains(materials[j])) {
                string path=$"Assets/GeneratedMaterials/{name}_{index++}.mat";var old=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(old){EditorUtility.CopySerialized(materials[j],old);materials[j]=old;}else AssetDatabase.CreateAsset(materials[j],path);
            }
            renderer.sharedMaterials=materials;
        }
        PrefabUtility.SaveAsPrefabAsset(root,"Assets/Resources/Prefabs/"+name+".prefab");UnityEngine.Object.DestroyImmediate(root);
    }
    [MenuItem("Tools/Overcooked/Build Windows Player")]
    public static void Build() {
        Setup();string location=Path.GetFullPath("../../output/Windows/OvercookedReplay.exe");Directory.CreateDirectory(Path.GetDirectoryName(location));
        var report=BuildPipeline.BuildPlayer(new[]{"Assets/Scenes/Replay.unity"},location,BuildTarget.StandaloneWindows64,BuildOptions.None);
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Windows build failed: "+report.summary.result);
        Debug.Log("Windows player built: "+location);
    }
}
