using System.Text.RegularExpressions;
using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
using DAOrganizer.Core;

namespace DAOrganizer.Game;

public enum ManualTradeOutcome { Inconclusive, Verified, ExplicitItemDenied }

public sealed record ManualTradeEvidence(bool PartnerIdentitiesMatched=false,bool OffersMatched=false,
    bool BothAccepted=false,bool EarlyEventFiveObserved=false,bool SuccessfulFinalSignalOnBothSides=false,
    bool ExactInventoryConservation=false,bool UnrelatedInventoryStable=false,bool GoldStable=false,
    bool ExplicitItemDenialObserved=false,bool UnknownCloseSignalObserved=false);

public sealed record ManualTradeAnalysis(ManualTradeOutcome Outcome,string Reason,string? Sender=null,
    string? Recipient=null,Item? Item=null,long Quantity=0,ManualTradeEvidence? Evidence=null)
{
    public bool Verified=>Outcome==ManualTradeOutcome.Verified;
    public bool ExplicitItemDenialObserved=>Evidence?.ExplicitItemDenialObserved==true;
}

// Pure review of two bounded captures. It sends nothing and never turns a timeout/cancel into non-tradeable evidence.
public static class ManualTradeAnalyzer
{
    public static ManualTradeAnalysis Analyze(ManualTradeResult first,ManualTradeResult second)
    {
        ManualTradeAnalysis Inconclusive(string reason,ManualTradeEvidence? evidence=null)=>
            new(ManualTradeOutcome.Inconclusive,reason,Evidence:evidence);
        if(first.OperationId!=second.OperationId||first.ProcessId==second.ProcessId||
           first.Character.Equals(second.Character,StringComparison.OrdinalIgnoreCase)||first.Truncated||second.Truncated||
           first.TimelineTruncated||second.TimelineTruncated)
            return Inconclusive("Capture sessions or packet bounds are invalid.");
        if(first.FilteredTimeline is null||second.FilteredTimeline is null)
            return Inconclusive("Both sessions need a complete payload-free opcode timeline.");
        var goldStable=first.BeforeGold==first.AfterGold&&second.BeforeGold==second.AfterGold;
        if(!goldStable)return Inconclusive("Gold changed during the exchange.",new(GoldStable:false));

        Side a,b;
        try{a=Decode(first);b=Decode(second);}
        catch{return Inconclusive("Malformed or unsupported trade packet.",new(GoldStable:true));}
        if(a.Invalid||b.Invalid)return Inconclusive("Extra trade action, offer, gold, cancellation, or inventory update.",new(GoldStable:true));
        var sender=a.Client.Count(x=>x.Message.Action==ExchangeClientActionType.BeginExchange)==1&&
                   b.Client.All(x=>x.Message.Action!=ExchangeClientActionType.BeginExchange)?a:
                   b.Client.Count(x=>x.Message.Action==ExchangeClientActionType.BeginExchange)==1&&
                   a.Client.All(x=>x.Message.Action!=ExchangeClientActionType.BeginExchange)?b:null;
        if(sender==null)return Inconclusive("Exactly one exchange initiator is required.",new(GoldStable:true));
        var recipient=ReferenceEquals(sender,a)?b:a;
        var identities=PartnerIdentitiesMatch(sender,recipient);

        var selected=sender.Client.Where(x=>x.Message.Action==ExchangeClientActionType.AddItem).ToArray();
        if(selected.Length==1&&selected[0].Message.Slot is byte deniedSlot&&identities&&
           ExplicitDenial(sender,out var denialReason)&&InventoriesEqual(first)&&InventoriesEqual(second))
        {
            var deniedItem=sender.Capture.BeforeInventory.SingleOrDefault(x=>x.Slot==deniedSlot);
            if(deniedItem!=null)return new(ManualTradeOutcome.ExplicitItemDenied,denialReason,sender.Capture.Character,
                recipient.Capture.Character,deniedItem,0,new(PartnerIdentitiesMatched:true,GoldStable:true,
                    ExplicitItemDenialObserved:true));
        }
        if(!identities)return Inconclusive("Partner identity is incomplete or reused.",new(GoldStable:true));
        if(sender.Server.Concat(recipient.Server).Any(x=>x.Message.Event==ExchangeServerEventType.Cancelled))
            return Inconclusive("Exchange was cancelled or rejected.",new(true,GoldStable:true));

        var stack=sender.Client.Where(x=>x.Message.Action==ExchangeClientActionType.AddStackableItem).ToArray();
        var senderActions=sender.Client.Select(x=>x.Message.Action);
        var expectedActions=stack.Length==1?
            new[]{ExchangeClientActionType.BeginExchange,ExchangeClientActionType.AddItem,
                ExchangeClientActionType.AddStackableItem,ExchangeClientActionType.Accept}:
            new[]{ExchangeClientActionType.BeginExchange,ExchangeClientActionType.AddItem,
                ExchangeClientActionType.Accept};
        if(!senderActions.SequenceEqual(expectedActions)||
           !recipient.Client.Select(x=>x.Message.Action).SequenceEqual([ExchangeClientActionType.Accept]))
            return Inconclusive("Unexpected, duplicate, or out-of-order trade action.",new(true,GoldStable:true));
        if(selected.Length!=1||stack.Length>1||selected[0].Message.Slot is not byte slot||
           stack.Any(x=>x.Message.Slot!=slot)||stack.Any(x=>x.Message.Quantity is null or 0))
            return Inconclusive("Source slot or stack quantity is ambiguous.",new(true,GoldStable:true));
        if(stack.Length==1&&sender.Server.Count(x=>x.Message.Event==ExchangeServerEventType.QuantityPrompt&&x.Message.Slot==slot)!=1||
           stack.Length==0&&sender.Server.Any(x=>x.Message.Event==ExchangeServerEventType.QuantityPrompt)||
           recipient.Server.Any(x=>x.Message.Event==ExchangeServerEventType.QuantityPrompt))
            return Inconclusive("Stack quantity prompt was not observed.",new(true,GoldStable:true));
        var quantity=stack.Length==1?stack[0].Message.Quantity!.Value:1;
        var item=sender.Capture.BeforeInventory.SingleOrDefault(x=>x.Slot==slot);
        if(item==null||item.Quantity<quantity)return Inconclusive("Source slot was not present before trade.",new(true,GoldStable:true));

        var offersMatched=OffersMatch(sender,recipient,item,quantity);
        var inventoryConserved=ExactInventoryConservation(sender.Capture,recipient.Capture,item,quantity);
        var unrelatedStable=UnrelatedInventoryStable(sender.Capture,item)&&UnrelatedInventoryStable(recipient.Capture,item);
        var inventoryPackets=InventoryPacketsMatch(sender,recipient,item,slot);
        var senderAccept=sender.Client.Single(x=>x.Message.Action==ExchangeClientActionType.Accept).ObservedAt;
        var recipientAccept=recipient.Client.Single(x=>x.Message.Action==ExchangeClientActionType.Accept).ObservedAt;
        var latestAccept=senderAccept>recipientAccept?senderAccept:recipientAccept;
        var bothAccepted=true;
        var earlyFive=sender.Server.Concat(recipient.Server).Any(x=>x.Message.Event==ExchangeServerEventType.Accepted&&x.ObservedAt<latestAccept);
        var unknownClose=UnknownCloseCandidate(sender)||UnknownCloseCandidate(recipient);
        // The controlled two-client capture showed the window close when both acceptances completed.
        // Require each side to see exactly one notice for each party, linked in time to that party's
        // accept, plus the recipient inventory packet after the second accept.
        var finalSignal=AcceptancePair(sender,ExchangeParty.You,senderAccept,ExchangeParty.Them,recipientAccept)&&
            AcceptancePair(recipient,ExchangeParty.Them,senderAccept,ExchangeParty.You,recipientAccept)&&
            sender.Server.Any(x=>x.Message.Event==ExchangeServerEventType.Accepted&&x.ObservedAt>latestAccept)&&
            recipient.Server.Any(x=>x.Message.Event==ExchangeServerEventType.Accepted&&x.ObservedAt>latestAccept)&&
            recipient.Capture.Packets.Any(x=>x.Direction=="Server"&&x.Opcode==0x0F&&x.ObservedAt>=latestAccept);
        var evidence=new ManualTradeEvidence(true,offersMatched,bothAccepted,earlyFive,finalSignal,inventoryConserved,
            unrelatedStable,GoldStable:true,UnknownCloseSignalObserved:unknownClose);
        if(!offersMatched)return Inconclusive("The two offered item views do not match the source item and quantity.",evidence);
        if(!inventoryConserved)return Inconclusive("Before/after inventories do not conserve the exact two-sided change.",evidence);
        if(!unrelatedStable)return Inconclusive("Unrelated inventory changed during the exchange.",evidence);
        if(!inventoryPackets)return Inconclusive("Inventory change packets were not observed on both sessions.",evidence);
        if(!finalSignal||unknownClose)return Inconclusive("Complete two-sided acceptance and final delivery are not proven.",evidence);
        return new(ManualTradeOutcome.Verified,"Two-sided acceptance and recipient delivery verified.",
            sender.Capture.Character,recipient.Capture.Character,item,quantity,evidence);
    }

