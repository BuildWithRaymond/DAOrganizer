using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Serialization;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using DAOrganizer.Core;
using DAOrganizer.Game;
using Xunit;

namespace DAOrganizer.Tests;

public class ManualTradeAnalyzerTests
{
    [Fact]
    public void PartialStackRequiresBothAcceptsAndExactTwoPartyInventoryChange()
    {
        var (sender,recipient)=PartialStack();
        var result=ManualTradeAnalyzer.Analyze(sender,recipient);
        Assert.True(result.Verified,result.Reason);
        Assert.Equal("Alpha",result.Sender);
        Assert.Equal("Beta",result.Recipient);
        Assert.Equal("Chest",result.Item?.Name);
        Assert.Equal(2,result.Quantity);
    }

    [Fact]
    public void AcceptanceEventsWithoutRecipientInventoryGainAreInconclusive()
    {
        var (sender,recipient)=PartialStack();
        recipient=recipient with {AfterInventory=[]};
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
    }

    [Fact]
    public void ExtraOfferOrGoldPreventsPositiveEvidence()
    {
        var (sender,recipient)=PartialStack();
        sender=sender with {Packets=[..sender.Packets,Server(new ServerExchangeMessage
        {
            Event=ExchangeServerEventType.GoldAdded,Party=ExchangeParty.You,GoldAmount=1
        })]};
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
    }

    [Fact]
    public void WrongPartnerIdentityPreventsPositiveEvidence()
    {
        var (sender,recipient)=PartialStack();
        sender=sender with {Packets=[Server(new ServerExchangeMessage
        {
            Event=ExchangeServerEventType.Started,TargetId=202,TargetName="Someone Else"
        }),..sender.Packets.Skip(1)]};
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
    }

    [Fact]
    public void MissingRecipientAcceptPreventsPositiveEvidenceEvenWithInventoriesChanged()
    {
        var (sender,recipient)=PartialStack();
        recipient=recipient with {Packets=recipient.Packets.Where(x=>x.Direction!="Client").ToArray()};
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
    }

    [Fact]
    public void UnrelatedSourceInventoryPacketCannotStandInForTransferEvidence()
    {
        var (sender,recipient)=PartialStack();
        sender=sender with {Packets=[..sender.Packets.Where(x=>x.Opcode!=0x0F),
            Server(new ServerAddInventoryMessage{Slot=9,Sprite=8,Name="Other",Quantity=1})]};
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
    }

    [Fact]
    public void WrongOfferedStackQuantityPreventsPositiveEvidence()
    {
        var (sender,recipient)=PartialStack();
        recipient=recipient with {Packets=recipient.Packets.Select(x=>x.Direction=="Server"&&x.Opcode==0x42&&
            x.PayloadHex.StartsWith("0201",StringComparison.Ordinal)?Server(new ServerExchangeMessage
            {
                Event=ExchangeServerEventType.ItemAdded,Party=ExchangeParty.Them,ItemIndex=1,ItemSprite=77,ItemName="Chest(3)"
            }):x).ToArray()};
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
    }

    [Fact]
    public void AddItemWithUnexpectedTrailingQuantityByteIsInconclusive()
    {
        var (sender,recipient)=PartialStack();
        sender=sender with {Packets=sender.Packets.Select(x=>x.Direction=="Client"&&x.Opcode==0x4A&&
            x.PayloadHex.StartsWith("01",StringComparison.Ordinal)?x with {PayloadHex=x.PayloadHex+"01"}:x).ToArray()};
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
    }

    [Fact]
    public void RecipientAddingAnItemPreventsSingleItemPositiveEvidence()
    {
        var (sender,recipient)=PartialStack();
        recipient=recipient with {Packets=[..recipient.Packets,
            Client(new ClientExchangeMessage{Action=ExchangeClientActionType.AddItem,TargetId=101,Slot=7})]};
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
    }

