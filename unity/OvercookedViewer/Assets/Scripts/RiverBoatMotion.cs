using System;
using UnityEngine;

namespace OvercookedReplay {
// Decorative time is independent of replay frames, seeks and playback speed.
public sealed class RiverBoatMotion : MonoBehaviour {
    Vector3 rest;
    float start,end,speed,phase;
    int direction;
    double elapsed;
    bool configured;
    public double Period=>(end-start)/speed;
    public void Configure(float from,float to,float unitsPerSecond,int travelDirection,float initialPhase) {
        if(to<=from||unitsPerSecond<=0)throw new ArgumentException("Boat route must have positive length and speed");
        var anchor=transform.localPosition;if(configured)anchor.y=rest.y;rest=anchor;configured=true;
        start=from;end=to;speed=unitsPerSecond;direction=travelDirection<0?-1:1;
        phase=initialPhase;elapsed=0;Evaluate(0);
    }
    public void Evaluate(double seconds) {
        double cycle=seconds/Period+phase;float progress=(float)(cycle-Math.Floor(cycle));
        var p=rest;p.z=Mathf.Lerp(start,end,direction>0?progress:1-progress);
        double carrier=seconds*1.7+phase*Math.PI*2;
        float wave=(float)(carrier%(Math.PI*2)),pitchWave=(float)((carrier*.81)%(Math.PI*2));
        p.y+=Mathf.Sin(wave)*.018f;
        transform.localPosition=p;
        transform.localRotation=Quaternion.Euler(Mathf.Sin(pitchWave)*.6f,direction>0?0:180,Mathf.Sin(wave)*1.1f);
    }
    void Update() {elapsed+=UnityEngine.Time.unscaledDeltaTime;Evaluate(elapsed);}
}
}
