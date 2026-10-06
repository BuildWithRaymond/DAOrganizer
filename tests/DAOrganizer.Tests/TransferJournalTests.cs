using System.Collections.Immutable;
using System.Text.Json;
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

    [Fact]
    public void SavedDeliveryEvidenceCorrectsHolderWithoutReplayingOrCompletingBanking()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var plan=ReadyPlan(store);store.SaveOrganizationPlan(plan);
        store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        var run=store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow);
        foreach(var (from,to) in new[]{
            (TransferRunState.Preparing,TransferRunState.InSourceInventory),
            (TransferRunState.InSourceInventory,TransferRunState.ExchangeOpen),
            (TransferRunState.ExchangeOpen,TransferRunState.Offered),
            (TransferRunState.Offered,TransferRunState.Accepting)})
            store.AdvanceTransferRun(run.Id,from,to,"Synthetic stage",DateTimeOffset.UtcNow);
        store.MarkTransferNeedsReconciliation(run.Id,
            "Recipient delivery was not proven: Complete two-sided acceptance and final delivery are not proven.",
            DateTimeOffset.UtcNow);

        var nextPlan=ReadyPlan(store);store.SaveOrganizationPlan(nextPlan);
        store.ApproveOrganizationPlan(nextPlan.Id,DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(()=>store.BeginTransferPreparation(nextPlan.Id,DateTimeOffset.UtcNow));

        var corrected=store.RecordVerifiedRecipientDelivery(run.Id,DateTimeOffset.UtcNow);
        Assert.Equal(TransferRunState.NeedsReconciliation,corrected.State);
        Assert.Equal("Bravo",corrected.LastVerifiedHolder);
        Assert.Contains("bank deposit not confirmed",corrected.Reason);
        Assert.Throws<InvalidOperationException>(()=>store.RecordVerifiedRecipientDelivery(run.Id,DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(()=>store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow));
        Assert.Equal(TransferRunState.Preparing,
            store.BeginTransferPreparation(nextPlan.Id,DateTimeOffset.UtcNow).State);
    }

    [Fact]
    public void UncommittedQuantityPromptCanReleaseReservationButNeverReplayOriginalStep()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var plan=ReadyPlan(store);store.SaveOrganizationPlan(plan);
        store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        var run=store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow);
        store.AdvanceTransferRun(run.Id,TransferRunState.Preparing,TransferRunState.InSourceInventory,
            "Synthetic withdrawal",DateTimeOffset.UtcNow);
        store.AdvanceTransferRun(run.Id,TransferRunState.InSourceInventory,TransferRunState.ExchangeOpen,
            "Synthetic exchange",DateTimeOffset.UtcNow);
        store.MarkTransferNeedsReconciliation(run.Id,
            "Exact two-sided offer was not verified: Source slot or quantity prompt is ambiguous.",DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(()=>store.ReleaseUncommittedQuantityPrompt(run.Id,
            DateTimeOffset.UtcNow.AddSeconds(-1)));
        var released=store.ReleaseUncommittedQuantityPrompt(run.Id,DateTimeOffset.UtcNow);
        Assert.Equal(TransferRunState.Failed,released.State);
        Assert.Equal("Alpha",released.LastVerifiedHolder);
        Assert.Throws<InvalidOperationException>(()=>store.ReleaseUncommittedQuantityPrompt(run.Id,DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(()=>store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow));
        var nextPlan=ReadyPlan(store);store.SaveOrganizationPlan(nextPlan);
        store.ApproveOrganizationPlan(nextPlan.Id,DateTimeOffset.UtcNow);
        Assert.Equal(TransferRunState.Preparing,
            store.BeginTransferPreparation(nextPlan.Id,DateTimeOffset.UtcNow).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeliveredUnitCanFinishBankingOnceWithoutRepeatingExchange(bool sourceSnapshotMatches)
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var original=ReadyPlan(store);var step=original.Steps[0];
        var plan=original with{Steps=[step with{Quantity=1,ExpectedAfter=
            [step.ExpectedAfter[0] with{Quantity=1},step.ExpectedAfter[1] with{Quantity=1}]}]};
        store.SaveOrganizationPlan(plan);store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        var run=store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow);
        foreach(var (from,to) in new[]{
            (TransferRunState.Preparing,TransferRunState.InSourceInventory),
            (TransferRunState.InSourceInventory,TransferRunState.ExchangeOpen),
            (TransferRunState.ExchangeOpen,TransferRunState.Offered),
            (TransferRunState.Offered,TransferRunState.Accepting),
            (TransferRunState.Accepting,TransferRunState.RecipientVerified),
            (TransferRunState.RecipientVerified,TransferRunState.Banking)})
            store.AdvanceTransferRun(run.Id,from,to,"Synthetic stage",DateTimeOffset.UtcNow);
        store.MarkTransferNeedsReconciliation(run.Id,
            "Approach and scan the same nearby banker before an approved deposit.",DateTimeOffset.UtcNow);
        Assert.Equal(TransferRunState.Banking,store.BeginRecoveredDeposit(run.Id,DateTimeOffset.UtcNow).State);
        Assert.Throws<InvalidOperationException>(()=>store.BeginRecoveredDeposit(run.Id,DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(()=>store.CompleteRecoveredDeposit(run.Id,DateTimeOffset.UtcNow));
        store.SaveSnapshot("Alpha","Bank",sourceSnapshotMatches?[Chest with{Quantity=1}]:[],true);
        store.SaveSnapshot("Bravo","Bank",[Chest with{Quantity=1}],true);
        store.SaveSnapshot("Bravo","Inventory",[],true);
        Assert.Equal(TransferRunState.Complete,store.CompleteRecoveredDeposit(run.Id,DateTimeOffset.UtcNow).State);
        Assert.Equal(PlanApprovalState.Completed,store.LoadOrganizationPlan(plan.Id)!.Approval);
        Assert.Throws<InvalidOperationException>(()=>store.BeginRecoveredDeposit(run.Id,DateTimeOffset.UtcNow));
    }

    [Fact]
    public void FailedRecoveryDepositCannotRequestAnotherAttempt()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[Chest],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        var original=ReadyPlan(store);var step=original.Steps[0];
        var plan=original with{Steps=[step with{Quantity=1,ExpectedAfter=
            [step.ExpectedAfter[0] with{Quantity=1},step.ExpectedAfter[1] with{Quantity=1}]}]};
        store.SaveOrganizationPlan(plan);store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
        var run=store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow);
        foreach(var (from,to) in new[]{
            (TransferRunState.Preparing,TransferRunState.InSourceInventory),
            (TransferRunState.InSourceInventory,TransferRunState.ExchangeOpen),
            (TransferRunState.ExchangeOpen,TransferRunState.Offered),
            (TransferRunState.Offered,TransferRunState.Accepting),
            (TransferRunState.Accepting,TransferRunState.RecipientVerified),
            (TransferRunState.RecipientVerified,TransferRunState.Banking)})
            store.AdvanceTransferRun(run.Id,from,to,"Synthetic stage",DateTimeOffset.UtcNow);
        const string oldGate="Approach and scan the same nearby banker before an approved deposit.";
        store.MarkTransferNeedsReconciliation(run.Id,oldGate,DateTimeOffset.UtcNow);
        store.BeginRecoveredDeposit(run.Id,DateTimeOffset.UtcNow);
        store.MarkTransferNeedsReconciliation(run.Id,oldGate,DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(()=>store.BeginRecoveredDeposit(run.Id,DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReopeningProfileUsesOnlyValidTwoSessionCaptureToCorrectLastHolder(bool malformed)
    {
        var directory=Path.Combine(Path.GetTempPath(),"da-transfer-recovery-"+Guid.NewGuid().ToString("N"));
        try
        {
            Guid runId;
            using(var app=new DAOrganizer.App.Organizer(directory))
            {
                var (sender,recipient)=ManualTradeAnalyzerTests.PartialStack();
                var item=Assert.Single(sender.BeforeInventory);
                app.Store.SaveSnapshot("Alpha","Bank",[item],true);
                app.Store.SaveSnapshot("Beta","Bank",[],true);
                var key=ItemGroups.Key(item);var shape=OrganizationPlanContract.ItemFingerprint(item);
                var step=new PlannedOrganizationStep(Guid.NewGuid(),0,"Alpha","Beta",key,item,"Bank",item.Slot,2,
                    TransferRouteKind.Direct,
                    [new(OrganizationLegKind.Withdraw,"Alpha","Alpha","Bank","Inventory"),
                     new(OrganizationLegKind.Exchange,"Alpha","Beta","Inventory","Inventory"),
                     new(OrganizationLegKind.Deposit,"Beta","Beta","Inventory","Bank")],
                    ImmutableArray<Guid>.Empty,OrganizationReadiness.Ready,
                    [new("Alpha","Bank",key,item.Slot,5,shape),new("Beta","Bank",key,null,0,shape)],
                    [new("Alpha","Bank",key,item.Slot,3,shape),new("Beta","Bank",key,null,2,shape)]);
                var plan=new ExactOrganizationPlan(Guid.NewGuid(),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(1),
                    app.Store.ReadOrganizationState().Fingerprint,[step]);
                app.Store.SaveOrganizationPlan(plan);app.Store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
                var run=app.Store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow);runId=run.Id;
                foreach(var (from,to) in new[]{
                    (TransferRunState.Preparing,TransferRunState.InSourceInventory),
                    (TransferRunState.InSourceInventory,TransferRunState.ExchangeOpen),
                    (TransferRunState.ExchangeOpen,TransferRunState.Offered),
                    (TransferRunState.Offered,TransferRunState.Accepting)})
                    app.Store.AdvanceTransferRun(run.Id,from,to,"Synthetic stage",DateTimeOffset.UtcNow);
                app.Store.MarkTransferNeedsReconciliation(run.Id,
                    "Recipient delivery was not proven: Complete two-sided acceptance and final delivery are not proven.",
                    DateTimeOffset.UtcNow);
                var trace=Path.Combine(directory,"diagnostics","transfer-runs",run.Id.ToString("N"));
                Directory.CreateDirectory(trace);
                File.WriteAllText(Path.Combine(trace,"first.json"),JsonSerializer.Serialize(sender with
                {OperationId=run.Id.ToString("N"),Character=malformed?null!:sender.Character}));
                File.WriteAllText(Path.Combine(trace,"second.json"),JsonSerializer.Serialize(recipient with{OperationId=run.Id.ToString("N")}));
            }
            using(var reopened=new DAOrganizer.App.Organizer(directory))
            {
                var run=reopened.Store.LoadTransferRun(runId)!;
                Assert.Equal(malformed?"Alpha":"Beta",run.LastVerifiedHolder);
                Assert.Equal(TransferRunState.NeedsReconciliation,run.State);
            }
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReopeningProfileReleasesOnlyUncommittedQuantityPromptWithMatchingSnapshots(bool changedSnapshot)
    {
        var directory=Path.Combine(Path.GetTempPath(),"da-transfer-prompt-"+Guid.NewGuid().ToString("N"));
        try
        {
            Guid runId;
            using(var app=new DAOrganizer.App.Organizer(directory))
            {
                app.Store.SaveSnapshot("Alpha","Bank",[Chest],true);
                app.Store.SaveSnapshot("Bravo","Bank",[],true);
                var carried=Chest with{Slot=38,Quantity=1};
                app.Store.SaveSnapshot("Alpha","Inventory",[carried],true);
                app.Store.SaveSnapshot("Bravo","Inventory",[],true);
                var original=ReadyPlan(app.Store);
                var step=original.Steps[0];
                var plan=original with{Steps=[step with{Quantity=1,ExpectedAfter=
                    [step.ExpectedAfter[0] with{Quantity=1},step.ExpectedAfter[1] with{Quantity=1}]}]};
                app.Store.SaveOrganizationPlan(plan);app.Store.ApproveOrganizationPlan(plan.Id,DateTimeOffset.UtcNow);
                var run=app.Store.BeginTransferPreparation(plan.Id,DateTimeOffset.UtcNow);runId=run.Id;
                app.Store.AdvanceTransferRun(run.Id,TransferRunState.Preparing,TransferRunState.InSourceInventory,
                    "Synthetic withdrawal",DateTimeOffset.UtcNow);
                app.Store.AdvanceTransferRun(run.Id,TransferRunState.InSourceInventory,TransferRunState.ExchangeOpen,
                    "Synthetic exchange",DateTimeOffset.UtcNow);
                app.Store.MarkTransferNeedsReconciliation(run.Id,
                    "Exact two-sided offer was not verified: Source slot or quantity prompt is ambiguous.",DateTimeOffset.UtcNow);
                var (sender,recipient)=ManualTradeAnalyzerTests.PartialStack();
                var trace=Path.Combine(directory,"diagnostics","transfer-runs",run.Id.ToString("N"));
                var senderStart=ManualTradeAnalyzerTests.Server(new Arbiter.Net.Server.Messages.ServerExchangeMessage
                {Event=Arbiter.Net.Types.ExchangeServerEventType.Started,TargetId=202,TargetName="Bravo"})
                    with{ObservedAt=sender.Packets[0].ObservedAt};
                Directory.CreateDirectory(trace);
                File.WriteAllText(Path.Combine(trace,"first.json"),JsonSerializer.Serialize(sender with
                {OperationId=run.Id.ToString("N"),BeforeInventory=[carried],AfterInventory=[carried],
                    Packets=[senderStart,..sender.Packets.Skip(1).Take(3)]}));
                File.WriteAllText(Path.Combine(trace,"second.json"),JsonSerializer.Serialize(recipient with
                {OperationId=run.Id.ToString("N"),Character="Bravo",AfterInventory=[],
                    Packets=recipient.Packets.Take(1).ToArray()}));
                if(changedSnapshot)app.Store.SaveSnapshot("Alpha","Inventory",[],true);
            }
            using(var reopened=new DAOrganizer.App.Organizer(directory))
                Assert.Equal(changedSnapshot?TransferRunState.NeedsReconciliation:TransferRunState.Failed,
                    reopened.Store.LoadTransferRun(runId)!.State);
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
}