    private static bool AcceptancePair(Side side,ExchangeParty firstParty,DateTimeOffset firstAccept,
        ExchangeParty secondParty,DateTimeOffset secondAccept)
    {
        var notices=side.Server.Where(x=>x.Message.Event==ExchangeServerEventType.Accepted).ToArray();
        return notices.Length==2&&notices.Count(x=>x.Message.Party==firstParty&&x.ObservedAt>=firstAccept)==1&&
            notices.Count(x=>x.Message.Party==secondParty&&x.ObservedAt>=secondAccept)==1;
    }

    private static bool PartnerIdentitiesMatch(Side sender,Side recipient)
    {
        var starts=sender.Server.Where(x=>x.Message.Event==ExchangeServerEventType.Started).ToArray();
        var recipientStarts=recipient.Server.Where(x=>x.Message.Event==ExchangeServerEventType.Started).ToArray();
        if(starts.Length!=1||recipientStarts.Length!=1)return false;
        var s=starts[0].Message;var r=recipientStarts[0].Message;
        return string.Equals(s.TargetName,recipient.Capture.Character,StringComparison.OrdinalIgnoreCase)&&
            string.Equals(r.TargetName,sender.Capture.Character,StringComparison.OrdinalIgnoreCase)&&
            s.TargetId is not null and not 0&&r.TargetId is not null and not 0&&
            sender.Client.All(x=>x.Message.TargetId==s.TargetId)&&recipient.Client.All(x=>x.Message.TargetId==r.TargetId);
    }

