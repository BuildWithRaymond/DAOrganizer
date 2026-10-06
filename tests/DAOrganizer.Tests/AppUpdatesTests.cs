using DAOrganizer.App;
using DAOrganizer.Core;
using Xunit;

namespace DAOrganizer.Tests;

public class AppUpdatesTests
{
    [Fact]
    public async Task DefaultAndDemoNeverCheckAutomatically()
    {
        using var store=new InventoryStore(":memory:");
        var backend=new TestUpdates();
        var updates=new AppUpdates(store,false,backend);
        await updates.Start();
        Assert.Equal(0,backend.Checks);
        updates.Automatic=true;
        await new AppUpdates(store,true,backend).Start();
        Assert.Equal(0,backend.Checks);
    }

    [Fact]
    public async Task OptInDownloadsButDoesNotApplyUntilSafeExit()
    {
        using var store=new InventoryStore(":memory:");
        var backend=new TestUpdates();
        var updates=new AppUpdates(store,false,backend){Automatic=true};
        await updates.Start();
        Assert.Equal(1,backend.Downloads);
        Assert.Equal(0,backend.Applies);
        Assert.False(updates.PrepareExit(busy:true,connected:false));
        Assert.False(updates.PrepareExit(busy:false,connected:true));
        Assert.Equal(0,backend.Applies);
        Assert.True(updates.PrepareExit(busy:false,connected:false));
        Assert.Equal(1,backend.Applies);
    }

    [Fact]
    public async Task OptOutKeepsPendingDownloadWithoutApplying()
    {
        using var store=new InventoryStore(":memory:");
        var backend=new TestUpdates();
        var updates=new AppUpdates(store,false,backend){Automatic=true};
        await updates.Start();
        updates.Automatic=false;
        Assert.False(updates.PrepareExit(false,false));
        Assert.Equal(0,backend.Applies);
        Assert.False(new AppUpdates(store,false,backend).Automatic);
    }

    [Fact]
    public async Task ManualDownloadRequiresExplicitApplyAndCannotBypassActiveClients()
    {
        using var store=new InventoryStore(":memory:");
        var backend=new TestUpdates();
        var updates=new AppUpdates(store,false,backend);
        await updates.Check();
        await updates.Download();
        Assert.False(updates.PrepareExit(false,false));
        Assert.Throws<InvalidOperationException>(()=>updates.RequestInstall(false,true));
        updates.RequestInstall(false,false);
        Assert.True(updates.PrepareExit(false,false));
    }

    [Fact]
    public async Task FailedCheckLeavesAppUsableAndDoesNotApply()
    {
        using var store=new InventoryStore(":memory:");
        var backend=new TestUpdates{Fail=true};
        var updates=new AppUpdates(store,false,backend){Automatic=true};
        await updates.Start();
        Assert.Contains("unavailable",updates.Status,StringComparison.OrdinalIgnoreCase);
        Assert.False(updates.PrepareExit(false,false));
        Assert.Equal(0,backend.Applies);
    }

    [Fact]
    public async Task DemoBlocksManualChecksDownloadsAndPreferenceWrites()
    {
        using var store=new InventoryStore(":memory:");
        var backend=new TestUpdates();
        var updates=new AppUpdates(store,true,backend);
        Assert.Throws<InvalidOperationException>(()=>updates.Automatic=true);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>updates.Check());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>updates.Download());
        Assert.False(updates.PrepareExit(false,false));
        Assert.Equal(0,backend.Checks);
        Assert.Equal(0,backend.Downloads);
    }

    [Fact]
    public async Task OpenOwnedClientBlocksInstallBeforeCharacterLogin()
    {
        var directory=Path.Combine(Path.GetTempPath(),"daorganizer-update-client-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var app=new Organizer(directory,updateBackend:new TestUpdates());
            await app.Updates.Check();await app.Updates.Download();
            var session=new DAOrganizer.Game.GameSession(app.Store);
            using var process=System.Diagnostics.Process.GetCurrentProcess();
            typeof(DAOrganizer.Game.GameSession).GetProperty("ProcessId")!.SetValue(session,process.Id);
            typeof(DAOrganizer.Game.GameSession).GetField("_ownedStarted",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(session,process.StartTime);
            app.Sessions.Add(session);
            Assert.False(session.Online);
            Assert.True(app.HasOpenClients);
            Assert.Throws<InvalidOperationException>(()=>app.Updates.RequestInstall(app.Busy,app.HasOpenClients));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory,true);
        }
    }

    private sealed class TestUpdates:IAppUpdateBackend
    {
        public bool IsInstalled=>true;
        public string? PendingVersion{get;private set;}
        public int Checks,Downloads,Applies;
        public bool Fail;
        public Task<string?> Check(){Checks++;if(Fail)throw new IOException("offline");return Task.FromResult<string?>("0.17.0");}
        public Task Download(){Downloads++;PendingVersion="0.17.0";return Task.CompletedTask;}
        public void PrepareExit()=>Applies++;
    }
}
