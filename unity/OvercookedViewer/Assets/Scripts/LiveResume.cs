using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace OvercookedReplay {
// Only the short-lived seat credential is cached. The server always enforces room expiry.
internal static class LiveResume {
    [Serializable] sealed class Seat {public string token;public long until;}
    internal static string Key(string address,int port,string name,bool host) {
        string identity=address.Trim().ToLowerInvariant()+"\n"+port+"\n"+name+"\n"+host;
        using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","");
    }
    static string PathFor(string key)=>Path.Combine(Application.persistentDataPath,"live-seat-"+key+".json");
    public static string Read(string key) {
        if(key==null)return null;
        try {
            var path=PathFor(key);if(!File.Exists(path))return null;
            var seat=JsonUtility.FromJson<Seat>(File.ReadAllText(path));
            return seat!=null&&seat.until>=DateTimeOffset.UtcNow.ToUnixTimeSeconds()?seat.token:null;
        } catch(Exception){return null;}
    }
    public static void Save(string key,string token) {
        if(key==null || string.IsNullOrEmpty(token))return;
        try {
            Directory.CreateDirectory(Application.persistentDataPath);
            File.WriteAllText(PathFor(key),JsonUtility.ToJson(new Seat{token=token,until=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+42}));
        } catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public static void Forget(string key) {
        if(key==null)return;
        try{File.Delete(PathFor(key));}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
}
