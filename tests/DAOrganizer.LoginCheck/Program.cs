using System.Reflection;
using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Filters;
using Arbiter.Net.Proxy;
using DAOrganizer.Core;
using DAOrganizer.Game;

// Explicit opt-in real-client test. Every outgoing CLogin is blocked. No vault access.
if(args.Length!=1||args[0]!="--run-client")throw new ArgumentException("Use --run-client to test the installed client. All login packets are blocked.");
const string name="CodexTest",ticket="12345678";
for(var pass=1;pass<=3;pass++)
{
    using var store=new InventoryStore(":memory:");using var session=new GameSession(store);
    var submitted=new TaskCompletionSource<(bool Name,bool Password)>(TaskCreationOptions.RunContinuationsAsynchronously);
    var proxy=(ProxyServer)typeof(GameSession).GetField("_proxy",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session)!;
    proxy.AddFilter(ClientCommand.Login,new NetworkPacketFilter((connection,packet,state)=>
    {
        var matches=(false,false);
        if(packet is ClientPacket client&&ClientMessageFactory.Default.TryCreate<ClientLoginMessage>(client,out var message))
            matches=(message.Name==name,message.Password==ticket);
        submitted.TrySetResult(matches);
        return null; // Block opcode 0x03 even if typed decoding fails.
    }){Name="BlockAllProbeLogins",Priority=int.MaxValue});
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
    try
    {
        await session.LaunchAsync(ClientLauncher.DefaultClient);
        Console.WriteLine($"Pass {pass}: owned test client {session.ProcessId}; outgoing login blocked.");
        await GameSession.WaitUntil(()=>{using var p=System.Diagnostics.Process.GetProcessById(session.ProcessId);return p.MainWindowHandle!=IntPtr.Zero;},TimeSpan.FromSeconds(15),timeout.Token);
        using var form=new NativeLoginForm(session.ProcessId);
        try{await LoginFormAutomation.Run(form,name,ticket,stage=>Console.WriteLine(stage),timeout.Token);}
        catch
        {
            Console.WriteLine("Failed form state (lengths and comparisons only): "+form.Read("C",ticket));
            throw;
        }
        var result=await submitted.Task.WaitAsync(TimeSpan.FromSeconds(12),timeout.Token);
        if(!result.Name||!result.Password)throw new Exception("Native packet did not match test fields.");
        Console.WriteLine($"PASS {pass}: real native client submitted matching name and password; packet blocked.");
    }
    finally
    {
        if(session.ProcessId!=0)
        {
            using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await session.CloseOwnedClient(cleanup.Token);
            Console.WriteLine("Closed only owned test client.");
        }
    }
}
