using System.Globalization;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Types;
using DAOrganizer.Core;
namespace DAOrganizer.Game;

public sealed partial class GameSession:IItemMaintenanceClient
{
    private Action MaintenanceGuard()
    {
        var map=MapId;var position=Position;var hp=Health;var manual=Interlocked.Read(ref _manualItemRevision);var movement=ManualMovementRevision;
        return ()=>
        {
            string? reason=Error!=null?"Tracking error: "+Error:
                !Online||_connection is not {IsConnected:true}?"Game connection closed":
                !Ready?"Inventory tracking is incomplete":
                MapId!=map?$"Map changed from {map} to {MapId}":
                Position!=position?$"Position changed from {position.X},{position.Y} to {Position.X},{Position.Y}":
                Health<hp?$"Health decreased from {hp} to {Health}":
                movement!=ManualMovementRevision?"Manual walking detected":
                manual!=Interlocked.Read(ref _manualItemRevision)?"Manual item action: "+_lastManualItemInput:null;
            if(reason!=null)throw new InvalidOperationException(reason+". Item action stopped.");
        };
    }
    public IReadOnlyList<Item> BankItems()=>_store.Items(Name,"Bank");
    private void CheckItem(Item item,long quantity)
    {
        if(!Ready||Error!=null)throw new InvalidOperationException(Error??"Wait for a complete inventory.");
        if(quantity<=0||quantity>uint.MaxValue||quantity>item.Quantity||item.Slot is <1 or >59)
            throw new InvalidOperationException("Invalid item quantity or slot.");
        if(!Inventory().Contains(item))throw new InvalidOperationException("Inventory changed before action. Review and try again.");
        var pins=_store.Get<HashSet<int>>("pins/"+Name.ToLowerInvariant())??[];
        if(MaintenancePlan.Protected(item,pins))throw new InvalidOperationException("Item is pinned or protected.");
    }
    private void CheckRule(Item item,ItemAction action)
    {
        if(new ItemRules(_store).Get(item)!=action)throw new InvalidOperationException("Item rule changed. Action stopped.");
    }
    private async Task<bool> ConfirmChange(Func<bool> changed,CancellationToken token,Action guard)
    {
        var end=DateTimeOffset.UtcNow.AddSeconds(5);var map=MapId;var position=Position;var hp=Health;
        while(!changed())
        {
            guard();
            token.ThrowIfCancellationRequested();
            if(!Online||_connection is not {IsConnected:true}||Error!=null||MapId!=map||Position!=position||Health<hp)
                throw new InvalidOperationException("Connection, position or health changed during item action. Stopped.");
            if(DateTimeOffset.UtcNow>=end)return false;
            await Task.Delay(50,token);
        }
        await Task.Delay(200,token);guard();return true;
    }
    private bool Decreased(Item item,long quantity)
    {
        var current=Inventory().FirstOrDefault(x=>x.Slot==item.Slot);
        if(current==null)return quantity==item.Quantity;
        if(ItemGroups.Key(current)!=ItemGroups.Key(item))throw new InvalidOperationException("Item slot changed during action.");
        return current.Quantity==item.Quantity-quantity;
    }
    public async Task<bool> DropItem(Item item,long quantity,CancellationToken token)
    {
        await _actionGate.WaitAsync(token);
        try
        {
            var guard=MaintenanceGuard();guard();
            lock(_gate)
            {
                token.ThrowIfCancellationRequested();CheckItem(item,quantity);CheckRule(item,ItemAction.Junk);
                SetStatus($"Dropping {quantity:N0} {item.Name}");
                Send(new ClientDropMessage{Slot=(byte)item.Slot,Quantity=(uint)quantity,X=(ushort)Position.X,Y=(ushort)Position.Y});
            }
            return await ConfirmChange(()=>Decreased(item,quantity),token,guard);
        }
        finally{_actionGate.Release();}
    }
    private uint HaxBankNpc()=>Mundanes().Where(x=>x.Id!=0)
        .OrderBy(x=>Math.Abs(x.X-Position.X)+Math.Abs(x.Y-Position.Y)).FirstOrDefault()?.Id
        ??throw new InvalidOperationException("No visible NPC for hax banking. Approach an NPC first.");
    private void CloseBankPopup(uint npc)
    {
        if(_expectedCloseNpc!=npc||DateTimeOffset.UtcNow>_expectedCloseUntil)_expectedCloseCount=0;
        _expectedCloseNpc=npc;_expectedCloseCount++;_expectedCloseUntil=DateTimeOffset.UtcNow.AddSeconds(5);
        Send(new ClientPursuitMessage{EntityType=EntityTypeFlags.Creature,EntityId=npc,PursuitId=0,StepId=1,ArgsType=DialogArgsType.None});
    }
    private async Task<bool> ConfirmBankChange(Func<bool> changed,DateTimeOffset sent,CancellationToken token,Action guard)
    {
        var confirmed=await ConfirmChange(changed,token,guard);
        // Excalibur spaces hax banking requests by 1100ms, including successful fast replies.
        var remaining=sent.AddMilliseconds(1100)-DateTimeOffset.UtcNow;
        if(remaining>TimeSpan.Zero)await Task.Delay(remaining,token);
        guard();return confirmed;
    }
    public async Task<bool> DepositItem(Item item,long quantity,CancellationToken token)
    {
        await _actionGate.WaitAsync(token);
        try
        {
            var guard=MaintenanceGuard();var sent=DateTimeOffset.UtcNow;
            lock(_gate)
            {
                token.ThrowIfCancellationRequested();guard();CheckItem(item,quantity);CheckRule(item,ItemAction.AutoDeposit);
                var npc=HaxBankNpc();
                SetStatus($"Hax deposit: {quantity:N0} {item.Name}");_store.MarkStale(Name,"Bank");
                // Excalibur DialogueRespond("Deposit slot [amount]") / "Deposit slot".
                // Send the final action directly: no root menu or quantity prompt needed.
                Send(item.Quantity==1
                    ?new ClientMerchantMessage{EntityType=EntityTypeFlags.Creature,EntityId=npc,PursuitId=0x53,Slot=(byte)item.Slot}
                    :new ClientMerchantMessage{EntityType=EntityTypeFlags.Creature,EntityId=npc,PursuitId=0x54,QuantitySlot=(byte)item.Slot,Arguments=[quantity.ToString(CultureInfo.InvariantCulture)]});
                CloseBankPopup(npc);
            }
            return await ConfirmBankChange(()=>Decreased(item,quantity),sent,token,guard);
        }
        finally{_actionGate.Release();}
    }
    // Approved organization transfers use their exact plan instead of the AutoDeposit rule.
    public async Task<bool> DepositApprovedTransfer(Item item,long quantity,CancellationToken token)
    {
        await _actionGate.WaitAsync(token);
        try
        {
            var guard=MaintenanceGuard();var sent=DateTimeOffset.UtcNow;
            lock(_gate)
            {
                token.ThrowIfCancellationRequested();guard();CheckItem(item,quantity);
                var npc=HaxBankNpc();
                var banker=Mundanes().SingleOrDefault(x=>x.Id==npc);
                if(BankNpcId!=npc||LastBankScan is null||banker is null||
                    Math.Abs(banker.X-Position.X)+Math.Abs(banker.Y-Position.Y)>1)
                    throw new InvalidOperationException("Approach and scan the same nearby banker before an approved deposit.");
                SetStatus($"Banking approved transfer: {quantity:N0} {item.Name}");_store.MarkStale(Name,"Bank");
                Send(item.Quantity==1
                    ?new ClientMerchantMessage{EntityType=EntityTypeFlags.Creature,EntityId=npc,PursuitId=0x53,Slot=(byte)item.Slot}
                    :new ClientMerchantMessage{EntityType=EntityTypeFlags.Creature,EntityId=npc,PursuitId=0x54,
                        QuantitySlot=(byte)item.Slot,Arguments=[quantity.ToString(CultureInfo.InvariantCulture)]});
                CloseBankPopup(npc);
            }
            return await ConfirmBankChange(()=>Decreased(item,quantity),sent,token,guard);
        }
        finally{_actionGate.Release();}
    }
    public async Task<bool> WithdrawOne(Item item,CancellationToken token)
    {
        await _actionGate.WaitAsync(token);
        try
        {
            var guard=MaintenanceGuard();long before;var sent=DateTimeOffset.UtcNow;
            lock(_gate)
            {
                token.ThrowIfCancellationRequested();guard();CheckRule(item,ItemAction.Junk);
                var bank=BankItems();var key=ItemGroups.Key(item);
                if(bank.Count(x=>x.Name.Equals(item.Name,StringComparison.OrdinalIgnoreCase))!=1||!bank.Any(x=>ItemGroups.Key(x)==key&&x.Quantity>0))return false;
                var inventory=Inventory();if(inventory.Length>=59)return false;
                var pins=_store.Get<HashSet<int>>("pins/"+Name.ToLowerInvariant())??[];
                if(inventory.Any(x=>ItemGroups.Key(x)==key&&MaintenancePlan.Protected(x,pins)))return false;
                before=inventory.Where(x=>ItemGroups.Key(x)==key).Sum(x=>x.Quantity);
                var npc=HaxBankNpc();SetStatus($"Hax withdraw: 1 {item.Name}");_store.MarkStale(Name,"Bank");
                // Excalibur DialogueRespond("Withdraw name [1]") then PopupClose(npc, 1).
                Send(new ClientMerchantMessage{EntityType=EntityTypeFlags.Creature,EntityId=npc,PursuitId=0x57,Arguments=[item.Name,"1"]});
                CloseBankPopup(npc);
            }
            bool Added()=>Inventory().Where(x=>ItemGroups.Key(x)==ItemGroups.Key(item)).Sum(x=>x.Quantity)==before+1;
            return await ConfirmBankChange(Added,sent,token,guard);
        }
        finally{_actionGate.Release();}
    }
    public async Task<Item?> WithdrawApprovedTransferOne(Item bankItem,DateTimeOffset approvedBankScan,CancellationToken token)
    {
        await _actionGate.WaitAsync(token);
        try
        {
            var guard=MaintenanceGuard();var sent=DateTimeOffset.UtcNow;
            lock(_gate)
            {
                token.ThrowIfCancellationRequested();guard();
                if(LastBankScan!=approvedBankScan||_store.Freshness(Name,"Bank")!="Current")
                    throw new InvalidOperationException("Source bank changed after the approved scan.");
                var matches=BankItems().Where(x=>x.Name.Equals(bankItem.Name,StringComparison.OrdinalIgnoreCase)).ToArray();
                if(matches.Length!=1||matches[0]!=bankItem||bankItem.Quantity<1||
                    Inventory().Length>=59||Inventory().Any(x=>ItemGroups.Key(x)==ItemGroups.Key(bankItem)))
                    throw new InvalidOperationException("Bank item name or source inventory is ambiguous.");
                var npc=BankNpcId??throw new InvalidOperationException("Scan a visible NPC before withdrawal.");
                if(!Mundanes().Any(x=>x.Id==npc))
                    throw new InvalidOperationException("Scanned NPC is no longer visible before withdrawal.");
                SetStatus($"Withdrawing approved transfer: 1 {bankItem.Name}");_store.MarkStale(Name,"Bank");
                Send(new ClientMerchantMessage{EntityType=EntityTypeFlags.Creature,EntityId=npc,
                    PursuitId=0x57,Arguments=[bankItem.Name,"1"]});
                CloseBankPopup(npc);
            }
            bool Added()=>Inventory().Count(x=>ItemGroups.Key(x)==ItemGroups.Key(bankItem)&&
                OrganizationPlanContract.MatchesObservedItem(x,bankItem)&&
                x.Quantity==1)==1;
            if(!await ConfirmBankChange(Added,sent,token,guard))return null;
            return Inventory().SingleOrDefault(x=>ItemGroups.Key(x)==ItemGroups.Key(bankItem)&&
                OrganizationPlanContract.MatchesObservedItem(x,bankItem)&&x.Quantity==1);
        }
        finally{_actionGate.Release();}
    }
}
