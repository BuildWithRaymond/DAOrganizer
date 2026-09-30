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

    [Theory]
    [InlineData("Source bank scan is too old for an approved withdrawal.")]
    [InlineData("Approach and scan the same nearby banker before withdrawal.")]
    public void KnownPreSendWithdrawalFailureReleasesOnlyItsSourceReservation(string reason)
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var plan=ReadyPlan(store);store.SaveOrganizationPlan(plan);
        store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        var run=store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow);
        store.MarkTransferNeedsReconciliation(run.Id,reason,DateTimeOffset.UtcNow);

        Assert.Equal(1,store.ResolveKnownNoSendWithdrawalFailures(DateTimeOffset.UtcNow));
        Assert.Equal(TransferRunState.Failed,store.LoadTransferRun(run.Id)!.State);
        Assert.Equal(0,store.ResolveKnownNoSendWithdrawalFailures(DateTimeOffset.UtcNow));

        var newPlan=ReadyPlan(store);store.SaveOrganizationPlan(newPlan);
        store.ApproveOrganizationPlan(newPlan.Id,DateTimeOffset.UtcNow);
        Assert.Equal(TransferRunState.Preparing,
            store.BeginTransferPreparation(newPlan.Id,DateTimeOffset.UtcNow).State);
    }

    [Fact]
    public void BankAgeReasonAfterCustodyActionStillNeedsReconciliation()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var plan=ReadyPlan(store);store.SaveOrganizationPlan(plan);
        store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        var run=store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow);
        store.AdvanceTransferRun(run.Id,TransferRunState.Preparing,TransferRunState.InSourceInventory,
            "Synthetic withdrawal",DateTimeOffset.UtcNow);
        store.MarkTransferNeedsReconciliation(run.Id,
            "Source bank scan is too old for an approved withdrawal.",DateTimeOffset.UtcNow);

        Assert.Equal(0,store.ResolveKnownNoSendWithdrawalFailures(DateTimeOffset.UtcNow));
        Assert.Equal(TransferRunState.NeedsReconciliation,store.LoadTransferRun(run.Id)!.State);
    }

    [Fact]
    public void JournalStagesAreOrderedAndOnlyDeliveryChangesHolder()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var plan=ReadyPlan(store);store.SaveOrganizationPlan(plan);
        store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        var run=store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(()=>store.AdvanceTransferRun(run.Id,TransferRunState.Preparing,
            TransferRunState.Accepting,"Skipped stages",DateTimeOffset.UtcNow));
        foreach(var (from,to) in new[]{
            (TransferRunState.Preparing,TransferRunState.InSourceInventory),
            (TransferRunState.InSourceInventory,TransferRunState.ExchangeOpen),
            (TransferRunState.ExchangeOpen,TransferRunState.Offered),
            (TransferRunState.Offered,TransferRunState.Accepting)})
        {
            run=store.AdvanceTransferRun(run.Id,from,to,"Synthetic stage",DateTimeOffset.UtcNow);
            Assert.Equal("Alpha",run.LastVerifiedHolder);
        }
        Assert.Throws<InvalidOperationException>(()=>store.AdvanceTransferRun(run.Id,TransferRunState.Offered,
            TransferRunState.RecipientVerified,"Wrong checkpoint",DateTimeOffset.UtcNow));
        run=store.AdvanceTransferRun(run.Id,TransferRunState.Accepting,TransferRunState.RecipientVerified,
            "Two-sided delivery verified",DateTimeOffset.UtcNow);
        Assert.Equal("Bravo",run.LastVerifiedHolder);
        run=store.MarkTransferNeedsReconciliation(run.Id,"Bank refused deposit",DateTimeOffset.UtcNow);
        Assert.Equal("Bravo",run.LastVerifiedHolder);
        Assert.Equal(TransferRunState.NeedsReconciliation,run.State);
        Assert.Throws<InvalidOperationException>(()=>store.AdvanceTransferRun(run.Id,TransferRunState.NeedsReconciliation,
            TransferRunState.Banking,"Unsafe replay",DateTimeOffset.UtcNow));
    }
}
