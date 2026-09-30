using System.Text.Json;
using Arbiter.Net.Types;
using DAOrganizer.Core;
using DAOrganizer.Game;

namespace DAOrganizer.App;

public sealed partial class Organizer
{
    public async Task ExecuteOneUnitDirectTrial(PlannedOrganizationStep selected,CancellationToken token)
    {
        RequireLiveProfile();
        if(ManualTradeCaptureRunning)throw new InvalidOperationException("Stop the manual trade capture first.");
        token.ThrowIfCancellationRequested();
        if(selected.RouteKind!=TransferRouteKind.Direct)
            throw new InvalidOperationException("Choose a Direct route.");
        var sender=Session(selected.SourceCharacter)??throw new InvalidOperationException("Source session is offline.");
        var recipient=Session(selected.DestinationCharacter)??throw new InvalidOperationException("Recipient session is offline.");
        if(selected.SourceLocation=="Bank")await sender.ScanNearbyBank(null,token);
        if(Store.Freshness(recipient.Name,"Bank")!="Current")await recipient.ScanNearbyBank(null,token);
        var state=Store.ReadOrganizationState();
        var now=DateTimeOffset.UtcNow;
        var exact=OrganizationPlanner.BuildExact(state,now,now.AddMinutes(30))
            ??throw new InvalidOperationException("No route remains after refreshing bank contents.");
        var match=exact.Steps.FirstOrDefault(x=>x.RouteKind==TransferRouteKind.Direct&&
            x.SourceCharacter.Equals(selected.SourceCharacter,StringComparison.OrdinalIgnoreCase)&&
            x.DestinationCharacter.Equals(selected.DestinationCharacter,StringComparison.OrdinalIgnoreCase)&&
            x.SourceLocation==selected.SourceLocation&&x.SourceSlot==selected.SourceSlot&&
            OrganizationPlanContract.ItemFingerprint(x.SourceItem)==OrganizationPlanContract.ItemFingerprint(selected.SourceItem))
            ??throw new InvalidOperationException("Selected route changed after bank refresh. Choose it again.");
        var chosen=OrganizationPlanSelection.SelectOneUnit(exact,state,match.Id);
        var ready=DirectPlanReadiness.Promote(chosen,state,
            sender.CaptureTradeEndpoint(recipient.Name,now),
            recipient.CaptureTradeEndpoint(sender.Name,now),now,true) with{Id=Guid.NewGuid()};
        Store.SaveOrganizationPlan(ready);
        Store.ApproveOrganizationPlan(ready.Id,DateTimeOffset.UtcNow);
        await ExecuteApprovedDirectPlan(ready.Id,token);
    }

