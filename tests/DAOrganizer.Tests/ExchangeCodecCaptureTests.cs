using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Serialization;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using Xunit;

namespace DAOrganizer.Tests;

public class ExchangeCodecCaptureTests
{
    // Payloads from one local manual single-item trade. Character names and inventories are not fixtures.
    [Fact]
    public void CapturedAddItemDoesNotHaveAQuantityByte()
    {
        byte[] observed=Convert.FromHexString("0100126D531E");
        var parsed=Assert.IsType<ClientExchangeMessage>(ClientMessageFactory.Default.Create(new ClientPacket((byte)ClientCommand.Exchange,observed)));
        Assert.Equal(ExchangeClientActionType.AddItem,parsed.Action);
        Assert.Equal(0x00126D53u,parsed.TargetId);
        Assert.Equal((byte)30,parsed.Slot);
        Assert.Equal((byte)1,parsed.Quantity);
        var builder=new NetworkPacketBuilder(ClientCommand.Exchange);
        try{parsed.Serialize(ref builder);Assert.Equal(observed,builder.ToPacket().Data);}
        finally{builder.Dispose();}
    }

    [Fact]
    public void CapturedOfferAndRecipientInventoryAddDecodeAsDifferentNames()
    {
        var offer=Convert.FromHexString("020101AF6C001541626F6D696E6174696F6E204D61736B2031303025");
        var offered=Assert.IsType<ServerExchangeMessage>(ServerMessageFactory.Default.Create(new ServerPacket((byte)ServerCommand.Exchange,offer)));
        Assert.Equal(ExchangeServerEventType.ItemAdded,offered.Event);
        Assert.Equal(ExchangeParty.Them,offered.Party);
        Assert.Equal((byte)1,offered.ItemIndex);
        Assert.Equal("Abomination Mask 100%",offered.ItemName);
        var offerBuilder=new NetworkPacketBuilder(ServerCommand.Exchange);
        try{offered.Serialize(ref offerBuilder);Assert.Equal(offer,offerBuilder.ToPacket().Data);}
        finally{offerBuilder.Dispose();}

        var inventory=Convert.FromHexString("02AF6C001041626F6D696E6174696F6E204D61736B0000000100000003E8000003E8");
        var added=Assert.IsType<ServerAddInventoryMessage>(ServerMessageFactory.Default.Create(new ServerPacket((byte)ServerCommand.AddInventory,inventory)));
        Assert.Equal((byte)2,added.Slot);
        Assert.Equal("Abomination Mask",added.Name);
        Assert.Equal(1u,added.Quantity);
        Assert.False(added.IsStackable);
        Assert.Equal(1000u,added.MaxDurability);
        Assert.Equal(1000u,added.Durability);
        var inventoryBuilder=new NetworkPacketBuilder(ServerCommand.AddInventory);
        try{added.Serialize(ref inventoryBuilder);Assert.Equal(inventory,inventoryBuilder.ToPacket().Data);}
        finally{inventoryBuilder.Dispose();}
    }

    [Fact]
    public void CapturedAcceptedEventHasPartyAndMessageButNoFinalityProof()
    {
        var payload=Convert.FromHexString("05000E596F752065786368616E6765642E");
        var message=Assert.IsType<ServerExchangeMessage>(ServerMessageFactory.Default.Create(new ServerPacket((byte)ServerCommand.Exchange,payload)));
        Assert.Equal(ExchangeServerEventType.Accepted,message.Event);
        Assert.Equal(ExchangeParty.You,message.Party);
        Assert.Equal("You exchanged.",message.Message);
    }

    [Fact]
    public void MerchantReceiverCompletedSubtypeWithoutMessageCanRoundTrip()
    {
        // DAMerchant's exchange-event5 fixture records this optional successful result.
        var observed=Convert.FromHexString("0502");
        var parsed=Assert.IsType<ServerExchangeMessage>(ServerMessageFactory.Default.Create(
            new ServerPacket((byte)ServerCommand.Exchange,observed)));
        Assert.Equal(ExchangeServerEventType.Accepted,parsed.Event);
        Assert.Equal(ExchangeParty.Completed,parsed.Party);
        var builder=new NetworkPacketBuilder(ServerCommand.Exchange);
        try{parsed.Serialize(ref builder);Assert.Equal(observed,builder.ToPacket().Data);}
        finally{builder.Dispose();}
    }

