using DAOrganizer.Core;
using Xunit;

namespace DAOrganizer.Tests;

public class ItemIntelligenceTests
{
    [Fact]
    public void CategoryUsesUserOverrideBeforeCanonicalAndRecommendation()
    {
        using var store=new InventoryStore(":memory:");
        var item=new Item(1,"Water Dungeon Chest",8,15,IsStackable:true);
        var catalog=new AccountCatalog(store);
        store.SetItemMetadata(item,new ItemMetadata("Materials",true,20,Tradeability.Unknown,"Canonical"));
        store.SetCommunityCategory(item,"Consumables");
        Assert.Equal("Materials",catalog.Category(item));
        store.SetItemOverride(item,new ItemOverride("Chests",null,false));
        Assert.Equal("Chests",catalog.Category(item));
        store.SetItemOverride(item,new ItemOverride(null,null,false));
        Assert.Equal("Materials",catalog.Category(item));
    }

    [Fact]
    public void TradeEvidenceSeparatesExplicitRejectionFromAmbiguousFailure()
    {
        using var store=new InventoryStore(":memory:");
        var item=new Item(1,"Water Dungeon Chest",8,15);
        store.AddTradeObservation(item,TradeOutcome.Inconclusive,"Timeout","1.0","client-hash");
        Assert.Equal(Tradeability.Unknown,store.TradeEvidence(item).State);
        store.AddTradeObservation(item,TradeOutcome.ExplicitRejection,"ItemDenied","1.0","client-hash");
        Assert.Equal(Tradeability.EvidenceNonTradeable,store.TradeEvidence(item).State);
        store.AddTradeObservation(item,TradeOutcome.Success,"BothInventoriesVerified","1.0","client-hash");
        var evidence=store.TradeEvidence(item);
        Assert.Equal(Tradeability.Unknown,evidence.State);
        Assert.Equal(1,evidence.Successes);
        Assert.Equal(1,evidence.ExplicitRejections);
        Assert.Equal(1,evidence.Inconclusive);
    }

    [Fact]
    public void ItemOverrideKeepsDestinationAndNeverMoveLocal()
    {
        using var store=new InventoryStore(":memory:");store.EnsureCharacter("StorageOne");
        var item=new Item(1,"Water Dungeon Chest",8,15);
        store.SetItemOverride(item,new ItemOverride(null,"StorageOne",true));
        Assert.Equal(new ItemOverride(null,"StorageOne",true),store.GetItemOverride(item));
    }

    [Fact]
    public void VerifiedManualCaptureCanOnlyVoteOncePerOperation()
    {
        using var store=new InventoryStore(":memory:");
        var item=new Item(1,"Chest",2,77);
        Assert.True(store.AddVerifiedManualTrade(item,"operation-1","0.15.0"));
        Assert.False(store.AddVerifiedManualTrade(item,"operation-1","0.15.0"));
        Assert.Equal(1,store.TradeEvidence(item).Successes);
        Assert.Equal(Tradeability.EvidenceTradeable,store.TradeEvidence(item).State);
    }
}
