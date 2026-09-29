using DAOrganizer.Core;
using Xunit;
namespace DAOrganizer.Tests;

public class BaselineTests
{
    [Fact]
    public void LoginControlAloneCannotCommitSnapshot()
    {
        var baseline=new LoginBaseline();var now=DateTimeOffset.UtcNow;
        baseline.ControlSeen=true;
        Assert.False(baseline.CanCommit(now));
        baseline.AppearanceSeen=true;baseline.MapSeen=true;baseline.StatusSeen=true;
        baseline.Changed(now);
        Assert.False(baseline.CanCommit(now.AddSeconds(1)));
        Assert.True(baseline.CanCommit(now.AddSeconds(3)));
    }
    [Fact]
    public void LateItemRestartsQuietPeriodAndParseFailurePreventsCommit()
    {
        var baseline=new LoginBaseline{ControlSeen=true,AppearanceSeen=true,MapSeen=true,StatusSeen=true};
        var now=DateTimeOffset.UtcNow;baseline.Changed(now);baseline.Changed(now.AddSeconds(2));
        Assert.False(baseline.CanCommit(now.AddSeconds(3)));
        baseline.Failed=true;
        Assert.False(baseline.CanCommit(now.AddMinutes(1)));
    }
}