    public async Task ExecuteApprovedDirectPlan(Guid planId,CancellationToken token)
    {
        RequireLiveProfile();
        if(ManualTradeCaptureRunning)throw new InvalidOperationException("Stop the manual trade capture first.");
        var saved=Store.LoadOrganizationPlan(planId)??throw new KeyNotFoundException("Plan not found.");
        if(saved.Approval!=PlanApprovalState.Approved||saved.Plan.Steps.Length!=1)
            throw new InvalidOperationException("Approve one ready direct step before execution.");
        var step=Store.CheckNextOrganizationStep(planId,DateTimeOffset.UtcNow)
            ??throw new InvalidOperationException("Plan is complete.");
        if(step.RouteKind!=TransferRouteKind.Direct||step.SourceLocation is not ("Inventory" or "Bank")||
            step.Readiness!=OrganizationReadiness.Ready)
            throw new InvalidOperationException("This executor supports an approved direct step.");
        var sender=Session(step.SourceCharacter)??throw new InvalidOperationException("Source session is offline.");
        var recipient=Session(step.DestinationCharacter)??throw new InvalidOperationException("Recipient session is offline.");
        var now=DateTimeOffset.UtcNow;
        var source=sender.CaptureTradeEndpoint(recipient.Name,now);
        var destination=recipient.CaptureTradeEndpoint(sender.Name,now);
        var bankState=Store.ReadOrganizationState();
        // Reuse the same capacity and tradeability gate used when making the step Ready.
        DirectPlanReadiness.Promote(saved.Plan with
        {
            Steps=[step with{Readiness=OrganizationReadiness.NeedsScan,ControlledTrial=false}]
        },bankState,source,destination,now,step.ControlledTrial);
        Item? carried=step.SourceLocation=="Inventory"?
            source.Inventory.SingleOrDefault(x=>x.Slot==step.SourceSlot):null;
        if(step.SourceLocation=="Inventory")
        {
            if(carried is null)throw new InvalidOperationException("Approved source slot is absent.");
            DirectTradePreflight.Check(source,destination,step,carried,now);
        }
        else DirectTradePreflight.CheckPartners(source,destination,step,now);
        DateTimeOffset? approvedBankScan=null;
        if(step.SourceLocation=="Bank")
        {
            await sender.ScanNearbyBank(null,token);
            approvedBankScan=sender.LastBankScan;
            // A changed bank is rejected before the custody journal or any item packet.
            Store.CheckNextOrganizationStep(planId,DateTimeOffset.UtcNow);
        }
        var run=Store.BeginTransferPreparation(planId,DateTimeOffset.UtcNow);
        var senderCapture=false;var recipientCapture=false;
        ManualTradeResult? senderResult=null,recipientResult=null;
        try
        {
            if(step.SourceLocation=="Bank")
            {
                carried=await sender.WithdrawApprovedTransferOne(step.SourceItem,approvedBankScan!.Value,token);
                if(carried is null)throw new InvalidOperationException("Source withdrawal was not confirmed; no exchange sent.");
            }
            var ordered=new[]{sender,recipient}.OrderBy(x=>x.ProcessId).ToArray();
            using var first=await ordered[0].AcquireExchangeLease(token);
            using var second=await ordered[1].AcquireExchangeLease(token);
            var senderLease=ReferenceEquals(ordered[0],sender)?first:second;
            var recipientLease=ReferenceEquals(ordered[0],recipient)?first:second;
            void CheckPartners()
            {
                senderLease.Check();recipientLease.Check();
                var s=sender.CaptureTradeEndpoint(recipient.Name,DateTimeOffset.UtcNow);
                var r=recipient.CaptureTradeEndpoint(sender.Name,DateTimeOffset.UtcNow);
                if(s.Partner.Id!=r.PlayerId||r.Partner.Id!=s.PlayerId||s.MapId!=r.MapId||
                    s.Partner.Position!=r.Position||r.Partner.Position!=s.Position||
                    Math.Abs(s.Position.X-r.Position.X)+Math.Abs(s.Position.Y-r.Position.Y)!=1)
                    throw new InvalidOperationException("Trade partner identity or position changed.");
            }
            now=DateTimeOffset.UtcNow;
            source=sender.CaptureTradeEndpoint(recipient.Name,now);
            destination=recipient.CaptureTradeEndpoint(sender.Name,now);
            var offeredItem=carried??throw new InvalidOperationException("Withdrawn item is missing.");
            var targets=DirectTradePreflight.Check(source,destination,step,offeredItem,now);
            Store.AdvanceTransferRun(run.Id,TransferRunState.Preparing,TransferRunState.InSourceInventory,
                "Exact source slot, recipient room and partners checked",DateTimeOffset.UtcNow);
            sender.StartManualTradeCapture(run.Id.ToString("N"));senderCapture=true;
            recipient.StartManualTradeCapture(run.Id.ToString("N"));recipientCapture=true;
            CheckPartners();token.ThrowIfCancellationRequested();
            Store.AdvanceTransferRun(run.Id,TransferRunState.InSourceInventory,TransferRunState.ExchangeOpen,
                "Exchange open intent persisted",DateTimeOffset.UtcNow);
            senderLease.Send(ExchangeClientActionType.BeginExchange,targets.SenderTargetId);
            await WaitTradeSignal(()=>sender.PeekManualTradeCapture(),0,TimeSpan.FromSeconds(8),CheckPartners,token);
            await WaitTradeSignal(()=>recipient.PeekManualTradeCapture(),0,TimeSpan.FromSeconds(8),CheckPartners,token);
            CheckPartners();token.ThrowIfCancellationRequested();
            senderLease.Send(ExchangeClientActionType.AddItem,targets.SenderTargetId,(byte)offeredItem.Slot);
            if(offeredItem.Quantity>1)
            {
                await WaitTradeSignal(()=>sender.PeekManualTradeCapture(),1,TimeSpan.FromSeconds(8),CheckPartners,token);
                CheckPartners();token.ThrowIfCancellationRequested();
                senderLease.Send(ExchangeClientActionType.AddStackableItem,targets.SenderTargetId,
                    (byte)offeredItem.Slot,(byte)step.Quantity);
            }
            var offerDeadline=DateTimeOffset.UtcNow.AddSeconds(8);
            ManualTradeOfferAnalysis offer;
            do
            {
                CheckPartners();token.ThrowIfCancellationRequested();
                var a=sender.PeekManualTradeCapture();var b=recipient.PeekManualTradeCapture();
                if(TradeCancelled(a)||TradeCancelled(b))throw new InvalidOperationException("Exchange was cancelled or denied.");
                offer=ManualTradeAnalyzer.AnalyzeOffer(a,b);
                if(offer.Verified)break;
                if(DateTimeOffset.UtcNow>=offerDeadline)throw new TimeoutException("Exact two-sided offer was not verified: "+offer.Reason);
                await Task.Delay(50,token);
            }while(true);
            if(!offer.Sender!.Equals(step.SourceCharacter,StringComparison.OrdinalIgnoreCase)||
                !offer.Recipient!.Equals(step.DestinationCharacter,StringComparison.OrdinalIgnoreCase)||
                offer.Quantity!=step.Quantity||offer.Item is null||
                !OrganizationPlanContract.MatchesObservedItem(offer.Item,step.SourceItem))
                throw new InvalidOperationException("Offer differs from the approved item or quantity.");
            Store.AdvanceTransferRun(run.Id,TransferRunState.ExchangeOpen,TransferRunState.Offered,
                "Two-sided exact offer verified",DateTimeOffset.UtcNow);
            CheckPartners();token.ThrowIfCancellationRequested();
            Store.AdvanceTransferRun(run.Id,TransferRunState.Offered,TransferRunState.Accepting,
                "Acceptance intent persisted before first accept",DateTimeOffset.UtcNow);
            senderLease.Send(ExchangeClientActionType.Accept,targets.SenderTargetId);
            CheckPartners();token.ThrowIfCancellationRequested();
            recipientLease.Send(ExchangeClientActionType.Accept,targets.RecipientTargetId);
            var deliveryDeadline=DateTimeOffset.UtcNow.AddSeconds(10);
            ManualTradeAnalysis delivery;
            do
            {
                CheckPartners();token.ThrowIfCancellationRequested();
                var a=sender.PeekManualTradeCapture();var b=recipient.PeekManualTradeCapture();
                if(TradeCancelled(a)||TradeCancelled(b))throw new InvalidOperationException("Exchange was cancelled.");
                delivery=ManualTradeAnalyzer.Analyze(a,b);
                if(delivery.Verified)break;
                if(DateTimeOffset.UtcNow>=deliveryDeadline)
                    throw new TimeoutException("Recipient delivery was not proven: "+delivery.Reason);
                await Task.Delay(50,token);
            }while(true);
            if(delivery.Quantity!=step.Quantity||delivery.Item is null||
                !OrganizationPlanContract.MatchesObservedItem(delivery.Item,step.SourceItem))
                throw new InvalidOperationException("Delivered item differs from the approved step.");
            senderResult=sender.StopManualTradeCapture();senderCapture=false;
            recipientResult=recipient.StopManualTradeCapture();recipientCapture=false;
            Store.AdvanceTransferRun(run.Id,TransferRunState.Accepting,TransferRunState.RecipientVerified,
                "Both acceptances and exact recipient inventory gain verified",DateTimeOffset.UtcNow);
        }
        catch(Exception ex)
        {
            if(senderCapture)senderResult=sender.StopManualTradeCapture();
            if(recipientCapture)recipientResult=recipient.StopManualTradeCapture();
            if(senderResult!=null&&recipientResult!=null)
            {
                try{SaveTransferTrace(run.Id,senderResult,recipientResult);}
                catch(IOException){/* Journal remains the recovery authority if local diagnostics fail. */}
            }
            Store.MarkTransferNeedsReconciliation(run.Id,ShortReason(ex),DateTimeOffset.UtcNow);
            throw;
        }
        try{SaveTransferTrace(run.Id,senderResult!,recipientResult!);}
        catch(IOException){/* Delivery evidence is in memory and the journal remains durable. */}
        // Both exchange leases have ended before travel and guarded bank actions.
        try
        {
            Store.AdvanceTransferRun(run.Id,TransferRunState.RecipientVerified,TransferRunState.Banking,
                "Recipient holds verified item; banking intent persisted",DateTimeOffset.UtcNow);
            await TravelToBank(sender,token);
            await sender.ScanNearbyBank(null,token);
            await TravelToBank(recipient,token);
            await recipient.ScanNearbyBank(null,token);
            var received=recipient.Inventory().Where(x=>ItemGroups.Key(x)==step.ItemKey&&
                OrganizationPlanContract.MatchesObservedItem(x,step.SourceItem))
                .OrderBy(x=>x.Slot).ToArray();
            if(received.Length!=1||received[0].Quantity<step.Quantity)
                throw new InvalidOperationException("Recipient item is not uniquely identified for banking.");
            if(!await recipient.DepositApprovedTransfer(received[0],step.Quantity,token))
                throw new InvalidOperationException("Bank did not confirm inventory decrease; item may remain with recipient.");
            await recipient.ScanNearbyBank(null,token);
            await GameSession.WaitUntil(()=>OrganizationPlanContract.Matches(Store.ReadOrganizationState(),step.ExpectedAfter),
                TimeSpan.FromSeconds(10),token);
            Store.ConfirmOrganizationStep(planId,step.Id,run.BeforeFingerprint,DateTimeOffset.UtcNow);
            Store.AdvanceTransferRun(run.Id,TransferRunState.Banking,TransferRunState.Complete,
                "Destination bank scan and plan checkpoint verified",DateTimeOffset.UtcNow);
        }
        catch(Exception ex)
        {
            Store.MarkTransferNeedsReconciliation(run.Id,ShortReason(ex),DateTimeOffset.UtcNow);
            throw;
        }
    }