    internal static (ManualTradeResult Sender,ManualTradeResult Recipient) PartialStack()
    {
        var now=DateTimeOffset.UtcNow;
        var item=new Item(38,"Chest",5,77,0,IsStackable:true);
        var senderPackets=new TradePacketTrace[]
        {
            Server(new ServerExchangeMessage{Event=ExchangeServerEventType.Started,TargetId=202,TargetName="Beta"}),
            Client(new ClientExchangeMessage{Action=ExchangeClientActionType.BeginExchange,TargetId=202}),
            Client(new ClientExchangeMessage{Action=ExchangeClientActionType.AddItem,TargetId=202,Slot=38}),
            Server(new ServerExchangeMessage{Event=ExchangeServerEventType.QuantityPrompt,Slot=38}),
            Client(new ClientExchangeMessage{Action=ExchangeClientActionType.AddStackableItem,TargetId=202,Slot=38,Quantity=2}),
            Server(new ServerAddInventoryMessage{Slot=38,Sprite=77,Name="Chest",Quantity=3,IsStackable=true}),
            Server(new ServerExchangeMessage{Event=ExchangeServerEventType.ItemAdded,Party=ExchangeParty.You,ItemIndex=1,ItemSprite=77,ItemName="Chest(2)"}),
            Client(new ClientExchangeMessage{Action=ExchangeClientActionType.Accept,TargetId=202}),
            Server(new ServerExchangeMessage{Event=ExchangeServerEventType.Accepted,Party=ExchangeParty.You,Message="You exchanged."}),
            Server(new ServerExchangeMessage{Event=ExchangeServerEventType.Accepted,Party=ExchangeParty.Them,Message="Beta exchanged."})
        };
        var recipientPackets=new TradePacketTrace[]
        {
            Server(new ServerExchangeMessage{Event=ExchangeServerEventType.Started,TargetId=101,TargetName="Alpha"}),
            Server(new ServerExchangeMessage{Event=ExchangeServerEventType.ItemAdded,Party=ExchangeParty.Them,ItemIndex=1,ItemSprite=77,ItemName="Chest(2)"}),
            Server(new ServerExchangeMessage{Event=ExchangeServerEventType.Accepted,Party=ExchangeParty.Them,Message="Alpha exchanged."}),
            Client(new ClientExchangeMessage{Action=ExchangeClientActionType.Accept,TargetId=101}),
            Server(new ServerAddInventoryMessage{Slot=3,Sprite=77,Name="Chest",Quantity=2,IsStackable=true}),
            Server(new ServerExchangeMessage{Event=ExchangeServerEventType.Accepted,Party=ExchangeParty.You,Message="You exchanged."})
        };
        return (
            new ManualTradeResult("operation","Alpha",1,now,now.AddSeconds(10),[item],[item with {Quantity=3}],0,0,senderPackets,false),
            new ManualTradeResult("operation","Beta",2,now,now.AddSeconds(10),[],[new Item(3,"Chest",2,77,0,IsStackable:true)],0,0,recipientPackets,false));
    }

    private static TradePacketTrace Client(ClientExchangeMessage message)
    {
        var builder=new NetworkPacketBuilder(ClientCommand.Exchange);
        try{message.Serialize(ref builder);return new(DateTimeOffset.UtcNow,"Client",0x4A,Convert.ToHexString(builder.ToPacket().Data),null);}
        finally{builder.Dispose();}
    }

    private static TradePacketTrace Server(ServerExchangeMessage message)
    {
        var builder=new NetworkPacketBuilder(ServerCommand.Exchange);
        try{message.Serialize(ref builder);return new(DateTimeOffset.UtcNow,"Server",0x42,Convert.ToHexString(builder.ToPacket().Data),null);}
        finally{builder.Dispose();}
    }

    private static TradePacketTrace Server(ServerAddInventoryMessage message)
    {
        var builder=new NetworkPacketBuilder(ServerCommand.AddInventory);
        try{message.Serialize(ref builder);return new(DateTimeOffset.UtcNow,"Server",0x0F,Convert.ToHexString(builder.ToPacket().Data),null);}
        finally{builder.Dispose();}
    }
}
