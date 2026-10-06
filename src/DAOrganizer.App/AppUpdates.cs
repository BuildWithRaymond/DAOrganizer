using DAOrganizer.Core;
namespace DAOrganizer.App;

public interface IAppUpdateBackend
{
    bool IsInstalled{get;}
    string? PendingVersion{get;}
    Task<string?> Check();
    Task Download();
    void PrepareExit();
}

public sealed class AppUpdates(InventoryStore store,bool demo,IAppUpdateBackend backend)
{
    public const string RepositoryUrl="https://github.com/BuildWithRaymond/DAOrganizer";
    public static string CurrentVersion=>typeof(AppUpdates).Assembly.GetCustomAttributes(false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0];
    private bool _installRequested;
    public bool Available=>!demo&&backend.IsInstalled;
    public bool Working{get;private set;}
    public string? AvailableVersion{get;private set;}
    public string? PendingVersion=>Available?backend.PendingVersion:null;
    public string Status{get;private set;}=demo?"Updates are disabled in demo mode.":"Check GitHub for a newer stable release.";
    public event Action? Changed;
    public bool Automatic
    {
        get=>!demo&&store.Get<bool>("updates/automatic");
        set
        {
            RequireAvailable();
            store.Put("updates/automatic",value);
            if(!value)_installRequested=false;
            Changed?.Invoke();
        }
    }
    public async Task Start()
    {
        if(!Available||!Automatic)return;
        await Check();
        if(Automatic&&AvailableVersion!=null)await Download();
    }
    public async Task Check()
    {
        RequireAvailable();
        if(Working)return;
        Working=true;Status="Checking GitHub releases…";Changed?.Invoke();
        try
        {
            AvailableVersion=await backend.Check();
            Status=PendingVersion is {} pending?$"Version {pending} is ready to install.":
                AvailableVersion is {} version?$"Version {version} is available.":"You have the latest stable release.";
        }
        catch(Exception ex){AvailableVersion=null;Status="Update check unavailable: "+ex.Message;}
        finally{Working=false;Changed?.Invoke();}
    }
    public async Task Download()
    {
        RequireAvailable();
        if(Working)return;
        if(AvailableVersion==null)throw new InvalidOperationException("Check for an update first.");
        Working=true;Status="Downloading update…";Changed?.Invoke();
        try
        {
            await backend.Download();
            Status=$"Version {PendingVersion} is ready. Finish game sessions, then exit to update.";
        }
        catch(Exception ex){Status="Update download unavailable: "+ex.Message;}
        finally{Working=false;Changed?.Invoke();}
    }
    public void RequestInstall(bool busy,bool connected)
    {
        RequireAvailable();
        if(busy||connected||Working)throw new InvalidOperationException("Finish current actions and close organizer-launched game clients before installing an update.");
        if(PendingVersion==null)throw new InvalidOperationException("Download an update first.");
        _installRequested=true;
    }
    public bool PrepareExit(bool busy,bool connected)
    {
        if(!Available||Working||busy||connected||(!Automatic&&!_installRequested)||PendingVersion==null)return false;
        try{backend.PrepareExit();return true;}
        catch(Exception ex){Status="Update install unavailable: "+ex.Message;return false;}
    }
    private void RequireAvailable()
    {
        if(demo)throw new InvalidOperationException("Demo mode cannot check, download or install updates.");
        if(!backend.IsInstalled)throw new InvalidOperationException("Install the latest GitHub release or use its portable package to enable updates.");
    }
}
