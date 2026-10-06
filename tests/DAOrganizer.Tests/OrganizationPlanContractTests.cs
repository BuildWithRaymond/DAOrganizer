using System.Collections.Immutable;
using DAOrganizer.Core;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace DAOrganizer.Tests;

public class OrganizationPlanContractTests
{
    private static readonly Item Chest=new(1,"Water Dungeon Chest",2,15,IsStackable:true);

    private static PlannedOrganizationStep Step(int order,string sourceLocation,string destinationLocation,
        Guid[]? dependencies=null)=>new(Guid.NewGuid(),order,"Alpha","Bravo",ItemGroups.Key(Chest),Chest,
        sourceLocation,1,2,TransferRouteKind.Direct,
        Route(sourceLocation,destinationLocation),
        (dependencies??[]).ToImmutableArray(),OrganizationReadiness.Ready,
        [new OrganizationItemExpectation("Alpha",sourceLocation,ItemGroups.Key(Chest),1,2)],
        [new OrganizationItemExpectation("Alpha",sourceLocation,ItemGroups.Key(Chest),1,0),
            new OrganizationItemExpectation("Bravo",destinationLocation,ItemGroups.Key(Chest),null,2,
                OrganizationPlanContract.ItemFingerprint(Chest))]);

    private static ImmutableArray<OrganizationRouteLeg> Route(string sourceLocation,string destinationLocation)
    {
        var route=ImmutableArray.CreateBuilder<OrganizationRouteLeg>();
        if(sourceLocation=="Bank")route.Add(new(OrganizationLegKind.Withdraw,"Alpha","Alpha","Bank","Inventory"));
        route.Add(new(OrganizationLegKind.Exchange,"Alpha","Bravo","Inventory","Inventory"));
        if(destinationLocation=="Bank")route.Add(new(OrganizationLegKind.Deposit,"Bravo","Bravo","Inventory","Bank"));
        return route.ToImmutable();
    }

    private static ExactOrganizationPlan Plan(InventoryStore store,params PlannedOrganizationStep[] steps)=>
        new(Guid.NewGuid(),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(1),
            store.ReadOrganizationState().Fingerprint,steps.ToImmutableArray());

