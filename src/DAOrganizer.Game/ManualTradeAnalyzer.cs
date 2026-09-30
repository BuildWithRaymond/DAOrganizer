using System.Text.RegularExpressions;
using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using DAOrganizer.Core;

namespace DAOrganizer.Game;

public sealed record ManualTradeAnalysis(bool Verified,string Reason,string? Sender=null,string? Recipient=null,
    Item? Item=null,long Quantity=0);

// Positive local item evidence only. This is not a live exchange coordinator or final-close detector.
public static class ManualTradeAnalyzer
{
    public static ManualTradeAnalysis Analyze(ManualTradeResult first,ManualTradeResult second)
    {
        static ManualTradeAnalysis Uncertain(string reason)=>new(false,reason);
        if(first.OperationId!=second.OperationId||first.ProcessId==second.ProcessId||
           first.Character.Equals(second.Character,StringComparison.OrdinalIgnoreCase)||first.Truncated||second.Truncated)
            return Uncertain("Capture sessions or packet bounds are invalid.");
        if(first.BeforeGold!=first.AfterGold||second.BeforeGold!=second.AfterGold)
            return Uncertain("Gold changed during the exchange.");

        Side a,b;
        try{a=Decode(first);b=Decode(second);}
        catch{return Uncertain("Malformed or unsupported trade packet.");}
        if(a.Invalid||b.Invalid)return Uncertain("Extra trade action, offer, gold, cancellation, or inventory update.");
        var sender=a.Client.Count(x=>x.Action==ExchangeClientActionType.BeginExchange)==1&&
                   b.Client.All(x=>x.Action!=ExchangeClientActionType.BeginExchange)?a:
                   b.Client.Count(x=>x.Action==ExchangeClientActionType.BeginExchange)==1&&
                   a.Client.All(x=>x.Action!=ExchangeClientActionType.BeginExchange)?b:null;
        if(sender==null)return Uncertain("Exactly one exchange initiator is required.");
        var recipient=ReferenceEquals(sender,a)?b:a;
        if(!ValidParties(sender,recipient))return Uncertain("Partner identity, accepts, or offer is incomplete.");

        var selected=sender.Client.Where(x=>x.Action==ExchangeClientActionType.AddItem).ToArray();
        var stack=sender.Client.Where(x=>x.Action==ExchangeClientActionType.AddStackableItem).ToArray();
        var senderActions=sender.Client.Select(x=>x.Action);
        var expectedActions=stack.Length==1?
            new[]{ExchangeClientActionType.BeginExchange,ExchangeClientActionType.AddItem,
                ExchangeClientActionType.AddStackableItem,ExchangeClientActionType.Accept}:
            new[]{ExchangeClientActionType.BeginExchange,ExchangeClientActionType.AddItem,
                ExchangeClientActionType.Accept};
        if(!senderActions.SequenceEqual(expectedActions)||
           !recipient.Client.Select(x=>x.Action).SequenceEqual([ExchangeClientActionType.Accept]))
            return Uncertain("Unexpected trade action order or recipient offer.");
        if(selected.Length!=1||stack.Length>1||selected[0].Slot is not byte slot||
           stack.Any(x=>x.Slot!=slot)||stack.Any(x=>x.Quantity is null or 0))
            return Uncertain("Source slot or stack quantity is ambiguous.");
        if(stack.Length==1&&sender.Server.Count(x=>x.Event==ExchangeServerEventType.QuantityPrompt&&x.Slot==slot)!=1||
           stack.Length==0&&sender.Server.Any(x=>x.Event==ExchangeServerEventType.QuantityPrompt)||
           recipient.Server.Any(x=>x.Event==ExchangeServerEventType.QuantityPrompt))
            return Uncertain("Stack quantity prompt was not observed.");
        var quantity=stack.Length==1?stack[0].Quantity!.Value:1;
        var item=sender.Capture.BeforeInventory.SingleOrDefault(x=>x.Slot==slot);
        if(item==null||item.Quantity<quantity)return Uncertain("Source slot was not present before trade.");
        var key=ItemGroups.Key(item);
        if(!InventoryChange(sender.Capture,key,-quantity)||!InventoryChange(recipient.Capture,key,quantity))
            return Uncertain("Before/after inventories do not conserve the offered quantity.");
        if(!OtherItemsStable(sender.Capture,key)||!OtherItemsStable(recipient.Capture,key))
            return Uncertain("Unrelated inventory changed.");

        var offeredBySender=sender.Server.SingleOrDefault(x=>x.Event==ExchangeServerEventType.ItemAdded);
        var offeredToRecipient=recipient.Server.SingleOrDefault(x=>x.Event==ExchangeServerEventType.ItemAdded);
        if(offeredBySender==null||offeredToRecipient==null||offeredBySender.Party!=ExchangeParty.You||
           offeredToRecipient.Party!=ExchangeParty.Them||offeredBySender.ItemIndex is null or 0||
           offeredBySender.ItemIndex!=offeredToRecipient.ItemIndex||
           offeredBySender.ItemSprite!=item.Sprite||offeredToRecipient.ItemSprite!=item.Sprite||
           (byte?)offeredBySender.ItemColor!=item.Color||(byte?)offeredToRecipient.ItemColor!=item.Color||
           offeredBySender.ItemName!=offeredToRecipient.ItemName||!OfferMatches(item,quantity,offeredBySender.ItemName))
            return Uncertain("The two offered item views do not match the source item and quantity.");

        var sourceAfter=sender.Capture.AfterInventory.SingleOrDefault(x=>x.Slot==slot);
        var sourceUpdated=sender.InventoryAdds.Count==1&&sender.InventoryRemoves.Count==0&&sourceAfter!=null&&
            sender.InventoryAdds[0].Slot==slot&&ItemGroups.Key(ToItem(sender.InventoryAdds[0]))==key&&
            sender.InventoryAdds[0].Quantity==sourceAfter.Quantity;
        var sourceRemoved=sender.InventoryAdds.Count==0&&sender.InventoryRemoves.Count==1&&sourceAfter==null&&
            sender.InventoryRemoves[0].Slot==slot;
        var recipientAdded=recipient.InventoryRemoves.Count==0&&recipient.InventoryAdds.Count==1&&
            ItemGroups.Key(ToItem(recipient.InventoryAdds[0]))==key&&
            recipient.Capture.AfterInventory.Any(y=>y.Slot==recipient.InventoryAdds[0].Slot&&
                ItemGroups.Key(y)==key&&y.Quantity==recipient.InventoryAdds[0].Quantity);
        if((!sourceUpdated&&!sourceRemoved)||!recipientAdded)
            return Uncertain("Inventory change packets were not observed on both sessions.");
        return new(true,"Both sessions, offers, accepts, and inventory deltas agree.",
            sender.Capture.Character,recipient.Capture.Character,item,quantity);
    }

