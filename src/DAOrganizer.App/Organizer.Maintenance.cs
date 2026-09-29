using DAOrganizer.Core;
using DAOrganizer.Game;
namespace DAOrganizer.App;

public sealed partial class Organizer
{
    public ItemRules Rules=>new(Store);
    public IReadOnlyList<MaintenanceEntry> PlanMaintenance(ItemAction action)=>MaintenancePlan.Build(Store.Search(""),Rules,action,Pins);
    public List<MaintenanceResult> MaintenanceResults {get;}=[];
    private void RecordMaintenance(MaintenanceResult result)
    {
        MaintenanceResults.Add(result);Store.Put("maintenanceResults",MaintenanceResults);
        QueueStatus=$"{result.Character}: {result.Item} — {result.Result}";
    }
    public async Task RunMaintenance(IReadOnlyList<MaintenanceEntry> plan)
    {
        RequireLiveProfile();
        if(plan.Count==0)return;
        if(World==null)throw new InvalidOperationException("Load WorldLogs before item maintenance.");
        var missing=plan.Select(x=>x.Character).Distinct(StringComparer.OrdinalIgnoreCase).Where(x=>Session(x)==null&&!CredentialVault.Exists(x)).ToArray();
        if(missing.Length>0)throw new InvalidOperationException("Save passwords for: "+string.Join(", ",missing));
        await RunOperation(async token=>
        {
            QueueRunning=true;MaintenanceResults.Clear();Store.Put("maintenanceResults",MaintenanceResults);
            try
            {
                foreach(var character in plan.GroupBy(x=>x.Character,StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    var existing=Session(character.Key);var session=existing??await Launch();
                    var client=new UpdatingClient(this,session,character.Key,false);
                    try
                    {
                        QueueStatus=$"{character.Key}: preparing item maintenance";
                        if(existing==null)await client.Login(token);
                        if(character.Any(x=>x.Location=="Bank"||x.Action==ItemAction.AutoDeposit))await TravelToBank(session,token);
                        await MaintenanceRunner.Run(session,character.ToArray(),Rules,()=>Pins(character.Key),RecordMaintenance,token);
                        if(character.Any(x=>x.Location=="Bank"||x.Action==ItemAction.AutoDeposit))await session.ScanNearbyBank(null,token);
                        if(existing==null){await client.Logout(token);await client.Close(token);}
                    }
                    catch(Exception ex)
                    {
                        RecordMaintenance(new(character.Key,"", "Stopped",ex is OperationCanceledException?"Stopped; client left open":ex.Message+"; client left open"));
                        throw;
                    }
                }
                QueueStatus="Item maintenance finished. Review results for skipped or unconfirmed items.";
            }
            finally{QueueRunning=false;}
        });
    }
    private async Task AutoDeposit(GameSession session,CancellationToken token)
    {
        var rows=session.Inventory().Select(x=>new StoredItem(session.Name,"Inventory",x,DateTimeOffset.UtcNow));
        var plan=MaintenancePlan.Build(rows,Rules,ItemAction.AutoDeposit,Pins);
        if(plan.Count==0)return;
        await MaintenanceRunner.Run(session,plan,Rules,()=>Pins(session.Name),RecordMaintenance,token);
        await session.ScanNearbyBank(null,token);
    }
}