    [Fact]
    public void ExactPlanRoundTripsAndApprovalChecksFreshState()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var step=Step(0,"Bank","Bank");var plan=Plan(store,step);
        store.SaveOrganizationPlan(plan);
        var loaded=store.LoadOrganizationPlan(plan.Id);
        Assert.NotNull(loaded);
        Assert.Equal(PlanApprovalState.Draft,loaded.Approval);
        Assert.Equal(plan.InputFingerprint,loaded.Plan.InputFingerprint);
        var roundTrip=Assert.Single(loaded.Plan.Steps);
        Assert.Equal(step.Id,roundTrip.Id);
        Assert.Equal(step.ItemKey,roundTrip.ItemKey);
        Assert.Equal(step.Quantity,roundTrip.Quantity);
        Assert.Equal(step.RouteLegs.ToArray(),roundTrip.RouteLegs.ToArray());
        var approved=store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        Assert.Equal(PlanApprovalState.Approved,approved.Approval);
        Assert.Equal(step.Id,store.CheckNextOrganizationStep(plan.Id,DateTimeOffset.UtcNow)?.Id);
    }

    [Fact]
    public void ChangedSlotOrRuleRejectsApproval()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var plan=Plan(store,Step(0,"Bank","Bank"));store.SaveOrganizationPlan(plan);
        store.SaveSnapshot("Alpha","Bank",[Chest with{Slot=2}],true);
        Assert.Throws<StaleOrganizationPlanException>(()=>store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ExternalChangeAfterApprovalBlocksNextStep()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var plan=Plan(store,Step(0,"Bank","Bank"));store.SaveOrganizationPlan(plan);
        store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        store.Put("pins/alpha",new HashSet<int>{1});
        Assert.Throws<StaleOrganizationPlanException>(()=>store.CheckNextOrganizationStep(plan.Id,DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ConfirmedStepRefreshesCheckpointForNextApprovedStep()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Alpha","Inventory",[],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        store.SaveSnapshot("Bravo","Inventory",[],true);
        var first=Step(0,"Bank","Inventory");
        var second=Step(1,"Inventory","Bank",[first.Id]) with
        {
            SourceCharacter="Bravo",SourceItem=Chest,DestinationCharacter="Bravo",RouteKind=TransferRouteKind.Local,
            RouteLegs=[new OrganizationRouteLeg(OrganizationLegKind.Deposit,"Bravo","Bravo","Inventory","Bank")],
            ExpectedBefore=[new OrganizationItemExpectation("Bravo","Inventory",ItemGroups.Key(Chest),1,2)],
            ExpectedAfter=[new OrganizationItemExpectation("Bravo","Inventory",ItemGroups.Key(Chest),1,0),
                new OrganizationItemExpectation("Bravo","Bank",ItemGroups.Key(Chest),null,2,
                    OrganizationPlanContract.ItemFingerprint(Chest))]
        };
        var plan=Plan(store,first,second);store.SaveOrganizationPlan(plan);
        store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        Assert.Equal(first.Id,store.CheckNextOrganizationStep(plan.Id,DateTimeOffset.UtcNow)?.Id);
        var before=store.ReadOrganizationState().Fingerprint;
        store.SaveSnapshot("Alpha","Bank",[],true);
        store.SaveSnapshot("Bravo","Inventory",[Chest],true);
        var advanced=store.ConfirmOrganizationStep(plan.Id,first.Id,before,DateTimeOffset.UtcNow);
        Assert.Equal(1,advanced.NextStepOrdinal);
        Assert.Equal(second.Id,store.CheckNextOrganizationStep(plan.Id,DateTimeOffset.UtcNow)?.Id);
    }

    [Fact]
    public void InvalidPlanAndExpiredApprovalFailClosed()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var bad=Plan(store,Step(0,"Bank","Bank") with{Quantity=0});
        Assert.Throws<InvalidDataException>(()=>store.SaveOrganizationPlan(bad));
        var expired=Plan(store,Step(0,"Bank","Bank")) with
        {CreatedAt=DateTimeOffset.UtcNow.AddMinutes(-2),ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(-1)};
        store.SaveOrganizationPlan(expired);
        Assert.Throws<InvalidOperationException>(()=>store.ApproveOrganizationPlan(expired.Id,DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SameKeyDifferentDurabilityDoesNotConfirmDelivery()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var step=Step(0,"Bank","Bank");var plan=Plan(store,step);
        store.SaveOrganizationPlan(plan);store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        var before=store.ReadOrganizationState().Fingerprint;
        store.SaveSnapshot("Alpha","Bank",[],true);
        store.SaveSnapshot("Bravo","Bank",[Chest with{Durability=1}],true);
        Assert.Throws<StaleOrganizationPlanException>(()=>
            store.ConfirmOrganizationStep(plan.Id,step.Id,before,DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ExistingDestinationCopyDoesNotHideWrongDeliveredVariant()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[Chest with{Quantity=1}],true);
        var step=Step(0,"Bank","Bank") with
        {
            ExpectedAfter=[new OrganizationItemExpectation("Alpha","Bank",ItemGroups.Key(Chest),1,0),
                new OrganizationItemExpectation("Bravo","Bank",ItemGroups.Key(Chest),null,3,
                    OrganizationPlanContract.ItemFingerprint(Chest))]
        };
        var plan=Plan(store,step);store.SaveOrganizationPlan(plan);
        store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        var before=store.ReadOrganizationState().Fingerprint;
        store.SaveSnapshot("Alpha","Bank",[],true);
        store.SaveSnapshot("Bravo","Bank",[Chest with{Quantity=1},Chest with{Slot=2,Durability=1}],true);
        Assert.Throws<StaleOrganizationPlanException>(()=>
            store.ConfirmOrganizationStep(plan.Id,step.Id,before,DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ExtraSameKeyVariantIsNotAnApprovedSideEffect()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var step=Step(0,"Bank","Bank");var plan=Plan(store,step);
        store.SaveOrganizationPlan(plan);store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        var before=store.ReadOrganizationState().Fingerprint;
        store.SaveSnapshot("Alpha","Bank",[],true);
        store.SaveSnapshot("Bravo","Bank",[Chest,Chest with{Slot=2,Durability=1}],true);
        Assert.Throws<StaleOrganizationPlanException>(()=>
            store.ConfirmOrganizationStep(plan.Id,step.Id,before,DateTimeOffset.UtcNow));
    }

    [Fact]
    public void LocalDepositCanBeAnExactApprovedStep()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Inventory",[Chest],true);
        store.SaveSnapshot("Alpha","Bank",[],true);
        var step=new PlannedOrganizationStep(Guid.NewGuid(),0,"Alpha","Alpha",ItemGroups.Key(Chest),Chest,
            "Inventory",1,2,TransferRouteKind.Local,
            [new OrganizationRouteLeg(OrganizationLegKind.Deposit,"Alpha","Alpha","Inventory","Bank")],
            [],OrganizationReadiness.Ready,
            [new OrganizationItemExpectation("Alpha","Inventory",ItemGroups.Key(Chest),1,2)],
            [new OrganizationItemExpectation("Alpha","Inventory",ItemGroups.Key(Chest),1,0),
                new OrganizationItemExpectation("Alpha","Bank",ItemGroups.Key(Chest),null,2,
                    OrganizationPlanContract.ItemFingerprint(Chest))]);
        var plan=Plan(store,step);
        store.SaveOrganizationPlan(plan);
        Assert.Equal(step.Id,store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow).Plan.Steps[0].Id);
    }

    [Fact]
    public void ReadyStepMustDescribeExactSourceDecrease()
    {
        using var store=new InventoryStore(":memory:");
        var missing=Step(0,"Bank","Bank") with
        {
            ExpectedAfter=[new OrganizationItemExpectation("Bravo","Bank",ItemGroups.Key(Chest),null,2,
                OrganizationPlanContract.ItemFingerprint(Chest))]
        };
        Assert.Throws<InvalidDataException>(()=>store.SaveOrganizationPlan(Plan(store,missing)));
        var unchanged=Step(0,"Bank","Bank") with
        {
            ExpectedAfter=[new OrganizationItemExpectation("Alpha","Bank",ItemGroups.Key(Chest),1,2),
                new OrganizationItemExpectation("Bravo","Bank",ItemGroups.Key(Chest),null,2,
                    OrganizationPlanContract.ItemFingerprint(Chest))]
        };
        Assert.Throws<InvalidDataException>(()=>store.SaveOrganizationPlan(Plan(store,unchanged)));
    }

    [Fact]
    public void NeedsScanCandidateCanPreserveUnknownSourceSlotWithoutApproval()
    {
        using var store=new InventoryStore(":memory:");
        var step=Step(0,"Bank","Bank") with
        {
            SourceSlot=null,Readiness=OrganizationReadiness.NeedsScan,
            RouteLegs=[],ExpectedBefore=[],ExpectedAfter=[]
        };
        var plan=Plan(store,step);store.SaveOrganizationPlan(plan);
        Assert.Null(Assert.Single(store.LoadOrganizationPlan(plan.Id)!.Plan.Steps).SourceSlot);
        Assert.Throws<InvalidOperationException>(()=>store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow));
    }

    [Fact]
    public void UnrelatedInventoryChangeDoesNotConfirmApprovedStep()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var step=Step(0,"Bank","Bank");var plan=Plan(store,step);
        store.SaveOrganizationPlan(plan);store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        var before=store.ReadOrganizationState().Fingerprint;
        store.SaveSnapshot("Alpha","Bank",[],true);
        store.SaveSnapshot("Bravo","Bank",[Chest,new Item(2,"Unknown Ring",1)],true);
        Assert.Throws<StaleOrganizationPlanException>(()=>
            store.ConfirmOrganizationStep(plan.Id,step.Id,before,DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MalformedSavedStepIsRejected()
    {
        var path=Path.Combine(Path.GetTempPath(),"DAOrganizer-plan-"+Guid.NewGuid()+".db");
        try
        {
            using(var store=new InventoryStore(path))
            {
                store.SaveSnapshot("Alpha","Bank",[Chest],true);
                store.SaveSnapshot("Bravo","Bank",[],true);
                var step=Step(0,"Bank","Bank");var plan=Plan(store,step);store.SaveOrganizationPlan(plan);
                using var db=new SqliteConnection($"Data Source={path}");db.Open();
                using var command=db.CreateCommand();command.CommandText="UPDATE plan_steps SET data='{}'";command.ExecuteNonQuery();
                Assert.Throws<InvalidDataException>(()=>store.LoadOrganizationPlan(plan.Id));
                command.CommandText="UPDATE plan_steps SET data=$data";
                command.Parameters.AddWithValue("$data",JsonSerializer.Serialize(step with{SourceItem=Chest with{Name=null!}}));
                command.ExecuteNonQuery();
                Assert.Throws<InvalidDataException>(()=>store.LoadOrganizationPlan(plan.Id));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach(var suffix in new[]{"","-wal","-shm"})File.Delete(path+suffix);
        }
    }
}