    private static bool ValidParties(Side sender,Side recipient)
    {
        var sStart=sender.Server.SingleOrDefault(x=>x.Event==ExchangeServerEventType.Started);
        var rStart=recipient.Server.SingleOrDefault(x=>x.Event==ExchangeServerEventType.Started);
        if(sStart==null||rStart==null||
           !string.Equals(sStart.TargetName,recipient.Capture.Character,StringComparison.OrdinalIgnoreCase)||
           !string.Equals(rStart.TargetName,sender.Capture.Character,StringComparison.OrdinalIgnoreCase)||
           sStart.TargetId is null or 0||rStart.TargetId is null or 0)return false;
        if(sender.Client.Any(x=>x.TargetId!=sStart.TargetId)||
           recipient.Client.Any(x=>x.TargetId!=rStart.TargetId))return false;
        if(sender.Client.Count(x=>x.Action==ExchangeClientActionType.Accept)!=1||
           recipient.Client.Count(x=>x.Action==ExchangeClientActionType.Accept)!=1)return false;
        foreach(var side in new[]{sender,recipient})
        {
            if(side.Server.Count(x=>x.Event==ExchangeServerEventType.ItemAdded)!=1)return false;
            if(side.Server.Count(x=>x.Event==ExchangeServerEventType.Accepted&&x.Party==ExchangeParty.You)!=1||
               side.Server.Count(x=>x.Event==ExchangeServerEventType.Accepted&&x.Party==ExchangeParty.Them)!=1)
                return false;
        }
        return true;
    }

