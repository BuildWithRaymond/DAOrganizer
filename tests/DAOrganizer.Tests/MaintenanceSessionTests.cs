using System.Net.Sockets;
using System.Reflection;
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

public class MaintenanceSessionTests
{
    [Theory]
    [InlineData(new byte[]{0xFF})]
    [InlineData(new byte[]{0xFF,0x12})]
    [InlineData(new byte[]{0xFF,0x12,0x00})]
    [InlineData(new byte[]{0xFF,0x12,0x00,0x00})]
    public async Task SoundPacketCannotPoisonDepositTracking(byte[] data)
    {
        using var game=new Replay();game.Npc();game.Add(9,2);game.Set("_ready",true);
        var item=Assert.Single(game.Session.Inventory());new ItemRules(game.Store).Set(item,ItemAction.AutoDeposit);
        var action=game.Session.DepositItem(item,2,default);
        game.Incoming(new ServerPacket((byte)ServerCommand.SoundEffect,data));
        await game.Drain();
        game.Receive(ServerCommand.RemoveInventory,new ServerRemoveInventoryMessage{Slot=9});
        Assert.True(await action);Assert.Null(game.Session.Error);Assert.Empty(game.Session.Inventory());
        Assert.Equal(new[]{ClientCommand.Merchant,ClientCommand.Pursuit},game.Sent.Select(x=>x.Command));
    }

