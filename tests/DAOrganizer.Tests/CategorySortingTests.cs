using DAOrganizer.Core;
using Xunit;
namespace DAOrganizer.Tests;

public class CategorySortingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitPinsOverrideQuickSlotSuggestions(bool category)
    {
        Item[] items=[new(1,"Red Potion",20),new(2,"Komadium",10),new(12,"Exkuranum",3),
            new(14,"Dibenomum",4),new(30,"Hemloch",5),new(3,"Emerald",6),new(40,"Pinned",1)];
        var plan=SlotPlanner.Sort(items,new HashSet<int>{1,2,3,12,40},category);
        Assert.All(items.Where(x=>new[]{1,2,3,12,40}.Contains(x.Slot)),x=>Assert.Equal(x,plan[x.Slot]));
        Assert.Equal(items[^1],plan[40]);
        Assert.Equal(items.OrderBy(x=>x.Slot),plan.Values.OrderBy(x=>x.Slot));
        Assert.NotEmpty(SlotPlanner.Swaps(items,plan));
    }

    [Fact]
    public void FullInventoryKeepsEveryStackAndPrefersSupplyAlreadyInPlace()
    {
        var items=Enumerable.Range(1,59).Select(i=>new Item(i,i is 1 or 19?"Komadium":i==28?"Hemloch":$"Item {i:00}",i)).ToArray();
        var plan=SlotPlanner.Sort(items,new HashSet<int>());
        Assert.Equal(items[0],plan[1]);Assert.Equal(items[27],plan[2]);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetectedCrystalArrowsComeBeforeCompactPotions(bool category)
    {
        Item[] items=[new(30,"Dual Crystal Arrows",1),new(8,"Red Potion",4),new(9,"Hemloch",3),new(10,"Wake Scroll",1)];
        var plan=SlotPlanner.Sort(items,new HashSet<int>(),category);
        Assert.Equal("Dual Crystal Arrows",plan[1].Name);Assert.Equal("Red Potion",plan[2].Name);Assert.Equal("Hemloch",plan[3].Name);
        Assert.Equal("Wake Scroll",plan[12].Name);
        Assert.Equal(items.OrderBy(x=>x.Slot),plan.Values.OrderBy(x=>x.Slot));
    }

    [Fact]
    public void TrinketOverflowConservesAllStacksInAFullInventory()
    {
        var items=Enumerable.Range(1,59).Select(i=>new Item(i,i==17?"Red Potion":i<=20?"Wake Scroll":$"Item {i}",i)).ToArray();
        var plan=SlotPlanner.Sort(items,new HashSet<int>{4,11},true);
        Assert.Equal(59,plan.Count);Assert.Equal("Red Potion",plan[1].Name);
        Assert.Equal(items.OrderBy(x=>x.Slot),plan.Values.OrderBy(x=>x.Slot));
        Assert.Equal(items[3],plan[4]);Assert.Equal(items[10],plan[11]);
    }

    [Fact]
    public void MissingPotionsPackLeftInPriorityOrder()
    {
        var plan=SlotPlanner.Sort([new(22,"Dibenomum",3),new(12,"Apple",4),new(30,"Red Potion",5),new(41,"Hemloch",2)],new HashSet<int>());
        Assert.Equal(new[]{"Red Potion","Dibenomum","Hemloch"},Enumerable.Range(1,3).Select(i=>plan[i].Name));
        Assert.Equal("Apple",plan[49].Name);
    }

    [Fact]
    public void TrinketsFillFirstRowFromRightAndNeverOverwritePotionsOrPins()
    {
        Item[] items=[new(21,"Wake Scroll",1),new(22,"Glowing Stone",1),new(23,"Nerve Stimulant",1),
            new(31,"Red Potion",4),new(12,"Pinned sword",1)];
        var plan=SlotPlanner.Sort(items,new HashSet<int>{12},true);
        Assert.Equal("Red Potion",plan[1].Name);Assert.Equal(items[^1],plan[12]);
        Assert.Equal(new[]{"Glowing Stone","Nerve Stimulant","Wake Scroll"},new[]{11,10,9}.Select(i=>plan[i].Name));
        Assert.Equal(items.OrderBy(x=>x.Slot),plan.Values.OrderBy(x=>x.Slot));
    }

    [Theory]
    [InlineData("Wake Scroll")]
    [InlineData("Glowing Stone")]
    [InlineData("Nerve Stimulant")]
    [InlineData("Vanishing Elixir")]
    [InlineData("Sprint Potion")]
    [InlineData("Monster Cloak")]
    [InlineData("Dragon's Scale")]
    public void VorlofTrinketsHaveTheirOwnCategory(string name)=>Assert.Equal("Trinkets",ItemCategories.Infer(name));

    [Fact]
    public void UnpinnedQuickPotionCanMoveButPinStillProtectsMaintenance()
    {
        var potion=new Item(1,"Komadium",3);
        Assert.False(MaintenancePlan.Protected(potion,new HashSet<int>()));
        Assert.True(MaintenancePlan.Protected(potion,new HashSet<int>{1}));
        Assert.Single(SlotPlanner.Swaps([potion],new(){{9,potion}}));
    }
}