    private static bool OffersMatch(Side sender,Side recipient,Item item,int quantity)
    {
        var offeredBySender=sender.Server.Where(x=>x.Message.Event==ExchangeServerEventType.ItemAdded).Select(x=>x.Message).ToArray();
        var offeredToRecipient=recipient.Server.Where(x=>x.Message.Event==ExchangeServerEventType.ItemAdded).Select(x=>x.Message).ToArray();
        if(offeredBySender.Length!=1||offeredToRecipient.Length!=1)return false;
        var own=offeredBySender[0];var other=offeredToRecipient[0];
        return own.Party==ExchangeParty.You&&other.Party==ExchangeParty.Them&&own.ItemIndex is not null and not 0&&
            own.ItemIndex==other.ItemIndex&&own.ItemSprite==item.Sprite&&other.ItemSprite==item.Sprite&&
            (byte?)own.ItemColor==item.Color&&(byte?)other.ItemColor==item.Color&&own.ItemName==other.ItemName&&
            OfferMatches(item,quantity,own.ItemName);
    }

    private static bool UnknownCloseCandidate(Side side)=>(side.Capture.FilteredTimeline??[])
        .Any(x=>x.ObservedAt>=side.Client.LastOrDefault(y=>y.Message.Action==ExchangeClientActionType.Accept)?.ObservedAt&&
            x.Direction=="Server"&&x.Opcode!=0x08);

