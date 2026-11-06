using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using OvercookedReplay;

public static class NativeDialogChecks {
    public static void Abi() {
        var type=typeof(NativeDialog).GetNestedType("OpenFileName",BindingFlags.NonPublic);
        if(type==null || !type.IsValueType)throw new Exception("OPENFILENAME must be a blittable value type; managed class initialization recurses in player Mono");
        foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance))
            if(!field.FieldType.IsValueType)throw new Exception("OPENFILENAME contains a managed field: "+field.Name);
        int expected=IntPtr.Size==8?152:88;
        if(Marshal.SizeOf(type)!=expected)throw new Exception("OPENFILENAME size does not match Windows ABI");
        CheckOffset(type,"owner",IntPtr.Size==8?8:4);
        CheckOffset(type,"file",IntPtr.Size==8?48:28);
        CheckOffset(type,"flags",IntPtr.Size==8?96:52);
        Debug.Log("Native dialog Windows ABI regression passed");
    }
    static void CheckOffset(Type type,string name,int expected) {
        if(Marshal.OffsetOf(type,name).ToInt32()!=expected)throw new Exception("Wrong native offset: "+name);
    }
    public static void Build() {
        Abi();ReplayChecks.SerializerRegression();ReplayChecks.Run();
        string location=Path.GetFullPath("../../output/Windows/OvercookedReplay.exe");
        var report=BuildPipeline.BuildPlayer(new[]{"Assets/Scenes/Replay.unity"},location,BuildTarget.StandaloneWindows64,BuildOptions.None);
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Replay build failed: "+report.summary.result);
        Debug.Log("Native dialog fixed player built: "+location);
    }
}
