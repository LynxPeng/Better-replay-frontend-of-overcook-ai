using System;
using System.Runtime.InteropServices;

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