    private static bool ExplicitDenial(Side side,out string reason)
    {
        var denials=side.Server.Where(x=>x.Message.Event==ExchangeServerEventType.Cancelled&&
            x.Message.Message is { } message&&Regex.IsMatch(message,
                @"\b(?:cannot|can't|unable|not allowed)\b.*\b(?:item|exchange|trade)\b|\bitem\b.*\b(?:cannot|can't|unable|not allowed)\b",
                RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)).ToArray();
        reason=denials.Length==1?denials[0].Message.Message!:"";
        return denials.Length==1;
    }

    private static bool InventoriesEqual(ManualTradeResult capture)=>InventoryShape(capture.BeforeInventory)
        .SequenceEqual(InventoryShape(capture.AfterInventory));

    private static bool ExactInventoryConservation(ManualTradeResult sender,ManualTradeResult recipient,Item item,long quantity)
    {
        var shape=Shape(item);
        var before=InventoryShape(sender.BeforeInventory).Concat(InventoryShape(recipient.BeforeInventory))
            .GroupBy(x=>x.Key).ToDictionary(x=>x.Key,x=>x.Sum(y=>y.Quantity));
        var after=InventoryShape(sender.AfterInventory).Concat(InventoryShape(recipient.AfterInventory))
            .GroupBy(x=>x.Key).ToDictionary(x=>x.Key,x=>x.Sum(y=>y.Quantity));
        if(before.Keys.Union(after.Keys).Any(key=>before.GetValueOrDefault(key)!=after.GetValueOrDefault(key)))return false;
        return Quantity(sender.AfterInventory,shape)-Quantity(sender.BeforeInventory,shape)==-quantity&&
            Quantity(recipient.AfterInventory,shape)-Quantity(recipient.BeforeInventory,shape)==quantity;
    }

    private static bool UnrelatedInventoryStable(ManualTradeResult capture,Item transferred)
    {
        var transferredShape=Shape(transferred);
        static string Row(Item item)=>$"{item.Slot}|{Shape(item)}|{item.Quantity}";
        return capture.BeforeInventory.Where(x=>Shape(x)!=transferredShape).Select(Row).OrderBy(x=>x,StringComparer.Ordinal)
            .SequenceEqual(capture.AfterInventory.Where(x=>Shape(x)!=transferredShape).Select(Row)
                .OrderBy(x=>x,StringComparer.Ordinal));
    }

    private static IEnumerable<(string Key,long Quantity)> InventoryShape(IEnumerable<Item> items)=>items
        .Select(x=>(Key:Shape(x),Quantity:x.Quantity)).OrderBy(x=>x.Key,StringComparer.Ordinal).ThenBy(x=>x.Quantity);
    private static long Quantity(IEnumerable<Item> items,string shape)=>items.Where(x=>Shape(x)==shape).Sum(x=>x.Quantity);
    private static string Shape(Item item)=>$"{ItemGroups.Key(item)}|{item.Durability}|{item.MaxDurability}|{item.IsStackable}";

