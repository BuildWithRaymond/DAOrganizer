using DAOrganizer.Core;
using Xunit;
namespace DAOrganizer.Tests;

public class InventoryTests
{
    [Fact] public void SortingPreservesPinnedSlotsAndEveryItem()
    {
        Item[] items = [new(1,"Zinc",2), new(2,"Apple",3), new(3,"Bread",1)];
        var plan = SlotPlanner.Sort(items, new HashSet<int>{1});
        Assert.Equal("Zinc", plan[1].Name);
        Assert.Equal("Apple", plan[49].Name);
        Assert.Equal("Bread", plan[50].Name);
        Assert.Equal(6L, plan.Values.Sum(x=>x.Quantity));
    }
    [Fact] public void SortingHandlesFullInventoryAndDuplicates()
    {
        var items = Enumerable.Range(1,59).Select(i=>new Item(i,i%2==0?"Apple":"Zinc",i)).ToArray();
        var plan = SlotPlanner.Sort(items, new HashSet<int>());
        Assert.Equal(59,plan.Count); Assert.Equal("Apple",plan[49].Name);
        Assert.Equal(items.Sum(x=>x.Quantity),plan.Values.Sum(x=>x.Quantity));
        Assert.DoesNotContain(60,plan.Keys);
    }
    [Fact] public void BottomRowsFillLeftToRightAndHealingHotkeysStayPut()
    {
        Item[] items=[new(1,"Red Potion",20),new(2,"Komadium",10),..Enumerable.Range(3,14).Select(i=>new Item(i,$"Item {i:00}",1))];
        var plan=SlotPlanner.Sort(items,new HashSet<int>{50});
        Assert.Equal(items[1],plan[1]);Assert.Equal(items[0],plan[2]);
        Assert.False(plan.ContainsKey(50));Assert.False(plan.ContainsKey(60));
        Assert.Equal("Item 03",plan[49].Name);Assert.Equal("Item 04",plan[51].Name);
        Assert.Equal("Item 13",plan[37].Name);
        Assert.Equal(items.Length,plan.Count);
        Assert.DoesNotContain(plan.Keys,x=>x is >=3 and <=36);
    }
    [Fact] public void PartialBankScanPreservesCompleteSnapshot()
    {
        using var db = new InventoryStore(":memory:");
        db.SaveSnapshot("Alice","Bank",[new(1,"Ruby",12)],true);
        db.SaveSnapshot("Alice","Bank",[new(1,"Ruby",2)],false);
        Assert.Equal(12,Assert.Single(db.Items("Alice","Bank")).Quantity);
        Assert.Equal("Incomplete",db.Freshness("Alice","Bank"));
    }
    [Fact] public void CharactersAreIsolatedAndNamesIgnoreCase()
    {
        using var db = new InventoryStore(":memory:");
        db.SaveSnapshot("Alice","Inventory",[new(1,"Ruby",12)],true);
        db.SaveSnapshot("Bob","Inventory",[new(1,"Ruby",2)],true);
        Assert.Equal(12,Assert.Single(db.Items("alice","Inventory")).Quantity);
        Assert.Equal(2,Assert.Single(db.Items("Bob","Inventory")).Quantity);
    }
    [Fact] public void EmptyCompleteSnapshotClearsOldItems()
    {
        using var db = new InventoryStore(":memory:");
        db.SaveSnapshot("Alice","Inventory",[new(1,"Ruby",12)],true);
        db.SaveSnapshot("Alice","Inventory",[],true);
        Assert.Empty(db.Items("Alice","Inventory"));
    }
    [Fact] public void ObservedStackabilitySurvivesSnapshotReload()
    {
        using var db=new InventoryStore(":memory:");
        db.SaveSnapshot("Alice","Inventory",[new Item(1,"Water Dungeon Chest",8,15,IsStackable:true)],true);
        Assert.True(Assert.Single(db.Items("Alice","Inventory")).IsStackable);
    }
    [Fact] public void RoutesRespectDirectedPortals()
    {
        var graph = new WorldGraph([new(1,"Start",10,10,[new(1,9,5,2,0,5)]),new(2,"Bank",10,10,[])]);
        Assert.Single(graph.Route(1,2));
        Assert.Throws<InvalidOperationException>(()=>graph.Route(2,1));
    }
    [Fact] public void TilePathAvoidsWalls()
    {
        var path = TilePath.Find(5,5,new(0,0),new(2,0),p=>p != new Tile(1,0));
        Assert.DoesNotContain(new Tile(1,0),path);
        Assert.Equal(new Tile(2,0),path[^1]);
    }
}
