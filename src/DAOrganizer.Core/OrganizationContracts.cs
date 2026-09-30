using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DAOrganizer.Core;

// Persisted approval contract. OrganizationPlan in OrganizationPlanner.cs remains a read-only preview.
public enum OrganizationReadiness { Unknown, NeedsScan, ManualOnly, Ready }
public enum PlanApprovalState { Draft, Approved, Completed }
public enum OrganizationLegKind { Withdraw, Exchange, Deposit, MoveWithinCharacter }

public sealed record OrganizationRouteLeg(OrganizationLegKind Kind,string SourceCharacter,string DestinationCharacter,
    string SourceLocation,string DestinationLocation);

// Slot=null compares the total quantity of this item identity at this location.
public sealed record OrganizationItemExpectation(string Character,string Location,string ItemKey,int? Slot,long Quantity,
    string? ItemFingerprint=null);

public sealed record PlannedOrganizationStep(Guid Id,int Order,string SourceCharacter,string DestinationCharacter,
    string ItemKey,Item SourceItem,string SourceLocation,int? SourceSlot,long Quantity,TransferRouteKind RouteKind,
    ImmutableArray<OrganizationRouteLeg> RouteLegs,ImmutableArray<Guid> DependsOn,OrganizationReadiness Readiness,
    ImmutableArray<OrganizationItemExpectation> ExpectedBefore,ImmutableArray<OrganizationItemExpectation> ExpectedAfter);

public sealed record ExactOrganizationPlan(Guid Id,DateTimeOffset CreatedAt,DateTimeOffset ExpiresAt,
    string InputFingerprint,ImmutableArray<PlannedOrganizationStep> Steps);

public sealed record SavedOrganizationPlan(ExactOrganizationPlan Plan,PlanApprovalState Approval,
    string? CheckpointFingerprint,int NextStepOrdinal);

public sealed class StaleOrganizationPlanException(string message):InvalidOperationException(message);

