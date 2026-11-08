using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace OvercookedReplay {
public sealed class BlueFishShowcase : MonoBehaviour {
    public BlueFishActor actor;
    public MonoBehaviour AlternateActor;
    public string DisplayName="蓝色大肥鱼",Credit="模型：木子恶魔\n现有骨架 + 原创动作 · 本地试用";
    IReplayChefActor ActiveActor => AlternateActor? (IReplayChefActor)AlternateActor:actor;
    public Transform counter;
    FishAction action=FishAction.Idle;float time,orbit=16;bool playing=true,cycle=true;
    Transform prop,tableProp,pot,spoon;string captureDirectory;int captureIndex;float captureTime;
    GUIStyle heading,label,button,toggle;Font font;
    static readonly string[] Names={"待机 · oh 与眨眼","走路 · 重心摆动","端盘行走","拿起食物","放下食物","开心交付 · 跳一下","搅拌烹饪","回头张望"};
    void Start() {
        font=ViewerFont.Create();
        if(!actor&&!AlternateActor)actor=FindFirstObjectByType<BlueFishActor>();
        tableProp=KitchenFactory.Item("dish",null,new Vector3(0,.37f,.65f),.26f).transform;
        pot=new GameObject("Demo cooking pot").transform;KitchenFactory.RestaurantProp("pot_A",pot,.32f,new Vector3(0,.36f,.65f));
        spoon=KitchenFactory.Part("Stirring spoon",null,PrimitiveType.Cylinder,Vector3.zero,new Vector3(.014f,.09f,.014f),KitchenFactory.Material("#7d5338")).transform;
        var args=Environment.GetCommandLineArgs();
        for(int i=0;i<args.Length-1;i++)if(args[i]=="--capture-actions")captureDirectory=args[i+1];
        if(captureDirectory!=null){Directory.CreateDirectory(captureDirectory);playing=false;cycle=false;}
        Select(FishAction.Idle);SetCamera();
    }
    void Select(FishAction next) {
        action=next;time=0;
        if(prop)Destroy(prop.gameObject);
        prop=KitchenFactory.Item("dish",ActiveActor.HeldAnchor,Vector3.zero,.28f).transform;
        prop.localPosition=Vector3.zero;prop.localRotation=Quaternion.identity;
        UpdatePose();
    }
    void SetCamera() {
        var camera=Camera.main;float angle=orbit*Mathf.Deg2Rad;
        camera.transform.position=new Vector3(Mathf.Sin(angle)*3.7f,1.6f,Mathf.Cos(angle)*3.7f);
        camera.transform.LookAt(new Vector3(0,.7f,0));
    }
    void Update() {
        if(captureDirectory!=null) {
            if(Time.frameCount<50)return;
            if(captureIndex>=64){Application.Quit();return;}
            var next=(FishAction)(captureIndex/8);if(action!=next)Select(next);
            captureTime=BlueFishMotion.Duration(next)*(captureIndex%8)/8f;
            if(captureIndex==3)captureTime=2.6f;
            time=captureTime;UpdatePose();
            if(captureIndex==0)ScreenCapture.CaptureScreenshot(Path.Combine(captureDirectory,"showcase-ui.png"));
            Capture(Path.Combine(captureDirectory,$"{captureIndex:D3}-{next}.png"));captureIndex++;return;
        }
        if(playing) {
            time+=Time.unscaledDeltaTime;
            if(cycle&&time>Mathf.Max(3,BlueFishMotion.Duration(action)*2))Select((FishAction)(((int)action+1)%8));
        }
        if(Input.GetKeyDown(KeyCode.Space))playing=!playing;
        if(Input.GetMouseButton(1)) {orbit+=Input.GetAxis("Mouse X")*3;SetCamera();}
        Camera.main.fieldOfView=Mathf.Clamp(Camera.main.fieldOfView-Input.mouseScrollDelta.y*2,22,55);
        UpdatePose();
    }
    void UpdatePose() {
        float t=BlueFishMotion.Loops(action)?time:Mathf.Min(time,BlueFishMotion.Duration(action));
        ActiveActor.Sample(action,t,1,time);
        bool work=action==FishAction.Pickup||action==FishAction.Place||action==FishAction.Deliver||action==FishAction.Cook;
        if(counter)counter.gameObject.SetActive(work);
        bool holding=action==FishAction.Carry||
            (action==FishAction.Pickup&&t>BlueFishMotion.Duration(action)*.5f)||
            ((action==FishAction.Place||action==FishAction.Deliver)&&t<BlueFishMotion.Duration(action)*.5f);
        if(prop)prop.gameObject.SetActive(holding);
        if(tableProp)tableProp.gameObject.SetActive(work&&action!=FishAction.Cook&&!holding);
        if(pot)pot.gameObject.SetActive(action==FishAction.Cook);
        if(spoon) {
            spoon.gameObject.SetActive(action==FishAction.Cook);
            spoon.position=ActiveActor.RightHand.position+Vector3.down*.045f+((MonoBehaviour)ActiveActor).transform.forward*.05f;
            spoon.rotation=Quaternion.Euler(Mathf.Sin(t*2*Mathf.PI/BlueFishMotion.Duration(action))*12,0,10);
        }
    }
    void OnGUI() {
        if(heading==null) {
            heading=new GUIStyle(GUI.skin.label){fontSize=22,font=font};heading.normal.textColor=new Color(.08f,.22f,.28f);
            label=new GUIStyle(heading){fontSize=16,wordWrap=true};
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=17,fixedHeight=38};
            button.normal.textColor=button.hover.textColor=button.active.textColor=heading.normal.textColor;
            button.normal.background=Swatch(new Color(.76f,.88f,.85f));button.hover.background=Swatch(new Color(.65f,.82f,.78f));button.active.background=Swatch(new Color(.52f,.74f,.70f));
            GUI.skin.box.normal.background=Swatch(new Color(.97f,.95f,.88f));
            toggle=new GUIStyle(GUI.skin.toggle){font=font,fontSize=16};toggle.normal.textColor=toggle.onNormal.textColor=heading.normal.textColor;
        }
        GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1280f,Screen.height/720f,1));
        GUI.Box(new Rect(24,24,325,672),GUIContent.none);
        GUILayout.BeginArea(new Rect(42,36,289,640));
        GUILayout.Label(DisplayName+" · 动作试演",heading);GUILayout.Space(7);
        GUILayout.Label(Credit,label);GUILayout.Space(15);
        for(int i=0;i<8;i++)if(GUILayout.Button(((int)action==i?"● ":"")+(AlternateActor&&i==0?"待机 · 呼吸与眨眼":Names[i]),button)) {Select((FishAction)i);cycle=false;}
        GUILayout.Space(13);GUILayout.BeginHorizontal();
        if(GUILayout.Button(playing?"暂停":"播放",button))playing=!playing;
        if(GUILayout.Button("重播",button))time=0;GUILayout.EndHorizontal();
        cycle=GUILayout.Toggle(cycle,"自动轮播",toggle);
        float duration=BlueFishMotion.Duration(action),value=BlueFishMotion.Loops(action)?Mathf.Repeat(time,duration):Mathf.Min(time,duration);
        float changed=GUILayout.HorizontalSlider(value,0,duration);
        if(Mathf.Abs(changed-value)>.005f){time=changed;playing=false;UpdatePose();}
        GUILayout.Label($"{value:F2} / {duration:F2} 秒\n右键拖动：环绕 · 滚轮：缩放\n空格：暂停 / 播放",label);
        GUILayout.EndArea();
    }
    static Texture2D Swatch(Color color) {var t=new Texture2D(1,1);t.SetPixel(0,0,color);t.Apply();return t;}
    void Capture(string path) {
        var camera=Camera.main;var target=new RenderTexture(960,960,24){antiAliasing=4};
        camera.aspect=1;
        RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest {destination=target});
        var previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(960,960,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,960,960),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        RenderTexture.active=previous;target.Release();Destroy(target);Destroy(image);camera.ResetAspect();
    }
}
}
