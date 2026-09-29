using System.Diagnostics;
using System.Security.Cryptography;
using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Filters;
using Arbiter.Net.Proxy;
using DAOrganizer.Core;
namespace DAOrganizer.Game;

public sealed partial class GameSession
{
    private string? _loginName,_loginTicket;
    private volatile bool _loginSubmitted;
    private DateTimeOffset _loginExpires;
    public async Task LoginSaved(string name,CancellationToken token,Action<string>? progress=null)
    {
        if(_loginTicket!=null)throw new InvalidOperationException("Saved login already attempted in this client. Close it and launch a fresh client to retry.");
        if(!CredentialVault.Exists(name))throw new InvalidOperationException("Save a password for this character first.");
        _loginName=name;_loginTicket=RandomNumberGenerator.GetInt32(10000000,99999999).ToString();_loginExpires=DateTimeOffset.UtcNow.AddSeconds(120);_loginSubmitted=false;
        // Keep the ticket filter for the entire session: an expired ticket must never reach the server.
        _proxy.AddFilter<ClientLoginMessage>(RewriteSavedLogin,"SavedLogin",100);
        try
        {
            await WaitUntil(()=>{using var p=Process.GetProcessById(ProcessId);return p.MainWindowHandle!=IntPtr.Zero;},TimeSpan.FromSeconds(15),token);
            NativeInput.Focus(ProcessId); // Best effort only; window-addressed login does not require focus.
            if(Online)throw new InvalidOperationException("Client is already logged in.");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(90));
            using var form=new NativeLoginForm(ProcessId);
            try{await LoginFormAutomation.Run(form,name,_loginTicket,stage=>{SetStatus(stage);progress?.Invoke(stage);},timeout.Token);}
            catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new TimeoutException("Login form did not become ready within 90 seconds.");}
            _loginExpires=DateTimeOffset.UtcNow.AddSeconds(12);
            try{await WaitUntil(()=>_loginSubmitted,TimeSpan.FromSeconds(12),token);}
            catch(TimeoutException){throw new TimeoutException("Login fields were verified, but the game did not submit the login packet.");}
        }
        finally{_loginExpires=DateTimeOffset.MinValue;}
    }
    private Arbiter.Net.NetworkPacket? RewriteSavedLogin(ProxyConnection connection,ClientLoginMessage message,object? state,NetworkMessageFilterResult<ClientLoginMessage> result)
    {
        if(_loginTicket==null||message.Password!=_loginTicket)return result.Passthrough();
        if(Online||_loginSubmitted||DateTimeOffset.UtcNow>_loginExpires||!string.Equals(message.Name,_loginName,StringComparison.OrdinalIgnoreCase))return result.Block();
        try
        {
            var original=(ClientPacket)result.Passthrough();
            var password=CredentialVault.Read(_loginName!)??throw new InvalidOperationException("Saved password unavailable.");
            var payload=LoginPayload.ReplacePassword(original.Data,_loginName!,_loginTicket,password);
            _loginSubmitted=true;
            return new ClientPacket((byte)ClientCommand.Login,payload,original.Checksum){Sequence=original.Sequence,Source=original.Source};
        }
        catch{SetStatus("Saved login unavailable. Sign in manually.");return result.Block();}
    }
}
