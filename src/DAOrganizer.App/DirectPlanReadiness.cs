using DAOrganizer.Core;
using DAOrganizer.Game;

namespace DAOrganizer.App;

public static class DirectPlanReadiness
{
    public static ExactOrganizationPlan Promote(ExactOrganizationPlan plan,OrganizationState state,
        DirectTradeEndpoint sender,DirectTradeEndpoint recipient,DateTimeOffset now,bool controlledTrial=false)
    {
        OrganizationPlanContract.Validate(plan);
        if(plan.Steps.Length!=1||plan.InputFingerprint!=state.Fingerprint||now>=plan.ExpiresAt)
            throw new InvalidOperationException("Prepare one current, selected direct step at a time.");
        var step=plan.Steps[0];
        if(step.RouteKind!=TransferRouteKind.Direct||step.SourceLocation is not ("Inventory" or "Bank")||
            step.Readiness!=OrganizationReadiness.NeedsScan||step.Quantity is <1 or >255||
            !OrganizationPlanContract.Matches(state,step.ExpectedBefore)||
            !OrganizationPlanContract.MatchesSource(state,step))
            throw new InvalidOperationException("The selected direct step needs an exact current source.");
        var source=state.Characters.SingleOrDefault(x=>x.Name.Equals(step.SourceCharacter,StringComparison.OrdinalIgnoreCase));
        var destination=state.Characters.SingleOrDefault(x=>x.Name.Equals(step.DestinationCharacter,StringComparison.OrdinalIgnoreCase));
        if(source?.InventoryState!="Current"||destination?.InventoryState!="Current"||
            step.SourceLocation=="Bank"&&source.BankState!="Current"||
            destination.BankState!="Current")
            throw new InvalidOperationException("Scan both inventories and the destination bank before approval.");
        state.Metadata.TryGetValue(step.ItemKey,out var metadata);
        state.TradeEvidence.TryGetValue(step.ItemKey,out var evidence);
        if(metadata?.Tradeability==Tradeability.NonTradeable||evidence?.ExplicitRejections>0)
            throw new InvalidOperationException("This item has negative trade evidence.");
        var shape=OrganizationPlanContract.ItemFingerprint(step.SourceItem);
        if(controlledTrial)
        {
            if(step.Quantity!=1)throw new InvalidOperationException("Controlled live trials are limited to one unit.");
        }
        else
        {
            if(metadata?.Stackable!=true||metadata.StackLimit is not >0||
                metadata.Tradeability!=Tradeability.Tradeable&&evidence?.Successes is not >0)
                throw new InvalidOperationException("A known stack limit and positive trade evidence are required.");
            var room=state.Items.Where(x=>x.Character.Equals(step.DestinationCharacter,StringComparison.OrdinalIgnoreCase)&&
                x.Location=="Bank"&&ItemGroups.Key(x.Item)==step.ItemKey&&
                OrganizationPlanContract.ItemFingerprint(x.Item)==shape)
                .Sum(x=>Math.Max(0,metadata.StackLimit.Value-x.Item.Quantity));
            if(room<step.Quantity)
                throw new InvalidOperationException("No verified room in an existing destination bank stack.");
        }
        var prepared=step with{Readiness=OrganizationReadiness.Ready,ControlledTrial=controlledTrial};
        if(step.SourceLocation=="Inventory")
        {
            var carried=sender.Inventory.SingleOrDefault(x=>x.Slot==step.SourceSlot)
                ??throw new InvalidOperationException("Source item is missing from the live inventory.");
            DirectTradePreflight.Check(sender,recipient,prepared,carried,now);
        }
        else
        {
            DirectTradePreflight.CheckPartners(sender,recipient,prepared,now);
            var sourceBank=state.Items.Where(x=>x.Character.Equals(step.SourceCharacter,StringComparison.OrdinalIgnoreCase)&&
                x.Location=="Bank"&&x.Item.Name.Equals(step.SourceItem.Name,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(step.Quantity!=1||sourceBank.Length!=1||sourceBank[0].Item.Slot!=step.SourceSlot||
                OrganizationPlanContract.ItemFingerprint(sourceBank[0].Item)!=shape||
                sender.Inventory.Count>=59||sender.Inventory.Any(x=>ItemGroups.Key(x)==step.ItemKey))
                throw new InvalidOperationException("Bank withdrawal needs one unique named item, one unit and a free, unambiguous source inventory slot.");
        }
        var ready=plan with{Steps=[prepared]};
        OrganizationPlanContract.Validate(ready);
        return ready;
    }
}
