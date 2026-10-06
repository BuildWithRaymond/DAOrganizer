using Velopack;
using Velopack.Sources;
using Velopack.Locators;
namespace DAOrganizer.App;

internal sealed class VelopackUpdates:IAppUpdateBackend
{
    private UpdateManager? _manager;
    private UpdateManager Manager=>_manager??=new(new GithubSource(AppUpdates.RepositoryUrl,null,false));
    private UpdateInfo? _update;
    public bool IsInstalled=>VelopackLocator.IsCurrentSet&&Manager.IsInstalled;
    public string? PendingVersion=>IsInstalled?Manager.UpdatePendingRestart?.Version.ToString():null;
    public async Task<string?> Check()
    {
        _update=await Manager.CheckForUpdatesAsync();
        return _update?.TargetFullRelease.Version.ToString();
    }
    public Task Download()=>Manager.DownloadUpdatesAsync(_update??throw new InvalidOperationException("Check for an update first."));
    public void PrepareExit()=>Manager.WaitExitThenApplyUpdates(Manager.UpdatePendingRestart,silent:true,restart:false);
}