    private static string ShortReason(Exception error)=>error.Message.Length<=256?error.Message:error.Message[..256];

    private static bool TradeCancelled(ManualTradeResult result)=>result.Packets.Any(x=>x.Direction=="Server"&&
        x.Opcode==0x42&&x.PayloadHex.StartsWith("04",StringComparison.Ordinal));

    private static async Task WaitTradeSignal(Func<ManualTradeResult> read,byte eventCode,TimeSpan timeout,
        Action guard,CancellationToken token)
    {
        var end=DateTimeOffset.UtcNow+timeout;
        while(true)
        {
            guard();token.ThrowIfCancellationRequested();
            var result=read();
            if(TradeCancelled(result))throw new InvalidOperationException("Exchange was cancelled or denied.");
            if(result.Packets.Any(x=>x.Direction=="Server"&&x.Opcode==0x42&&x.PayloadHex.Length>=2&&
                Convert.FromHexString(x.PayloadHex)[0]==eventCode))return;
            if(DateTimeOffset.UtcNow>=end)throw new TimeoutException("Exchange server response was not observed; no retry sent.");
            await Task.Delay(50,token);
        }
    }

    private string SaveTransferTrace(Guid id,ManualTradeResult first,ManualTradeResult second)
    {
        var directory=Path.Combine(DataDirectory,"diagnostics","transfer-runs",id.ToString("N"));
        Directory.CreateDirectory(directory);
        var options=new JsonSerializerOptions{WriteIndented=true};
        File.WriteAllText(Path.Combine(directory,"first.json"),JsonSerializer.Serialize(first,options));
        File.WriteAllText(Path.Combine(directory,"second.json"),JsonSerializer.Serialize(second,options));
        File.WriteAllText(Path.Combine(directory,"analysis.json"),JsonSerializer.Serialize(
            ManualTradeAnalyzer.Analyze(first,second),options));
        return directory;
    }
}
