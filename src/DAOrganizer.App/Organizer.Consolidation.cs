using DAOrganizer.Core;
using DAOrganizer.Game;

namespace DAOrganizer.App;

public sealed record ConsolidationProgress(string CurrentCharacter,string CurrentAction,long CompletedTransfers,
    long TotalTransfers,string? Blocker=null,string? LastVerifiedHolder=null,Guid? RecoveryRunId=null)
{
    public bool Finished=>Blocker is null&&CompletedTransfers==TotalTransfers;
}

public sealed partial class Organizer
{
    public ConsolidationProgress? ConsolidationStatus{get;private set;}
    public event Action<ConsolidationProgress>? ConsolidationChanged;

    public ConsolidationSummary BuildConsolidationSummary(Item item,string destination)
    {
        var key=ItemGroups.Key(item);
        var pending=Store.ListTransferRuns(200).FirstOrDefault(x=>x.State is not (TransferRunState.Complete or TransferRunState.Failed)&&
            Store.LoadOrganizationPlan(x.PlanId)?.Plan.Steps.SingleOrDefault(s=>s.Id==x.StepId)?.ItemKey==key);
        var summary=ConsolidationPlanner.Build(Store.ReadOrganizationState(),item,destination);
        if(summary.Blocker is null&&pending is not null)
            return summary with{Blocker=$"{pending.LastVerifiedHolder} holds an unfinished transfer of this item. Finish recovery first."};
        if(summary.Blocker is not null)return summary;
        if(World is null)return summary with{Blocker="Bank routes are unavailable. Reopen the app to load bundled routes, or check the optional custom folder in Settings."};
        var needed=summary.Sources.Where(x=>x.EligibleQuantity>0).Select(x=>x.Character)
            .Append(destination).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var missingAccounts=needed.Where(x=>Accounts.AccountFor(x) is null).ToArray();
        if(missingAccounts.Length>0)
            return summary with{Blocker="Assign a game account for: "+string.Join(", ",missingAccounts)};
        var missingCredentials=needed.Where(x=>Session(x)==null&&!CredentialVault.Exists(x)).ToArray();
        if(missingCredentials.Length>0)
            return summary with{Blocker="Save a login for: "+string.Join(", ",missingCredentials)};
        foreach(var name in needed.Where(x=>Session(x)?.Ready==true))
        {
            try{BankRoutes.Closest(World,Session(name)!.MapId,Session(name)!.Position);}
            catch(InvalidOperationException){return summary with{Blocker=$"No reachable bank route is known for {name}."};}
        }
        return summary;
    }

    public async Task ConsolidateToBank(Item item,string destination,CancellationToken token)
    {
        try{await ConsolidateToBankCore(item,destination,token);}
        catch(Exception ex)
        {
            if(ConsolidationStatus?.Blocker is null)
            {
                var current=ConsolidationStatus?.CurrentCharacter??destination;
                var completed=ConsolidationStatus?.CompletedTransfers??0;
                var total=ConsolidationStatus?.TotalTransfers??0;
                Report(new(current,"Stopped",completed,total,ShortReason(ex),current));
            }
            throw;
        }
    }

