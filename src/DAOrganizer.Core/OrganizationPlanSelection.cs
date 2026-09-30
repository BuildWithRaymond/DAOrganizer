using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace DAOrganizer.Core;

public static class OrganizationPlanSelection
{
    public static ExactOrganizationPlan Select(ExactOrganizationPlan original,OrganizationState state,
        IReadOnlyCollection<Guid> selectedStepIds)
    {
        OrganizationPlanContract.Validate(original);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(selectedStepIds);
        if(state.Fingerprint!=original.InputFingerprint)
            throw new StaleOrganizationPlanException("Plan inputs changed. Replan before selecting steps.");
        var ids=selectedStepIds.ToHashSet();
        if(ids.Count==0||ids.Count!=selectedStepIds.Count||
            ids.Any(id=>!original.Steps.Any(step=>step.Id==id)))
            throw new ArgumentException("Select one or more known, distinct steps.",nameof(selectedStepIds));

        var sourceRemaining=new Dictionary<(string Character,string Location,int Slot,string Shape),long>();
        var destinationTotals=new Dictionary<(string Character,string ItemKey,string Shape),long>();
        var steps=ImmutableArray.CreateBuilder<PlannedOrganizationStep>();
        foreach(var step in original.Steps.Where(step=>ids.Contains(step.Id)))
        {
            if(step.SourceSlot is not int slot)
                throw new InvalidDataException("Selected step has no source slot.");
            var shape=OrganizationPlanContract.ItemFingerprint(step.SourceItem);
            var sourceKey=(step.SourceCharacter.ToUpperInvariant(),step.SourceLocation,slot,shape);
            var destinationKey=(step.DestinationCharacter.ToUpperInvariant(),step.ItemKey,shape);
            if(!sourceRemaining.TryGetValue(sourceKey,out var sourceBefore))
                sourceBefore=state.Items.Where(x=>x.Character.Equals(step.SourceCharacter,StringComparison.OrdinalIgnoreCase)&&
                    x.Location==step.SourceLocation&&x.Item.Slot==slot&&ItemGroups.Key(x.Item)==step.ItemKey&&
                    OrganizationPlanContract.ItemFingerprint(x.Item)==shape).Sum(x=>(long)x.Item.Quantity);
            if(!destinationTotals.TryGetValue(destinationKey,out var destinationBefore))
                destinationBefore=state.Items.Where(x=>x.Character.Equals(step.DestinationCharacter,StringComparison.OrdinalIgnoreCase)&&
                    x.Location=="Bank"&&ItemGroups.Key(x.Item)==step.ItemKey&&
                    OrganizationPlanContract.ItemFingerprint(x.Item)==shape).Sum(x=>(long)x.Item.Quantity);
            if(sourceBefore<step.Quantity)throw new InvalidDataException("Selected steps exceed the source quantity.");
            var destinationAfter=checked(destinationBefore+step.Quantity);
            var sourceAfter=sourceBefore-step.Quantity;
            var before=ImmutableArray.Create(
                new OrganizationItemExpectation(step.SourceCharacter,step.SourceLocation,step.ItemKey,slot,sourceBefore,shape),
                new OrganizationItemExpectation(step.DestinationCharacter,"Bank",step.ItemKey,null,destinationBefore,shape));
            var after=ImmutableArray.Create(
                new OrganizationItemExpectation(step.SourceCharacter,step.SourceLocation,step.ItemKey,slot,sourceAfter,shape),
                new OrganizationItemExpectation(step.DestinationCharacter,"Bank",step.ItemKey,null,destinationAfter,shape));
            steps.Add(step with{Order=steps.Count,
                DependsOn=steps.Count==0?ImmutableArray<Guid>.Empty:ImmutableArray.Create(steps[^1].Id),
                ExpectedBefore=before,ExpectedAfter=after});
            sourceRemaining[sourceKey]=sourceAfter;
            destinationTotals[destinationKey]=destinationAfter;
        }
        var seed=$"selection|{original.Id:D}|{string.Join('|',steps.Select(x=>x.Id.ToString("D")))}";
        var id=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(seed)).AsSpan(0,16));
        var result=original with{Id=id,Steps=steps.ToImmutable()};
        OrganizationPlanContract.Validate(result);
        return result;
    }
}