    private static bool OfferMatches(Item item,int quantity,string? display)
    {
        if(display==null)return false;
        if(quantity>1)return display==$"{item.Name}({quantity})";
        return display==item.Name||display==$"{item.Name}(1)"||
            Regex.IsMatch(display,@"^"+Regex.Escape(item.Name)+@" (?:100|[1-9]?[0-9])%$",RegexOptions.CultureInvariant);
    }

    private static bool InventoryChange(ManualTradeResult capture,string key,long expected)
    {
        static long Total(IEnumerable<Item> items,string wanted)=>items.Where(x=>ItemGroups.Key(x)==wanted).Sum(x=>x.Quantity);
        return Total(capture.AfterInventory,key)-Total(capture.BeforeInventory,key)==expected;
    }

    private static bool OtherItemsStable(ManualTradeResult capture,string transferred)
    {
        static string[] Unrelated(IEnumerable<Item> items,string key)=>items
            .Where(x=>ItemGroups.Key(x)!=key)
            .Select(x=>$"{x.Slot}|{ItemGroups.Key(x)}|{x.Quantity}|{x.Durability}|{x.MaxDurability}")
            .OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        return Unrelated(capture.BeforeInventory,transferred).SequenceEqual(Unrelated(capture.AfterInventory,transferred));
    }

    private static Item ToItem(ServerAddInventoryMessage message)=>new(message.Slot,message.Name,message.Quantity,
        message.Sprite,(byte)message.Color);

    private sealed class Side(ManualTradeResult capture)
    {
        public ManualTradeResult Capture {get;}=capture;
        public List<ClientExchangeMessage> Client {get;}=[];
        public List<ServerExchangeMessage> Server {get;}=[];
        public List<ServerAddInventoryMessage> InventoryAdds {get;}=[];
        public List<ServerRemoveInventoryMessage> InventoryRemoves {get;}=[];
        public bool Invalid {get;set;}
    }

    private static Side Decode(ManualTradeResult capture)
    {
        var side=new Side(capture);
        foreach(var packet in capture.Packets)
        {
            var bytes=Convert.FromHexString(packet.PayloadHex);
            if(packet.Direction=="Client"&&packet.Opcode==0x4A)
            {
                var expectedLength=bytes.Length==0?0:bytes[0] switch
                {
                    0 or 5=>5,
                    1=>6,
                    2=>7,
                    _=>-1
                };
                if(bytes.Length!=expectedLength)throw new FormatException("Unexpected client exchange payload length.");
                if(ClientMessageFactory.Default.Create(new ClientPacket(packet.Opcode,bytes)) is not ClientExchangeMessage client)
                    throw new FormatException("Unexpected client exchange packet.");
                if(client.Action is not (ExchangeClientActionType.BeginExchange or ExchangeClientActionType.AddItem or
                    ExchangeClientActionType.AddStackableItem or ExchangeClientActionType.Accept))side.Invalid=true;
                side.Client.Add(client);
            }
            else if(packet.Direction=="Server"&&packet.Opcode==0x42)
            {
                if(ServerMessageFactory.Default.Create(new ServerPacket(packet.Opcode,bytes)) is not ServerExchangeMessage server)
                    throw new FormatException("Unexpected server exchange packet.");
                if(server.Event is ExchangeServerEventType.GoldAdded or ExchangeServerEventType.Cancelled||
                   !Enum.IsDefined(server.Event))side.Invalid=true;
                side.Server.Add(server);
            }
            else if(packet.Direction=="Server"&&packet.Opcode==0x0F)
            {
                if(ServerMessageFactory.Default.Create(new ServerPacket(packet.Opcode,bytes)) is not ServerAddInventoryMessage added)
                    throw new FormatException("Unexpected inventory add packet.");
                side.InventoryAdds.Add(added);
            }
            else if(packet.Direction=="Server"&&packet.Opcode==0x10)
            {
                if(ServerMessageFactory.Default.Create(new ServerPacket(packet.Opcode,bytes)) is not ServerRemoveInventoryMessage removed)
                    throw new FormatException("Unexpected inventory remove packet.");
                side.InventoryRemoves.Add(removed);
            }
            else side.Invalid=true;
        }
        return side;
    }
}