    private async Task ConsolidateToBankCore(Item item,string destination,CancellationToken token)
    {
        RequireLiveProfile();
        if(ManualTradeCaptureRunning)throw new InvalidOperationException("Stop the diagnostic trade capture first.");
        var summary=BuildConsolidationSummary(item,destination);
        if(!summary.CanStart)throw new InvalidOperationException(summary.Blocker??"Nothing is eligible to consolidate.");
        var preference=Store.GetItemOverride(item);
        Store.SetItemOverride(item,new(preference?.Category,destination,preference?.NeverMove??false));
        var total=summary.EligibleQuantity;
        Report(new(destination,"Logging in destination",0,total));
        var (recipient,recipientCreated)=await OpenCharacter(destination,token);
        _=recipientCreated; // The destination intentionally remains online at the bank.
        if(World is null)throw new InvalidOperationException("WorldLogs routes are unavailable.");
        var destinationRoute=BankRoutes.Closest(World,recipient.MapId,recipient.Position);
        Report(new(destination,$"Walking to {destinationRoute.Map.Name}",0,total));
        await TravelToBank(recipient,destinationRoute,token);
        var completed=0;
        foreach(var source in summary.Sources.Where(x=>x.EligibleQuantity>0))
        {
            GameSession? sender=null;UpdatingClient? client=null;
            try
            {
                Report(new(source.Character,"Logging in source",completed,total));
                (sender,_)=await OpenCharacter(source.Character,token);
                client=new(this,sender,source.Character,false);
                // Route proof happens after login but before this source moves or any item action occurs.
                var route=World.Route(sender.MapId,destinationRoute.Map.Id);
                Report(new(source.Character,$"Walking to {destinationRoute.Map.Name}",completed,total));
                await TravelToBank(sender,destinationRoute,route,token);
                var refreshed=ConsolidationPlanner.Build(Store.ReadOrganizationState(),item,destination).Sources
                    .SingleOrDefault(x=>x.Character.Equals(source.Character,StringComparison.OrdinalIgnoreCase));
                if(refreshed?.EligibleQuantity!=source.EligibleQuantity)
                    throw new InvalidOperationException($"{source.Character}'s eligible quantity changed. Review the summary again; no item was moved.");
                var remaining=source.EligibleQuantity;
                while(remaining>0)
                {
                    token.ThrowIfCancellationRequested();
                    await new Navigation(World,Path.GetDirectoryName(ClientPath)!).Meet(sender,recipient,token);
                    var step=FindConsolidationStep(summary.ItemKey,source.Character,destination);
                    Report(new(source.Character,$"Transferring 1 {summary.Item.Name}",completed,total));
                    await ExecuteOneUnitDirectTrial(step,token);
                    remaining--;completed++;
                    Report(new(source.Character,$"Verified {completed:N0} of {total:N0}",completed,total));
                }
                Report(new(source.Character,"Logging out safely",completed,total));
                await client.Logout(token);await client.Close(token);client=null;
            }
            catch(Exception ex)
            {
                var pending=Store.ListTransferRuns(20).FirstOrDefault(x=>
                    x.SourceCharacter.Equals(source.Character,StringComparison.OrdinalIgnoreCase)&&
                    x.DestinationCharacter.Equals(destination,StringComparison.OrdinalIgnoreCase)&&
                    x.State==TransferRunState.NeedsReconciliation);
                var holder=pending?.LastVerifiedHolder??source.Character;
                Report(new(source.Character,"Stopped",completed,total,ShortReason(ex),holder,pending?.Id));
                throw;
            }
        }
        Report(new(destination,"Complete — destination remains at the bank",completed,total));
    }

    private PlannedOrganizationStep FindConsolidationStep(string itemKey,string source,string destination)
    {
        var state=Store.ReadOrganizationState();var now=DateTimeOffset.UtcNow;
        var plan=OrganizationPlanner.BuildExact(state,now,now.AddMinutes(30))
            ??throw new InvalidOperationException("Saved item quantities changed; consolidation stopped before another transfer.");
        return plan.Steps.FirstOrDefault(x=>x.ItemKey==itemKey&&
            x.SourceCharacter.Equals(source,StringComparison.OrdinalIgnoreCase)&&
            x.DestinationCharacter.Equals(destination,StringComparison.OrdinalIgnoreCase)&&
            x.RouteKind==TransferRouteKind.Direct)
            ??throw new InvalidOperationException("No eligible direct quantity remains for this source.");
    }

    private async Task<(GameSession Session,bool Created)> OpenCharacter(string name,CancellationToken token)
    {
        if(Session(name) is {Ready:true} existing)return(existing,false);
        if(Session(name) is not null)throw new InvalidOperationException($"{name} is connected but not ready.");
        var session=await Launch();var client=new UpdatingClient(this,session,name,false);
        try{await client.Login(token);return(session,true);}
        catch{session.Dispose();Sessions.Remove(session);throw;}
    }

    private void Report(ConsolidationProgress progress)
    {
        ConsolidationStatus=progress;ConsolidationChanged?.Invoke(progress);
    }
}
