using System.Net.Sockets;
using System.Reflection;
using Arbiter.Net;
using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Filters;
using Arbiter.Net.Proxy;
using Arbiter.Net.Serialization;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using DAOrganizer.Core;
using DAOrganizer.Game;
using Xunit;
namespace DAOrganizer.Tests;

public class TravelSessionTests
{
    [Theory]
    [InlineData(WorldDirection.Up,10,11)]
    [InlineData(WorldDirection.Right,11,12)]
    [InlineData(WorldDirection.Down,10,13)]
    [InlineData(WorldDirection.Left,9,12)]
    public void EveryDirectionUsesServerConfirmedCoordinates(WorldDirection direction,int x,int y)
    {
        using var game=new Replay();game.Step(direction);
        game.Receive(ServerCommand.Move,new ServerMoveMessage{Direction=direction,PreviousX=10,PreviousY=12});
        Assert.Equal(new Tile(x,y),game.Session.Position);
        Assert.Equal((byte)direction,Assert.Single(game.Sent.OfType<ClientPacket>()).Data[0]);
    }

    [Fact]
    public void NativeAndInjectedWalkingShareCounterIncludingRollover()
    {
        using var game=new Replay();
        for(var i=0;i<256;i++)
        {
            var native=game.NativeStep(new ClientPacket((byte)ClientCommand.Move,new byte[]{1,77,91}));
            Assert.Equal(new byte[]{1,(byte)i,91},native.Data);
        }
        game.Step(WorldDirection.Right);
        Assert.Equal(new byte[]{1,0},game.Sent.OfType<ClientPacket>().Last().Data);
        game.Receive(ServerCommand.Move,new ServerMoveMessage{Direction=WorldDirection.Right,PreviousX=10,PreviousY=12});
        Assert.Equal(new byte[]{2,1},game.NativeStep(new ClientPacket((byte)ClientCommand.Move,new byte[]{2,77})).Data);
    }

    [Fact]
    public void UnconfirmedStepCannotBeSentTwice()
    {
        using var game=new Replay();game.Step(WorldDirection.Right);
        Assert.Throws<InvalidOperationException>(()=>game.Session.SendWalk(WorldDirection.Right));
        Assert.Single(game.Sent);
    }

    [Fact]
    public void RejectedWalkReplyKeepsServerPositionAndDoesNotAnimate()
    {
        using var game=new Replay();game.Step(WorldDirection.Right);
        game.Receive(ServerCommand.Move,new ServerMoveMessage{Direction=WorldDirection.None,PreviousX=10,PreviousY=12});
        Assert.Equal(new Tile(10,12),game.Session.Position);
        Assert.Empty(game.Sent.OfType<ServerPacket>());
        game.Step(WorldDirection.Down);
        Assert.Equal(new byte[]{2,1},game.Sent.OfType<ClientPacket>().Last().Data);
    }

    [Fact]
    public void UnexpectedWalkCoordinatesResynchronizeNativeClient()
    {
        using var game=new Replay();game.Step(WorldDirection.Right);
        game.Receive(ServerCommand.Move,new ServerMoveMessage{Direction=WorldDirection.Right,PreviousX=20,PreviousY=22});
        Assert.Equal(new Tile(21,22),game.Session.Position);
        var correction=Assert.Single(game.Sent.OfType<ServerPacket>());
        Assert.Equal(ServerCommand.UserPosition,correction.Command);
        Assert.Equal(new byte[]{0,21,0,22,0,0,0,0},correction.Data);
    }

    [Theory]
    [InlineData("Would you like to go to your home inn?")]
    [InlineData("Would you like to go to your nation?")]
    public async Task InventoryReadinessWaitsForHomeInnMapAndPosition(string content)
    {
        using var game=new Replay();
        game.Receive(ServerCommand.Status,new ServerStatusMessage());
        game.Receive(ServerCommand.UserReady,new ServerUserReadyMessage());
        var prompt=HomePrompt();prompt.Content=content;
        game.Receive(ServerCommand.PursuitMessage,prompt);
        await Task.Delay(2400);
        Assert.False(game.Session.Ready);
        game.Receive(ServerCommand.MapSize,new ServerMapSizeMessage{MapId=2,Width=50,Height=50,Name="Inn"});
        await Task.Delay(2400);
        Assert.False(game.Session.Ready);
        game.Receive(ServerCommand.UserPosition,new ServerUserPositionMessage{X=15,Y=16});
        await GameSession.WaitUntil(()=>game.Session.Ready,TimeSpan.FromSeconds(5),CancellationToken.None);
        Assert.Equal(2,game.Session.MapId);
        Assert.Equal(new Tile(15,16),game.Session.Position);
    }

