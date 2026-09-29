using System.Text.RegularExpressions;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Types;
namespace DAOrganizer.Game;

public sealed partial class GameSession
{
    private DateTimeOffset _loginPromptExpires,_homeInnDeadline;
    private bool _homeInnAccepted,_homeInnPending,_homeInnMapSeen;
    private IClientMessage? _homeInnReply;

    private static string CleanDialogText(string? text)=>Regex.Replace(text??"",@"\{=.","").Trim();
    private static bool IsHomeInnOffer(string? text)
    {
        var content=CleanDialogText(text);
        // The official guildhouse login offer names the destination "your nation".
        if(Regex.IsMatch(content,@"\AWould\s+you\s+like\s+to\s+go\s+to\s+your\s+nation\?\z",RegexOptions.IgnoreCase))return true;
        return Regex.IsMatch(content,@"\bhome\s+inn\b",RegexOptions.IgnoreCase)
            &&Regex.IsMatch(content,@"\b(go|return|teleport|transport|transported)\b",RegexOptions.IgnoreCase)
            &&!Regex.IsMatch(content,@"\b(change|set|buy|purchase|pay|cost)\b",RegexOptions.IgnoreCase);
    }
    private static bool IsYes(string text)=>Regex.IsMatch(CleanDialogText(text),@"^yes(?:$|[\s,.!])",RegexOptions.IgnoreCase);

    private void ObserveHomeInn(ServerPursuitMessage menu)
    {
        if(_homeInnAccepted||DateTimeOffset.UtcNow>_loginPromptExpires||!IsHomeInnOffer(menu.Content)
            ||menu.DialogType is not (DialogType.Menu or DialogType.CreatureMenu)
            ||menu.EntityId==null||menu.PursuitId==null||menu.StepId==null)return;
        var yes=menu.MenuChoices.FindIndex(IsYes);
        if(yes<0||yes>=255||menu.StepId==ushort.MaxValue)return;
        _homeInnReply=new ClientPursuitMessage
        {
            EntityType=menu.EntityType,EntityId=menu.EntityId.Value,PursuitId=menu.PursuitId.Value,
            StepId=(ushort)(menu.StepId.Value+1),ArgsType=DialogArgsType.MenuChoice,MenuChoice=(byte)(yes+1)
        };
    }
    private void ObserveHomeInn(ServerScreenMenuMessage menu)
    {
        if(_homeInnAccepted||DateTimeOffset.UtcNow>_loginPromptExpires||!IsHomeInnOffer(menu.Content)
            ||menu.MenuType!=DialogMenuType.Menu||menu.EntityId==null)return;
        var yes=menu.MenuChoices.FirstOrDefault(x=>IsYes(x.Text));
        if(yes==null)return;
        _homeInnReply=new ClientMerchantMessage{EntityType=menu.EntityType,EntityId=menu.EntityId.Value,PursuitId=yes.PursuitId};
    }
    private void AcceptHomeInn()
    {
        if(_homeInnReply==null||!Online||_homeInnAccepted)return;
        Send(_homeInnReply);_homeInnReply=null;
        _homeInnAccepted=true;_homeInnPending=true;_homeInnMapSeen=false;_ready=false;
        _homeInnDeadline=DateTimeOffset.UtcNow.AddSeconds(15);
        _baseline.Changed(DateTimeOffset.UtcNow);Status="Returning to home inn";
    }
    private void ObserveHomeInnArrival()
    {
        if(!_homeInnPending||!_homeInnMapSeen)return;
        _homeInnPending=false;_baseline.Changed(DateTimeOffset.UtcNow);
        _connection?.EnqueueMessage(new ServerPursuitMessage{DialogType=DialogType.CloseDialog});
        Status="Arrived at home inn; reading inventory";
    }
}