public static class OrganizationPlanContract
{
    public static void Validate(ExactOrganizationPlan plan)
    {
        if(plan.Id==Guid.Empty||plan.CreatedAt>=plan.ExpiresAt||plan.InputFingerprint is null||
            plan.InputFingerprint.Length!=64||
            !plan.InputFingerprint.All(Uri.IsHexDigit)||plan.Steps.IsDefaultOrEmpty)
            throw new InvalidDataException("Organization plan has invalid identity, dates, fingerprint or steps.");
        var seen=new HashSet<Guid>();
        for(var i=0;i<plan.Steps.Length;i++)
        {
            var step=plan.Steps[i];
            if(step.Id==Guid.Empty||!seen.Add(step.Id)||step.Order!=i||string.IsNullOrWhiteSpace(step.SourceCharacter)||
                string.IsNullOrWhiteSpace(step.DestinationCharacter)||step.SourceItem is null||
                string.IsNullOrWhiteSpace(step.SourceItem.Name)||
                step.ItemKey!=ItemGroups.Key(step.SourceItem)||step.SourceSlot!=step.SourceItem.Slot||
                step.Quantity<=0||step.Quantity>step.SourceItem.Quantity||string.IsNullOrWhiteSpace(step.SourceLocation)||
                step.SourceSlot is <=0||step.DependsOn.IsDefault||step.RouteLegs.IsDefault||
                step.ExpectedBefore.IsDefault||step.ExpectedAfter.IsDefault||
                step.RouteLegs.Any(x=>x is null)||step.ExpectedBefore.Any(x=>x is null)||
                step.ExpectedAfter.Any(x=>x is null)||
                step.DependsOn.Distinct().Count()!=step.DependsOn.Length||step.DependsOn.Any(x=>!seen.Contains(x)||x==step.Id))
                throw new InvalidDataException("Organization plan step is malformed or has invalid dependencies.");
            if(step.Readiness==OrganizationReadiness.Ready)
            {
                if(step.SourceSlot is null||step.RouteLegs.IsEmpty||step.ExpectedBefore.IsEmpty||step.ExpectedAfter.IsEmpty||
                    step.RouteKind==TransferRouteKind.ManualOnly||
                    !step.ExpectedBefore.Any(x=>x.Character.Equals(step.SourceCharacter,StringComparison.OrdinalIgnoreCase)&&
                        x.Location==step.SourceLocation&&x.ItemKey==step.ItemKey&&x.Slot==step.SourceSlot&&x.Quantity>=step.Quantity)||
                    !step.ExpectedBefore.Any(before=>before.Character.Equals(step.SourceCharacter,StringComparison.OrdinalIgnoreCase)&&
                        before.Location==step.SourceLocation&&before.ItemKey==step.ItemKey&&before.Slot==step.SourceSlot&&
                        step.ExpectedAfter.Any(after=>after.Character.Equals(before.Character,StringComparison.OrdinalIgnoreCase)&&
                            after.Location==before.Location&&after.ItemKey==before.ItemKey&&after.Slot==before.Slot&&
                            after.Quantity==before.Quantity-step.Quantity))||
                    !step.ExpectedAfter.Any(x=>x.Character.Equals(step.DestinationCharacter,StringComparison.OrdinalIgnoreCase)&&
                        x.ItemKey==step.ItemKey&&x.Quantity>=step.Quantity&&
                        x.ItemFingerprint==ItemFingerprint(step.SourceItem))||
                    step.RouteLegs[0].SourceCharacter!=step.SourceCharacter||
                    step.RouteLegs[0].SourceLocation!=step.SourceLocation||
                    step.RouteLegs[^1].DestinationCharacter!=step.DestinationCharacter||
                    step.RouteLegs.Any(x=>string.IsNullOrWhiteSpace(x.SourceCharacter)||
                        string.IsNullOrWhiteSpace(x.DestinationCharacter)||string.IsNullOrWhiteSpace(x.SourceLocation)||
                        string.IsNullOrWhiteSpace(x.DestinationLocation))||
                    Enumerable.Range(1,step.RouteLegs.Length-1).Any(j=>
                        step.RouteLegs[j-1].DestinationCharacter!=step.RouteLegs[j].SourceCharacter||
                        step.RouteLegs[j-1].DestinationLocation!=step.RouteLegs[j].SourceLocation)||
                    (step.RouteKind==TransferRouteKind.Local&&
                        (step.SourceCharacter!=step.DestinationCharacter||step.RouteLegs.Any(x=>x.Kind==OrganizationLegKind.Exchange)))||
                    (step.RouteKind==TransferRouteKind.Direct&&step.RouteLegs.Count(x=>x.Kind==OrganizationLegKind.Exchange)!=1)||
                    (step.RouteKind==TransferRouteKind.Middleman&&step.RouteLegs.Count(x=>x.Kind==OrganizationLegKind.Exchange)!=2))
                    throw new InvalidDataException("Ready step lacks exact source, route or state expectations.");
            }
            foreach(var expectation in step.ExpectedBefore.Concat(step.ExpectedAfter))
                if(string.IsNullOrWhiteSpace(expectation.Character)||string.IsNullOrWhiteSpace(expectation.Location)||
                    string.IsNullOrWhiteSpace(expectation.ItemKey)||
                    expectation.Slot is <=0||expectation.Quantity<0||
                    expectation.ItemFingerprint is { } fingerprint&&
                    (fingerprint.Length!=64||!fingerprint.All(Uri.IsHexDigit)))
                    throw new InvalidDataException("Organization state expectation is malformed.");
        }
    }

    public static bool Matches(OrganizationState state,ImmutableArray<OrganizationItemExpectation> expectations)
    {
        foreach(var expected in expectations)
        {
            var rows=state.Items.Where(x=>x.Character.Equals(expected.Character,StringComparison.OrdinalIgnoreCase)&&
                x.Location==expected.Location&&ItemGroups.Key(x.Item)==expected.ItemKey&&
                (expected.Slot is null||x.Item.Slot==expected.Slot)&&
                (expected.ItemFingerprint is null||ItemFingerprint(x.Item)==expected.ItemFingerprint));
            if(rows.Sum(x=>x.Item.Quantity)!=expected.Quantity)return false;
        }
        return true;
    }

    public static bool MatchesSource(OrganizationState state,PlannedOrganizationStep step)=>
        state.Items.Any(x=>x.Character.Equals(step.SourceCharacter,StringComparison.OrdinalIgnoreCase)&&
            x.Location==step.SourceLocation&&x.Item.Slot==step.SourceSlot&&x.Item.Quantity>=step.Quantity&&
            SameItemShape(x.Item,step.SourceItem));

    public static string ItemFingerprint(Item item)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new{item.Name,item.Sprite,item.Color,item.Durability,item.MaxDurability,item.IsStackable}))));

    private static bool SameItemShape(Item a,Item b)=>a with{Slot=0,Quantity=0,Category=""}==
        b with{Slot=0,Quantity=0,Category=""};
}
