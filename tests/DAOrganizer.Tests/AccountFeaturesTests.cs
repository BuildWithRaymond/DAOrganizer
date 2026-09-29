using DAOrganizer.Core;
using Xunit;
namespace DAOrganizer.Tests;
public class AccountFeaturesTests
{
    [Theory]
    [InlineData("Mileth Storage",true)]
    [InlineData("Bank of Plamit",true)]
    [InlineData("Rucesion Inn",false)]
    [InlineData("Loures Weapon Storage",false)]
    public void DetectsBanks(string name,bool expected)=>Assert.Equal(expected,BankRoutes.IsBank(name));
    [Fact]
    public void ClosestBankUsesWalkingDistanceAndSkipsUnreachable()
    {
        var graph=new WorldGraph([
            new(1,"Town",100,100,[new(1,90,90,2,1,1),new(1,2,1,3,1,1)]),
            new(2,"Far Bank",10,10,[]),new(3,"Near Storage",10,10,[]),new(4,"Disconnected Bank",10,10,[])]);
        var route=BankRoutes.Closest(graph,1,new(1,1));
        Assert.Equal(3,route.Map.Id);Assert.Single(route.Portals);
        Assert.Empty(BankRoutes.Closest(graph,3,new(2,2)).Portals);
    }
    [Fact]
    public void DisplayFilterIncludesBankAndInventoryAndPreservesHiddenHistory()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Inventory",[new(1,"Ruby",2)],true);
        store.SaveSnapshot("Alpha","Bank",[new(1,"Ruby",7)],true);
        store.SaveSnapshot("Beta","Inventory",[new(1,"Ruby",1)],true);
        var catalog=new AccountCatalog(store);
        Assert.Equal(3,catalog.Items("").Count);
        catalog.SetDisplay("beta",false);
        Assert.Equal(2,new AccountCatalog(store).Items("ruby").Count);
        Assert.Single(store.Items("Beta","Inventory"));
        catalog.SetDisplay("Beta",true);Assert.Equal(3,catalog.Items("").Count);
    }
    [Fact]
    public void CategorySortDiffersFromNameAndManualCategoryWins()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Inventory",[new(1,"Apple",1),new(2,"Zinc Sword",1),new(3,"Emerald",2)],true);
        var catalog=new AccountCatalog(store);
        var items=catalog.Items("").Select(x=>x.Item).ToArray();
        var byName=SlotPlanner.Sort(items,new HashSet<int>());
        var byCategory=SlotPlanner.Sort(items,new HashSet<int>(),true);
        Assert.NotEqual(byName[49].Name,byCategory[49].Name);
        store.Put("category/zinc sword","Quest");Assert.Equal("Quest",catalog.Category(new(1,"Zinc Sword",1)));
    }
    [Fact]
    public void EquippedWeaponTeachesCategoryForSameNamedInventoryItem()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Equipment",[new(1,"Thunderfury",1)],true);
        var catalog=new AccountCatalog(store);Assert.Equal("Weapons",catalog.Category(new(12,"Thunderfury",1)));
    }
}