    [Fact]
    public async Task MalformedInventoryStillStopsTracking()
    {
        using var game=new Replay();game.Add(9,2);game.Set("_ready",true);
        game.Incoming(new ServerPacket((byte)ServerCommand.AddInventory,new byte[]{9}));await game.Drain();
        Assert.Contains("Tracking packet 0x0F",game.Session.Error);Assert.False(game.Session.Ready);
        Assert.Equal(2,Assert.Single(game.Session.Inventory()).Quantity);
    }
    [Fact]
    public void CorrelatedSilentBankBecomesEmpty()
    {
        using var game=new Replay();game.StartBank();
        game.Store.SaveSnapshot("Example","Bank",[new(1,"Old item",3)],true);
        game.Set("_bankRequested",DateTimeOffset.UtcNow.AddSeconds(-11));game.Call("CompleteSilentBank");
        Assert.Empty(game.Store.Items("Example","Bank"));Assert.Equal("Current",game.Store.Freshness("Example","Bank"));Assert.NotNull(game.Session.LastBankScan);
    }
    [Theory]
    [InlineData("disconnected")]
    [InlineData("unrelated menu")]
    [InlineData("too soon")]
    [InlineData("tracking error")]
    public void IncompleteOrUncorrelatedBankDoesNotEraseHistory(string reason)
    {
        using var game=new Replay();game.StartBank();game.Store.SaveSnapshot("Example","Bank",[new(1,"Old item",3)],true);
        game.Set("_bankRequested",DateTimeOffset.UtcNow.AddSeconds(reason=="too soon"?-1:-11));
        if(reason=="disconnected")game.Disconnect();
        if(reason=="unrelated menu")game.Receive(ServerCommand.ScreenMenu,new ServerScreenMenuMessage{MenuType=DialogMenuType.Menu,EntityId=99});
        if(reason=="tracking error")typeof(GameSession).GetProperty("Error")!.SetValue(game.Session,"Bad packet");
        game.Call("CompleteSilentBank");Assert.Single(game.Store.Items("Example","Bank"));Assert.Null(game.Session.LastBankScan);
    }
    [Fact]
    public async Task DropUsesCurrentSlotPositionQuantityAndWaitsForRemoval()
    {
        using var game=new Replay();game.Add(3,2);game.Set("_ready",true);var item=Assert.Single(game.Session.Inventory());
        new ItemRules(game.Store).Set(item,ItemAction.Junk);
        var action=game.Session.DropItem(item,2,default);var sent=Assert.Single(game.Sent);
        Assert.Equal(ClientCommand.Drop,sent.Command);Assert.Equal(new byte[]{3,0,10,0,12,0,0,0,2},sent.Data);Assert.False(action.IsCompleted);
        game.Receive(ServerCommand.RemoveInventory,new ServerRemoveInventoryMessage{Slot=3});Assert.True(await action);
    }
    [Fact]
    public async Task DropOfPinnedOrUnmarkedItemSendsNothing()
    {
        using var game=new Replay();game.Add(3,2);game.Set("_ready",true);var item=Assert.Single(game.Session.Inventory());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>game.Session.DropItem(item,2,default));
        new ItemRules(game.Store).Set(item,ItemAction.Junk);game.Store.Put("pins/example",new[]{3});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>game.Session.DropItem(item,2,default));Assert.Empty(game.Sent);
    }
    [Fact]
    public void DepositQuantityMatchesExcaliburArgumentLayout()
    {
        var builder=new NetworkPacketBuilder(ClientCommand.Merchant);
        try
        {
            new ClientMerchantMessage{EntityType=EntityTypeFlags.Creature,EntityId=42,PursuitId=0x54,QuantitySlot=7,Arguments=["25"]}.Serialize(ref builder);
            Assert.Equal(new byte[]{1,0,0,0,42,0,0x54,1,7,2,(byte)'2',(byte)'5'},builder.ToPacket().Data);
            var parsed=Assert.IsType<ClientMerchantMessage>(ClientMessageFactory.Default.Create((ClientPacket)builder.ToPacket()));
            Assert.Equal((byte)7,parsed.QuantitySlot);Assert.Equal("25",Assert.Single(parsed.Arguments));
        }
        finally{builder.Dispose();}
    }
    [Theory]
    [InlineData(1u, 1L, (ushort)0x53)]
    [InlineData(25u, 25L, (ushort)0x54)]
    [InlineData(25u, 1L, (ushort)0x54)]
    public async Task HaxDepositSendsDirectTransferThenPopupClose(uint carried,long quantity,ushort pursuit)
    {
        using var game=new Replay();game.Add(3,carried);game.Npc();game.Set("_ready",true);
        var item=Assert.Single(game.Session.Inventory());new ItemRules(game.Store).Set(item,ItemAction.AutoDeposit);
        game.OnSent=packet=>
        {
            Assert.NotEqual(ClientCommand.RequestObjectInfo,packet.Command);
            if(packet.Command==ClientCommand.Merchant)
            {
                var request=(ClientMerchantMessage)ClientMessageFactory.Default.Create(packet)!;
                Assert.Equal(pursuit,request.PursuitId);Assert.Equal(42u,request.EntityId);
                if(pursuit==0x53){Assert.Equal((byte)3,request.Slot);Assert.Empty(request.Arguments);}
                else{Assert.Equal((byte)3,request.QuantitySlot);Assert.Equal(quantity.ToString(),Assert.Single(request.Arguments));}
            }
            else
            {
                Assert.Equal(ClientCommand.Pursuit,packet.Command);
                Assert.Equal(new byte[]{1,0,0,0,42,0,0,0,1},packet.Data);
                if(carried==quantity)game.Receive(ServerCommand.RemoveInventory,new ServerRemoveInventoryMessage{Slot=3});
                else game.Add(3,carried-(uint)quantity);
            }
        };
        Assert.True(await game.Session.DepositItem(item,quantity,default));Assert.Equal(2,game.Sent.Count);
        Assert.Equal(carried-quantity,game.Session.Inventory().Sum(x=>x.Quantity));
    }
    [Fact]
    public async Task HaxWithdrawalSendsOneUnitWithoutBankMenuThenClosesPopup()
    {
        using var game=new Replay();game.Npc();game.Set("_ready",true);var item=new Item(1,"Emerald",12,15);
        game.Store.SaveSnapshot("Example","Bank",[item],true);new ItemRules(game.Store).Set(item,ItemAction.Junk);
        game.OnSent=packet=>
        {
            Assert.NotEqual(ClientCommand.RequestObjectInfo,packet.Command);
            if(packet.Command==ClientCommand.Merchant)
            {
                Assert.Equal(new byte[]{1,0,0,0,42,0,0x57,7,69,109,101,114,97,108,100,1,49},packet.Data);
            }
            else
            {
                Assert.Equal(ClientCommand.Pursuit,packet.Command);Assert.Equal(new byte[]{1,0,0,0,42,0,0,0,1},packet.Data);
                game.Add(5,1);
            }
        };
        Assert.True(await game.Session.WithdrawOne(item,default));Assert.Equal(1,Assert.Single(game.Session.Inventory()).Quantity);
        Assert.Equal(2,game.Sent.Count);
    }
    [Fact]
    public async Task HaxScanRequestsWithdrawalListDirectlyAndCorrelatesResponse()
    {
        using var game=new Replay();game.Npc();
        game.OnSent=packet=>
        {
            Assert.Equal(ClientCommand.Merchant,packet.Command);Assert.Equal(new byte[]{1,0,0,0,42,0,0x45},packet.Data);
            game.Receive(ServerCommand.ScreenMenu,new ServerScreenMenuMessage{EntityId=42,MenuType=DialogMenuType.ItemChoices,PursuitId=86,ItemChoices=[new(){Name="Emerald",Sprite=15,Price=12,Description=""}]});
        };
        await game.Session.ScanNearbyBank(null,default);Assert.Single(game.Sent);
        Assert.Equal(12,Assert.Single(game.Session.BankItems()).Quantity);Assert.NotNull(game.Session.LastBankScan);
    }
    [Fact]
    public async Task AccountBankRefreshAtInnNeedsNoWorldLogsOrWalking()
    {
        using var game=new Replay();game.Npc("Innkeeper");game.Set("_ready",true);
        using var app=new DAOrganizer.App.Organizer(Path.Combine(Path.GetTempPath(),"DAOrganizer-tests",Guid.NewGuid().ToString("N")));
        game.OnSent=packet=>
        {
            Assert.Equal(ClientCommand.Merchant,packet.Command);
            Assert.Equal(new byte[]{1,0,0,0,42,0,0x45},packet.Data);
            game.Receive(ServerCommand.ScreenMenu,new ServerScreenMenuMessage{EntityId=42,MenuType=DialogMenuType.ItemChoices,PursuitId=86,ItemChoices=[new(){Name="Emerald",Sprite=15,Price=12,Description=""}]});
        };
        await app.RefreshBank(game.Session,true,default);
        Assert.Single(game.Sent);Assert.Equal(12,Assert.Single(game.Session.BankItems()).Quantity);
        Assert.Null(game.Store.Get<string>("banker/"+game.Session.MapId));
    }

    [Fact]
    public async Task AutoDepositCannotTransferAtInnWhenBankRouteIsMissing()
    {
        using var game=new Replay();game.Npc("Innkeeper");game.Add(5,2);game.Set("_ready",true);
        using var app=new DAOrganizer.App.Organizer(Path.Combine(Path.GetTempPath(),"DAOrganizer-tests",Guid.NewGuid().ToString("N")));
        app.Rules.Set(Assert.Single(game.Session.Inventory()),ItemAction.AutoDeposit);
        game.OnSent=packet=>game.Receive(ServerCommand.ScreenMenu,new ServerScreenMenuMessage{EntityId=42,MenuType=DialogMenuType.ItemChoices,PursuitId=86,ItemChoices=[]});
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>app.RefreshBank(game.Session,true,default));
        Assert.Contains("WorldLogs",error.Message);Assert.Single(game.Sent);
        Assert.Equal(new byte[]{1,0,0,0,42,0,0x45},game.Sent[0].Data);
    }

    [Fact]
    public async Task ManualPotionMoveIsAllowedAndConfirmed()
    {
        using var game=new Replay();game.Set("_ready",true);
        game.Receive(ServerCommand.AddInventory,new ServerAddInventoryMessage{Slot=1,Name="Komadium",Quantity=3,Sprite=47});
        var potion=Assert.Single(game.Session.Inventory());
        game.OnSent=packet=>
        {
            Assert.Equal(ClientCommand.ChangeSlot,packet.Command);
            game.Receive(ServerCommand.RemoveInventory,new ServerRemoveInventoryMessage{Slot=1});
            game.Receive(ServerCommand.AddInventory,new ServerAddInventoryMessage{Slot=9,Name="Komadium",Quantity=3,Sprite=47});
        };
        await game.Session.ApplyLayout(new(){{9,potion}},default);
        Assert.Equal(9,Assert.Single(game.Session.Inventory()).Slot);Assert.Single(game.Sent);
    }

    [Fact]
    public async Task HaxCleanupWithdrawsAndDropsOnlyApprovedQuantity()
    {
        using var game=new Replay();game.Npc();game.Set("_ready",true);var item=new Item(1,"Emerald",12,15);
        game.Store.SaveSnapshot("Example","Bank",[item],true);var rules=new ItemRules(game.Store);rules.Set(item,ItemAction.Junk);
        game.OnSent=packet=>
        {
            if(packet.Command==ClientCommand.Merchant)game.Add(5,1);
            if(packet.Command==ClientCommand.Drop)game.Receive(ServerCommand.RemoveInventory,new ServerRemoveInventoryMessage{Slot=5});
        };
        var results=new List<MaintenanceResult>();
        await MaintenanceRunner.Run(game.Session,[new("Example","Bank",item with{Quantity=2},ItemAction.Junk)],rules,()=>[],results.Add,default);
        Assert.Equal(new[]{ClientCommand.Merchant,ClientCommand.Pursuit,ClientCommand.Drop,ClientCommand.Merchant,ClientCommand.Pursuit,ClientCommand.Drop},game.Sent.Select(x=>x.Command));
        Assert.Empty(game.Session.Inventory());Assert.Contains(results,x=>x.Result=="2 withdrawn and dropped");
    }
    [Fact]
    public async Task HaxWithdrawalWithoutConfirmationSendsNoRetryOrDrop()
    {
        using var game=new Replay();game.Npc();game.Set("_ready",true);var item=new Item(1,"Emerald",12,15);
        game.Store.SaveSnapshot("Example","Bank",[item],true);var rules=new ItemRules(game.Store);rules.Set(item,ItemAction.Junk);
        await MaintenanceRunner.Run(game.Session,[new("Example","Bank",item,ItemAction.Junk)],rules,()=>[],_=>{},default);
        Assert.Equal(new[]{ClientCommand.Merchant,ClientCommand.Pursuit},game.Sent.Select(x=>x.Command));Assert.Empty(game.Session.Inventory());
    }
    [Fact]
    public async Task CancelledHaxScanCannotLaterRecordAnEmptyBank()
    {
        using var game=new Replay();game.Npc();game.StartBank();game.Sent.Clear();
        game.Store.SaveSnapshot("Example","Bank",[new(1,"Emerald",12,15)],true);
        using var stop=new CancellationTokenSource();var scan=game.Session.ScanNearbyBank(null,stop.Token);stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>scan);
        game.Set("_bankRequested",DateTimeOffset.UtcNow.AddSeconds(-11));game.Call("CompleteSilentBank");
        Assert.Single(game.Session.BankItems());Assert.Null(game.Session.LastBankScan);
    }
    [Fact]
    public async Task HaxTransferRequiresVisibleNpc()
    {
        using var game=new Replay();game.Add(3,2);game.Set("_ready",true);var item=Assert.Single(game.Session.Inventory());
        new ItemRules(game.Store).Set(item,ItemAction.AutoDeposit);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>game.Session.DepositItem(item,2,default));Assert.Empty(game.Sent);
    }
    [Fact]
    public async Task RejectedDropHasOnePacketAndNoRetry()
    {
        using var game=new Replay();game.Add(3,2);game.Set("_ready",true);var item=Assert.Single(game.Session.Inventory());new ItemRules(game.Store).Set(item,ItemAction.Junk);
        Assert.False(await game.Session.DropItem(item,2,default));Assert.Single(game.Sent);Assert.Equal(2,Assert.Single(game.Session.Inventory()).Quantity);
    }
    [Fact]
    public async Task ManualInterventionStopsPendingDrop()
    {
        using var game=new Replay();game.Add(3,2);game.Set("_ready",true);var item=Assert.Single(game.Session.Inventory());new ItemRules(game.Store).Set(item,ItemAction.Junk);
        var action=game.Session.DropItem(item,2,default);game.Set("_manualItemRevision",1L);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>action);Assert.Single(game.Sent);
    }
    [Fact]
    public async Task TicketPopupFollowedByDelayedInventoryDoesNotSendAnotherWithdrawal()
    {
        using var game=new Replay();game.Npc();game.Set("_ready",true);var item=new Item(1,"Amusement Park Ticket",2,15);
        game.Store.SaveSnapshot("Example","Bank",[item],true);new ItemRules(game.Store).Set(item,ItemAction.Junk);
        var requests=0;
        game.OnSent=packet=>
        {
            if(packet.Command!=ClientCommand.Merchant)return;
            var request=(ClientMerchantMessage)ClientMessageFactory.Default.Create(packet)!;
            Assert.Equal((ushort)0x57,request.PursuitId);Assert.Equal(new[]{item.Name,"1"},request.Arguments);
            requests++;
            game.Receive(ServerCommand.ScreenMenu,new ServerScreenMenuMessage{EntityId=42,MenuType=DialogMenuType.TextInputWithArgs,PursuitId=0x57,Prompt=item.Name,Content="How many would you like to withdraw?"});
        };
        var action=game.Session.WithdrawOne(item,default);await Task.Delay(200);
        game.Receive(ServerCommand.AddInventory,new ServerAddInventoryMessage{Slot=5,Name=item.Name,Quantity=1,Sprite=15,IsStackable=true});
        Assert.True(await action);Assert.Equal(1,requests);Assert.Equal(1,Assert.Single(game.Session.Inventory()).Quantity);
    }
    [Fact]
    public async Task ExpectedNativePopupCloseDoesNotCancelWithdrawal()
    {
        using var game=new Replay();game.Npc();game.Set("_ready",true);var item=new Item(1,"Emerald",2,15);
        game.Store.SaveSnapshot("Example","Bank",[item],true);new ItemRules(game.Store).Set(item,ItemAction.Junk);
        game.OnSent=packet=>
        {
            if(packet.Command!=ClientCommand.Pursuit)return;
            game.Native(packet);game.Add(5,1);
        };
        Assert.True(await game.Session.WithdrawOne(item,default));Assert.Equal(2,game.Sent.Count);
    }
    [Theory]
    [InlineData("quantity")]
    [InlineData("other npc")]
    [InlineData("menu choice")]
    public async Task OtherNativeBankActionsStillCancelWithdrawal(string kind)
    {
        using var game=new Replay();game.Npc();game.Set("_ready",true);var item=new Item(1,"Emerald",2,15);
        game.Store.SaveSnapshot("Example","Bank",[item],true);new ItemRules(game.Store).Set(item,ItemAction.Junk);
        game.OnSent=packet=>
        {
            if(packet.Command!=ClientCommand.Pursuit)return;
            var builder=new NetworkPacketBuilder(kind=="quantity"?ClientCommand.Merchant:ClientCommand.Pursuit);
            try
            {
                IClientMessage input=kind=="quantity"
                    ?new ClientMerchantMessage{EntityType=EntityTypeFlags.Creature,EntityId=42,PursuitId=0x57,Arguments=[item.Name,"2"]}
                    :new ClientPursuitMessage{EntityType=EntityTypeFlags.Creature,EntityId=kind=="other npc"?43u:42u,PursuitId=0,StepId=1,ArgsType=kind=="menu choice"?DialogArgsType.MenuChoice:DialogArgsType.None,MenuChoice=1};
                input.Serialize(ref builder);game.Native((ClientPacket)builder.ToPacket());
            }
            finally{builder.Dispose();}
        };
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>game.Session.WithdrawOne(item,default));
        Assert.Contains("Manual item action",error.Message);Assert.Equal(2,game.Sent.Count);
    }
    [Fact]
    public async Task TrackingFailureReportsActualCauseInsteadOfManualInput()
    {
        using var game=new Replay();game.Add(3,2);game.Set("_ready",true);var item=Assert.Single(game.Session.Inventory());new ItemRules(game.Store).Set(item,ItemAction.Junk);
        var action=game.Session.DropItem(item,1,default);
        typeof(GameSession).GetProperty("Error")!.SetValue(game.Session,"Incomplete quantity popup");
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>action);
        Assert.Contains("Incomplete quantity popup",error.Message);
    }
    private sealed class Replay:IDisposable
    {
        public InventoryStore Store=new(":memory:");public GameSession Session;
        private ProxyConnection Connection=new(1,new TcpClient());public List<ClientPacket> Sent=[];public Action<ClientPacket>? OnSent;
        public Replay()
        {
            Session=new(Store);typeof(ProxyConnection).GetProperty("Name")!.SetValue(Connection,"Example");
            foreach(var field in new[]{"_isClientConnected","_isServerConnected"})typeof(ProxyConnection).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Connection,1);
            Connection.PacketQueued+=(_,e)=>{if(e.Packet is ClientPacket packet){Sent.Add(packet);OnSent?.Invoke(packet);}};
            Receive(ServerCommand.UserAppearance,new ServerUserAppearanceMessage{UserId=10});
            Receive(ServerCommand.UserPosition,new ServerUserPositionMessage{X=10,Y=12});
        }
        public void Npc(string name="Banker")=>Receive(ServerCommand.DrawObjects,new ServerDrawObjectsMessage{Entities=[new ServerCreatureEntity{Id=42,X=10,Y=12,CreatureType=CreatureType.Mundane,Name=name}]});
        public void StartBank()
        {
            Receive(ServerCommand.ScreenMenu,new ServerScreenMenuMessage{MenuType=DialogMenuType.Menu,EntityId=42,MenuChoices=[new(){Text="Withdraw Items",PursuitId=69}]});
            Session.Send(new ClientMerchantMessage{EntityId=42,PursuitId=69});
        }
        public void Add(byte slot,uint quantity)=>Receive(ServerCommand.AddInventory,new ServerAddInventoryMessage{Slot=slot,Name="Emerald",Quantity=quantity,Sprite=15});
        public void Receive(ServerCommand command,IServerMessage message)
        {
            var builder=new NetworkPacketBuilder(command);
            try{message.Serialize(ref builder);typeof(GameSession).GetMethod("Observe",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Session,[Connection,builder.ToPacket()]);}
            finally{builder.Dispose();}
        }
        public void Set(string field,object value)=>typeof(GameSession).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Session,value);
        public void Native(ClientPacket packet)
        {
            packet=new ClientPacket((byte)packet.Command,packet.Data);
            var proxy=typeof(GameSession).GetField("_proxy",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Session)!;
            typeof(ProxyServer).GetMethod("OnRecv",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(proxy,[Connection,new NetworkTransferEventArgs(NetworkDirection.Receive,packet,packet)]);
        }
        public void Incoming(ServerPacket packet)
        {
            var proxy=typeof(GameSession).GetField("_proxy",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Session)!;
            typeof(ProxyServer).GetMethod("OnRecv",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(proxy,[Connection,new NetworkTransferEventArgs(NetworkDirection.Receive,packet,packet)]);
        }
        public Task Drain()
        {
            var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var events=(System.Threading.Channels.Channel<Action>)typeof(GameSession).GetField("_events",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Session)!;
            events.Writer.TryWrite(()=>done.SetResult());return done.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        public void Call(string name)=>typeof(GameSession).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Session,[]);
        public void Disconnect()=>typeof(ProxyConnection).GetField("_isServerConnected",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Connection,0);
        public void Dispose(){Session.Dispose();Connection.Dispose();Store.Dispose();}
    }
}
