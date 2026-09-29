using DAOrganizer.Core;
using Xunit;
namespace DAOrganizer.Tests;

public class CategorySortingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuppliesTakeTheirFixedSlotsDespiteOldPins(bool category)
    {
        Item[] items=[new(1,"Red Potion",20),new(2,"Komadium",10),new(12,"Exkuranum",3),
            new(14,"Dibenomum",4),new(30,"Hemloch",5),new(3,"Emerald",6),new(40,"Pinned",1)];
        var plan=SlotPlanner.Sort(items,new HashSet<int>{1,2,3,12,40},category);
        Assert.Equal(new[]{"Komadium","Red Potion","Exkuranum","Dibenomum","Hemloch"},
            Enumerable.Range(1,5).Select(i=>plan[i].Name));
        Assert.Equal(items[^1],plan[40]);
        Assert.Equal(items.OrderBy(x=>x.Slot),plan.Values.OrderBy(x=>x.Slot));
        Assert.NotEmpty(SlotPlanner.Swaps(items,plan));
    }

    [Fact]
    public void FullInventoryKeepsEveryStackAndPrefersSupplyAlreadyInPlace()
    {
        var items=Enumerable.Range(1,59).Select(i=>new Item(i,i is 1 or 19?"Komadium":i==28?"Hemloch":$"Item {i:00}",i)).ToArray();
        var plan=SlotPlanner.Sort(items,new HashSet<int>());
        Assert.Equal(items[0],plan[1]);Assert.Equal(items[27],plan[5]);
        Assert.Equal(items.OrderBy(x=>x.Slot),plan.Values.OrderBy(x=>x.Slot));
        var occupants=items.ToDictionary(x=>x.Slot);
        foreach(var (from,to) in SlotPlanner.Swaps(items,plan))
        {
            occupants.Remove(from,out var a);occupants.Remove(to,out var b);
            occupants[to]=a!;if(b!=null)occupants[from]=b;
        }
        Assert.All(plan,x=>Assert.Equal(x.Value,occupants[x.Key]));
    }

    [Theory]
    [InlineData("Komadium","Consumables")]
    [InlineData("Exkuranum","Consumables")]
    [InlineData("Dibenomum","Consumables")]
    [InlineData("Hemloch","Consumables")]
    [InlineData("Satchel of Hemloch","Consumables")]
    [InlineData("ard ioc deum","Consumables")]
    [InlineData("Blue Extonic","Consumables")]
    [InlineData("Mileth Song","Consumables")]
    [InlineData("Papaya","Consumables")]
    [InlineData("Ruby Whip","Weapons")]
    [InlineData("Magus Zeus","Weapons")]
    [InlineData("Snow Secret","Weapons")]
    [InlineData("Leather Bliaut","Armor")]
    [InlineData("Silk White Dugon","Armor")]
    [InlineData("Mushroom Cap","Armor")]
    [InlineData("Amusement Park Ticket","Tools")]
    [InlineData("Mystery Unclassified Object","Other")]
    public void CategoriesRecognizeGameNames(string name,string category)=>Assert.Equal(category,ItemCategories.Infer(name));

    [Fact]
    public void LikeGearStaysAdjacentBeforeAlphabeticalNames()
    {
        using var db=new InventoryStore(":memory:");var catalog=new AccountCatalog(db);
        var items=catalog.Categorize([new(10,"Copper Boots",1),new(11,"Gold Shield",1),new(12,"Iron Boots",1),new(13,"Silver Shield",1)]);
        var plan=SlotPlanner.Sort(items,new HashSet<int>(),true);
        var names=SlotPlanner.BottomFirstSlots().Where(plan.ContainsKey).Select(i=>plan[i].Name).ToArray();
        Assert.Equal(1,Math.Abs(Array.IndexOf(names,"Copper Boots")-Array.IndexOf(names,"Iron Boots")));
        Assert.Equal(1,Math.Abs(Array.IndexOf(names,"Gold Shield")-Array.IndexOf(names,"Silver Shield")));
        var groups=ItemGroups.Build(items.Select(x=>new StoredItem("Example","Inventory",x,DateTimeOffset.UtcNow)));
        Assert.Equal(names,groups.Select(x=>x.Item.Name));
    }

    [Fact]
    public void StaleAutomaticCategoriesRefreshButManualOverridesWin()
    {
        using var db=new InventoryStore(":memory:");var catalog=new AccountCatalog(db);
        var item=new Item(9,"Komadium",10,Category:"Materials");
        Assert.Equal("Consumables",catalog.Category(item));
        db.Put("category/komadium","My supplies");Assert.Equal("My supplies",catalog.Category(item));
    }

    [Fact]
    public void SavedLayoutCannotDisplaceSuppliesAndKeepsDisplacedItems()
    {
        Item[] current=[new(1,"Komadium",3),new(2,"Red Potion",4),new(18,"Exkuranum",2),new(25,"Emerald",5)];
        var layout=new Dictionary<int,Item>{{49,current[0]},{50,current[1]},{51,current[2]},{3,current[3]}};
        PotionSlots.Place(layout);
        Assert.Equal(current[0],layout[1]);Assert.Equal(current[1],layout[2]);Assert.Equal(current[2],layout[3]);
        Assert.Equal(current[3],layout[51]);Assert.Equal(current.Length,layout.Count);
        PotionSlots.ValidateLocks(current,layout);Assert.NotEmpty(SlotPlanner.Swaps(current,layout));
    }

    [Fact]
    public void DragCannotReplaceALockedPotionButUnrelatedItemsCanMove()
    {
        Item[] current=[new(1,"Komadium",3),new(9,"Emerald",2)];
        var valid=new Dictionary<int,Item>{{1,current[0]},{10,current[1]}};
        PotionSlots.ValidateLocks(current,valid);
        var invalid=new Dictionary<int,Item>{{1,current[1]},{9,current[0]}};
        Assert.Throws<InvalidOperationException>(()=>PotionSlots.ValidateLocks(current,invalid));
    }

    [Fact]
    public void MissingPotionsDoNotShiftOtherHotkeysOrWasteFullInventorySpace()
    {
        var plan=SlotPlanner.Sort([new(22,"Dibenomum",3),new(12,"Apple",4)],new HashSet<int>());
        Assert.Equal("Dibenomum",plan[4].Name);Assert.Equal("Apple",plan[49].Name);
        Assert.Equal(2,plan.Count);
    }

    [Theory]
    [InlineData(1,"Komadium")]
    [InlineData(2,"Red Potion")]
    [InlineData(3,"Exkuranum")]
    [InlineData(4,"Dibenomum")]
    [InlineData(5,"Hemloch")]
    public void MaintenanceProtectsAllFiveLockedSupplies(int slot,string name)
    {
        Assert.True(MaintenancePlan.Protected(new(slot,name,10),new HashSet<int>()));
        Assert.False(MaintenancePlan.Protected(new(30,name,10),new HashSet<int>()));
    }
}
