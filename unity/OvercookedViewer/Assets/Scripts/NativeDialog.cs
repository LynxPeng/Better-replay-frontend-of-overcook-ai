using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace OvercookedReplay {
public static class NativeDialog {
    // Use pointers, not managed strings/StringBuilder fields: the latter cause
    // recursive Mono marshalling in the standalone Windows player.
    [StructLayout(LayoutKind.Sequential)] struct OpenFileName {
        public uint size;public IntPtr owner,instance,filter,custom;
        public uint maxCustom,index;public IntPtr file;public uint maxFile;
        public IntPtr fileTitle;public uint maxTitle;public IntPtr directory,title;
        public uint flags;public ushort offset,extension;public IntPtr defExt,data,hook,template,reserved;
        public uint dwReserved,flagsEx;
    }
    [DllImport("comdlg32.dll",EntryPoint="GetOpenFileNameW",ExactSpelling=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool GetOpenFileName(ref OpenFileName name);
    [DllImport("comdlg32.dll",ExactSpelling=true)]static extern uint CommDlgExtendedError();
    [DllImport("user32.dll",ExactSpelling=true)]static extern IntPtr GetActiveWindow();

    public static string Open() {
        if(Application.platform==RuntimePlatform.OSXPlayer || Application.platform==RuntimePlatform.OSXEditor)
            return OpenMac();
        if(Application.platform!=RuntimePlatform.WindowsPlayer && Application.platform!=RuntimePlatform.WindowsEditor)
            throw new PlatformNotSupportedException("文件选择器支持 Windows 和 macOS。");
        return OpenWindows();
    }

    static string OpenMac() {
        // Feed a fixed script through stdin; never interpolate selected paths into a shell.
        var start=new ProcessStartInfo("/usr/bin/osascript","-") {
            UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,
            RedirectStandardError=true,CreateNoWindow=true,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
        };
        using(var process=Process.Start(start)) {
            if(process==null)throw new InvalidOperationException("无法启动 macOS 文件选择器。");
            process.StandardInput.Write("try\nreturn POSIX path of (choose file with prompt \"选择 Overcooked 回放\")\non error number -128\nreturn \"\"\nend try\n");
            process.StandardInput.Close();
            var output=process.StandardOutput.ReadToEndAsync();
            var error=process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return DecodeMacResult(process.ExitCode,output.GetAwaiter().GetResult(),error.GetAwaiter().GetResult());
        }
    }

    static string DecodeMacResult(int exitCode,string output,string error) {
        if(exitCode!=0)throw new InvalidOperationException("macOS 文件选择器错误："+error);
        string path=output.TrimEnd('\r','\n');
        return path.Length==0?null:path;
    }

    static string OpenWindows() {
        const int capacity=32768;
        IntPtr file=IntPtr.Zero,filter=IntPtr.Zero,title=IntPtr.Zero;
        try {
            file=Marshal.AllocHGlobal(capacity*2);Marshal.WriteInt16(file,0);
            filter=Marshal.StringToHGlobalUni("回放文件\0*.ocrep.json\0JSON\0*.json\0\0");
            title=Marshal.StringToHGlobalUni("选择 Overcooked 回放");
            var request=new OpenFileName {
                size=(uint)Marshal.SizeOf(typeof(OpenFileName)),owner=GetActiveWindow(),
                filter=filter,index=1,file=file,maxFile=capacity,title=title,
                // Explorer, file/path must exist, preserve process working directory.
                flags=0x80000|0x1000|0x800|0x8
            };
            if(GetOpenFileName(ref request))return Marshal.PtrToStringUni(file);
            uint code=CommDlgExtendedError();
            if(code!=0)throw new InvalidOperationException("Windows 文件对话框错误 0x"+code.ToString("X"));
            return null; // User cancellation is not a failure.
        } finally {
            if(title!=IntPtr.Zero)Marshal.FreeHGlobal(title);
            if(filter!=IntPtr.Zero)Marshal.FreeHGlobal(filter);
            if(file!=IntPtr.Zero)Marshal.FreeHGlobal(file);
        }
    }
}
}
