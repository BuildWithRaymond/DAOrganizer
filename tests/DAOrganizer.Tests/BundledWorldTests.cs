using DAOrganizer.App;
using Xunit;

namespace DAOrganizer.Tests;

public class BundledWorldTests
{
    [Fact]
    public async Task FreshProfileLoadsBundledRoutesWithoutSetup()
    {
        var directory=Path.Combine(Path.GetTempPath(),"daorganizer-routes-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var organizer=new Organizer(directory);
            await organizer.LoadWorld();
            Assert.NotNull(organizer.World);
            Assert.True(organizer.World.Maps.Count>=1400);
            Assert.Contains(organizer.Banks(),x=>x.Name.Contains("Mileth",StringComparison.OrdinalIgnoreCase));
            Assert.Equal(135,organizer.World.Route(500,135).Last().To);
            Assert.Equal(500,organizer.World.Route(135,500).Last().To);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory,true);
        }
    }

    [Fact]
    public async Task MissingLegacyFolderFallsBackToBundledRoutes()
    {
        var directory=Path.Combine(Path.GetTempPath(),"daorganizer-routes-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var organizer=new Organizer(directory);
            organizer.WorldLogsPath=Path.Combine(directory,"missing");
            await organizer.LoadWorld();
            Assert.NotNull(organizer.World);
            Assert.True(organizer.World.Maps.Count>=1400);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory,true);
        }
    }

    [Fact]
    public async Task DemoNeverLoadsRoutes()
    {
        using var organizer=new Organizer(demo:true);
        await organizer.LoadWorld();
        Assert.Null(organizer.World);
    }
}
