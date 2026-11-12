using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OvercookedReplay {
public partial class ReplayPlayer : MonoBehaviour {
    public ReplayFile Data {get;private set;} public ReplayClock Clock {get;private set;}
    public ReplayPresentationClock Presentation {get;private set;}
    Transform world,objects; Transform[] chefs; string[] heldNames;
    IReplayChefActor[] characterActors;
    static readonly Color[] ChefColors={new Color(.22f,.52f,.7f),new Color(.86f,.43f,.25f),new Color(.22f,.65f,.44f)};
    readonly Dictionary<Vector2Int,Renderer> handoffStations=new Dictionary<Vector2Int,Renderer>();
    readonly Dictionary<Vector2Int,PotSimmer> stoves=new Dictionary<Vector2Int,PotSimmer>();
    string[] samples; string error=""; int applied=-1; float yaw=0;
    ViewerPreferences preferences=new ViewerPreferences();readonly ViewerSettingsPanel settings=new ViewerSettingsPanel();
    KitchenView view=KitchenView.LongEdge;int screenWidth,screenHeight;
    readonly CameraNavigation navigation=new CameraNavigation();LevelSky sky;
    bool labels=true; string capture; int captureFrame=-1; bool captureQuit,captureUi;
    float captureAfter;
    public bool UseBlueFish=true;
    public bool UseGuguGaga=true;
    AudioSource sound; AudioClip delivery;
    InteractionHints interactionHints;
    LiveClient live;
    internal bool ModalOpen => settings.Open;
    internal bool IsLive => live && !live.Reviewing;
    SyncHandoff[] LastHandoffs => Clock.Index>0 ? Data.segments[Clock.Index-1].sync_handoffs : null;
    public static Vector3 World(int[] p) => new Vector3(p[0],0,-p[1]);
    public static string CharacterResource(int agent,bool enabled) {
        if(agent<0||agent>=ChefColors.Length)throw new ArgumentOutOfRangeException(nameof(agent));
        if(!enabled)return null;
        return agent switch {0=>"LocalCharacters/BlueFatFish",1=>"LocalCharacters/GuguGaga",_=>null};
    }
    void Start() {
        live=GetComponent<LiveClient>();
        preferences=ViewerPreferences.Load(ViewerPreferences.DefaultPath);view=(KitchenView)preferences.viewMode;
        ApplyPreferences(preferences,false);
        var skyPrefab=Resources.Load<GameObject>("Prefabs/ReplaySky");
        if(skyPrefab){sky=Instantiate(skyPrefab).GetComponent<LevelSky>();if(sky)sky.Follow(Camera.main.transform);}
        sound=gameObject.AddComponent<AudioSource>();sound.playOnAwake=false;
        delivery=AudioClip.Create("Delivery chime (original)",11025,1,44100,false);
        var wave=new float[11025];for(int i=0;i<wave.Length;i++) {
            float t=i/44100f;wave[i]=.12f*Mathf.Exp(-t*15)*(Mathf.Sin(t*440*2*Mathf.PI)+Mathf.Sin(t*660*2*Mathf.PI)+Mathf.Sin(t*880*2*Mathf.PI));
        }delivery.SetData(wave,0);
        samples=Directory.GetFiles(Application.streamingAssetsPath+"/Samples","*.ocrep.json");Array.Sort(samples);
        var args=Environment.GetCommandLineArgs();string file=samples.Length>0?samples[0]:null;
        for(int i=0;i<args.Length-1;i++) {
            if(args[i]=="--replay")file=args[i+1]; if(args[i]=="--capture")capture=args[i+1];
            if(args[i]=="--frame")int.TryParse(args[i+1],out captureFrame);
            if(args[i]=="--view")view=args[i+1]=="iso"?KitchenView.Isometric:KitchenView.LongEdge;
        }
        captureQuit=Array.IndexOf(args,"--quit-after-capture")>=0;
        captureUi=Array.IndexOf(args,"--capture-ui")>=0;
        captureAfter=Time.realtimeSinceStartup+1.5f;
        if(captureUi&&capture!=null)for(int i=0;i<args.Length-1;i++)if(args[i]=="--capture-size") {
            var size=args[i+1].Split('x');if(size.Length==2&&int.TryParse(size[0],out int cw)&&int.TryParse(size[1],out int ch)&&cw>=1024&&ch>=720)Screen.SetResolution(cw,ch,FullScreenMode.Windowed);
        }
        if(Array.IndexOf(args,"--night")>=0)preferences.night=true;
        if(Array.IndexOf(args,"--day")>=0)preferences.night=false;
        SetNight(preferences.night,false);
        if(file!=null&&!IsLive)Load(file);
        if(captureFrame>=0 && Clock!=null)Seek(captureFrame);
        if(Array.IndexOf(args,"--show-settings")>=0)settings.Show(preferences);
    }
    public void Load(string path) {
        try {
            BeginPresentation(ReplayFile.Load(path),Path.GetFileName(path));
        }catch(Exception ex){error=ex.Message;Debug.LogWarning(error);}
    }
    internal void BeginPresentation(ReplayFile next,string name) {
            Data=next;Clock=new ReplayClock(next);Clock.Playing=false;applied=-1;error="";loadedName=name;
            Presentation=new ReplayPresentationClock(Clock,0);
            if(world)Destroy(world.gameObject);
            world=new GameObject("Kitchen").transform;
            interactionHints=world.gameObject.AddComponent<InteractionHints>();
            objects=new GameObject("Objects").transform;objects.SetParent(world,false);
            stoves.Clear();
            handoffStations.Clear();
            int w=Data.header.terrain[0].Length,h=Data.header.terrain.Length;
            int playerCount=Data.header.players.Length;
            chefs=new Transform[playerCount];heldNames=new string[playerCount];characterActors=new IReplayChefActor[playerCount];
            interactionHints.PlayerCount=playerCount;
            KitchenFactory.Part("IslandBase",world,PrimitiveType.Cube,new Vector3((w-1)*.5f,-.24f,-(h-1)*.5f),new Vector3(w+.65f,.4f,h+.65f),KitchenFactory.Material("#8f6946"));
            RestaurantBackdrop.Build(world,w,h);
            for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
                var p=new Vector3(x,0,-y);char tile=Data.header.terrain[y][x];
                if(tile!=' ') {var prefab=Resources.Load<GameObject>("Prefabs/Station_"+tile);
                    var g=prefab?Instantiate(prefab):KitchenFactory.Station(tile);g.transform.SetParent(world,false);g.transform.localPosition=p;
                    var cabinet=g.transform.Find("kitchencounter_straight_B");
                    if(cabinet)foreach(var direction in new[]{new Vector2Int(0,1),new Vector2Int(1,0),new Vector2Int(0,-1),new Vector2Int(-1,0)}) {
                        int nx=x+direction.x,ny=y+direction.y;
                        if(nx>=0&&nx<w&&ny>=0&&ny<h&&Data.header.terrain[ny][nx]==' ') {
                            cabinet.localRotation=Quaternion.LookRotation(new Vector3(direction.x,0,-direction.y));break;
                        }
                    }
                    var rounded=g.GetComponentInChildren<RoundedWorktop>();if(rounded)rounded.Apply();
                    var cell=new Vector2Int(x,y);
                    if(tile=='H') {
                        bool northSouth=y>0 && y+1<h && Data.header.terrain[y-1][x]==' ' && Data.header.terrain[y+1][x]==' ';
                        handoffStations[cell]=KitchenFactory.HandoffMarker(g.transform,northSouth);
                    } else interactionHints.Register(cell,g);
                    if(tile=='P')stoves.Add(cell,new PotSimmer(g.transform,x,y));
                }
            }
            for(int i=0;i<playerCount;i++) {
                string resource=CharacterResource(i,i==0?UseBlueFish:UseGuguGaga);
                var prefab=resource!=null?Resources.Load<GameObject>(resource):null;
                if(!prefab)prefab=Resources.Load<GameObject>("Prefabs/Chef"+i);
                var g=prefab?Instantiate(prefab):KitchenFactory.Chef(ChefColors[i]);
                chefs[i]=g.transform;chefs[i].SetParent(world,false);heldNames[i]=null;
                characterActors[i]=g.GetComponent<IReplayChefActor>();
            }
            float tail=0;
            for(int i=0;i<playerCount;i++)if(characterActors[i]!=null) {
                float finalPhase=BlueFishReplayPose.CelebrationPhase(Data,Data.frames.Length-1,i,Clock.Duration);
                if(finalPhase>=0)tail=Mathf.Max(tail,(1-finalPhase)*BlueFishMotion.CelebrationDuration);
            }
            Presentation=new ReplayPresentationClock(Clock,tail);
            ResetCamera();Apply();SetNight(preferences.night,false);
    }
    internal void LiveStep(Frame frame,Segment segment) {
        // A bounded rolling window retains earlier delivery events for the full animation.
        var old=Data;int keep=Mathf.Min(old.frames.Length,12);
        var frames=new Frame[keep+1];var segments=new Segment[keep];
        Array.Copy(old.frames,old.frames.Length-keep,frames,0,keep);
        if(keep>1)Array.Copy(old.segments,old.segments.Length-keep+1,segments,0,keep-1);
        frames[keep]=frame;segments[keep-1]=segment;
        old.frames=frames;old.segments=segments;
        Clock.SeekFrame(keep-1);
        Presentation=new ReplayPresentationClock(Clock,BlueFishMotion.CelebrationDuration);
        Clock.Playing=true;applied=-1;Apply();
        if(sound && segment.events!=null)foreach(var e in segment.events)
            if(e.kind=="soup_delivery")sound.PlayOneShot(delivery);
    }
    internal void SettleLiveFrame() {
        if(Clock==null)return;
        Clock.SeekFrame(Data.frames.Length-1);applied=-1;Apply();
    }
    void FrameCamera() {
        if(Data==null)return; var camera=Camera.main;
        camera.rect=CameraNavigation.SceneViewport;
        ApplyCamera(camera);
        screenWidth=Screen.width;screenHeight=Screen.height;
    }
    void ApplyCamera(Camera camera) {
        var focus=KitchenCamera.Apply(camera,Data.header.terrain[0].Length,Data.header.terrain.Length,view,.9f,yaw,preferences.cameraPitch);
        navigation.Apply(camera,focus);if(sky)sky.UpdatePose();
    }
    void ResetCamera() {navigation.Reset();yaw=0;CancelCameraDrag();FrameCamera();}
    void CancelCameraDrag() {navigation.CancelDrag(Input.mousePosition);}
    void OnApplicationFocus(bool focused) {CancelCameraDrag();}
    void NavigateCamera() {
        var input=new CameraInputFrame {
            Pointer=Input.mousePosition,ScreenSize=new Vector2(Screen.width,Screen.height),
            Focused=Application.isFocused,ModalOpen=settings.Open,Wheel=Input.mouseScrollDelta.y,
            ShiftHeld=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift),
            GroundPressed=Input.GetMouseButtonDown(2),GroundHeld=Input.GetMouseButton(2),
            PitchPressed=Input.GetMouseButtonDown(1),PitchHeld=Input.GetMouseButton(1),
            Axes=IsLive && !live.Spectating?Vector2.zero:new Vector2((Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),(Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0))
        };
        if(navigation.Step(Camera.main,input,UnityEngine.Time.unscaledDeltaTime))FrameCamera();
    }
    void ApplyPreferences(ViewerPreferences next,bool save) {
        preferences=next.Copy();preferences.viewMode=(int)view;preferences.Sanitize();ViewerGraphics.Apply(preferences);
        if(!Application.isBatchMode)Screen.SetResolution(preferences.width,preferences.height,preferences.borderless?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed);
        if(save)preferences.Save(ViewerPreferences.DefaultPath);SceneAtmosphere.Apply(preferences.night);FrameCamera();
        Debug.Log($"Viewer graphics: {preferences.width}x{preferences.height}, MSAA {preferences.msaa}, render scale {preferences.renderScale}, FPS {preferences.frameRate}");
    }
    public void SetNight(bool night,bool save=true) {
        preferences.night=night;SceneAtmosphere.Apply(night);
        if(save)preferences.Save(ViewerPreferences.DefaultPath);
    }
    public void Seek(int index) {if(Clock==null)return;if(sound)sound.Stop();bool resume=Presentation.Playing;Clock.SeekFrame(index);Presentation.ResetAfterSeek(resume);applied=-1;Apply();}
    void Update() {
        if(Clock==null){if(captureUi)CaptureRequestedFrame();return;}
        int old=Clock.Index;Presentation.Advance(UnityEngine.Time.unscaledDeltaTime);Apply();
        if(screenWidth!=Screen.width||screenHeight!=Screen.height)FrameCamera();
        if(!IsLive && Clock.Index>old && sound)for(int k=old;k<Clock.Index;k++) {
            var events=Data.segments[k].events;if(events!=null)foreach(var e in events)if(e.kind=="soup_delivery")sound.PlayOneShot(delivery);
        }
        if(!IsLive && !settings.Open) {
            if(Input.GetKeyDown(KeyCode.Space))Presentation.Toggle();
            if(Input.GetKeyDown(KeyCode.RightArrow))Seek(Clock.Index+1);
            if(Input.GetKeyDown(KeyCode.LeftArrow))Seek(Clock.Index-1);
        }
        NavigateCamera();
        CaptureRequestedFrame();
    }
    void CaptureRequestedFrame() {
        if(capture!=null && IsLive && captureFrame>=0 && (!live.ShowKitchen || live.ConfirmedTick<captureFrame))return;
        if(capture!=null && UnityEngine.Time.frameCount>40 && Time.realtimeSinceStartup>=captureAfter
            && (!captureUi || SplashScreen.isFinished)) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(capture)));
            if(captureUi){var destination=capture;capture=null;StartCoroutine(CaptureInterface(destination));return;}
            // Explicit render request works in a windowless player as well.
            int width=preferences.width,height=preferences.height;var camera=Camera.main;var oldRect=camera.rect;
            camera.rect=new Rect(0,0,1,1);camera.aspect=(float)width/height;
            ApplyCamera(camera);
            var target=new RenderTexture(width,height,24);target.antiAliasing=preferences.msaa;
            var request=new RenderPipeline.StandardRequest {destination=target};
            RenderPipeline.SubmitRenderRequest(Camera.main,request);
            var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            File.WriteAllBytes(capture,image.EncodeToPNG());RenderTexture.active=previous;
            target.Release();Destroy(target);Destroy(image);capture=null;
            camera.rect=oldRect;camera.ResetAspect();FrameCamera();
        } else if(captureQuit && capture==null && UnityEngine.Time.frameCount>55) Application.Quit();
    }
    void Apply() {
        if(Data==null)return;int index=Clock.Index;var state=Data.frames[index].state;
        if(applied!=index) {
            foreach(Transform child in objects)Destroy(child.gameObject);
            foreach(var stove in stoves) {
                Item soup=null;
                foreach(var candidate in state.objects)
                    if(candidate.name=="soup" && candidate.position[0]==stove.Key.x && candidate.position[1]==stove.Key.y){soup=candidate;break;}
                stove.Value.SetSoup(soup);
            }
            foreach(var marker in handoffStations.Values)marker.sharedMaterial=KitchenFactory.Material("#306F73");
            var handoffs=LastHandoffs;
            if(handoffs!=null)foreach(var handoff in handoffs) {
                var cell=new Vector2Int(handoff.station[0],handoff.station[1]);
                handoffStations[cell].sharedMaterial=KitchenFactory.Material(handoff.success?"#19824B":"#B15B19");
            }
            foreach(var o in state.objects) {
                Vector3 p=World(o.position)+Vector3.up*KitchenFactory.CounterTopHeight;
                if(o.name=="soup" && Data.header.terrain[o.position[1]][o.position[0]]=='P') {
                    if(o._ingredients!=null)for(int j=0;j<o._ingredients.Length;j++)
                        KitchenFactory.Item(o._ingredients[j].name,objects,p+new Vector3((j-1)*.17f,.13f,0),.17f);
                    float ratio=o.is_idle?0:Mathf.Clamp01(o.cooking_tick/Mathf.Max(1,o.cook_time));
                    KitchenFactory.Part("CookingProgress",objects,PrimitiveType.Cube,p+new Vector3((ratio-1)*.28f,.35f,.38f),new Vector3(Mathf.Max(.01f,ratio*.56f),.045f,.055f),KitchenFactory.Material(o.is_ready?"#78c994":"#eebd64"));
                } else KitchenFactory.Item(o.name,objects,p);
            }
            applied=index;
        }
        for(int i=0;i<chefs.Length;i++) {
            var p=state.players[i];Vector3 pos=World(p.position);float angle=Mathf.Atan2(p.orientation[0],-p.orientation[1])*Mathf.Rad2Deg;
            bool moving=false;float turnProgress=Clock.Alpha;
            if(index<Data.segments.Length && Data.segments[index].kind=="step") {
                var q=Data.frames[index+1].state.players[i];var dest=World(q.position);moving=pos!=dest;
                pos=Vector3.Lerp(pos,dest,Clock.Alpha);
                float duration=Data.frames[index+1].time-Data.frames[index].time;
                turnProgress=duration>0?Mathf.Clamp01(Clock.Alpha*duration/Mathf.Min(.11f,duration)):0;
                angle=Mathf.LerpAngle(angle,Mathf.Atan2(q.orientation[0],-q.orientation[1])*Mathf.Rad2Deg,Mathf.SmoothStep(0,1,turnProgress));
            }
            var chef=chefs[i];chef.localPosition=pos;chef.localRotation=Quaternion.Euler(0,angle,0);
            bool interacting=false;
            if(index<Data.segments.Length&&Data.segments[index].kind=="step"&&Data.segments[index].actions[i]==5&&Data.segments[index].events!=null)
                foreach(var e in Data.segments[index].events)if(e.agent_index==i)interacting=true;
            string held=p.held_object?.name??"";
            var character=characterActors[i];
            if(character!=null) {
                var action=BlueFishReplayPose.Select(Data,index,i);
                float visualTime=Presentation.Time;
                float celebration=BlueFishReplayPose.CelebrationPhase(Data,index,i,visualTime);
                float actionTime=BlueFishMotion.Loops(action)?visualTime:(action==FishAction.Turn?turnProgress:Clock.Alpha)*BlueFishMotion.Duration(action);
                character.Sample(action,actionTime,moving?1:0,visualTime,celebration);
            } else {
                float walk=moving?Mathf.Sin(Clock.Time*22)*22:0;
                chef.Find("LeftLeg").localRotation=Quaternion.Euler(walk,0,0);chef.Find("RightLeg").localRotation=Quaternion.Euler(-walk,0,0);
                float reach=interacting?Mathf.Sin(Clock.Alpha*Mathf.PI)*-65:0;
                chef.Find("LeftArm").localRotation=Quaternion.Euler(reach,0,0);chef.Find("RightArm").localRotation=Quaternion.Euler(reach,0,0);
            }
            if(heldNames[i]!=held) {
                var anchor=character!=null?character.HeldAnchor:chef.Find("HeldAnchor");foreach(Transform child in anchor)Destroy(child.gameObject);
                if(held!="")KitchenFactory.Item(held,anchor,Vector3.zero,character!=null ? .30f : .4f);heldNames[i]=held;
            }
        }
        if(stoves.Count>0) {
            var view=Camera.main.transform.rotation;
            foreach(var stove in stoves.Values)stove.Sample(Clock.Time,view);
        }
        if(interactionHints)interactionHints.Refresh(Data.header,state);
    }
}
}
