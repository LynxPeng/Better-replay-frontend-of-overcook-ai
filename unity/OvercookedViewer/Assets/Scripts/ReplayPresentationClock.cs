using UnityEngine;

namespace OvercookedReplay {
// The recorded clock stops at its terminal snapshot. This optional, pausable visual tail
// lets the final celebration finish without changing frames, rewards or recorded time.
public sealed class ReplayPresentationClock {
    readonly ReplayClock recorded;readonly float limit;bool tailPlaying;
    public float Tail {get;private set;}
    public float Time => recorded.Time+Tail;
    public bool Playing => recorded.Playing||tailPlaying;
    public ReplayPresentationClock(ReplayClock recordedClock,float tailDuration) {recorded=recordedClock;limit=Mathf.Max(0,tailDuration);}
    public void Advance(float seconds) {
        float delta=Mathf.Max(0,seconds)*recorded.Speed,remaining=recorded.Duration-recorded.Time;
        bool advancing=recorded.Playing;recorded.Advance(seconds);
        if(advancing&&recorded.Time>=recorded.Duration&&Tail<limit) {
            tailPlaying=true;Tail=Mathf.Min(limit,Tail+Mathf.Max(0,delta-remaining));
        } else if(tailPlaying)Tail=Mathf.Min(limit,Tail+delta);
        if(Tail>=limit)tailPlaying=false;
    }
    public void Pause() {recorded.Playing=false;tailPlaying=false;}
    public void Toggle() {
        if(Playing){Pause();return;}
        if(recorded.Time>=recorded.Duration){tailPlaying=Tail<limit;return;}
        recorded.Playing=true;
    }
    public void ResetAfterSeek(bool resume) {
        Tail=0;tailPlaying=resume&&recorded.Time>=recorded.Duration&&limit>0;
        recorded.Playing=resume&&recorded.Time<recorded.Duration;
    }
}
}