    [Fact]
    public void HomeInnTimeoutStopsReadinessWithUsefulError()
    {
        using var game=new Replay();game.Receive(ServerCommand.PursuitMessage,HomePrompt());
        typeof(GameSession).GetField("_homeInnDeadline",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(game.Session,DateTimeOffset.MinValue);
        typeof(GameSession).GetMethod("Flush",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(game.Session,null);
        Assert.Contains("Home inn travel was not confirmed",game.Session.Error);
        Assert.False(game.Session.Ready);
    }

    [Fact]
    public void PromptWithoutYesAndPromptAfterLoginWindowAreIgnored()
    {
        using var game=new Replay();var prompt=HomePrompt();prompt.MenuChoices=["No"];
        game.Receive(ServerCommand.PursuitMessage,prompt);Assert.Empty(game.Sent);
        typeof(GameSession).GetField("_loginPromptExpires",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(game.Session,DateTimeOffset.MinValue);
        game.Receive(ServerCommand.PursuitMessage,HomePrompt());Assert.Empty(game.Sent);
    }

    [Fact]
    public void ServerMapTransitionClearsPendingStepWithoutAnimation()
    {
        using var game=new Replay();game.Step(WorldDirection.Right);
        game.Receive(ServerCommand.MapSize,new ServerMapSizeMessage{MapId=2,Width=50,Height=50,Name="Inn"});
        game.Receive(ServerCommand.UserPosition,new ServerUserPositionMessage{X=2,Y=3});
        Assert.Empty(game.Sent.OfType<ServerPacket>());
        game.Step(WorldDirection.Down);
        Assert.Equal(new byte[]{2,1},game.Sent.OfType<ClientPacket>().Last().Data);
    }
    [Theory]
    [InlineData("Would you like to go to your home inn?")]
    [InlineData("Would you like to go to your nation?")]
    [InlineData("{=qWould you like to go to your nation?")]
    public void HomeInnPromptSelectsYesOnceAndWaitsForArrival(string content)
    {
        using var game=new Replay();
        var prompt=HomePrompt();prompt.Content=content;
        game.Receive(ServerCommand.PursuitMessage,prompt);
        var reply=Assert.Single(game.Sent.OfType<ClientPacket>());
        Assert.Equal(ClientCommand.Pursuit,reply.Command);
        Assert.Equal(new byte[]{1,0,0,0,42,0x12,0x34,0,8,1,2},reply.Data);
        Assert.False(game.Session.Ready);
        game.Receive(ServerCommand.PursuitMessage,prompt);
        Assert.Single(game.Sent.OfType<ClientPacket>());
        game.Receive(ServerCommand.MapSize,new ServerMapSizeMessage{MapId=2,Width=50,Height=50,Name="Home Inn"});
        Assert.False(game.Session.Ready);
        game.Receive(ServerCommand.UserPosition,new ServerUserPositionMessage{X=10,Y=12});
        Assert.Equal(new Tile(10,12),game.Session.Position);
    }

    [Theory]
    [InlineData("Would you like to buy a room at your home inn?")]
    [InlineData("Would you like to change your home inn?")]
    [InlineData("Would you like to go to the arena?")]
    [InlineData("Would you like to change your nation?")]
    [InlineData("Would you like to go to your nation for 10000 gold?")]
    public void OtherPromptsAreNotAccepted(string content)
    {
        using var game=new Replay();var prompt=HomePrompt();prompt.Content=content;
        game.Receive(ServerCommand.PursuitMessage,prompt);
        Assert.Empty(game.Sent);
    }

    [Theory]
    [InlineData("Would you like to return to your home inn?")]
    [InlineData("Would you like to go to your nation?")]
    public void MerchantHomeInnPromptUsesItsYesPursuit(string content)
    {
        using var game=new Replay();
        game.Receive(ServerCommand.ScreenMenu,new ServerScreenMenuMessage
        {
            MenuType=DialogMenuType.Menu,EntityType=EntityTypeFlags.Creature,EntityId=42,
            Content=content,
            MenuChoices=[new(){Text="No",PursuitId=17},new(){Text="Yes",PursuitId=29}]
        });
        var reply=Assert.Single(game.Sent.OfType<ClientPacket>());
        Assert.Equal(ClientCommand.Merchant,reply.Command);
        Assert.Equal(new byte[]{1,0,0,0,42,0,29},reply.Data);
    }

    [Fact]
    public void WalkSendsMovementAndAnimatesOnlyAfterServerConfirmation()
    {
        using var game=new Replay();game.Step(WorldDirection.Right);
        var request=Assert.Single(game.Sent.OfType<ClientPacket>());
        Assert.Equal(ClientCommand.Move,request.Command);
        Assert.Equal(new byte[]{1,0},request.Data);
        Assert.Equal(new Tile(10,12),game.Session.Position);
        Assert.Empty(game.Sent.OfType<ServerPacket>());
        game.Receive(ServerCommand.Move,new ServerMoveMessage{Direction=WorldDirection.Right,PreviousX=10,PreviousY=12});
        Assert.Equal(new Tile(11,12),game.Session.Position);
        var animation=Assert.Single(game.Sent.OfType<ServerPacket>());
        Assert.Equal(ServerCommand.MoveObject,animation.Command);
        Assert.Equal(new byte[]{0,0,0,10,0,10,0,12,1,0},animation.Data);
        game.Step(WorldDirection.Down);
        Assert.Equal(new byte[]{2,1},game.Sent.OfType<ClientPacket>().Last().Data);
    }

    [Fact]
    public void PositionCorrectionDoesNotAnimateRejectedStep()
    {
        using var game=new Replay();game.Step(WorldDirection.Left);
        game.Receive(ServerCommand.UserPosition,new ServerUserPositionMessage{X=10,Y=12});
        Assert.Equal(new Tile(10,12),game.Session.Position);
        Assert.Empty(game.Sent.OfType<ServerPacket>());
        game.Step(WorldDirection.Down);
        Assert.Equal(new byte[]{2,1},game.Sent.OfType<ClientPacket>().Last().Data);
    }

    private static ServerPursuitMessage HomePrompt()=>new()
    {
        DialogType=DialogType.Menu,EntityType=EntityTypeFlags.Creature,EntityId=42,PursuitId=0x1234,StepId=7,
        Content="Would you like to go to your home inn?",MenuChoices=["No","{=qYes"]
    };

    private sealed class Replay:IDisposable
    {
        private readonly InventoryStore _store=new(":memory:");
        private readonly ProxyConnection _connection=new(1,new TcpClient());
        public GameSession Session {get;}
        public List<NetworkPacket> Sent {get;}=[];
        public Replay()
        {
            Session=new(_store);
            var proxy=typeof(GameSession).GetField("_proxy",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Session)!;
            typeof(ProxyServer).GetMethod("InheritFilters",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(proxy,[_connection]);
            typeof(ProxyConnection).GetProperty("Name")!.SetValue(_connection,"Example");
            foreach(var field in new[]{"_isClientConnected","_isServerConnected"})
                typeof(ProxyConnection).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(_connection,1);
            _connection.PacketQueued+=(_,e)=>Sent.Add(e.Packet);
            Receive(ServerCommand.UserAppearance,new ServerUserAppearanceMessage{UserId=10,CanMove=true,HasGuild=true});
            Receive(ServerCommand.MapSize,new ServerMapSizeMessage{MapId=1,Width=50,Height=50,Name="Guild House"});
            Receive(ServerCommand.UserPosition,new ServerUserPositionMessage{X=10,Y=12});
        }
        public void Receive(ServerCommand command,IServerMessage message)
        {
            var builder=new NetworkPacketBuilder(command);
            try
            {
                message.Serialize(ref builder);
                typeof(GameSession).GetMethod("Observe",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Session,[_connection,builder.ToPacket()]);
            }
            finally{builder.Dispose();}
        }
        public NetworkPacket NativeStep(ClientPacket packet)
        {
            var result=(NetworkFilterResult)typeof(ProxyConnection).GetMethod("FilterPacket",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(_connection,[packet])!;
            Assert.Null(result.Exception);return result.Output!;
        }
        public void Step(WorldDirection direction)=>Session.SendWalk(direction);
        public void Dispose(){Session.Dispose();_connection.Dispose();_store.Dispose();}
    }
}