    [Theory]
    [InlineData("0000126D53",ExchangeClientActionType.BeginExchange)]
    [InlineData("0500126D53",ExchangeClientActionType.Accept)]
    public void CapturedOpenAndAcceptPreservePartnerId(string hex,ExchangeClientActionType action)
    {
        var observed=Convert.FromHexString(hex);
        var parsed=Assert.IsType<ClientExchangeMessage>(ClientMessageFactory.Default.Create(new ClientPacket((byte)ClientCommand.Exchange,observed)));
        Assert.Equal(action,parsed.Action);
        Assert.Equal(0x00126D53u,parsed.TargetId);
        var builder=new NetworkPacketBuilder(ClientCommand.Exchange);
        try{parsed.Serialize(ref builder);Assert.Equal(observed,builder.ToPacket().Data);}
        finally{builder.Dispose();}
    }

    [Fact]
    public void CapturedInventoryRemovalUsesFirstByteAsSlotWithUnresolvedTrailingBytes()
    {
        var observed=Convert.FromHexString("1E0C00CD");
        var removed=Assert.IsType<ServerRemoveInventoryMessage>(ServerMessageFactory.Default.Create(new ServerPacket((byte)ServerCommand.RemoveInventory,observed)));
        Assert.Equal((byte)30,removed.Slot);
        Assert.Equal(4,observed.Length);
    }

    [Fact]
    public void CapturedPartialStackActionContainsSlotAndOneByteQuantity()
    {
        var observed=Convert.FromHexString("0200126A5D2602");
        var parsed=Assert.IsType<ClientExchangeMessage>(ClientMessageFactory.Default.Create(new ClientPacket((byte)ClientCommand.Exchange,observed)));
        Assert.Equal(ExchangeClientActionType.AddStackableItem,parsed.Action);
        Assert.Equal(0x00126A5Du,parsed.TargetId);
        Assert.Equal((byte)38,parsed.Slot);
        Assert.Equal((byte)2,parsed.Quantity);
        var builder=new NetworkPacketBuilder(ClientCommand.Exchange);
        try{parsed.Serialize(ref builder);Assert.Equal(observed,builder.ToPacket().Data);}
        finally{builder.Dispose();}
    }

    [Fact]
    public void CapturedStackPromptPreservesUnexplainedTrailingByte()
    {
        var observed=Convert.FromHexString("012600");
        var parsed=Assert.IsType<ServerExchangeMessage>(ServerMessageFactory.Default.Create(new ServerPacket((byte)ServerCommand.Exchange,observed)));
        Assert.Equal(ExchangeServerEventType.QuantityPrompt,parsed.Event);
        Assert.Equal((byte)38,parsed.Slot);
        Assert.Equal(new byte[]{0},parsed.TrailingData);
        var builder=new NetworkPacketBuilder(ServerCommand.Exchange);
        try{parsed.Serialize(ref builder);Assert.Equal(observed,builder.ToPacket().Data);}
        finally{builder.Dispose();}
    }

    [Fact]
    public void CapturedRecipientStackAddPreservesUnexplainedTrailingByte()
    {
        var observed=Convert.FromHexString("038CA3000D517565656E27732043686573740000000201000000000000000000");
        var parsed=Assert.IsType<ServerAddInventoryMessage>(ServerMessageFactory.Default.Create(new ServerPacket((byte)ServerCommand.AddInventory,observed)));
        Assert.Equal("Queen's Chest",parsed.Name);
        Assert.Equal(2u,parsed.Quantity);
        Assert.True(parsed.IsStackable);
        Assert.Equal(new byte[]{0},parsed.TrailingData);
        var builder=new NetworkPacketBuilder(ServerCommand.AddInventory);
        try{parsed.Serialize(ref builder);Assert.Equal(observed,builder.ToPacket().Data);}
        finally{builder.Dispose();}
    }

    [Fact]
    public void CapturedSenderStackUpdateAndOfferExposeQuantityTwo()
    {
        var inventory=Convert.FromHexString("268CA3000D517565656E277320436865737400000017010000000000000000");
        var updated=Assert.IsType<ServerAddInventoryMessage>(ServerMessageFactory.Default.Create(new ServerPacket((byte)ServerCommand.AddInventory,inventory)));
        Assert.Equal((byte)38,updated.Slot);
        Assert.Equal(23u,updated.Quantity);
        Assert.True(updated.IsStackable);
        Assert.Empty(updated.TrailingData);

        var offer=Convert.FromHexString("0200018CA30010517565656E2773204368657374283229");
        var offered=Assert.IsType<ServerExchangeMessage>(ServerMessageFactory.Default.Create(new ServerPacket((byte)ServerCommand.Exchange,offer)));
        Assert.Equal(ExchangeParty.You,offered.Party);
        Assert.Equal((byte)1,offered.ItemIndex);
        Assert.Equal("Queen's Chest(2)",offered.ItemName);
        Assert.Empty(offered.TrailingData);
    }
}
