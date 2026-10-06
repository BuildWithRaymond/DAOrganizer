using System.Net.Sockets;
using System.Reflection;
using Arbiter.Net.Proxy;
using Arbiter.Net.Serialization;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using DAOrganizer.Core;
using DAOrganizer.Game;
using Xunit;

namespace DAOrganizer.Tests;

public class GameSessionTradeTargetTests
{
    [Fact]
    public void VisibilityInspectionCountsDrawsAndExpectedPartner()
    {
        using var replay=new Replay();
        replay.Receive(ServerCommand.MapSize,new ServerMapSizeMessage{MapId=7,Width=40,Height=40,Name="Test"});
        replay.Receive(ServerCommand.DrawHumanObjects,Human(11,"Beta",11,12));
        var inspection=replay.Session.InspectTradeTarget("Beta",DateTimeOffset.UtcNow);
        Assert.Equal(7,inspection.MapId);
        Assert.Equal(1,inspection.HumanDrawPackets);
        Assert.Equal(1,inspection.VisibleTargets);
        Assert.Equal(1,inspection.NamedMatches);
        Assert.Equal(1,inspection.RecentMatches);
    }

    [Fact]
    public void CaptureRejectsDisconnectedSessionBeforeCheckingPartnerVisibility()
    {
        using var replay=new Replay();
        var error=Assert.Throws<InvalidOperationException>(()=>
            replay.Session.CaptureTradeEndpoint("Beta",DateTimeOffset.UtcNow));
        Assert.Contains("not connected",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TargetMustBeNamedVisibleAndUniqueUntilRemoved()
    {
        using var replay=new Replay();
        replay.Receive(ServerCommand.UserAppearance,new ServerUserAppearanceMessage{UserId=10});
        replay.Receive(ServerCommand.MapSize,new ServerMapSizeMessage{MapId=7,Width=40,Height=40,Name="Test"});
        replay.Receive(ServerCommand.DrawHumanObjects,Human(11,"Beta",11,12));
        var now=DateTimeOffset.UtcNow;
        var target=replay.Session.ResolveTradeTarget("Beta",now);
        Assert.Equal(11u,target.Id);
        Assert.Equal(new Tile(11,12),target.Position);
        Assert.Equal(11u,replay.Session.ResolveTradeTarget("Beta",now.AddMinutes(3)).Id);
        replay.Receive(ServerCommand.DrawHumanObjects,Human(12,"Beta",12,12));
        Assert.Throws<InvalidOperationException>(()=>replay.Session.ResolveTradeTarget("Beta",DateTimeOffset.UtcNow));
        replay.Receive(ServerCommand.RemoveObjects,new ServerRemoveObjectsMessage{EntityId=12});
        Assert.Equal(11u,replay.Session.ResolveTradeTarget("Beta",DateTimeOffset.UtcNow).Id);
        replay.Receive(ServerCommand.RemoveObjects,new ServerRemoveObjectsMessage{EntityId=11});
        Assert.Throws<InvalidOperationException>(()=>replay.Session.ResolveTradeTarget("Beta",DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MovementUpdatesTargetAndMapChangeClearsIt()
    {
        using var replay=new Replay();
        replay.Receive(ServerCommand.MapSize,new ServerMapSizeMessage{MapId=7,Width=40,Height=40,Name="Test"});
        replay.Receive(ServerCommand.DrawHumanObjects,Human(11,"Beta",11,12));
        replay.Receive(ServerCommand.MoveObject,new ServerMoveObjectMessage{EntityId=11,OriginX=11,OriginY=12,
            Direction=WorldDirection.Right});
        Assert.Equal(new Tile(12,12),replay.Session.ResolveTradeTarget("Beta",DateTimeOffset.UtcNow).Position);
        replay.Receive(ServerCommand.MapSize,new ServerMapSizeMessage{MapId=8,Width=40,Height=40,Name="Other"});
        Assert.Throws<InvalidOperationException>(()=>replay.Session.ResolveTradeTarget("Beta",DateTimeOffset.UtcNow));
    }

    private static ServerDrawHumanObjectsMessage Human(uint id,string name,ushort x,ushort y)=>new()
    {
        EntityId=id,Name=name,X=x,Y=y,BodySprite=BodySprite.Male
    };

    private sealed class Replay:IDisposable
    {
        private readonly InventoryStore _store=new(":memory:");
        private readonly ProxyConnection _connection=new(1,new TcpClient());
        public GameSession Session{get;}
        public Replay()=>Session=new(_store);
        public void Receive(ServerCommand command,IServerMessage message)
        {
            var builder=new NetworkPacketBuilder(command);
            try
            {
                message.Serialize(ref builder);
                typeof(GameSession).GetMethod("Observe",BindingFlags.Instance|BindingFlags.NonPublic)!
                    .Invoke(Session,[_connection,builder.ToPacket()]);
            }
            finally{builder.Dispose();}
        }
        public void Dispose(){Session.Dispose();_connection.Dispose();_store.Dispose();}
    }
}
