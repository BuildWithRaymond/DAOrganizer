using System.Net.Sockets;
using System.Reflection;
using Arbiter.Net;
using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Proxy;
using Arbiter.Net.Serialization;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using DAOrganizer.Core;
using DAOrganizer.Game;
using Xunit;
namespace DAOrganizer.Tests;

public class SessionTests
{
    [Fact]
    public void LoginRejectionProvidesAnErrorInsteadOfWaitingForInventory()
    {
        using var store=new InventoryStore(":memory:");using var session=new GameSession(store);using var connection=Connection();
        Observe(session,connection,Packet(ServerCommand.LoginCheck,new ServerLoginCheckMessage{Result=LoginResult.IncorrectPassword}));
        Assert.Equal("Login rejected (IncorrectPassword).",session.Error);Assert.False(session.Ready);
    }
    [Fact]
    public void SafeQuitApprovalAcceptsBodyWithoutOptionalPadding()
    {
        using var store=new InventoryStore(":memory:");using var session=new GameSession(store);using var connection=Connection();
        Observe(session,connection,Packet(ServerCommand.UserAppearance,new ServerUserAppearanceMessage{UserId=10}));
        Observe(session,connection,new ServerPacket((byte)ServerCommand.Quit,new byte[]{1}));
        Assert.True(session.Online);
    }
    [Fact]
    public void OwnDrawUpdatesSavedAppearanceButAnotherCharacterDoesNot()
    {
        using var store=new InventoryStore(":memory:");using var session=new GameSession(store);using var connection=Connection();
        Observe(session,connection,Packet(ServerCommand.UserAppearance,new ServerUserAppearanceMessage{UserId=10}));
        void Draw(uint entity,ushort head)=>Observe(session,connection,Packet(ServerCommand.DrawHumanObjects,new ServerDrawHumanObjectsMessage
        {
            EntityId=entity,Name="Example",HeadSprite=head,BodySprite=BodySprite.Male,FaceShape=2,HairColor=DyeColor.Default,
            SkinColor=SkinColor.Default
        }));
        Draw(11,7);
        Assert.Null(store.Get<System.Text.Json.JsonElement?>("appearance/example"));
        Draw(10,8);
        Assert.Equal(8,store.Get<System.Text.Json.JsonElement>("appearance/example").GetProperty("HeadSprite").GetInt32());
        Draw(10,9);
        Assert.Equal(9,store.Get<System.Text.Json.JsonElement>("appearance/example").GetProperty("HeadSprite").GetInt32());
    }
    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    public void SafeQuitResponseIsApprovalNotDisconnection(byte approval)
    {
        using var store=new InventoryStore(":memory:");using var session=new GameSession(store);using var connection=Connection();
        Observe(session,connection,Packet(ServerCommand.UserAppearance,new ServerUserAppearanceMessage{UserId=10}));
        Observe(session,connection,Packet(ServerCommand.Quit,new ServerQuitMessage{Result=approval}));
        Assert.True(session.Online);
    }
    private static void Observe(GameSession session,ProxyConnection connection,NetworkPacket packet)=>
        typeof(GameSession).GetMethod("Observe",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(session,[connection,packet]);
    private static ProxyConnection Connection()
    {
        var connection=new ProxyConnection(1,new TcpClient());
        typeof(ProxyConnection).GetProperty("Name")!.SetValue(connection,"Example");return connection;
    }
    private static ServerPacket Packet(ServerCommand command,IServerMessage message)
    {
        var builder=new NetworkPacketBuilder(command);
        try{message.Serialize(ref builder);return (ServerPacket)builder.ToPacket();}finally{builder.Dispose();}
    }
    [Fact]
    public void InventoryPacketKeepsObservedStackability()
    {
        using var store=new InventoryStore(":memory:");using var session=new GameSession(store);using var connection=Connection();
        Observe(session,connection,Packet(ServerCommand.AddInventory,new ServerAddInventoryMessage
        {
            Slot=4,Name="Water Dungeon Chest",Quantity=8,Sprite=15,IsStackable=true
        }));
        Assert.True(Assert.Single(session.Inventory()).IsStackable);
    }
    [Fact]
    public async Task PacketReplayCommitsAfterLoginThenTracksRemoval()
    {
        using var store=new InventoryStore(":memory:");using var session=new GameSession(store);using var connection=Connection();
        store.SaveSnapshot("Example","Inventory",[new(8,"Old item",1)],true);
        Observe(session,connection,Packet(ServerCommand.AddInventory,new ServerAddInventoryMessage{Slot=2,Name="Ruby",Quantity=7}));
        Observe(session,connection,Packet(ServerCommand.UserAppearance,new ServerUserAppearanceMessage{UserId=10}));
        Observe(session,connection,Packet(ServerCommand.MapSize,new ServerMapSizeMessage{MapId=1,Width=50,Height=50,Name="Map"}));
        Observe(session,connection,Packet(ServerCommand.Status,new ServerStatusMessage()));
        Observe(session,connection,Packet(ServerCommand.UserReady,new ServerUserReadyMessage()));
        Assert.False(session.Ready);Assert.Equal("Old item",Assert.Single(store.Items("Example","Inventory")).Name);
        await GameSession.WaitUntil(()=>session.Ready,TimeSpan.FromSeconds(5),CancellationToken.None);
        Assert.Equal("Ruby",Assert.Single(store.Items("Example","Inventory")).Name);
        Observe(session,connection,new ServerPacket((byte)ServerCommand.RemoveInventory,new byte[]{2}));
        await GameSession.WaitUntil(()=>store.Items("Example","Inventory").Count==0,TimeSpan.FromSeconds(3),CancellationToken.None);
    }
    [Fact]
    public void MalformedInventoryPreservesPreviousSnapshot()
    {
        using var store=new InventoryStore(":memory:");using var session=new GameSession(store);using var connection=Connection();
        store.SaveSnapshot("Example","Inventory",[new(8,"Old item",1)],true);
        Assert.Throws<TargetInvocationException>(()=>Observe(session,connection,new ServerPacket((byte)ServerCommand.AddInventory,new byte[]{1})));
        Assert.False(session.Ready);Assert.Equal("Old item",Assert.Single(store.Items("Example","Inventory")).Name);
        Assert.Equal("Incomplete",store.Freshness("Example","Inventory"));
    }
    [Fact]
    public void ShopCannotOverwriteBankButCorrelatedWithdrawalCan()
    {
        using var store=new InventoryStore(":memory:");using var session=new GameSession(store);using var connection=Connection();
        store.SaveSnapshot("Example","Bank",[new(1,"Old item",1)],true);
        var items=new ServerPacket((byte)ServerCommand.ScreenMenu,BankTests.Packet(300));
        Observe(session,connection,items);
        Assert.Equal("Old item",Assert.Single(store.Items("Example","Bank")).Name);
        Observe(session,connection,Packet(ServerCommand.ScreenMenu,new ServerScreenMenuMessage
        {
            MenuType=DialogMenuType.Menu,EntityId=42,Name="Banker",Content="Welcome",MenuChoices=[new(){Text="Withdraw Items",PursuitId=0x42}]
        }));
        var builder=new NetworkPacketBuilder(ClientCommand.Merchant);
        try
        {
            new ClientMerchantMessage{EntityId=42,PursuitId=0x42}.Serialize(ref builder);
            Observe(session,connection,builder.ToPacket());
        }
        finally{builder.Dispose();}
        Observe(session,connection,items);
        Assert.Equal(300,store.Items("Example","Bank").Count);
        Assert.Equal("Current",store.Freshness("Example","Bank"));
    }
}
