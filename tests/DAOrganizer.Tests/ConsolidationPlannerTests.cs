using DAOrganizer.Core;
using Xunit;

namespace DAOrganizer.Tests;

public class ConsolidationPlannerTests
{
    [Fact]
    public void SummarizesEligibleQuantitiesAndPinnedCopies()
    {
        using var store=Store();
        store.SaveSnapshot("Alpha","Inventory",[Potion(1,4),Potion(2,3)],true);
        store.SaveSnapshot("Alpha","Bank",[Potion(9,8)],true);
        store.SaveSnapshot("Bravo","Bank",[Potion(4,5)],true);
        store.Put("pins/alpha",new HashSet<int>{2});

        var summary=ConsolidationPlanner.Build(store.ReadOrganizationState(),Potion(0,1),"Bravo");

        var source=Assert.Single(summary.Sources);
        Assert.Equal(4,source.InventoryQuantity);
        Assert.Equal(8,source.BankQuantity);
        Assert.Equal(3,source.ProtectedQuantity);
        Assert.Equal(12,summary.EligibleQuantity);
        Assert.Equal(5,summary.DestinationBankQuantity);
        Assert.True(summary.CanStart);
    }

    [Theory]
    [InlineData(CoexistencePolicy.Unknown)]
    [InlineData(CoexistencePolicy.No)]
    public void BlocksBeforeStartingWithoutProvenCoexistence(CoexistencePolicy policy)
    {
        using var store=Store(policy);
        store.SaveSnapshot("Alpha","Bank",[Potion(1,2)],true);
        store.SaveSnapshot("Bravo","Bank",[],true);

        var summary=ConsolidationPlanner.Build(store.ReadOrganizationState(),Potion(0,1),"Bravo");

        Assert.False(summary.CanStart);
        Assert.Contains("proven direct handoff",summary.Blocker);
    }

    [Fact]
    public void PendingSourceCustodyIsOneClearBlocker()
    {
        using var store=Store();
        store.SaveSnapshot("Alpha","Bank",[Potion(1,2)],true);
        store.SaveSnapshot("Bravo","Bank",[],true);

        var summary=ConsolidationPlanner.Build(store.ReadOrganizationState(),Potion(0,1),"Bravo",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase){"Alpha"});

        Assert.Equal("Alpha has an unfinished transfer. Review its last verified holder first.",summary.Blocker);
    }

    [Fact]
    public void NeverMoveAndRejectedTradeEvidenceBlockAllMovement()
    {
        using var store=Store();
        store.SaveSnapshot("Alpha","Bank",[Potion(1,2)],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        store.SetItemOverride(Potion(0,1),new(null,null,true));
        Assert.Contains("Never move",ConsolidationPlanner.Build(store.ReadOrganizationState(),Potion(0,1),"Bravo").Blocker);
        store.SetItemOverride(Potion(0,1),new(null,null,false));
        store.AddTradeObservation(Potion(0,1),TradeOutcome.ExplicitRejection,"Denied","test",null);
        Assert.Contains("cannot be exchanged",ConsolidationPlanner.Build(store.ReadOrganizationState(),Potion(0,1),"Bravo").Blocker);
    }

    private static InventoryStore Store(CoexistencePolicy policy=CoexistencePolicy.Yes)
    {
        var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Inventory",[],true);
        store.SaveSnapshot("Bravo","Inventory",[],true);
        var a=store.CreateGameAccount("A");var b=store.CreateGameAccount("B");
        store.AssignCharacter("Alpha",a.Id);store.AssignCharacter("Bravo",b.Id);
        store.SetPairCoexistence(a.Id,b.Id,policy);
        return store;
    }

    private static Item Potion(int slot,long quantity)=>new(slot,"Shrinking Potion",quantity,15,2,IsStackable:true);
}
