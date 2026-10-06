using System.Text.Json;
using DAOrganizer.Game;

namespace DAOrganizer.App;

public sealed partial class Organizer
{
    private (string OperationId,GameSession First,GameSession Second)? _manualTradeCapture;
    public bool ManualTradeCaptureRunning=>_manualTradeCapture!=null;
    public ManualTradeAnalysis? LastManualTradeAnalysis{get;private set;}

    public string StartManualTradeCapture(string firstName,string secondName)
    {
        RequireLiveProfile();
        if(_manualTradeCapture!=null)throw new InvalidOperationException("Finish the current capture first.");
        if(firstName.Equals(secondName,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose two different characters.");
        var first=Session(firstName)??throw new InvalidOperationException($"{firstName} is offline.");
        var second=Session(secondName)??throw new InvalidOperationException($"{secondName} is offline.");
        if(!first.Ready||!second.Ready||first.ProcessId==second.ProcessId)
            throw new InvalidOperationException("Both characters must be ready in distinct client sessions.");
        var id=Guid.NewGuid().ToString("N");
        first.StartManualTradeCapture(id);
        try{second.StartManualTradeCapture(id);}
        catch{first.StopManualTradeCapture();throw;}
        _manualTradeCapture=(id,first,second);
        return id;
    }

    public string StopManualTradeCapture()
    {
        var capture=_manualTradeCapture??throw new InvalidOperationException("No manual trade capture is running.");
        var first=capture.First.StopManualTradeCapture();
        var second=capture.Second.StopManualTradeCapture();
        _manualTradeCapture=null;
        return SaveManualTradeCapture(first,second);
    }

    public string SaveManualTradeCapture(ManualTradeResult first,ManualTradeResult second)
    {
        RequireLiveProfile();
        if(first.OperationId!=second.OperationId||first.OperationId.Length is <1 or >64||
           first.OperationId.Any(x=>!char.IsAsciiLetterOrDigit(x)&&x!='-'))
            throw new ArgumentException("Both captures need the same safe operation ID.");
        var directory=Path.Combine(DataDirectory,"diagnostics","manual-trades",first.OperationId);
        Directory.CreateDirectory(directory);
        var options=new JsonSerializerOptions{WriteIndented=true};
        File.WriteAllText(Path.Combine(directory,"first.json"),JsonSerializer.Serialize(first,options));
        File.WriteAllText(Path.Combine(directory,"second.json"),JsonSerializer.Serialize(second,options));
        var analysis=ManualTradeAnalyzer.Analyze(first,second);
        File.WriteAllText(Path.Combine(directory,"analysis.json"),JsonSerializer.Serialize(analysis,options));
        LastManualTradeAnalysis=analysis;
        if(analysis.Verified&&analysis.Item is { } item)
            Store.AddVerifiedManualTrade(item,first.OperationId,typeof(Organizer).Assembly.GetName().Version?.ToString()??"unknown");
        return directory;
    }
}
