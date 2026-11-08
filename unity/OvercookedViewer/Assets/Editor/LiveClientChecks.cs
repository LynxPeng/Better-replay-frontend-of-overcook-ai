using System;
using System.IO;
using System.Reflection;
using System.Text;
using OvercookedReplay;
using UnityEngine;

public static class LiveClientChecks {
    sealed class FragmentedStream : MemoryStream {
        public FragmentedStream(byte[] bytes):base(bytes){}
        public override int Read(byte[] buffer,int offset,int count)=>base.Read(buffer,offset,Math.Min(count,2));
    }
    static void Expect<T>(byte[] bytes) where T:Exception {
        try{using(var stream=new FragmentedStream(bytes))LiveWire.Receive(stream);}
        catch(T){return;}
        throw new Exception("Invalid wire frame was accepted: "+typeof(T).Name);
    }
    public static void Run() {
        if(!LiveClient.TryEndpoint(" 192.168.1.45:9001 ","8765",out var server,out var port)||server!="192.168.1.45"||port!=9001
            ||!LiveClient.TryEndpoint("kitchen-laptop","8765",out server,out port)||server!="kitchen-laptop"||port!=8765
            ||LiveClient.TryEndpoint("192.168.1.45:70000","8765",out server,out port))
            throw new Exception("Invalid join endpoint or port precedence");
        var directions=new LiveDirectionKeys();
        directions.Press(2);directions.Press(3);directions.Press(0);
        if(directions.Movement(13)!=0||directions.Movement(12)!=3||directions.Movement(4)!=2)
            throw new Exception("Releasing the latest key must restore the last still-held direction");
        directions.Clear();
        if(directions.Movement(4)!=4)throw new Exception("Reset must discard held directions");
        const string first="{\"type\":\"hello\",\"name\":\"企鹅🐧\"}";
        const string second="{\"type\":\"pong\"}";
        using(var encoded=new MemoryStream()) {
            LiveWire.Send(encoded,first);LiveWire.Send(encoded,second);
            var bytes=encoded.ToArray();
            if(bytes[0]!=0||bytes[1]!=0||bytes[2]!=0||bytes[3]!=Encoding.UTF8.GetByteCount(first))throw new Exception("Wire header must be the UTF-8 byte length in big-endian order");
            using(var input=new FragmentedStream(bytes)) {
                if(LiveWire.Receive(input)!=first||LiveWire.Receive(input)!=second||input.ReadByte()!=-1)
                    throw new Exception("Fragmented or coalesced packets lost");
            }
        }
        Expect<EndOfStreamException>(new byte[]{0,0,0,4,65});
        Expect<InvalidDataException>(new byte[]{0,16,0,1});
        Expect<InvalidDataException>(new byte[4]);
        Expect<DecoderFallbackException>(new byte[]{0,0,0,1,0xFF});
        // Parse a real snapshot + transition with the same wire DTO used by the network client.
        var replay=ReplayFile.Load(Path.Combine(Application.streamingAssetsPath,"Samples","cramped_room.ocrep.json"));
        int last=replay.frames.Length-1;
        string json="{\"type\":\"tick\",\"tick\":"+last+",\"frame\":"+JsonUtility.ToJson(replay.frames[last])+",\"segment\":"+JsonUtility.ToJson(replay.segments[last-1])+"}";
        Type dto=typeof(LiveClient).Assembly.GetType("OvercookedReplay.LiveMessage",true);
        object message=JsonUtility.FromJson(json,dto);
        var frame=(Frame)dto.GetField("frame").GetValue(message);
        var segment=(Segment)dto.GetField("segment").GetValue(message);
        if(frame.score!=20||frame.state.players.Length!=2||segment.actions.Length!=2||Array.Find(segment.events,e=>e.kind=="soup_delivery")==null)
            throw new Exception("Wire DTO lost terminal delivery data");
        Debug.Log("Live client checks passed: fragmented/coalesced UTF-8 framing, corrupt input rejection, terminal snapshot, actions and delivery event.");
    }
}
