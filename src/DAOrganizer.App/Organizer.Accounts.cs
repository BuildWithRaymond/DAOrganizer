using DAOrganizer.Core;
using DAOrganizer.Game;
namespace DAOrganizer.App;
public sealed partial class Organizer
{
    public Dictionary<string,AccountProgress> QueueProgress {get;}=new(StringComparer.OrdinalIgnoreCase);
    public string QueueStatus {get;private set;}="";
    public bool QueueRunning {get;private set;}
    public async Task RefreshBank(GameSession session,bool autoDeposit,CancellationToken token)
    {
        RequireLiveProfile();
        if(!session.Ready)throw new InvalidOperationException("Wait for login and a complete inventory before scanning.");
        try{await GameSession.WaitUntil(()=>session.Mundanes().Length>0||session.Error!=null,TimeSpan.FromSeconds(5),token);}
        catch(TimeoutException){throw new InvalidOperationException("No NPC is visible. Approach an NPC and scan again; saved bank contents were retained.");}
        if(session.Error!=null)throw new InvalidOperationException(session.Error);
        await session.ScanNearbyBank(null,token);
        var rows=session.Inventory().Select(x=>new StoredItem(session.Name,"Inventory",x,DateTimeOffset.UtcNow));
        if(autoDeposit&&MaintenancePlan.Build(rows,Rules,ItemAction.AutoDeposit,Pins).Count>0)
        {
            await TravelToBank(session,token);
            await AutoDeposit(session,token);
        }
    }
    private async Task TravelToBank(GameSession session,CancellationToken token)
    {
        if(World==null)throw new InvalidOperationException("Load WorldLogs before depositing or withdrawing items at a bank.");
        var route=BankRoutes.Closest(World,session.MapId,session.Position);
        var destination=new BankDestination(route.Map.Id,route.Map.Name,Store.Get<string>("banker/"+route.Map.Id));
        using var travel=CancellationTokenSource.CreateLinkedTokenSource(token);travel.CancelAfter(TimeSpan.FromMinutes(10));
        void Damaged()=>travel.Cancel();session.Damaged+=Damaged;
        try{await new Navigation(World,Path.GetDirectoryName(ClientPath)!).Travel(session,destination,travel.Token,route.Portals);}
        finally{session.Damaged-=Damaged;}
    }
    public async Task UpdateAccounts()
    {
        RequireLiveProfile();
        var names=Store.Characters().Where(x=>Accounts.UpdateEnabled(x.Name)).Select(x=>x.Name).ToArray();
        if(names.Length==0)throw new InvalidOperationException("Select characters in the Update column first.");
        var missing=names.Where(x=>Session(x)==null&&!CredentialVault.Exists(x)).ToArray();
        if(missing.Length>0)throw new InvalidOperationException("Save passwords for: "+string.Join(", ",missing));
        await RunOperation(async token=>
        {
            QueueRunning=true;QueueProgress.Clear();MaintenanceResults.Clear();Store.Put("maintenanceResults",MaintenanceResults);
            try
            {
                await AccountUpdateQueue.Run(names,async(name,t)=>
                {
                    t.ThrowIfCancellationRequested();var session=await Launch();
                    return new UpdatingClient(this,session,name);
                },name=>Session(name)!=null,update=>
                {
                    QueueProgress[update.Name]=update;QueueStatus=$"{update.Name}: {update.State}"+(update.Detail.Length>0?" — "+update.Detail:"");
                    if(update.State is "Complete" or "Failed" or "Stopped")Store.Put("lastUpdate/"+update.Name.ToLowerInvariant(),update);
                },token);
                QueueStatus=QueueProgress.Values.Any(x=>x.State is "Failed" or "Stopped")?"Account update stopped. Review account results.":"Account update complete.";
            }
            catch(OperationCanceledException){QueueStatus="Account update stopped.";throw;}
            finally{QueueRunning=false;}
        });
    }
    private sealed class UpdatingClient(Organizer app,GameSession session,string name,bool autoDeposit=true):IAccountUpdateClient
    {
        public async Task Login(CancellationToken token)
        {
            await session.LoginSaved(name,token,stage=>
            {
                app.QueueProgress[name]=new(name,"Logging in",stage);app.QueueStatus=$"{name}: {stage}";
            });
            await GameSession.WaitUntil(()=>session.Ready||session.Error!=null,TimeSpan.FromSeconds(35),token);
            if(session.Error!=null)throw new InvalidOperationException(session.Error);
            if(!string.Equals(session.Name,name,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unexpected character logged in. Queue stopped.");
        }
        public Task ScanBank(CancellationToken token)=>app.RefreshBank(session,autoDeposit,token);
        public Task Logout(CancellationToken token)=>session.SafeLogout(token);
        public async Task Close(CancellationToken token)
        {
            await session.CloseOwnedClient(token);session.Dispose();app.Sessions.Remove(session);
        }
    }
}
