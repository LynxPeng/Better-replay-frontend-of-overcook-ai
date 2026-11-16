using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using UnityEngine;

namespace OvercookedReplay {
public sealed partial class LiveClient {
    [Serializable] sealed class RoomReady { public int port; }

    Process localRoom;
    string hostExecutable, runtimeDirectory, readyFile, tokenFile, stopFile, roomLog;
    float roomStartupDeadline;
    bool CanCreateRoom => runtimeDirectory != null;
    bool StartingLocalRoom => readyFile != null;

    void FindPortableHost() {
        var platform=Application.platform;
        var architecture=RuntimeInformation.ProcessArchitecture==Architecture.Arm64?"arm64":"x86_64";
        var executable=PortableHostPath(Application.dataPath,platform,architecture);
        if(!File.Exists(executable))return;
        hostExecutable=executable;
        var executableDirectory=Path.GetDirectoryName(executable);
        runtimeDirectory=platform==RuntimePlatform.OSXPlayer?Path.GetDirectoryName(executableDirectory):executableDirectory;
    }

    static string PortableHostPath(string dataPath,RuntimePlatform platform,string architecture) {
        if(platform==RuntimePlatform.OSXPlayer)
            return Path.Combine(dataPath,"Resources","HostRuntime",architecture,"OvercookedHost");
        return Path.Combine(Path.GetDirectoryName(dataPath),"HostRuntime","OvercookedHost.exe");
    }

    void CreateLocalRoom() {
        if(localRoom!=null || !CanCreateRoom)return;
        try {
            string data=Argument("--portable-data") ?? Path.Combine(Application.persistentDataPath,"Live");
            string session=Path.Combine(data,"rooms",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(session);
            tokenFile=Path.Combine(session,"token");
            readyFile=Path.Combine(session,"ready.json");
            stopFile=Path.Combine(session,"stop");
            roomLog=Path.Combine(session,"server.log");

            var bytes=new byte[32];
            using(var random=RandomNumberGenerator.Create())random.GetBytes(bytes);
            hostToken=Convert.ToBase64String(bytes);
            File.WriteAllText(tokenFile,hostToken);
            int owner;
            using(var process=Process.GetCurrentProcess())owner=process.Id;
            string[] arguments={"--bind","0.0.0.0","--port","0","--owner-pid",owner.ToString(),
                "--host-token-file",tokenFile,"--ready-file",readyFile,"--stop-file",stopFile,
                "--log-file",roomLog,"--records",Path.Combine(data,"records"),
                "--models",Path.Combine(runtimeDirectory,"Models")};
            var launch=new ProcessStartInfo(hostExecutable) {
                Arguments=string.Join(" ",Array.ConvertAll(arguments,argument=>"\""+argument+"\"")),
                WorkingDirectory=runtimeDirectory,UseShellExecute=false,CreateNoWindow=true
            };
            localRoom=Process.Start(launch) ?? throw new IOException("无法启动房主程序");
            roomStartupDeadline=Time.realtimeSinceStartup+60;
            error="";
            connectionStatus="正在创建房间…";
        } catch(Exception ex) { LocalRoomFailed(ex.Message); }
    }

    void PollLocalRoom(float now) {
        if(localRoom==null)return;
        if(localRoom.HasExited) {
            LocalRoomFailed("房主服务已退出");
            return;
        }
        if(!StartingLocalRoom)return;
        try {
            if(File.Exists(readyFile)) {
                var ready=JsonUtility.FromJson<RoomReady>(File.ReadAllText(readyFile));
                if(ready==null || ready.port<1 || ready.port>65535)throw new IOException("房主返回的端口无效");
                File.Delete(tokenFile);
                tokenFile=null;
                readyFile=null;
                address="127.0.0.1";
                portText=ready.port.ToString();
                Connect(false);
            } else if(now>=roomStartupDeadline) {
                LocalRoomFailed("创建房间超时");
            }
        } catch(Exception ex) { LocalRoomFailed(ex.Message); }
    }

    void LocalRoomFailed(string reason) {
        StopLocalRoom();
        string message=string.IsNullOrEmpty(roomLog)?reason:reason+"。日志位置 "+roomLog;
        Disconnect(message,false);
        if(smoke!=null)SmokeFail(message);
    }

    void StopLocalRoom() {
        try {
            if(localRoom!=null && !localRoom.HasExited)File.WriteAllText(stopFile,"stop");
            if(tokenFile!=null)File.Delete(tokenFile);
        } catch(IOException ex) { UnityEngine.Debug.LogWarning(ex.Message); }
        catch(UnauthorizedAccessException ex) { UnityEngine.Debug.LogWarning(ex.Message); }
        finally {
            localRoom?.Dispose();
            localRoom=null;
            readyFile=tokenFile=stopFile=null;
            hostToken=null;
        }
    }
}
}
