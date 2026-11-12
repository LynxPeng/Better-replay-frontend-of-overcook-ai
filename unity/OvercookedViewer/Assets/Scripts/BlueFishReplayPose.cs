namespace OvercookedReplay {
// Interpret only confirmed state changes or successful events for the acting chef.
public static class BlueFishReplayPose {
    public static float CelebrationPhase(ReplayFile data,int index,int agent,float time) {
        for(int k=System.Math.Min(index,data.segments.Length-1);k>=0;k--) {
            float age=time-data.frames[k].time;
            if(age<0)continue;
            if(age+.0001f>=BlueFishMotion.CelebrationDuration)break;
            var step=data.segments[k];if(step.kind=="gap")break;
            if(step.actions==null||step.actions[agent]!=5||step.events==null)continue;
            foreach(var e in step.events)if(e.kind=="soup_delivery"&&e.agent_index==agent) {
                return age/BlueFishMotion.CelebrationDuration;
            }
        }
        return -1;
    }
    public static FishAction Select(ReplayFile data,int index,int agent) {
        var before=data.frames[index].state.players[agent];
        bool holding=!string.IsNullOrEmpty(before.held_object?.name);
        FishAction baseline=holding?FishAction.Carry:FishAction.Idle;
        if(index>=data.segments.Length||data.segments[index].kind!="step")return baseline;
        var step=data.segments[index];var after=data.frames[index+1].state.players[agent];
        bool moving=before.position[0]!=after.position[0]||before.position[1]!=after.position[1];
        if(moving)return holding?FishAction.Carry:FishAction.Walk;
        if(step.actions[agent]==5) {
            bool cooking=false,delivery=false,success=false;
            if(step.events!=null)foreach(var e in step.events) {
                bool matches=e.agent_index==agent;
                // Automatic pot events have no player id. Match the interacted tile explicitly.
                if(e.agent_index<0&&e.kind=="start_cooking"&&e.position!=null&&e.position.Length>=2)
                {
                    matches=Faces(before,e.position);
                    for(int other=0;other<data.frames[index].state.players.Length;other++)
                        if(other!=agent&&step.actions[other]==5&&Faces(data.frames[index].state.players[other],e.position))matches=false;
                }
                if(!matches||e.kind.StartsWith("sync_handoff_",System.StringComparison.Ordinal))continue;
                success=true;
                cooking|=e.kind=="start_cooking";delivery|=e.kind=="soup_delivery";
            }
            string previous=before.held_object?.name,next=after.held_object?.name;
            if(string.IsNullOrEmpty(previous))previous=null;if(string.IsNullOrEmpty(next))next=null;
            if(previous!=next) {
                if(next!=null)return FishAction.Pickup; // includes dish -> plated soup
                if(previous!=null)return delivery?FishAction.Deliver:FishAction.Place;
            }
            if(cooking||success)return FishAction.Cook;
        }
        if(!holding&&(before.orientation[0]!=after.orientation[0]||before.orientation[1]!=after.orientation[1]))return FishAction.Turn;
        return baseline;
    }
    static bool Faces(ChefState chef,int[] tile) => tile[0]==chef.position[0]+chef.orientation[0]&&tile[1]==chef.position[1]+chef.orientation[1];
}
}
