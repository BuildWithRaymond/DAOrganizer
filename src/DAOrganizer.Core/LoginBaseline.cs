namespace DAOrganizer.Core;

/// <summary>The protocol has no item-count terminator. Require login context and a quiet window.</summary>
public sealed class LoginBaseline
{
    public bool ControlSeen {get;set;}
    public bool AppearanceSeen {get;set;}
    public bool MapSeen {get;set;}
    public bool StatusSeen {get;set;}
    public bool Failed {get;set;}
    private DateTimeOffset _lastChange=DateTimeOffset.UtcNow;
    public void Changed(DateTimeOffset now)=>_lastChange=now;
    public bool CanCommit(DateTimeOffset now)=>!Failed&&ControlSeen&&AppearanceSeen&&MapSeen&&StatusSeen&&now-_lastChange>=TimeSpan.FromSeconds(2);
}
