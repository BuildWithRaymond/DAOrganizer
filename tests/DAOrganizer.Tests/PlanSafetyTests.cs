using DAOrganizer.Core;
using Xunit;
namespace DAOrganizer.Tests;
public class PlanSafetyTests
{
    [Fact]
    public void StalePreviewIsRejectedBeforeSendingAnySwaps()
    {
        Item[] current=[new(1,"Potion",5),new(2,"Sword",1)];
        var desired=SlotPlanner.Sort(current, new HashSet<int>());
        current[0]=current[0] with{Quantity=4};
        Assert.Throws<InvalidOperationException>(()=>SlotPlanner.Swaps(current,desired));
    }
    [Fact]
    public void DuplicateAndOutOfRangeDestinationsAreRejected()
    {
        Item[] current=[new(1,"Potion",5),new(2,"Sword",1)];
        Assert.Throws<InvalidOperationException>(()=>SlotPlanner.Swaps(current,new(){{1,current[0]},{2,current[0]}}));
        Assert.Throws<InvalidOperationException>(()=>SlotPlanner.Swaps(current,new(){{1,current[0]},{60,current[1]}}));
    }
    [Fact]
    public void DatabasePersistsAcrossReopenAndFailedTransactionKeepsOldItems()
    {
        var path=Path.Combine(Path.GetTempPath(),"DAOrganizer-"+Guid.NewGuid()+".db");
        try
        {
            using(var store=new InventoryStore(path))
            {
                store.SaveSnapshot("Example","Bank",[new(1,"Emerald",9)],true);
                Assert.ThrowsAny<Exception>(()=>store.SaveSnapshot("Example","Bank",[new(1,"A",1),new(1,"B",2)],true));
            }
            using(var store=new InventoryStore(path))Assert.Equal("Emerald",Assert.Single(store.Items("Example","Bank")).Name);
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();File.Delete(path);}
    }
}
