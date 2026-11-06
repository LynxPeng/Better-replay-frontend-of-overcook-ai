using UnityEngine;

namespace OvercookedReplay {
public struct CameraInputFrame {
    public Vector2 Pointer,ScreenSize,Axes;
    public float Wheel;
    public bool GroundPressed,GroundHeld,PitchPressed,PitchHeld,ModalOpen,Focused,ShiftHeld;
}
// Presentation-only offsets from the fitted kitchen view. Reframing for a new
// resolution never compounds a previous camera transform or loses the offsets.
public sealed class CameraNavigation {
    public static readonly Rect SceneViewport=new Rect(.23f,.17f,.77f,.83f);
    public bool Locked {get;set;}=true;
    Vector3 pan;
    float dolly=1,pitchOffset,heightOffset;
    bool draggingGround,draggingPitch;Vector2 previousPointer;
    public void Reset() {pan=Vector3.zero;dolly=1;pitchOffset=heightOffset=0;CancelDrag(previousPointer);}
    public void CancelDrag(Vector2 pointer) {draggingGround=false;draggingPitch=false;previousPointer=pointer;}
    public bool Step(Camera camera,CameraInputFrame input,float seconds) {
        if(Locked||!input.Focused||!AllowsInput(input.Pointer,input.ScreenSize,input.ModalOpen)) {CancelDrag(input.Pointer);return false;}
        if(input.GroundPressed) {draggingGround=true;draggingPitch=false;}
        if(input.PitchPressed) {draggingPitch=true;draggingGround=false;}
        if(!input.GroundHeld)draggingGround=false;
        if(!input.PitchHeld)draggingPitch=false;
        bool changed=false;
        if(Mathf.Abs(input.Wheel)>.01f){if(input.ShiftHeld)Elevate(input.Wheel);else Scroll(input.Wheel);changed=true;}
        if(draggingGround&&!input.GroundPressed){Drag(camera,previousPointer,input.Pointer);changed=true;}
        if(draggingPitch&&!input.PitchPressed){Pitch((input.Pointer.y-previousPointer.y)*.18f);changed=true;}
        if(input.Axes.sqrMagnitude>0){Move(camera,input.Axes,seconds);changed=true;}
        previousPointer=input.Pointer;return changed;
    }
    public void Scroll(float ticks) {if(!Locked)dolly=Mathf.Clamp(dolly*Mathf.Exp(-ticks*.12f),.25f,4);}
    public void Elevate(float ticks) {if(!Locked)heightOffset=Mathf.Clamp(heightOffset+ticks*.6f,-30,30);}
    public void Pitch(float degrees) {if(!Locked)pitchOffset=Mathf.Clamp(pitchOffset+degrees,-65,65);}
    public void Move(Camera camera,Vector2 axes,float seconds) {
        if(Locked)return;
        var forward=Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up).normalized;
        var right=Vector3.ProjectOnPlane(camera.transform.right,Vector3.up).normalized;
        axes=Vector2.ClampMagnitude(axes,1);
        pan+=(right*axes.x+forward*axes.y)*Mathf.Max(3,camera.transform.position.y*.6f)*Mathf.Clamp(seconds,0,.1f);
    }
    public void Drag(Camera camera,Vector2 from,Vector2 to) {
        if(Locked)return;
        var plane=new Plane(Vector3.up,Vector3.zero);
        var first=camera.ScreenPointToRay(from);var second=camera.ScreenPointToRay(to);
        if(plane.Raycast(first,out float a)&&plane.Raycast(second,out float b))pan+=first.GetPoint(a)-second.GetPoint(b);
    }
    public void Apply(Camera camera,Vector3 fittedFocus) {
        var forward=camera.transform.forward;
        float pitch=Mathf.Clamp(Mathf.Asin(-forward.y)*Mathf.Rad2Deg+pitchOffset,15,80)*Mathf.Deg2Rad;
        var horizontal=Vector3.ProjectOnPlane(forward,Vector3.up).normalized;
        forward=horizontal*Mathf.Cos(pitch)-Vector3.up*Mathf.Sin(pitch);
        float distance=Mathf.Clamp(Vector3.Distance(camera.transform.position,fittedFocus)*dolly,4,300);
        var position=fittedFocus+pan-forward*distance+Vector3.up*heightOffset;
        position.y=Mathf.Max(1.4f,position.y);
        camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(forward,Vector3.up));
    }
    // Input.mousePosition uses a bottom-left origin; IMGUI uses a top-left
    // 1280-wide canvas with a variable height. Margins exclude bordering controls.
    public static bool AllowsInput(Vector2 point,Vector2 screen,bool modalOpen) {
        if(modalOpen||screen.x<=0||screen.y<=0)return false;
        var ui=ReplayUiLayout.CanvasPoint(point,screen);
        return ui.x>302&&ui.x<1264&&ui.y>78&&!ReplayUiLayout.NavigationHint.Contains(ui)&&ui.y<(1-SceneViewport.yMin)*ReplayUiLayout.Height(screen);
    }
}
}
