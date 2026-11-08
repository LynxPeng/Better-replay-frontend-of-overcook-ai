using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OvercookedReplay {
// JSON is framed in bytes rather than characters. The strict decoder rejects truncated UTF-8.
public static class LiveWire {
    public const int MaxBytes=1024*1024;
    static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
    static void Fill(Stream stream,byte[] buffer) {
        int offset=0;
        while(offset<buffer.Length) {
            int n=stream.Read(buffer,offset,buffer.Length-offset);
            if(n==0)throw new EndOfStreamException("房主已断开连接");
            offset+=n;
        }
    }
    public static string Receive(Stream stream) {
        var size=new byte[4];Fill(stream,size);
        uint n=((uint)size[0]<<24)|((uint)size[1]<<16)|((uint)size[2]<<8)|size[3];
        if(n==0||n>MaxBytes)throw new InvalidDataException("无效的联机消息长度");
        var payload=new byte[n];Fill(stream,payload);return Utf8.GetString(payload);
    }
    public static void Send(Stream stream,string json) {
        byte[] payload=Utf8.GetBytes(json);int n=payload.Length;
        if(n==0||n>MaxBytes)throw new InvalidDataException("联机消息过大");
        var prefix=new byte[]{(byte)(n>>24),(byte)(n>>16),(byte)(n>>8),(byte)n};
        stream.Write(prefix,0,4);stream.Write(payload,0,n);
    }
}

// Only this connection's queues cross the thread boundary. Reconnecting allocates a fresh
// instance so a late welcome/tick on the old socket can never reach the new game.
internal sealed class LiveConnection : IDisposable {
    readonly TcpClient socket=new TcpClient();
    readonly ConcurrentQueue<string> inbound=new ConcurrentQueue<string>();
    readonly BlockingCollection<string> outbound=new BlockingCollection<string>(64);
    public volatile string Failure;
    volatile bool disposed;
    public void Begin(string address,int port,string hello) {
        Task.Run(async ()=>{
            try {
                Task connection=socket.ConnectAsync(address,port);
                if(await Task.WhenAny(connection,Task.Delay(3500))!=connection)throw new TimeoutException("连接超时");
                await connection;
                socket.NoDelay=true;socket.ReceiveTimeout=15000;socket.SendTimeout=4000;
                using(var stream=socket.GetStream()) {
                    LiveWire.Send(stream,hello);
                    var writer=Task.Run(()=>{
                        try {
                            var heartbeat=Stopwatch.StartNew();
                            while(!outbound.IsCompleted) {
                                if(outbound.TryTake(out string json,200))LiveWire.Send(stream,json);
                                // Scene creation and the OS window drag loop can block Unity Update.
                                if(heartbeat.ElapsedMilliseconds>=1000){
                                    LiveWire.Send(stream,"{\"type\":\"ping\"}");heartbeat.Restart();
                                }
                            }
                            socket.Close();
                        }
                        catch(Exception ex){if(!disposed)Failure=ex.Message;socket.Close();}
                    });
                    try {
                        while(!disposed) {
                            string json=LiveWire.Receive(stream);
                            if(inbound.Count>=128)throw new IOException("客户端来不及处理房主消息");
                            inbound.Enqueue(json);
                        }
                    } finally {outbound.CompleteAdding();socket.Close();await writer;}
                }
            }catch(Exception ex){if(!disposed)Failure=ex.GetBaseException().Message;}
            finally{socket.Close();}
        });
    }
    public bool Read(out string json)=>inbound.TryDequeue(out json);
    public void Write(string json) {
        try {if(!disposed&&!outbound.TryAdd(json))Failure="发送队列已满";}
        catch(InvalidOperationException){if(!disposed)Failure="连接已关闭";}
    }
    public void Dispose(){disposed=true;outbound.CompleteAdding();socket.Close();}
    public void Leave(){Write("{\"type\":\"leave\"}");outbound.CompleteAdding();}
}

[Serializable] internal sealed class LiveModel {
    public string id,label;
}
[Serializable] internal sealed class LiveMessage {
    public string type,mode,phase,layout,model0,model1,resume_token,message,reason,record,match_id,policy_fault;
    public int version,slot,tick,horizon,seed,host_slot;
    public float countdown,tick_rate,reconnect_remaining;
    public bool host,fatal,retryable,old_dynamics,ready0,ready1,connected0,connected1,complete,warming;
    public LiveModel[] models;public string[] layouts,lan_addresses;
    public Frame frame;public Segment segment;public ReplayHeader header;
}
// Separate outbound types avoid sending unrelated defaults that could overwrite lobby settings.
[Serializable] internal class LiveHello { public string type="hello",host_token,resume_token,name;public int version=1; }
[Serializable] internal class LiveControl { public string type; }
[Serializable] internal class LiveReady : LiveControl { public bool ready; }
[Serializable] internal class LivePause : LiveControl { public bool paused; }
[Serializable] internal class LiveInput : LiveControl { public string match_id;public int tick,seq,action; }
[Serializable] internal class LiveConfiguration {
    public string type="configure",mode="human_ai",layout="cramped_room",model0="greedy",model1="greedy";
    public int horizon=400,tick_rate=2,seed,host_slot;
    public bool old_dynamics;
}
}
