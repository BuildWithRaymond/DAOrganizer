using System.Diagnostics;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Types;
namespace DAOrganizer.Game;
public sealed partial class GameSession
{
    private DateTime _ownedStarted;
    private long _quitRevision;
    private byte _quitApproval;
    private bool _safeQuitApproved;
    private bool _safeQuitRequested;
    private long _quitRequestRevision;
    public async Task SafeLogout(CancellationToken token)
    {
        if(!Online||_safeQuitApproved)return;
        SetStatus("Waiting for safe logout");
        if(!_safeQuitRequested)
        {
            _quitRequestRevision=Interlocked.Read(ref _quitRevision);
            Send(new ClientQuitMessage{Reason=ClientExitReason.UserRequested});_safeQuitRequested=true;
        }
        await WaitUntil(()=>!Online||Interlocked.Read(ref _quitRevision)>_quitRequestRevision,TimeSpan.FromSeconds(18),token);
        if(!Online)return;
        if(_quitApproval!=1)throw new InvalidOperationException("Server has not approved safe logout. Client left open.");
        Send(new ClientQuitMessage{Reason=ClientExitReason.None});
        _safeQuitApproved=true;await Task.Delay(300,token);
    }
    public async Task CloseOwnedClient(CancellationToken token)
    {
        if(Online&&!_safeQuitApproved)throw new InvalidOperationException("Safe logout required before closing this client.");
        Process process;
        try{process=Process.GetProcessById(ProcessId);}catch(ArgumentException){return;}
        using(process)
        {
            if(process.HasExited)return;
            if(process.StartTime!=_ownedStarted||!process.ProcessName.Equals("Darkages",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Client ownership changed; no window was closed.");
            if(!process.CloseMainWindow())throw new InvalidOperationException("Client window could not close. Close it manually before retrying.");
            await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(8),token);
        }
    }
}
