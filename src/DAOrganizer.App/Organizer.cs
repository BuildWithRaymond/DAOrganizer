using DAOrganizer.Core;
using DAOrganizer.Game;
namespace DAOrganizer.App;

public sealed partial class Organizer:IDisposable
{
    public InventoryStore Store{get;}
    public AccountCatalog Accounts{get;}
    public List<GameSession> Sessions{get;}=[];
    public string ClientPath{get;set;}
    public string WorldLogsPath{get;set;}
    public WorldGraph? World{get;private set;}
    public string WorldStatus{get;private set;}="Routes not loaded";
    public string DataDirectory{get;}
    public bool IsDemo{get;}
    public CancellationTokenSource? Operation{get;private set;}
    public bool Busy=>Operation!=null;
    public Organizer(string? directory=null,bool demo=false)
    {
        IsDemo=demo;
        DataDirectory=directory??Environment.GetEnvironmentVariable("DAORGANIZER_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DAOrganizer");
        if(!demo)Directory.CreateDirectory(DataDirectory);
        Store=new(demo?":memory:":Path.Combine(DataDirectory,"inventory.db"));
        if(!demo)Store.ResolveKnownNoSendWithdrawalFailures(DateTimeOffset.UtcNow);
        if(!demo)ReconcileRecordedTransferDeliveries();
        if(!demo)ReconcileUncommittedQuantityPrompts();
        Accounts=new(Store);
        ClientPath=Store.Get<string>("client")??ClientLauncher.DefaultClient;
        WorldLogsPath=Store.Get<string>("worldLogs")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Dark Ages","WorldLogs");
        foreach(var character in Store.Characters())Store.MarkStale(character.Name);
        if(demo)
        {
            if(Environment.GetEnvironmentVariable("DAORGANIZER_GAME_DATA") is {Length:>0} folder)ClientPath=Path.Combine(folder,"Darkages.exe");
            DemoCollection.Seed(this);
        }
    }
    public async Task LoadWorld()
    {
        if(IsDemo||!Directory.Exists(WorldLogsPath)){World=null;WorldStatus="Choose your WorldLogs folder in Settings to enable bank travel.";return;}
        World=await Task.Run(()=>WorldGraph.Load(WorldLogsPath));WorldStatus=$"{World.Maps.Count:N0} maps loaded";
    }
    public void SaveSettings(){RequireLiveProfile();ClientLauncher.Validate(ClientPath);Store.Put("client",ClientPath);Store.Put("worldLogs",WorldLogsPath);}
    public GameSession? Session(string? name)=>Sessions.LastOrDefault(x=>x.Online&&string.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase));
    public async Task<GameSession> Launch()
    {
        RequireLiveProfile();
        var session=new GameSession(Store);try{await session.LaunchAsync(ClientPath);Sessions.Add(session);return session;}catch{session.Dispose();throw;}
    }
    public Item[] Items(string name,string location)
    {
        var session=Session(name);
        var items=location=="Inventory"&&session?.Ready==true?session.Inventory():location=="Equipment"&&session?.Ready==true?session.Equipment():Store.Items(name,location).ToArray();
        return Accounts.Categorize(items);
    }
    public HashSet<int> Pins(string name)=>Store.Get<HashSet<int>>("pins/"+name.ToLowerInvariant())??[];
    public void TogglePin(string name,int slot){var pins=Pins(name);if(!pins.Add(slot))pins.Remove(slot);Store.Put("pins/"+name.ToLowerInvariant(),pins);}
    public IReadOnlyList<BankDestination> Banks()
    {
        if(World==null)return [];
        return World.Maps.Values.Where(x=>BankRoutes.IsBank(x.Name))
            .Select(x=>new BankDestination(x.Id,x.Name,Store.Get<string>("banker/"+x.Id))).OrderBy(x=>x.Name).ToArray();
    }
    public async Task RunOperation(Func<CancellationToken,Task> work)
    {
        RequireLiveProfile();
        if(Busy)throw new InvalidOperationException("Stop the current operation first.");
        var cts=new CancellationTokenSource();Operation=cts;
        try{await work(cts.Token);}finally{Operation=null;cts.Dispose();}
    }
    public void Stop()=>Operation?.Cancel();
    public void RequireLiveProfile(){if(IsDemo)throw new InvalidOperationException("Demo mode cannot launch clients, change game items or save credentials. Open the app without --demo to use your own accounts.");}
    public void Dispose(){Stop();foreach(var session in Sessions)session.Dispose();Store.Dispose();}
}
