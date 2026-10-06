using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Arbiter.Net;
using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Proxy;
using Arbiter.Net.Serialization;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Server.Types;
using Arbiter.Net.Types;
using DAOrganizer.Core;
using DAOrganizer.Game;
using Xunit;

namespace DAOrganizer.Tests;

public class NavigationRouteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockedExitUsesAnotherRecordedExitToTheSameDestination(bool worldMap)
    {
        using var replay=new Replay(worldMap);
        await replay.Travel();
        Assert.Equal(2,replay.Session.MapId);
        Assert.Equal(new Tile(3,3),Assert.Single(replay.UsedExits));
        Assert.NotNull(replay.Session.LastBankScan);
        Assert.Equal(12,Assert.Single(replay.Session.BankItems()).Quantity);
        Assert.All(replay.Sent.OfType<ClientMerchantMessage>(),x=>Assert.Equal((ushort)0x45,x.PursuitId));
        var selections=replay.Sent.OfType<ClientFieldMapMessage>().ToArray();
        Assert.Equal(worldMap?1:0,selections.Length);
        Assert.All(selections,x=>Assert.Equal((ushort)2,x.MapId));
    }

    [Fact]
    public async Task UnreachableExitsReportBothMapsAndDoNotUseAnotherDestination()
    {
        using var replay=new Replay(false);
        replay.Block(new(3,3));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(replay.Travel);
        Assert.Contains("Test Town",error.Message);
        Assert.Contains("Test Bank",error.Message);
        Assert.Contains("2 recorded exits",error.Message);
        Assert.Equal(error.Message,replay.Session.Status);
        Assert.Empty(replay.Sent);
        Assert.Null(replay.Session.LastBankScan);
    }

    [Fact]
    public async Task ExitFallbackDoesNotChangeBetweenWalkingAndWorldMapTransitions()
    {
        using var replay=new Replay(true,includeAlternative:false);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(replay.Travel);
        Assert.Contains("1 recorded exit",error.Message);
        Assert.Empty(replay.Sent);
    }

    [Fact]
    public async Task UnconfirmedMovementStopsWithoutTryingAnotherExit()
    {
        using var replay=new Replay(false);
        replay.AcknowledgeWalks=false;
        await Assert.ThrowsAsync<TimeoutException>(replay.Travel);
        Assert.IsType<ClientMoveMessage>(Assert.Single(replay.Sent));
        Assert.Empty(replay.UsedExits);
        Assert.Null(replay.Session.LastBankScan);
    }

    [Fact]
    public async Task CancelledTravelDoesNotAttemptAnExit()
    {
        using var replay=new Replay(false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>replay.Travel(new CancellationToken(true)));
        Assert.Empty(replay.Sent);
    }

    private sealed class Replay:IDisposable
    {
        private readonly InventoryStore _store=new(":memory:");
        private readonly ProxyConnection _connection=new(1,new TcpClient());
        private readonly string _directory=Path.Combine(Path.GetTempPath(),"DAOrganizer-navigation-"+Guid.NewGuid());
        private readonly Navigation _navigation;
        private readonly Portal _planned;
        private readonly bool _worldMap;
        private uint _npcId=100;
        public GameSession Session{get;}
        public List<IClientMessage> Sent{get;}=[];
        public List<Tile> UsedExits{get;}=[];
        public bool AcknowledgeWalks{get;set;}=true;

        public Replay(bool worldMap,bool includeAlternative=true)
        {
            _worldMap=worldMap;
            _planned=new(1,3,2,2,1,2,worldMap);
            // A nearer exit to another map and a different transition kind must stay excluded.
            var portals=new List<Portal>{_planned,new(1,2,2,3,1,2,worldMap),new(1,2,1,2,1,2,!worldMap)};
            if(includeAlternative)portals.Add(new(1,3,3,2,1,2,worldMap));
            var world=new WorldGraph([new(1,"Test Town",7,5,portals),new(2,"Test Bank",7,5,[]),new(3,"Other Map",7,5,[])]);
            Directory.CreateDirectory(Path.Combine(_directory,"maps"));
            foreach(var id in new[]{1,2})File.WriteAllBytes(Path.Combine(_directory,"maps",$"lod{id}.map"),new byte[7*5*6]);
            // A tiny synthetic archive supplies collision flags without installed game assets.
            using(var output=new BinaryWriter(File.Create(Path.Combine(_directory,"test.dat"))))
            {
                output.Write(2u);output.Write(38u);
                output.Write(Encoding.ASCII.GetBytes("sotp.dat\0\0\0\0\0"));
                output.Write(39u);output.Write(new byte[13]);output.Write((byte)0);
            }
            _navigation=new(world,_directory);
            Session=new(_store);
            typeof(ProxyConnection).GetProperty("Name")!.SetValue(_connection,"Example");
            foreach(var field in new[]{"_isClientConnected","_isServerConnected"})
                typeof(ProxyConnection).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(_connection,1);
            _connection.PacketQueued+=(_,e)=>
            {
                if(e.Packet is ClientPacket packet&&ClientMessageFactory.Default.Create(packet) is { } message)
                {Sent.Add(message);Reply(message);}
            };
            Receive(ServerCommand.UserAppearance,new ServerUserAppearanceMessage{UserId=10});
            Arrive(1);
            typeof(GameSession).GetProperty(nameof(GameSession.ProcessId))!.SetValue(Session,-1);
            typeof(GameSession).GetProperty(nameof(GameSession.Health))!.SetValue(Session,100u);
            typeof(GameSession).GetField("_ready",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Session,true);
            Block(new(3,2));
        }

        public Task Travel()=>Travel(CancellationToken.None);
        public Task Travel(CancellationToken token)=>_navigation.Travel(Session,new(2,"Test Bank"),token,[_planned]);
        public void Block(Tile tile)=>Receive(ServerCommand.DrawObjects,new ServerDrawObjectsMessage
        {
            Entities=[new ServerCreatureEntity{Id=_npcId++,X=(ushort)tile.X,Y=(ushort)tile.Y,CreatureType=CreatureType.Mundane,Name="Obstacle"}]
        });
        private void Arrive(ushort map)
        {
            Receive(ServerCommand.MapSize,new ServerMapSizeMessage{MapId=map,Width=7,Height=5,Name=map==1?"Test Town":"Test Bank"});
            Receive(ServerCommand.UserPosition,new ServerUserPositionMessage{X=1,Y=2});
            if(map==2)Receive(ServerCommand.DrawObjects,new ServerDrawObjectsMessage
            {Entities=[new ServerCreatureEntity{Id=42,X=2,Y=2,CreatureType=CreatureType.Mundane,Name="Banker"}]});
        }
        private void Reply(IClientMessage message)
        {
            if(message is ClientMoveMessage move&&AcknowledgeWalks)
            {
                var before=Session.Position;
                Receive(ServerCommand.Move,new ServerMoveMessage{Direction=move.Direction,PreviousX=(ushort)before.X,PreviousY=(ushort)before.Y});
                if(Session.MapId==1&&Session.Position==new Tile(3,3))
                {
                    UsedExits.Add(Session.Position);
                    if(_worldMap)Receive(ServerCommand.FieldMap,new ServerFieldMapMessage
                    {Locations=[new(){MapId=2,MapX=1,MapY=2,Name="Test Bank",Checksum=123}]});
                    else Arrive(2);
                }
            }
            else if(message is ClientFieldMapMessage)Arrive(2);
            else if(message is ClientMerchantMessage merchant)
            {
                Assert.Equal((ushort)0x45,merchant.PursuitId);
                Receive(ServerCommand.ScreenMenu,new ServerScreenMenuMessage{EntityId=42,
                    MenuType=DialogMenuType.ItemChoices,PursuitId=86,
                    ItemChoices=[new(){Name="Emerald",Sprite=15,Price=12,Description=""}]});
            }
        }
        private void Receive(ServerCommand command,IServerMessage message)
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
        public void Dispose()
        {
            Session.Dispose();_connection.Dispose();_store.Dispose();
            Directory.Delete(_directory,true);
        }
    }
}
