using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

public static class MacBuild {
    public static void Build() => BuildPlayer(false);
    public static void BuildLive() => BuildPlayer(true);

    static void BuildPlayer(bool live) {
        if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,BuildTarget.StandaloneOSX))
            throw new InvalidOperationException("Install macOS Build Support (Mono) for this Unity Editor first.");
        NativeDialogChecks.Abi();NativeDialogChecks.MacResults();
        PlatformResourceChecks.ReimportCharacters();
        if(Resources.Load<GameObject>("LocalCharacters/GuguGaga"))GuguGagaChecks.Run();
        else Debug.Log("No local penguin prefab; using built-in chef fallback.");
        if(Resources.Load<GameObject>("LocalCharacters/BlueFatFish"))BlueFishChecks.Actor();
        else Debug.Log("No local fish prefab; using built-in chef fallback.");
        PlatformResourceChecks.Prepare();PlatformResourceChecks.Run();
        if(live)LiveBuild.Prepare();
        else {
            ReplayUiChecks.State();SimmerChecks.Run();CafeFixtureChecks.Run();
            ReplayChecks.SerializerRegression();ReplayChecks.Run();
        }
        var target=NamedBuildTarget.Standalone;
        var backend=PlayerSettings.GetScriptingBackend(target);
        var architecture=PlayerSettings.GetArchitecture(target);
        string identifier=PlayerSettings.GetApplicationIdentifier(target);
        string desktopArchitecture=EditorUserBuildSettings.GetPlatformSettings("OSXUniversal","Architecture");
        bool automaticApi=PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX);
        var graphicsApis=PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneOSX);
        try {
            PlayerSettings.SetScriptingBackend(target,ScriptingImplementation.Mono2x);
            PlayerSettings.SetApplicationIdentifier(target,live?"com.lynxpeng.overcookedlive":"com.lynxpeng.betterovercookedreplay");
            PlayerSettings.SetArchitecture(target,2); // Unity: Universal (Intel x86_64 + Apple Silicon arm64).
            // Desktop architecture is also stored per platform in recent Unity versions.
            EditorUserBuildSettings.SetPlatformSettings("OSXUniversal","Architecture","x64arm64");
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX,new[]{GraphicsDeviceType.Metal});
            string location=Path.GetFullPath(live?"../../output/macOS/OvercookedLive.app":"../../output/macOS/OvercookedReplay.app");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{live?LiveBuild.Scene:"Assets/Scenes/Replay.unity"},locationPathName=location,
                target=BuildTarget.StandaloneOSX,options=BuildOptions.None
            });
            if(report.summary.result!=BuildResult.Succeeded)
                throw new Exception("macOS build failed: "+report.summary.result);
            Debug.Log("macOS Universal Mono player built: "+location+"; bytes="+report.summary.totalSize);
        } finally {
            PlayerSettings.SetScriptingBackend(target,backend);
            PlayerSettings.SetApplicationIdentifier(target,identifier);
            PlayerSettings.SetArchitecture(target,architecture);
            EditorUserBuildSettings.SetPlatformSettings("OSXUniversal","Architecture",desktopArchitecture);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX,graphicsApis);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX,automaticApi);
        }
    }
}
