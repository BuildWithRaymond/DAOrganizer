using Arbiter.Net;
using Arbiter.Net.Client;
using Arbiter.Net.Server;
using DAOrganizer.Core;

namespace DAOrganizer.Game;

public sealed record TradePacketTrace(DateTimeOffset ObservedAt,string Direction,byte Opcode,string PayloadHex,string? SessionName);
public sealed record ManualTradeResult(string OperationId,string Character,int ProcessId,DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,IReadOnlyList<Item> BeforeInventory,IReadOnlyList<Item> AfterInventory,
    long BeforeGold,long AfterGold,IReadOnlyList<TradePacketTrace> Packets,bool Truncated);

public sealed class ManualTradeTrace
{
    private readonly Lock _gate=new();
    private readonly int _maxPackets;
    private readonly List<TradePacketTrace> _packets=[];
    private readonly Item[] _before;
    private readonly long _beforeGold;
    private bool _finished,_truncated;
    public string OperationId{get;}
    public string Character{get;}
    public int ProcessId{get;}
    public DateTimeOffset StartedAt{get;}=DateTimeOffset.UtcNow;

    public ManualTradeTrace(string operationId,string character,int processId,IEnumerable<Item> beforeInventory,long beforeGold,int maxPackets=1024)
    {
        if(maxPackets<1)throw new ArgumentOutOfRangeException(nameof(maxPackets));
        OperationId=operationId;Character=character;ProcessId=processId;
        _before=beforeInventory.OrderBy(x=>x.Slot).ToArray();_beforeGold=beforeGold;_maxPackets=maxPackets;
    }

    public bool Add(NetworkPacket packet,string? sessionName=null)
    {
        var relevant=packet switch
        {
            ClientPacket client=>(byte)client.Command==0x4A,
            ServerPacket server=>(byte)server.Command is 0x42 or 0x0F or 0x10 or 0x37,
            _=>false
        };
        if(!relevant)return false;
        lock(_gate)
        {
            if(_finished)return false;
            if(_packets.Count>=_maxPackets){_truncated=true;return true;}
            _packets.Add(new(DateTimeOffset.UtcNow,packet is ClientPacket?"Client":"Server",packet.Command,
                Convert.ToHexString(packet.Data),sessionName));
            return true;
        }
    }

    public ManualTradeResult Finish(IEnumerable<Item> afterInventory,long afterGold)
    {
        lock(_gate)
        {
            if(_finished)throw new InvalidOperationException("Capture already finished.");
            _finished=true;
            return new(OperationId,Character,ProcessId,StartedAt,DateTimeOffset.UtcNow,_before,
                afterInventory.OrderBy(x=>x.Slot).ToArray(),_beforeGold,afterGold,_packets.ToArray(),_truncated);
        }
    }
}
