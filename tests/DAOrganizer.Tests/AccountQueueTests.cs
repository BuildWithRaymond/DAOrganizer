using DAOrganizer.Core;
using Xunit;
namespace DAOrganizer.Tests;
public class AccountQueueTests
{
    private sealed class Client(string name,List<string> log,bool fail=false,Action<CancellationToken>? scan=null,bool failLogout=false):IAccountUpdateClient
    {
        public Task Login(CancellationToken token){log.Add(name+" login");return Task.CompletedTask;}
        public Task ScanBank(CancellationToken token){log.Add(name+" scan");scan?.Invoke(token);if(fail)throw new InvalidOperationException("Bank unavailable");return Task.CompletedTask;}
        public Task Logout(CancellationToken token){log.Add(name+" logout");if(failLogout)throw new InvalidOperationException("Logout refused");return Task.CompletedTask;}
        public Task Close(CancellationToken token){log.Add(name+" close");return Task.CompletedTask;}
    }
    [Fact]
    public async Task RunsExactlyOnePassSequentiallyAndSkipsExistingSession()
    {
        var log=new List<string>();var progress=new List<AccountProgress>();
        await AccountUpdateQueue.Run(["Beta","Alpha","Online","ALPHA"],(n,t)=>Task.FromResult<IAccountUpdateClient>(new Client(n,log)),n=>n=="Online",progress.Add,CancellationToken.None);
        Assert.Equal(new[]{"Alpha login","Alpha scan","Alpha logout","Alpha close","Beta login","Beta scan","Beta logout","Beta close"},log);
        Assert.Contains(progress,x=>x.Name=="Online"&&x.State=="Skipped");
    }
    [Fact]
    public async Task FailureLeavesOwnedClientOpenAndStopsBeforeNextAccount()
    {
        var log=new List<string>();var progress=new List<AccountProgress>();
        await AccountUpdateQueue.Run(["Alpha","Beta"],(n,t)=>Task.FromResult<IAccountUpdateClient>(new Client(n,log,true)),_=>false,progress.Add,CancellationToken.None);
        Assert.Equal(new[]{"Alpha login","Alpha scan"},log);
        Assert.Contains(progress,x=>x.State=="Failed"&&x.Detail.Contains("Bank unavailable"));
        Assert.Contains(progress,x=>x.State=="Failed"&&x.Detail.Contains("client left open"));
        Assert.DoesNotContain(progress,x=>x.Name=="Beta"&&x.State=="Complete");
    }
    [Fact]
    public async Task FocusFailureDoesNotCloseTheGameWindow()
    {
        var log=new List<string>();var progress=new List<AccountProgress>();
        await AccountUpdateQueue.Run(["Alpha","Beta"],
            (n,t)=>Task.FromResult<IAccountUpdateClient>(new Client(n,log,scan:_=>throw new InvalidOperationException("Game lost focus. Travel stopped."))),
            _=>false,progress.Add,CancellationToken.None);
        Assert.Equal(new[]{"Alpha login","Alpha scan"},log);
        Assert.Contains(progress,x=>x.Name=="Beta"&&x.State=="Not run");
        Assert.Contains(progress,x=>x.State=="Failed"&&x.Detail.Contains("Game lost focus"));
    }
    [Theory]
    [InlineData("Travel stopped with Escape.")]
    [InlineData("Manual movement detected. Travel stopped.")]
    public async Task LocalTravelStopPreservesClientAndItsActualReason(string reason)
    {
        var log=new List<string>();var progress=new List<AccountProgress>();
        await AccountUpdateQueue.Run(["Alpha","Beta"],
            (n,t)=>Task.FromResult<IAccountUpdateClient>(new Client(n,log,scan:_=>throw new OperationCanceledException(reason))),
            _=>false,progress.Add,CancellationToken.None);
        Assert.Equal(new[]{"Alpha login","Alpha scan"},log);
        Assert.Contains(progress,x=>x.State=="Stopped"&&x.Detail.Contains(reason)&&x.Detail.Contains("client left open"));
        Assert.Contains(progress,x=>x.Name=="Beta"&&x.State=="Not run");
    }
    [Fact]
    public async Task CancelDuringScanCleansUpThenStops()
    {
        var log=new List<string>();var progress=new List<AccountProgress>();using var cancel=new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>AccountUpdateQueue.Run(["Alpha","Beta"],
            (n,t)=>Task.FromResult<IAccountUpdateClient>(new Client(n,log,scan:ct=>{cancel.Cancel();ct.ThrowIfCancellationRequested();})),_=>false,progress.Add,cancel.Token));
        Assert.Equal(new[]{"Alpha login","Alpha scan","Alpha logout","Alpha close"},log);
        Assert.Contains(progress,x=>x.Name=="Beta"&&x.State=="Not run");
    }
    [Fact]
    public async Task FailedLogoutLeavesWindowOpenAndStopsQueue()
    {
        var log=new List<string>();var progress=new List<AccountProgress>();
        await AccountUpdateQueue.Run(["Alpha","Beta"],(n,t)=>Task.FromResult<IAccountUpdateClient>(new Client(n,log,failLogout:true)),_=>false,progress.Add,CancellationToken.None);
        Assert.DoesNotContain("Alpha close",log);Assert.DoesNotContain("Beta login",log);
        Assert.Contains(progress,x=>x.State=="Failed"&&x.Detail.Contains("client left open"));
    }
    [Fact]
    public async Task CancelledQueueDoesNotOpenAnyClient()
    {
        var opened=false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>AccountUpdateQueue.Run(["Alpha"],(n,t)=>{opened=true;return Task.FromResult<IAccountUpdateClient>(null!);},_=>false,_=>{},new CancellationToken(true)));
        Assert.False(opened);
    }
}
