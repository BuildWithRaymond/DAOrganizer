using System.Reflection;
using DAOrganizer.Core;
using DAOrganizer.Game;
using Xunit;
namespace DAOrganizer.Tests;

public class NavigationControlTests
{
    [Fact]
    public void PacketTravelDoesNotRequireForegroundFocus()
    {
        using var store=new InventoryStore(":memory:");using var session=new GameSession(store);
        Configure(session,true,100);
        Check(session,CancellationToken.None);
    }

    [Theory]
    [InlineData(false,100u)]
    [InlineData(true,0u)]
    public void BackgroundTravelStillStopsWhenCharacterUnavailable(bool online,uint health)
    {
        using var store=new InventoryStore(":memory:");using var session=new GameSession(store);
        Configure(session,online,health);
        var error=Assert.Throws<TargetInvocationException>(()=>Check(session,CancellationToken.None));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Contains("Character unavailable",error.InnerException.Message);
    }

    [Fact]
    public void StopCancelsBackgroundTravel()
    {
        using var store=new InventoryStore(":memory:");using var session=new GameSession(store);
        Configure(session,true,100);
        var error=Assert.Throws<TargetInvocationException>(()=>Check(session,new CancellationToken(true)));
        Assert.IsType<OperationCanceledException>(error.InnerException);
    }

    private static void Configure(GameSession session,bool online,uint health)
    {
        // No real game window is controlled. A negative PID can never have focus.
        typeof(GameSession).GetProperty(nameof(GameSession.ProcessId))!.SetValue(session,-1);
        typeof(GameSession).GetProperty(nameof(GameSession.Online))!.SetValue(session,online);
        typeof(GameSession).GetProperty(nameof(GameSession.Health))!.SetValue(session,health);
    }
    private static void Check(GameSession session,CancellationToken token)=>
        typeof(Navigation).GetMethod("CheckControl",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[session,token]);
}
