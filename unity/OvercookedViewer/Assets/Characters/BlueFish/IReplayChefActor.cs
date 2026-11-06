using UnityEngine;

namespace OvercookedReplay {
// Model-specific adapters share recorded action/time semantics and item attachment.
public interface IReplayChefActor {
    Transform HeldAnchor {get;}
    Transform RightHand {get;}
    void Sample(FishAction action,float seconds,float locomotion=1,float? expressionTime=null,float? celebrationPhase=null);
}
}
