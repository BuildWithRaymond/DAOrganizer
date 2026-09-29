using DAOrganizer.Core;
using Xunit;
namespace DAOrganizer.Tests;

public class ItemMaintenanceTests
{
    private static StoredItem Row(string owner,string location,int slot,long count,byte color=0)=>new(owner,location,new(slot,"Emerald",count,15,color),DateTimeOffset.UtcNow);
    [Fact]
    public void TotalsCombineOwnersAndLocationsButPreserveVariants()
    {
        var groups=ItemGroups.Build([Row("Alpha","Inventory",2,9),Row("Beta","Bank",1,30),Row("Beta","Bank",2,2,1)]);
        Assert.Equal(2,groups.Count);
        var plain=Assert.Single(groups,x=>x.Item.Color==0);
        Assert.Equal(39,plain.Quantity);Assert.Equal(2,plain.Owners.Count);
    }
    [Fact]
    public void RulesPersistAndPlanIgnoresEquipmentAndPins()
    {
        using var store=new InventoryStore(":memory:");var rules=new ItemRules(store);
        var rows=new[]{Row("Alpha","Inventory",2,9),Row("Beta","Bank",1,30),Row("Beta","Equipment",1,1)};
        rules.Set(rows[0].Item,ItemAction.Junk);
        Assert.Equal(ItemAction.Junk,new ItemRules(store).Get(rows[1].Item));
        var plan=MaintenancePlan.Build(rows,rules,ItemAction.Junk,n=>n=="Alpha"?[2]:[]);
        Assert.Single(plan);Assert.Equal("Bank",plan[0].Location);
        rules.Set(rows[0].Item,ItemAction.AutoDeposit);
        Assert.Empty(MaintenancePlan.Build(rows,rules,ItemAction.AutoDeposit,_=>[2]));
        Assert.Single(MaintenancePlan.Build(rows,rules,ItemAction.AutoDeposit,_=>[]));
    }
    [Fact]
    public void NoRuleMeansKeepAndIdentityIgnoresQuantitySlotAndCategory()
    {
        using var store=new InventoryStore(":memory:");var rules=new ItemRules(store);var item=Row("Alpha","Inventory",2,9).Item;
        Assert.Equal(ItemAction.Keep,rules.Get(item));
        rules.Set(item,ItemAction.Junk);
        Assert.Equal(ItemAction.Junk,rules.Get(item with{Name="EMERALD",Quantity=40,Slot=8,Category="Gem"}));
        Assert.Equal(ItemAction.Keep,rules.Get(item with{Color=1}));
    }
    [Fact]
    public async Task RejectedBankDropLeavesOneWithdrawnItemAndNeverRetriesIt()
    {
        using var store=new InventoryStore(":memory:");var rules=new ItemRules(store);var item=new Item(1,"Emerald",30,15);
        rules.Set(item,ItemAction.Junk);var client=new FakeClient{Bank=[item],DropAccepted=false};var results=new List<MaintenanceResult>();
        await MaintenanceRunner.Run(client,[new("Alpha","Bank",item,ItemAction.Junk)],rules,()=>[],results.Add,default);
        Assert.Equal(1,client.Withdrawals);Assert.Equal(1,client.Drops);Assert.Equal(1,Assert.Single(client.Inventory()).Quantity);
        Assert.Contains(results,x=>x.Result.Contains("left in inventory"));
    }
    [Fact]
    public async Task SuccessfulBankCleanupNeverDropsMoreThanApproved()
    {
        using var store=new InventoryStore(":memory:");var rules=new ItemRules(store);var item=new Item(1,"Emerald",2,15);
        rules.Set(item,ItemAction.Junk);var client=new FakeClient{Bank=[item with{Quantity=30}]};
        await MaintenanceRunner.Run(client,[new("Alpha","Bank",item,ItemAction.Junk)],rules,()=>[],_=>{},default);
        Assert.Equal(2,client.Drops);Assert.Equal(28,Assert.Single(client.Bank).Quantity);Assert.Empty(client.Inventory());
    }
    [Fact]
    public async Task ChangedRuleAndPinnedSlotCannotBeDroppedFromOldPreview()
    {
        using var store=new InventoryStore(":memory:");var rules=new ItemRules(store);var item=new Item(3,"Emerald",2,15);
        var client=new FakeClient{Items=[item]};
        var plan=new[]{new MaintenanceEntry("Alpha","Inventory",item,ItemAction.Junk)};
        await MaintenanceRunner.Run(client,plan,rules,()=>[],_=>{},default);Assert.Equal(0,client.Drops);
        rules.Set(item,ItemAction.Junk);
        await MaintenanceRunner.Run(client,plan,rules,()=>[3],_=>{},default);Assert.Equal(0,client.Drops);
    }
    [Fact]
    public async Task SameNameBankVariantsAndFullInventoryAreSkipped()
    {
        using var store=new InventoryStore(":memory:");var rules=new ItemRules(store);var item=new Item(1,"Emerald",2,15);
        rules.Set(item,ItemAction.Junk);var client=new FakeClient{Bank=[item,item with{Slot=2,Color=1}]};
        var plan=new[]{new MaintenanceEntry("Alpha","Bank",item,ItemAction.Junk)};
        await MaintenanceRunner.Run(client,plan,rules,()=>[],_=>{},default);Assert.Equal(0,client.Withdrawals);
        client.Bank=[item];client.Items=Enumerable.Range(1,59).Select(x=>new Item(x,"Other",1)).ToArray();
        await MaintenanceRunner.Run(client,plan,rules,()=>[],_=>{},default);Assert.Equal(0,client.Withdrawals);
    }
    private sealed class FakeClient:IItemMaintenanceClient
    {
        public Item[] Items=[];public Item[] Bank=[];public bool DropAccepted=true;public int Drops,Withdrawals;
        public Item[] Inventory()=>Items;
        public IReadOnlyList<Item> BankItems()=>Bank;
        public Task<bool> DropItem(Item item,long quantity,CancellationToken token)
        {
            Drops++;if(DropAccepted)Items=Items.Where(x=>x.Slot!=item.Slot).ToArray();return Task.FromResult(DropAccepted);
        }
        public Task<bool> DepositItem(Item item,long quantity,CancellationToken token)=>throw new NotImplementedException();
        public Task<bool> WithdrawOne(Item item,CancellationToken token)
        {
            Withdrawals++;Bank=Bank.Select(x=>x with{Quantity=x.Quantity-1}).ToArray();Items=[item with{Slot=4,Quantity=1}];return Task.FromResult(true);
        }
    }
}