    private static bool InventoryPacketsMatch(Side sender,Side recipient,Item item,byte slot)
    {
        var key=ItemGroups.Key(item);var sourceAfter=sender.Capture.AfterInventory.SingleOrDefault(x=>x.Slot==slot);
        var sourceUpdated=sender.InventoryAdds.Count==1&&sender.InventoryRemoves.Count==0&&sourceAfter!=null&&
            sender.InventoryAdds[0].Slot==slot&&ItemGroups.Key(ToItem(sender.InventoryAdds[0]))==key&&
            sender.InventoryAdds[0].Quantity==sourceAfter.Quantity;
        var sourceRemoved=sender.InventoryAdds.Count==0&&sender.InventoryRemoves.Count==1&&sourceAfter==null&&
            sender.InventoryRemoves[0].Slot==slot;
        var recipientAdded=recipient.InventoryRemoves.Count==0&&recipient.InventoryAdds.Count==1&&
            ItemGroups.Key(ToItem(recipient.InventoryAdds[0]))==key&&recipient.Capture.AfterInventory.Any(y=>
                y.Slot==recipient.InventoryAdds[0].Slot&&Shape(y)==Shape(item)&&y.Quantity==recipient.InventoryAdds[0].Quantity);
        return (sourceUpdated||sourceRemoved)&&recipientAdded;
    }

    private static bool OfferMatches(Item item,int quantity,string? display)
    {
        if(display==null)return false;
        if(quantity>1)return Regex.IsMatch(display,"^"+Regex.Escape(item.Name)+@"\s*\("+quantity+@"\)$",RegexOptions.CultureInvariant);
        return display==item.Name||display==$"{item.Name}(1)"||
            Regex.IsMatch(display,@"^"+Regex.Escape(item.Name)+@" (?:100|[1-9]?[0-9])%$",RegexOptions.CultureInvariant);
    }

    private static Item ToItem(ServerAddInventoryMessage message)=>new(message.Slot,message.Name,message.Quantity,
        message.Sprite,(byte)message.Color,Durability:message.Durability,MaxDurability:message.MaxDurability,
        IsStackable:message.IsStackable);

    private sealed class Side(ManualTradeResult capture)
    {
        public ManualTradeResult Capture {get;}=capture;
        public List<Timed<ClientExchangeMessage>> Client {get;}=[];
        public List<Timed<ServerExchangeMessage>> Server {get;}=[];
        public List<ServerAddInventoryMessage> InventoryAdds {get;}=[];
        public List<ServerRemoveInventoryMessage> InventoryRemoves {get;}=[];
        public bool Invalid {get;set;}
    }
    private sealed record Timed<T>(DateTimeOffset ObservedAt,T Message);

    private static Side Decode(ManualTradeResult capture)
    {
        var side=new Side(capture);DateTimeOffset? last=null;
        foreach(var packet in capture.Packets??[])
        {
            if(last>packet.ObservedAt)throw new FormatException("Packet timeline is out of order.");
            last=packet.ObservedAt;
            var bytes=Convert.FromHexString(packet.PayloadHex);
            if(packet.Direction=="Client"&&packet.Opcode==0x4A)
            {
                var expectedLength=bytes.Length==0?0:bytes[0] switch{0 or 5=>5,1=>6,2=>7,_=>-1};
                if(bytes.Length!=expectedLength||ClientMessageFactory.Default.Create(new ClientPacket(packet.Opcode,bytes)) is not ClientExchangeMessage client)
                    throw new FormatException("Unexpected client exchange packet.");
                if(client.Action is not (ExchangeClientActionType.BeginExchange or ExchangeClientActionType.AddItem or
                    ExchangeClientActionType.AddStackableItem or ExchangeClientActionType.Accept))side.Invalid=true;
                side.Client.Add(new(packet.ObservedAt,client));
            }
            else if(packet.Direction=="Server"&&packet.Opcode==0x42)
            {
                if(ServerMessageFactory.Default.Create(new ServerPacket(packet.Opcode,bytes)) is not ServerExchangeMessage server)
                    throw new FormatException("Unexpected server exchange packet.");
                if(server.Event==ExchangeServerEventType.GoldAdded||!Enum.IsDefined(server.Event))side.Invalid=true;
                side.Server.Add(new(packet.ObservedAt,server));
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
