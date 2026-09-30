using DAOrganizer.Core;

namespace DAOrganizer.Game;

public sealed record VisibleTradeTarget(uint Id,string Name,Tile Position,int MapId,DateTimeOffset ObservedAt);
public sealed record TradeTargetInspection(string Name,bool Connected,bool Ready,int MapId,string MapName,Tile Position,
    bool PlayerIdKnown,int EntityDrawPackets,int HumanDrawPackets,int VisibleTargets,int NamedMatches,
    int RecentMatches,DateTimeOffset? LatestMatchAt);

public sealed partial class GameSession
{
    private readonly Dictionary<uint,VisibleTradeTarget> _visibleTradeTargets=[];
    private int _entityDrawPackets,_humanDrawPackets;

    public TradeTargetInspection InspectTradeTarget(string expectedPartner,DateTimeOffset now)
    {
        if(string.IsNullOrWhiteSpace(expectedPartner))throw new ArgumentException("Expected partner name is required.",nameof(expectedPartner));
        lock(_gate)
        {
            var named=_visibleTradeTargets.Values.Where(x=>x.Name.Equals(expectedPartner,StringComparison.OrdinalIgnoreCase)).ToArray();
            return new(Name,_connection is {IsConnected:true},Ready,MapId,MapName,Position,_playerId!=0,
                _entityDrawPackets,_humanDrawPackets,_visibleTradeTargets.Count,named.Length,
                named.Count(x=>x.MapId==MapId&&x.Id!=0&&x.ObservedAt<=now&&now-x.ObservedAt<=TimeSpan.FromMinutes(2)),
                named.Length==0?null:named.Max(x=>x.ObservedAt));
        }
    }

    public VisibleTradeTarget ResolveTradeTarget(string name,DateTimeOffset now)
    {
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Expected partner name is required.",nameof(name));
        lock(_gate)
        {
            var matches=_visibleTradeTargets.Values.Where(x=>x.MapId==MapId&&x.Id!=0&&
                x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)&&x.ObservedAt<=now&&
                now-x.ObservedAt<=TimeSpan.FromMinutes(2)).ToArray();
            if(matches.Length!=1)
                throw new InvalidOperationException("Expected partner is not uniquely visible with a recent server ID.");
            return matches[0];
        }
    }

    public DirectTradeEndpoint CaptureTradeEndpoint(string expectedPartner,DateTimeOffset now)
    {
        lock(_gate)
        {
            if(_connection is not {IsConnected:true}||!Online)
                throw new InvalidOperationException("Character is not connected through DA Organizer.");
            return new(Name,ProcessId,Ready,MapId,_playerId,Position,_inventory.Values.OrderBy(x=>x.Slot).ToArray(),
                Gold,ResolveTradeTarget(expectedPartner,now),
                _store.Get<HashSet<int>>("pins/"+Name.ToLowerInvariant())??[],now);
        }
    }

    public ManualTradeResult PeekManualTradeCapture()
    {
        lock(_gate)
            return (_manualTradeTrace??throw new InvalidOperationException("No trade capture is running."))
                .Snapshot(_inventory.Values,Gold);
    }
}
