using DAOrganizer.Core;
using DAOrganizer.Game;
namespace DAOrganizer.App;
public sealed partial class Organizer
{
    public Dictionary<string,AccountProgress> QueueProgress {get;}=new(StringComparer.OrdinalIgnoreCase);
    public string QueueStatus {get;private set;}="";
    public bool QueueRunning {get;private set;}
    public async Task UpdateAccounts()
    {
        RequireLiveProfile();
        if(World==null)throw new InvalidOperationException("Load WorldLogs before updating accounts.");
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
        public async Task ScanBank(CancellationToken token)
        {
            var route=BankRoutes.Closest(app.World!,session.MapId,session.Position);
            var destination=new BankDestination(route.Map.Id,route.Map.Name,app.Store.Get<string>("banker/"+route.Map.Id));
            using var scan=CancellationTokenSource.CreateLinkedTokenSource(token);
            scan.CancelAfter(TimeSpan.FromMinutes(10));
            void Damaged()=>scan.Cancel();session.Damaged+=Damaged;
            try
            {
                await new Navigation(app.World!,Path.GetDirectoryName(app.ClientPath)!).Travel(session,destination,scan.Token,route.Portals);
                if(autoDeposit)await app.AutoDeposit(session,scan.Token);
            }
            finally{session.Damaged-=Damaged;}
        }
        public Task Logout(CancellationToken token)=>session.SafeLogout(token);
        public async Task Close(CancellationToken token)
        {
            await session.CloseOwnedClient(token);session.Dispose();app.Sessions.Remove(session);
        }
    }
}
