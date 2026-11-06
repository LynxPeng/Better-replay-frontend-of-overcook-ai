using System;
using System.IO;
using System.Net;
using UnityEngine;

namespace OvercookedReplay {
public sealed class LiveDirectionKeys {
    readonly System.Collections.Generic.List<int> order=new System.Collections.Generic.List<int>(4);
    public void Press(int direction){order.Remove(direction);order.Add(direction);}
    public void Clear()=>order.Clear();
    public int Movement(int heldMask) {
        for(int i=order.Count-1;i>=0;i--)
            if((heldMask & (1<<order[i]))==0)order.RemoveAt(i);
        return order.Count==0?4:order[order.Count-1];
    }
}

[RequireComponent(typeof(ReplayPlayer))]
public sealed partial class LiveClient : MonoBehaviour {
    ReplayPlayer player;
    LiveConnection connection;
    LiveMessage lobby=new LiveMessage(),result;
    LiveModel[] models=Array.Empty<LiveModel>();
    string[] layouts=Array.Empty<string>();
    string[] lanAddresses=Array.Empty<string>();
    LiveConfiguration draft=new LiveConfiguration();
    string address="127.0.0.1",portText="8765",nickname="厨师";
    string seatKey;float lastSeatSave;
    string resumeToken,hostToken,matchId,error="",connectionStatus="输入房主的局域网地址";
    bool welcomed,host,dirty,retrying,focusBlocked,applying;
    int slot=-1,latestTick=-1,sequence,lastSentTick=-1,lastAction=4,interactTick=-1;
    readonly LiveDirectionKeys directions=new LiveDirectionKeys();
    string seedText="0";
    float retryDeadline,retryAt,lastReceived,lobbyReceivedAt;
    public bool Reviewing { get; private set; }
    internal bool Spectating => lobby.mode=="ai_ai";
    internal int ConfirmedTick => latestTick;
    internal bool ShowKitchen => !Reviewing && welcomed && latestTick>=0 && lobby.phase!="lobby";
    internal string Phase => lobby.phase??"";
    internal static bool Local(string value) => string.Equals(value,"localhost",StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(value,out var ip)&&IPAddress.IsLoopback(ip));
    public static bool TryEndpoint(string input,string portField,out string server,out int port) {
        server=(input??"").Trim();port=0;
        int colon=server.IndexOf(':');
        if(colon>=0 && colon==server.LastIndexOf(':')) {
            portField=server.Substring(colon+1);
            server=server.Substring(0,colon).Trim();
        }
        return server.Length>0 && int.TryParse(portField,out port) && port>=1 && port<=65535;
    }
    static string Argument(string key) {
        var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,key);
        return index>=0 && index+1<args.Length ? args[index+1] : null;
    }
    void Start() {
        Application.runInBackground=true;
        player=GetComponent<ReplayPlayer>();
        string target=Argument("--live-address");
        portText=Argument("--live-port")??portText;nickname=Argument("--live-name")??nickname;
        if(!string.IsNullOrEmpty(target)) {
            address=target.Trim();
            if(TryEndpoint(address,portText,out var server,out int port)){
                address=server;portText=port.ToString();
                if(Local(address))hostToken=Environment.GetEnvironmentVariable("BETTER_REPLAY_HOST_TOKEN");
            }
            SetupSmoke();
            Connect(false);
        }
    }
    void Connect(bool resume) {
        if(!TryEndpoint(address,portText,out var server,out int port)) {
            error="请输入有效的房主地址和端口（1–65535）";return;
        }
        address=server;portText=port.ToString();
        connection?.Dispose();
        if(!resume) {
            resumeToken=null;sequence=0;latestTick=-1;dirty=false;applying=false;result=null;host=false;slot=-1;
            lobby=new LiveMessage();retrying=false;Reviewing=false;
            seatKey=LiveResume.Key(address,port,nickname.Trim(),Local(address)&&!string.IsNullOrEmpty(hostToken));
            if(smoke==null)resumeToken=LiveResume.Read(seatKey);
            resume=!string.IsNullOrEmpty(resumeToken);
        }
        connection=new LiveConnection();welcomed=false;error="";
        connectionStatus=resume?"正在恢复连接…":"正在连接房主…";
        var hello=new LiveHello{name=nickname.Trim(),host_token=!resume&&Local(address)?hostToken:null,resume_token=resume?resumeToken:null};
        connection.Begin(address.Trim(),port,JsonUtility.ToJson(hello));
        lastReceived=Time.realtimeSinceStartup;
    }
    void Send(object message) {connection?.Write(JsonUtility.ToJson(message));}
    void Control(string type){Send(new LiveControl{type=type});}
    void ResetInput(){lastSentTick=-1;lastAction=4;interactTick=-1;directions.Clear();}
    void Disconnect(string reason,bool canResume) {
        connection?.Dispose();connection=null;welcomed=false;ResetInput();
        if(player && player.Presentation!=null && !Reviewing)player.Presentation.Pause();
        if(canResume && !string.IsNullOrEmpty(resumeToken) && result==null) {
            if(!retrying)retryDeadline=Time.realtimeSinceStartup+30;
            retrying=true;retryAt=Time.realtimeSinceStartup+1;connectionStatus="连接中断，正在重连…";
        } else {retrying=false;resumeToken=null;LiveResume.Forget(seatKey);connectionStatus=reason;}
        error=reason;
    }
    void Update() {
        float now=Time.realtimeSinceStartup;
        if(retrying&&now>=retryDeadline)Disconnect("重连已超时，请重新加入房间",false);
        if(retrying&&connection==null&&now>=retryAt)Connect(true);
        if(connection!=null) {
            try {
                while(connection!=null && connection.Read(out string json)) {
                    lastReceived=now;Handle(JsonUtility.FromJson<LiveMessage>(json));
                }
            }catch(Exception ex){Disconnect("无法读取房间状态："+ex.Message,false);}
            if(connection!=null && connection.Failure!=null)Disconnect(connection.Failure,true);
            if(connection!=null && welcomed && now-lastReceived>3)Disconnect("房主响应超时",true);
            if(connection!=null && !welcomed && now-lastReceived>8)Disconnect("握手超时",true);
            if(welcomed && result==null && smoke==null && now-lastSeatSave>=8){LiveResume.Save(seatKey,resumeToken);lastSeatSave=now;}
        }
        if(smoke!=null){AdvanceSmoke();return;}
        if(!Reviewing && welcomed && latestTick>=0 && !player.ModalOpen && Input.GetKeyDown(KeyCode.Escape)
            && (Phase=="running"||Phase=="paused"))Send(new LivePause{type="pause",paused=Phase=="running"});
        if(!Reviewing && welcomed && latestTick>=0 && lobby.phase=="running" && MayAct)ReadInput();
    }
    void OnDestroy(){connection?.Dispose();}
    void OnApplicationFocus(bool focused) {
        if(!focused){focusBlocked=true;Neutralize();}
    }
    void Neutralize() {
        if(MayAct && welcomed && latestTick>=0 && lobby.phase=="running"){
            // A real interaction is latched on the host. Only an explicit cancellation
            // from the same controller can clear it before its authoritative boundary.
            Send(new LiveInput{type="cancel_input",match_id=matchId,tick=latestTick+1,seq=++sequence});
            lastSentTick=latestTick+1;lastAction=4;
        }
        interactTick=-1;directions.Clear();
    }
    bool MayAct => lobby.mode=="human_human" || (lobby.mode=="human_ai" && slot==0);
    void Handle(LiveMessage message) {
        if(message==null||message.type==null)throw new InvalidDataException("消息缺少类型");
        switch(message.type) {
            case "welcome":
                if(message.version!=1||message.slot<0||message.slot>1||string.IsNullOrEmpty(message.resume_token))
                    throw new InvalidDataException("房间版本或席位无效");
                host=message.host;slot=message.slot;resumeToken=message.resume_token;
                if(smoke!=null && dropped && retrying)resumedWelcome=true;
                models=message.models??Array.Empty<LiveModel>();layouts=message.layouts??Array.Empty<string>();
                lanAddresses=message.lan_addresses??Array.Empty<string>();
                if(smoke==null)LiveResume.Save(seatKey,resumeToken);
                welcomed=true;retrying=false;connectionStatus="已连接";error="";return;
            case "error":
                if(applying){applying=false;dirty=true;}
                error=message.message??"房主拒绝请求";
                bool tryAgain=message.fatal&&message.retryable&&!welcomed&&!string.IsNullOrEmpty(resumeToken);
                if(message.fatal)Disconnect(error,tryAgain);
                if(smoke!=null&&!tryAgain)SmokeFail(error);return;
            case "pong":return;
        }
        if(!welcomed)throw new InvalidDataException("尚未收到房间身份信息");
        switch(message.type) {
            case "lobby":
                if(message.phase!=lobby.phase) {
                    ResetInput();
                    if(message.phase=="paused"||message.phase=="reconnecting") {
                        if(player.Clock!=null){player.SettleLiveFrame();player.Presentation.Pause();}
                    }
                }
                lobby=message;lobbyReceivedAt=Time.realtimeSinceStartup;
                if(applying && draft.mode==lobby.mode&&draft.layout==lobby.layout&&draft.seed==lobby.seed
                    && draft.model0==lobby.model0&&draft.model1==lobby.model1&&draft.host_slot==lobby.host_slot
                    && draft.old_dynamics==lobby.old_dynamics&&draft.horizon==lobby.horizon&&draft.tick_rate==lobby.tick_rate){
                    applying=false;dirty=false;
                }
                if(Phase=="lobby"){latestTick=-1;result=null;}
                if(!dirty) {
                    draft.mode=lobby.mode;draft.layout=lobby.layout;draft.model0=lobby.model0;draft.model1=lobby.model1;
                    draft.horizon=lobby.horizon;draft.tick_rate=Mathf.RoundToInt(lobby.tick_rate);
                    draft.old_dynamics=lobby.old_dynamics;draft.seed=lobby.seed;draft.host_slot=lobby.host_slot;
                    seedText=draft.seed.ToString();
                }
                break;
            case "start":
                result=null;matchId=message.match_id;
                CheckHeader(message.header);CheckFrame(message.frame,message.header);
                var data=ReplayFile.Validate(new ReplayFile{header=message.header,frames=new[]{message.frame},segments=Array.Empty<Segment>()});
                player.BeginPresentation(data,"实时对局");latestTick=message.tick;
                if(resumedWelcome && resyncTick<0)resyncTick=latestTick;
                ResetInput();break;
            case "tick":
                if(message.match_id!=matchId)return;
                if(latestTick<0||player.Data==null)throw new InvalidDataException("缺少初始状态");
                if(message.tick<=latestTick)return;
                if(message.tick!=latestTick+1){Disconnect("状态序号不连续，重新同步",true);return;}
                CheckFrame(message.frame,player.Data.header);
                var previous=player.Data.frames[player.Data.frames.Length-1];
                ReplayFile.Validate(new ReplayFile{header=player.Data.header,frames=new[]{previous,message.frame},segments=new[]{message.segment}});
                player.LiveStep(message.frame,message.segment);latestTick=message.tick;
                ObserveSmokeTick(latestTick);break;
            case "end":
                if(message.match_id!=matchId)return;
                if(message.frame!=null && player.Data!=null && message.tick==latestTick)player.SettleLiveFrame();
                result=message;LiveResume.Forget(seatKey);break;
        }
    }
    static void CheckHeader(ReplayHeader header) {
        if(header==null||header.terrain==null||header.terrain.Length==0||header.terrain.Length>64||
           string.IsNullOrEmpty(header.terrain[0])||header.terrain[0].Length>64||header.players==null||header.players.Length!=2)
            throw new InvalidDataException("无效的厨房地图或角色");
    }
    static void CheckFrame(Frame frame,ReplayHeader header) {
        if(frame?.state?.objects==null)throw new InvalidDataException("缺少物品状态");
        foreach(var item in frame.state.objects) {
            if(item?.position==null||item.position.Length!=2||item.position[1]<0||item.position[1]>=header.terrain.Length||
               item.position[0]<0||item.position[0]>=header.terrain[0].Length)throw new InvalidDataException("物品坐标越界");
        }
    }
    static readonly KeyCode[] Arrows={KeyCode.UpArrow,KeyCode.DownArrow,KeyCode.RightArrow,KeyCode.LeftArrow};
    static readonly KeyCode[] Letters={KeyCode.W,KeyCode.S,KeyCode.D,KeyCode.A};
    static bool Held(int i) => Input.GetKey(Arrows[i]) || Input.GetKey(Letters[i]);
    void ReadInput() {
        if(!Application.isFocused||player.ModalOpen) {
            if(!focusBlocked){Neutralize();focusBlocked=true;}
            return;
        }
        if(focusBlocked) {
            for(int i=0;i<4;i++)if(Held(i))return;
            if(Input.GetKey(KeyCode.Space)||Input.GetKey(KeyCode.E))return;
            focusBlocked=false;
        }
        int heldMask=0;
        for(int i=0;i<4;i++){
            if(Input.GetKeyDown(Arrows[i])||Input.GetKeyDown(Letters[i]))directions.Press(i);
            if(Held(i))heldMask|=1<<i;
        }
        if(Input.GetKeyDown(KeyCode.Space)||Input.GetKeyDown(KeyCode.E))interactTick=latestTick+1;
        int move=directions.Movement(heldMask);
        int action=interactTick==latestTick+1?5:move;
        // Keep a short key tap for its intended tick even if the key has already been released.
        if(action==4 && lastSentTick==latestTick+1)return;
        Submit(action);
    }
    void Submit(int action) {
        int target=latestTick+1;
        if(lastSentTick==target && lastAction==action)return;
        Send(new LiveInput{type="input",match_id=matchId,action=action,tick=target,seq=++sequence});
        if(lastSentTick!=target)ObserveSmokeInput(target);
        lastSentTick=target;lastAction=action;
    }
}
}
