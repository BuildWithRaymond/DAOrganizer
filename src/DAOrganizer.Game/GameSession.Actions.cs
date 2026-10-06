using Arbiter.Net.Client.Messages;
using Arbiter.Net.Client.Types;
using Arbiter.Net.Types;
using DAOrganizer.Core;
namespace DAOrganizer.Game;

public sealed partial class GameSession
{
    private readonly SemaphoreSlim _actionGate=new(1,1);
    public async Task ApplyLayout(Dictionary<int,Item> desired,CancellationToken token)
    {
        if(!Ready)throw new InvalidOperationException("Wait for a complete inventory before moving items.");
        await _actionGate.WaitAsync(token);
        try
        {
            var before=Inventory();var expected=before.ToDictionary(x=>x.Slot);
            foreach(var (from,to) in SlotPlanner.Swaps(before,desired))
            {
                token.ThrowIfCancellationRequested();
                if(!Inventory().SequenceEqual(expected.Values.OrderBy(x=>x.Slot)))throw new InvalidOperationException("Inventory changed. Review layout and try again.");
                expected.Remove(from,out var source);expected.Remove(to,out var destination);
                if(source!=null)expected[to]=source with{Slot=to};if(destination!=null)expected[from]=destination with{Slot=from};
                SetStatus($"Moving slot {from} → {to}");
                Send(new ClientChangeSlotMessage{Pane=ClientSlotSwapType.Inventory,SourceSlot=(byte)from,TargetSlot=(byte)to});
                await WaitUntil(()=>Inventory().SequenceEqual(expected.Values.OrderBy(x=>x.Slot)),TimeSpan.FromSeconds(4),token);
                await Task.Delay(150,token);
            }
            SetStatus("Inventory organized");
        }
        finally{_actionGate.Release();}
    }
    public async Task ScanNearbyBank(string? npcName,CancellationToken token)
    {
        if(!Online)throw new InvalidOperationException("Log in before scanning bank.");
        var candidates=Mundanes().Where(x=>string.IsNullOrWhiteSpace(npcName)||string.Equals(x.Name,npcName,StringComparison.OrdinalIgnoreCase))
            .OrderBy(x=>Math.Abs(x.X-Position.X)+Math.Abs(x.Y-Position.Y)).ToArray();
        if(candidates.Length==0)throw new InvalidOperationException("No NPC is visible. Approach an NPC and scan again; saved bank contents were retained.");
        var npc=candidates[0];var previous=LastBankScan;DateTimeOffset requested;
        lock(_gate)
        {
            token.ThrowIfCancellationRequested();
            SetStatus("Reading bank contents via hax banking");
            BankNpcId=npc.Id;_pendingBank=npc.Id;_bankRequested=requested=DateTimeOffset.UtcNow;
            _bankDialogRevision=DialogRevision;LastBankMenu=null;
            // Excalibur's bare /withdraw: request list directly from the visible mundane.
            try{Send(new ClientMerchantMessage{EntityType=EntityTypeFlags.Creature,EntityId=npc.Id,PursuitId=0x45});requested=_bankRequested;}
            catch{_pendingBank=null;throw;}
        }
        try
        {
            await WaitUntil(()=>LastBankScan!=previous,TimeSpan.FromSeconds(12),token);
            // A completed bank list is an NPC popup in the native client. Close only the menu
            // returned by this request; a silent empty-bank scan has no popup to close.
            lock(_gate)
                if(_bankRequested==requested&&LastBankMenu?.NpcId==npc.Id)CloseBankPopup(npc.Id);
        }
        catch(TimeoutException ex){throw new TimeoutException("Hax bank list did not complete. Saved contents retained.",ex);}
        finally
        {
            lock(_gate)if(_pendingBank==npc.Id&&_bankRequested==requested)_pendingBank=null;
        }
    }
    // Silence is an empty bank only after a correlated Withdraw Items request.
    private void CompleteSilentBank()
    {
        if(_pendingBank==null||DateTimeOffset.UtcNow-_bankRequested<TimeSpan.FromSeconds(10))return;
        if(!Online||_connection is not {IsConnected:true}||Error!=null||DialogRevision!=_bankDialogRevision||Name.Length==0)
        {_pendingBank=null;return;}
        _store.SaveSnapshot(Name,"Bank",[],true);LastBankMenu=null;LastBankScan=DateTimeOffset.UtcNow;
        _pendingBank=null;Status="Bank empty (no withdrawal response)";
    }
    public static async Task WaitUntil(Func<bool> condition,TimeSpan timeout,CancellationToken token)
    {
        var end=DateTimeOffset.UtcNow+timeout;
        while(!condition())
        {
            token.ThrowIfCancellationRequested();
            if(DateTimeOffset.UtcNow>=end)throw new TimeoutException("Game did not confirm the action. Stopped; no retry sent.");
            await Task.Delay(50,token);
        }
    }
}
