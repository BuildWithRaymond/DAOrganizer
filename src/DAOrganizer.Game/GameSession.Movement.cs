using Arbiter.Net;
using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Filters;
using Arbiter.Net.Proxy;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using DAOrganizer.Core;
namespace DAOrganizer.Game;

public sealed partial class GameSession
{
    private byte _walkCounter;
    private uint _playerId;
    private (int Map,Tile Origin,WorldDirection Direction)? _pendingWalk;
    public long PositionRevision {get;private set;}
    public long ManualMovementRevision {get;private set;}

    private NetworkPacket? RewriteManualWalk(ProxyConnection connection,ClientMoveMessage message,object? state,NetworkMessageFilterResult<ClientMoveMessage> result)
    {
        lock(_gate)
        {
            // Excalibur assigns one shared counter to native and automated steps.
            // Preserve any native trailing bytes when replacing that counter.
            var original=(ClientPacket)result.Passthrough();
            var payload=original.Data.ToArray();payload[1]=_walkCounter++;
            _pendingWalk=null;ManualMovementRevision++;
            return new ClientPacket((byte)ClientCommand.Move,payload,original.Checksum){Sequence=original.Sequence,Source=original.Source};
        }
    }

    public long SendWalk(WorldDirection direction)
    {
        lock(_gate)
        {
            if((byte)direction>3)throw new ArgumentOutOfRangeException(nameof(direction));
            if(_pendingWalk!=null)throw new InvalidOperationException("Previous walk step is still awaiting confirmation.");
            if(_homeInnPending)throw new InvalidOperationException("Wait for arrival at the home inn before walking.");
            var revision=PositionRevision;
            _pendingWalk=(MapId,Position,direction);
            try{Send(new ClientMoveMessage{Direction=direction,StepCount=_walkCounter});_walkCounter++;}
            catch{_pendingWalk=null;throw;}
            return revision;
        }
    }

    private void ObserveWalk(ServerMoveMessage movement)
    {
        var origin=new Tile(movement.PreviousX,movement.PreviousY);
        var delta=movement.Direction switch
        {
            WorldDirection.Up=>new Tile(0,-1),WorldDirection.Right=>new Tile(1,0),
            WorldDirection.Down=>new Tile(0,1),WorldDirection.Left=>new Tile(-1,0),_=>new Tile(0,0)
        };
        Position=new(origin.X+delta.X,origin.Y+delta.Y);PositionRevision++;
        var pending=_pendingWalk;_pendingWalk=null;
        if(pending is { } step&&step.Map==MapId&&step.Origin==origin&&step.Direction==movement.Direction)
        {
            // Like Excalibur's acknowledged walking: 0x0B confirms the position;
            // 0x0C presents our injected step in the native client.
            _connection?.EnqueueMessage(new ServerMoveObjectMessage
            {
                EntityId=_playerId,OriginX=movement.PreviousX,OriginY=movement.PreviousY,Direction=movement.Direction
            });
        }
        else if(pending is { } corrected&&Position!=corrected.Origin)
        {
            _connection?.EnqueueMessage(new ServerUserPositionMessage{X=(ushort)Position.X,Y=(ushort)Position.Y});
        }
    }
}
