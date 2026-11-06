using UnityEngine;

namespace OvercookedReplay {
public sealed class AmbientSway : MonoBehaviour {
    public Vector3 Pivot {get;private set;}
    Vector3 restPosition;Quaternion restRotation;
    float amplitude,frequency,phase;
    double elapsed;
    public void Configure(Vector3 pivot,float degrees,float cyclesPerSecond,float initialPhase) {
        Pivot=pivot;restPosition=transform.localPosition;restRotation=transform.localRotation;
        amplitude=degrees;frequency=cyclesPerSecond;phase=initialPhase;elapsed=0;Evaluate(0);
    }
    public void Evaluate(double seconds) {
        float wave=(float)((seconds*frequency+phase)%1)*Mathf.PI*2;
        var tilt=Quaternion.Euler(amplitude*.4f*Mathf.Sin(wave+1.7f),0,amplitude*Mathf.Sin(wave));
        transform.localPosition=Pivot+tilt*(restPosition-Pivot);transform.localRotation=tilt*restRotation;
    }
    void Update() {elapsed+=UnityEngine.Time.unscaledDeltaTime;Evaluate(elapsed);}
}
}
