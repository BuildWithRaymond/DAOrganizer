using System.Collections.Immutable;
using DAOrganizer.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DAOrganizer.Tests;

public class TransferJournalTests
{
    private static readonly Item Chest=new(1,"Chest",2,15,IsStackable:true);

    private static ExactOrganizationPlan ReadyPlan(InventoryStore store)
    {
        var key=ItemGroups.Key(Chest);
        var shape=OrganizationPlanContract.ItemFingerprint(Chest);
        var step=new PlannedOrganizationStep(Guid.NewGuid(),0,"Alpha","Bravo",key,Chest,"Bank",1,2,
            TransferRouteKind.Direct,
            [new(OrganizationLegKind.Withdraw,"Alpha","Alpha","Bank","Inventory"),
             new(OrganizationLegKind.Exchange,"Alpha","Bravo","Inventory","Inventory"),
             new(OrganizationLegKind.Deposit,"Bravo","Bravo","Inventory","Bank")],
            ImmutableArray<Guid>.Empty,OrganizationReadiness.Ready,
            [new("Alpha","Bank",key,1,2,shape),new("Bravo","Bank",key,null,0,shape)],
            [new("Alpha","Bank",key,1,0,shape),new("Bravo","Bank",key,null,2,shape)]);
        return new(Guid.NewGuid(),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(1),
            store.ReadOrganizationState().Fingerprint,[step]);
    }

    [Fact]
    public void PreparationIsDurableAndNeverStartsSameStepTwice()
    {
        var path=Path.Combine(Path.GetTempPath(),"da-transfer-journal-"+Guid.NewGuid()+".db");
        try
        {
            Guid runId,planId;
            using(var store=new InventoryStore(path))
            {
                store.SaveSnapshot("Alpha","Bank",[Chest],true);
                store.SaveSnapshot("Bravo","Bank",[],true);
                var plan=ReadyPlan(store);planId=plan.Id;
                store.SaveOrganizationPlan(plan);store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
                var run=store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow);
                runId=run.Id;
                Assert.Equal(TransferRunState.Preparing,run.State);
                Assert.Equal("Alpha",run.LastVerifiedHolder);
                Assert.Equal(2,run.Quantity);
                Assert.Throws<InvalidOperationException>(()=>store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow));
            }
            using(var reopened=new InventoryStore(path))
            {
                var loaded=reopened.LoadTransferRun(runId);
                Assert.NotNull(loaded);
                Assert.Equal(planId,loaded.PlanId);
                Assert.Equal(TransferRunState.Preparing,loaded.State);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach(var suffix in new[]{"","-wal","-shm"})File.Delete(path+suffix);
        }
    }

    [Fact]
    public void ReconciliationKeepsLastVerifiedHolderAndBlocksReplay()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var plan=ReadyPlan(store);store.SaveOrganizationPlan(plan);
        store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        var run=store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow);
        var stopped=store.MarkTransferNeedsReconciliation(run.Id,"Session disconnected",DateTimeOffset.UtcNow);
        Assert.Equal(TransferRunState.NeedsReconciliation,stopped.State);
        Assert.Equal("Alpha",stopped.LastVerifiedHolder);
        Assert.Throws<InvalidOperationException>(()=>store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow));
    }
}
