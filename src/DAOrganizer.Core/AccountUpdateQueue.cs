namespace DAOrganizer.Core;
public interface IAccountUpdateClient
{
    Task Login(CancellationToken token);
    Task ScanBank(CancellationToken token);
    Task Logout(CancellationToken token);
    Task Close(CancellationToken token);
}
public sealed record AccountProgress(string Name,string State,string Detail="");
public static class AccountUpdateQueue
{
    public static async Task Run(IEnumerable<string> names,Func<string,CancellationToken,Task<IAccountUpdateClient>> create,Func<string,bool> alreadyOnline,Action<AccountProgress> progress,CancellationToken token)
    {
        var accounts=names.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach(var name in accounts)progress(new(name,"Queued"));
        for(var index=0;index<accounts.Length;index++)
        {
            token.ThrowIfCancellationRequested();var name=accounts[index];
            if(alreadyOnline(name)){progress(new(name,"Skipped","Already connected"));continue;}
            IAccountUpdateClient? client=null;
            try
            {
                progress(new(name,"Logging in"));client=await create(name,token);await client.Login(token);
                progress(new(name,"Scanning bank"));await client.ScanBank(token);
                progress(new(name,"Logging out"));await client.Logout(token);await client.Close(token);
                client=null;progress(new(name,"Complete",DateTimeOffset.Now.ToString("g")));
            }
            catch(Exception ex)
            {
                var userStopped=ex is OperationCanceledException&&token.IsCancellationRequested;
                var detail=userStopped?"Stopped by user":ex.Message;
                if(client!=null&&userStopped)
                {
                    progress(new(name,"Logging out",detail));
                    using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    try{await client.Logout(cleanup.Token);await client.Close(cleanup.Token);}
                    catch(Exception cleanupError){detail+="; client left open: "+cleanupError.Message;}
                }
                else if(client!=null)detail+="; client left open for manual handling";
                progress(new(name,ex is OperationCanceledException?"Stopped":"Failed",detail));
                foreach(var pending in accounts.Skip(index+1))progress(new(pending,"Not run","Queue stopped"));
                if(userStopped)throw;
                return;
            }
        }
    }
}
