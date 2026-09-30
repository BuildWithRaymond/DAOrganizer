using DAOrganizer.Core;

namespace DAOrganizer.Game;

public sealed record DirectTradeEndpoint(string Name,int ProcessId,bool Ready,int MapId,uint PlayerId,Tile Position,
    IReadOnlyList<Item> Inventory,uint Gold,VisibleTradeTarget Partner,IReadOnlyCollection<int> PinnedSlots,
    DateTimeOffset CapturedAt);

public sealed record DirectTradeTargets(uint SenderTargetId,uint RecipientTargetId);

public static class DirectTradePreflight
{
    public static DirectTradeTargets Check(DirectTradeEndpoint sender,DirectTradeEndpoint recipient,
        PlannedOrganizationStep step,Item carried,DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(carried);
        var targets=CheckPartners(sender,recipient,step,now);
        if(carried.Slot is <1 or >59||carried.Quantity<step.Quantity||
            ItemGroups.Key(carried)!=step.ItemKey||
            OrganizationPlanContract.ItemFingerprint(carried)!=OrganizationPlanContract.ItemFingerprint(step.SourceItem)||
            step.SourceLocation=="Inventory"&&carried.Slot!=step.SourceSlot||
            !sender.Inventory.Contains(carried)||sender.PinnedSlots.Contains(carried.Slot))
            throw new InvalidOperationException("Exact source slot, shape or quantity changed.");
        return targets;
    }

    public static DirectTradeTargets CheckPartners(DirectTradeEndpoint sender,DirectTradeEndpoint recipient,
        PlannedOrganizationStep step,DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(step);
        if(step.RouteKind!=TransferRouteKind.Direct||step.Readiness!=OrganizationReadiness.Ready||
            step.Quantity is <1 or >255||!sender.Ready||!recipient.Ready||
            sender.ProcessId<=0||recipient.ProcessId<=0||sender.ProcessId==recipient.ProcessId||
            sender.PlayerId==0||recipient.PlayerId==0||sender.PlayerId==recipient.PlayerId||
            sender.MapId<=0||sender.MapId!=recipient.MapId||
            !sender.Name.Equals(step.SourceCharacter,StringComparison.OrdinalIgnoreCase)||
            !recipient.Name.Equals(step.DestinationCharacter,StringComparison.OrdinalIgnoreCase)||
            sender.Name.Equals(recipient.Name,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Direct transfer needs distinct ready sessions on the same map.");
        if(now<sender.CapturedAt||now<recipient.CapturedAt||
            now-sender.CapturedAt>TimeSpan.FromSeconds(5)||now-recipient.CapturedAt>TimeSpan.FromSeconds(5))
            throw new InvalidOperationException("Live inventory observations are stale.");
        if(sender.Partner.Id!=recipient.PlayerId||recipient.Partner.Id!=sender.PlayerId||
            !sender.Partner.Name.Equals(recipient.Name,StringComparison.OrdinalIgnoreCase)||
            !recipient.Partner.Name.Equals(sender.Name,StringComparison.OrdinalIgnoreCase)||
            sender.Partner.MapId!=sender.MapId||recipient.Partner.MapId!=recipient.MapId||
            sender.Partner.Position!=recipient.Position||recipient.Partner.Position!=sender.Position||
            now<sender.Partner.ObservedAt||now<recipient.Partner.ObservedAt||
            Math.Abs(sender.Position.X-recipient.Position.X)+Math.Abs(sender.Position.Y-recipient.Position.Y)!=1)
            throw new InvalidOperationException("Trade partners are not mutually identified and adjacent.");
        if(recipient.Inventory.Count(x=>x.Slot is >=1 and <=59)>=59)
            throw new InvalidOperationException("Recipient inventory has no free item slot.");
        return new(sender.Partner.Id,recipient.Partner.Id);
    }
}
