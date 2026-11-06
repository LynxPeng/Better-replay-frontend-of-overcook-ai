using System;
using System.IO;
using OvercookedReplay;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LiveBuild {
    const string Scene = "Assets/Scenes/Live.unity";

    public static void Build() {
        // Only bootstrap the new scene once. Subsequent builds preserve scene edits.
        if (!File.Exists(Scene) && !AssetDatabase.CopyAsset("Assets/Scenes/Replay.unity", Scene))
            throw new IOException("Could not create live scene");
        var scene = EditorSceneManager.OpenScene(Scene);
        var presenter = UnityEngine.Object.FindFirstObjectByType<ReplayPlayer>();
        if (presenter == null) throw new InvalidOperationException("Live scene needs a ReplayPlayer");
        if (!presenter.GetComponent<LiveClient>()) {
            presenter.gameObject.AddComponent<LiveClient>();
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save live scene");
        }
        NativeDialogChecks.Abi();
        LiveClientChecks.Run();
        ReplayUiChecks.State();
        CameraLandscapeChecks.Navigation();
        CameraLandscapeChecks.Gestures();
        InteractionHintChecks.Surfaces();
        ReplayCharacterChecks.Slots();
        CharacterTimingChecks.Run();
        SimmerChecks.Run();
        CafeFixtureChecks.Run();
        ReplayChecks.SerializerRegression();
        Directory.CreateDirectory("../../output");
        ReplayChecks.Run();
        var destination = Environment.GetEnvironmentVariable("BETTER_REPLAY_LIVE_BUILD");
        if (String.IsNullOrEmpty(destination))
            destination = Path.GetFullPath("../../output/Live/OvercookedLive.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        var report = BuildPipeline.BuildPlayer(new[] { Scene }, destination,
            BuildTarget.StandaloneWindows64, BuildOptions.None);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Live build failed: " + report.summary.result);
        Debug.Log("Built live client: " + destination);
    }
}
