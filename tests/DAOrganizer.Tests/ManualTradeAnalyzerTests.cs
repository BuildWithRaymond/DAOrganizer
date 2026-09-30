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
    public void PartialStackWithBothAcceptanceNotificationsAndDeliveryIsVerified()
    {
        var (sender,recipient)=PartialStack();
        var result=ManualTradeAnalyzer.Analyze(sender,recipient);
        Assert.True(result.Verified);
        Assert.Equal(ManualTradeOutcome.Verified,result.Outcome);
        Assert.True(result.Evidence?.EarlyEventFiveObserved);
        Assert.True(result.Evidence?.SuccessfulFinalSignalOnBothSides);
    }

    [Fact]
    public void OptionalCompletedSubtypeAfterDeliveryKeepsExactTradeVerified()
    {
        var (sender,recipient)=PartialStack();
        var completed=new TradePacketTrace(sender.FinishedAt.AddMilliseconds(-1),"Server",0x42,"0502",null);
        sender=sender with{Packets=[..sender.Packets,completed]};
        recipient=recipient with{Packets=[..recipient.Packets,completed]};
        Assert.True(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
    }

    [Fact]
    public void MatchingPartialStackOfferCanBeVerifiedBeforeAcceptance()
    {
        var (sender,recipient)=PartialStack();
        sender=sender with{Packets=sender.Packets.Take(7).ToArray(),FinishedAt=sender.Packets[6].ObservedAt};
        recipient=recipient with{Packets=recipient.Packets.Take(2).ToArray(),
            AfterInventory=[],FinishedAt=recipient.Packets[1].ObservedAt};
        var offer=ManualTradeAnalyzer.AnalyzeOffer(sender,recipient);
        Assert.True(offer.Verified);
        Assert.Equal(2,offer.Quantity);
        Assert.Equal(38,offer.Item?.Slot);
        var wrong=recipient with{Packets=[recipient.Packets[0]]};
        Assert.False(ManualTradeAnalyzer.AnalyzeOffer(sender,wrong).Verified);
    }

    [Fact]
    public void OneCarriedUnitStillNeedsTheObservedStackQuantityPrompt()
    {
        var (sender,recipient)=PartialStack();
        sender=sender with{BeforeInventory=[sender.BeforeInventory[0] with{Quantity=1}],
            AfterInventory=[sender.BeforeInventory[0] with{Quantity=1}],
            Packets=sender.Packets.Take(4).ToArray()};
        recipient=recipient with{AfterInventory=recipient.BeforeInventory,Packets=recipient.Packets.Take(1).ToArray()};
        Assert.Equal(ExchangeAddItemResponse.QuantityPrompt,
            ManualTradeAnalyzer.ObserveAddItemResponse(sender,recipient,38));
        Assert.Equal(ExchangeAddItemResponse.Waiting,
            ManualTradeAnalyzer.ObserveAddItemResponse(sender with{Packets=sender.Packets.Take(3).ToArray()},recipient,38));
        Assert.Equal(ExchangeAddItemResponse.Invalid,
            ManualTradeAnalyzer.ObserveAddItemResponse(sender,recipient,39));
        Assert.NotNull(ManualTradeAnalyzer.UncommittedQuantityPromptItem(sender,recipient,38));
        Assert.Null(ManualTradeAnalyzer.UncommittedQuantityPromptItem(
            sender with{AfterInventory=[]},recipient,38));
    }

    [Fact]
    public void OneUnitStackOfferWithQuantityResponseIsVerified()
    {
        var (sender,recipient)=PartialStack();
        sender=ReplaceOffer(sender,"Chest(1)");recipient=ReplaceOffer(recipient,"Chest(1)");
        var quantity=Client(new ClientExchangeMessage{Action=ExchangeClientActionType.AddStackableItem,
            TargetId=202,Slot=38,Quantity=1}) with{ObservedAt=sender.Packets[4].ObservedAt};
        var remove=Server(new ServerRemoveInventoryMessage{Slot=38}) with{ObservedAt=sender.Packets[5].ObservedAt};
        sender=sender with{BeforeInventory=[sender.BeforeInventory[0] with{Quantity=1}],AfterInventory=[],
            Packets=[..sender.Packets.Take(4),quantity,remove,sender.Packets[6]]};
        recipient=recipient with{AfterInventory=recipient.BeforeInventory,Packets=recipient.Packets.Take(2).ToArray()};
        var offer=ManualTradeAnalyzer.AnalyzeOffer(sender,recipient);
        Assert.True(offer.Verified);
        Assert.Equal(1,offer.Quantity);
    }

    [Fact]
    public void OfferRejectsEarlyRecipientGainExtraGoldAndChangedQuantity()
    {
        var (sender,recipient)=PartialStack();
        sender=sender with{Packets=sender.Packets.Take(7).ToArray()};
        recipient=recipient with{Packets=recipient.Packets.Take(2).ToArray(),AfterInventory=[]};
        Assert.True(ManualTradeAnalyzer.AnalyzeOffer(sender,recipient).Verified);
        Assert.False(ManualTradeAnalyzer.AnalyzeOffer(sender,recipient with{AfterInventory=
            [new Item(3,"Chest",2,77,0,IsStackable:true)]}).Verified);
        Assert.False(ManualTradeAnalyzer.AnalyzeOffer(sender,recipient with{AfterGold=1}).Verified);
        var changed=sender with{Packets=sender.Packets.Select(x=>x.Direction=="Client"&&x.Opcode==0x4A&&
            x.PayloadHex.StartsWith("02",StringComparison.Ordinal)?
            Client(new ClientExchangeMessage{Action=ExchangeClientActionType.AddStackableItem,
                TargetId=202,Slot=38,Quantity=3}) with{ObservedAt=x.ObservedAt}:x).ToArray()};
        Assert.False(ManualTradeAnalyzer.AnalyzeOffer(changed,recipient).Verified);
    }

    [Fact]
    public void LegacyCaptureWithoutOpcodeTimelineRemainsInconclusive()
    {
        var (sender,recipient)=PartialStack();
        Assert.False(ManualTradeAnalyzer.Analyze(sender with {FilteredTimeline=null},recipient).Verified);
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient with {FilteredTimeline=null}).Verified);
    }

    [Fact]
    public void AcceptanceEventsWithoutRecipientInventoryGainAreInconclusive()
    {
        var (sender,recipient)=PartialStack();
        recipient=recipient with {AfterInventory=[]};
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
        Assert.Equal(ManualTradeOutcome.Inconclusive,ManualTradeAnalyzer.Analyze(sender,recipient).Outcome);
    }

    [Fact]
    public void EarlyEventFiveWithoutPostAcceptFinalSignalIsInconclusive()
    {
        var (sender,recipient)=PartialStack();
        sender=sender with {Packets=sender.Packets.Where(x=>!(x.Direction=="Server"&&x.Opcode==0x42&&
            x.PayloadHex.StartsWith("0501",StringComparison.Ordinal)&&x.ObservedAt>recipient.Packets
                .Single(y=>y.Direction=="Client").ObservedAt)).ToArray()};
        var result=ManualTradeAnalyzer.Analyze(sender,recipient);
        Assert.False(result.Verified);Assert.True(result.Evidence?.EarlyEventFiveObserved);
    }

    [Fact]
    public void MissingRecipientPostAcceptNotificationPreventsVerification()
    {
        var (sender,recipient)=PartialStack();
        recipient=recipient with {Packets=recipient.Packets.Where(x=>!(x.Direction=="Server"&&x.Opcode==0x42&&
            x.PayloadHex.StartsWith("0500",StringComparison.Ordinal)&&x.ObservedAt>recipient.Packets
                .Single(y=>y.Direction=="Client").ObservedAt)).ToArray()};
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
    }

    [Fact]
    public void DuplicateAcceptanceNotificationPreventsVerification()
    {
        var (sender,recipient)=PartialStack();
        sender=sender with {Packets=[..sender.Packets,sender.Packets[^1] with {ObservedAt=sender.FinishedAt.AddMilliseconds(-1)}]};
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
    }

    [Fact]
    public void UnknownCloseSubtypeStaysAResearchLead()
    {
        var (sender,recipient)=PartialStack();
        sender=sender with {FilteredTimeline=[new(sender.FinishedAt.AddMilliseconds(-1),"Server",0x7A)]};
        var result=ManualTradeAnalyzer.Analyze(sender,recipient);
        Assert.False(result.Verified);Assert.True(result.Evidence?.UnknownCloseSignalObserved);
    }

    [Fact]
    public void MotionPacketsAfterBothAcceptsDoNotInvalidateExactDelivery()
    {
        var (sender,recipient)=PartialStack();
        sender=sender with {FilteredTimeline=[new(sender.FinishedAt.AddMilliseconds(-1),"Server",0x1A)]};
        recipient=recipient with {FilteredTimeline=[new(recipient.FinishedAt.AddMilliseconds(-1),"Server",0x1A)]};
        var result=ManualTradeAnalyzer.Analyze(sender,recipient);
        Assert.True(result.Verified);
        Assert.False(result.Evidence?.UnknownCloseSignalObserved);
    }

    [Fact]
    public void RuntimeIdReuseAndDuplicateEventsAreInconclusive()
    {
        var (sender,recipient)=PartialStack();
        sender=sender with {Packets=[sender.Packets[0],sender.Packets[0],..sender.Packets.Skip(1)]};
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
    }

    [Fact]
    public void OutOfOrderTimingIsInconclusive()
    {
        var (sender,recipient)=PartialStack();
        sender=sender with {Packets=sender.Packets.Select((x,i)=>i==1?x with {ObservedAt=sender.StartedAt.AddSeconds(-1)}:x).ToArray()};
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
    }

    [Fact]
    public void DisconnectOrTruncationIsInconclusive()
    {
        var (sender,recipient)=PartialStack();
        recipient=recipient with {FilteredTimeline=[new(recipient.FinishedAt,"Server",0x4C)]};
        recipient=recipient with {Packets=recipient.Packets.Where(x=>x.Direction!="Client").ToArray()};
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient).Verified);
        Assert.False(ManualTradeAnalyzer.Analyze(sender,recipient with {Truncated=true}).Verified);
    }

    [Fact]
    public void ExplicitItemSpecificDenialIsSeparateFromAmbiguousCancellation()
    {
        var (sender,recipient)=PartialStack();
        var denial=Server(new ServerExchangeMessage{Event=ExchangeServerEventType.Cancelled,Party=ExchangeParty.You,
            Message="You cannot exchange that item."}) with {ObservedAt=sender.StartedAt.AddSeconds(4)};
        sender=sender with {AfterInventory=sender.BeforeInventory,
            Packets=[..sender.Packets.Take(3),denial]};
        recipient=recipient with {AfterInventory=recipient.BeforeInventory,Packets=[recipient.Packets[0]]};
        var result=ManualTradeAnalyzer.Analyze(sender,recipient);
        Assert.Equal(ManualTradeOutcome.ExplicitItemDenied,result.Outcome);
        Assert.True(result.ExplicitItemDenialObserved);Assert.False(result.Verified);

        sender=sender with {Packets=[..sender.Packets.Take(3),denial with {PayloadHex=Server(new ServerExchangeMessage
            {Event=ExchangeServerEventType.Cancelled,Party=ExchangeParty.You,Message="Exchange ended."}).PayloadHex}]};
        Assert.Equal(ManualTradeOutcome.Inconclusive,ManualTradeAnalyzer.Analyze(sender,recipient).Outcome);
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
    public void PartialStackSuffixAllowsWhitespaceButNotDifferentQuantity()
    {
        var (sender,recipient)=PartialStack();
        sender=ReplaceOffer(sender,"Chest (2)");recipient=ReplaceOffer(recipient,"Chest (2)");
        Assert.Equal(ManualTradeOutcome.Verified,ManualTradeAnalyzer.Analyze(sender,recipient).Outcome);
    }

    [Fact]
    public void SameKeyDurabilityChangeFailsExactConservation()
    {
        var (sender,recipient)=PartialStack();
        recipient=recipient with {AfterInventory=[recipient.AfterInventory[0] with {Durability=1}]};
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
        senderPackets=senderPackets.Select((x,i)=>x with {ObservedAt=now.AddSeconds(new[]{1,2,3,4,5,6,7,8,9,12}[i])}).ToArray();
        recipientPackets=recipientPackets.Select((x,i)=>x with {ObservedAt=now.AddSeconds(new[]{1,7,9,10,11,12}[i])}).ToArray();
        return (
            new ManualTradeResult("operation","Alpha",1,now,now.AddSeconds(13),[item],[item with {Quantity=3}],0,0,senderPackets,false,[]),
            new ManualTradeResult("operation","Beta",2,now,now.AddSeconds(13),[],[new Item(3,"Chest",2,77,0,IsStackable:true)],0,0,recipientPackets,false,[]));
    }

    private static ManualTradeResult ReplaceOffer(ManualTradeResult result,string name)=>result with
    {
        Packets=result.Packets.Select(x=>x.Direction=="Server"&&x.Opcode==0x42&&x.PayloadHex.StartsWith("02",StringComparison.Ordinal)?
            Server(new ServerExchangeMessage{Event=ExchangeServerEventType.ItemAdded,
                Party=x.PayloadHex.StartsWith("0200",StringComparison.Ordinal)?ExchangeParty.You:ExchangeParty.Them,
                ItemIndex=1,ItemSprite=77,ItemName=name}) with {ObservedAt=x.ObservedAt}:x).ToArray()
    };

    private static TradePacketTrace Client(ClientExchangeMessage message)
    {
        var builder=new NetworkPacketBuilder(ClientCommand.Exchange);
        try{message.Serialize(ref builder);return new(DateTimeOffset.UtcNow,"Client",0x4A,Convert.ToHexString(builder.ToPacket().Data),null);}
        finally{builder.Dispose();}
    }

    internal static TradePacketTrace Server(ServerExchangeMessage message)
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

    private static TradePacketTrace Server(ServerRemoveInventoryMessage message)
    {
        var builder=new NetworkPacketBuilder(ServerCommand.RemoveInventory);
        try{message.Serialize(ref builder);return new(DateTimeOffset.UtcNow,"Server",0x10,Convert.ToHexString(builder.ToPacket().Data),null);}
        finally{builder.Dispose();}
    }
}
