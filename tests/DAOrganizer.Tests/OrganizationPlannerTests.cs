using DAOrganizer.Core;
using Xunit;

namespace DAOrganizer.Tests;

public class OrganizationPlannerTests
{
    [Fact]
    public void FiveObservedStackableBankOwnersOfferFourPotentialSlots()
    {
        using var store=new InventoryStore(":memory:");
        foreach(var name in new[]{"Alpha","Bravo","Charlie","Delta","Echo"})
            store.SaveSnapshot(name,"Bank",[new Item(1,"Water Dungeon Chest",1,15,IsStackable:true)],true);
        store.SetItemOverride(new Item(1,"Water Dungeon Chest",1,15),new ItemOverride(null,"Charlie",false));

        var input=store.ReadOrganizationState();
        var plan=OrganizationPlanner.Build(input);
        var group=Assert.Single(plan.Groups);
        Assert.Equal("Charlie",group.ProposedHolder);
        Assert.Equal(5,group.CurrentBankSlots);
        Assert.Equal(1,group.PotentialBankSlots);
        Assert.Equal(4,group.PotentialSlotsFreed);
        Assert.Equal(0,group.VerifiedSlotsFreed);
        Assert.Equal(4,group.Routes.Count);
        Assert.All(group.Routes,route=>Assert.Equal(TransferRouteKind.ManualOnly,route.Kind));
    }

    [Fact]
    public void KnownCapacityLimitsTheEstimateAndNonstackablesDoNotSaveSlots()
    {
        using var store=new InventoryStore(":memory:");
        var chest=new Item(1,"Water Dungeon Chest",3,15,IsStackable:true);
        var sword=new Item(2,"Unique Sword",1,22,IsStackable:false);
        foreach(var name in new[]{"Alpha","Bravo","Charlie","Delta","Echo"})
            store.SaveSnapshot(name,"Bank",[chest,sword],true);
        store.SetItemMetadata(chest,new ItemMetadata(null,true,10,Tradeability.Unknown,"LocalObservation"));

        var plan=OrganizationPlanner.Build(store.ReadOrganizationState());
        Assert.Equal(3,Assert.Single(plan.Groups,x=>x.Item.Name==chest.Name).PotentialSlotsFreed);
        Assert.Equal(0,Assert.Single(plan.Groups,x=>x.Item.Name==sword.Name).PotentialSlotsFreed);
    }

    [Fact]
    public void SameAccountRouteUsesExplicitMiddlemanPair()
    {
        using var store=new InventoryStore(":memory:");
        foreach(var name in new[]{"Alpha","Bravo","Middle"})store.EnsureCharacter(name);
        store.SaveSnapshot("Alpha","Bank",[new Item(1,"Chest",2,15,IsStackable:true)],true);
        store.SaveSnapshot("Bravo","Bank",[new Item(1,"Chest",2,15,IsStackable:true)],true);
        var accounts=new AccountCatalog(store);
        var main=accounts.CreateGameAccount("Main");var other=accounts.CreateGameAccount("Other");
        accounts.AssignCharacter("Alpha",main.Id);accounts.AssignCharacter("Bravo",main.Id);
        accounts.AssignCharacter("Middle",other.Id);
        accounts.SetSameAccountCoexistence(main.Id,CoexistencePolicy.No);
        accounts.SetPairCoexistence(main.Id,other.Id,CoexistencePolicy.Yes);
        accounts.SetMiddlemanCapable("Middle",true);
        store.SetItemOverride(new Item(1,"Chest",2,15),new ItemOverride(null,"Bravo",false));

        var route=Assert.Single(Assert.Single(OrganizationPlanner.Build(store.ReadOrganizationState()).Groups).Routes);
        Assert.Equal(TransferRouteKind.Middleman,route.Kind);
        Assert.Equal("Middle",route.Middleman);
    }

    [Fact]
    public void SettingChangeInvalidatesReadOnlyPlanFingerprint()
    {
        using var store=new InventoryStore(":memory:");
        store.SaveSnapshot("Alpha","Bank",[new Item(1,"Chest",2,15,IsStackable:true)],true);
        var before=store.ReadOrganizationState().Fingerprint;
        store.Put("pins/alpha",new HashSet<int>{1});
        Assert.NotEqual(before,store.ReadOrganizationState().Fingerprint);
    }

    [Fact]
    public void AccountAssignmentInvalidatesPlanFingerprint()
    {
        using var store=new InventoryStore(":memory:");store.EnsureCharacter("Alpha");
        var before=store.ReadOrganizationState().Fingerprint;
        var account=store.CreateGameAccount("Main");store.AssignCharacter("Alpha",account.Id);
        Assert.NotEqual(before,store.ReadOrganizationState().Fingerprint);
    }

    [Fact]
    public void ExistingCategoryOverrideCanSelectStorageRole()
    {
        using var store=new InventoryStore(":memory:");
        foreach(var name in new[]{"Alpha","Bravo","StorageOne"})store.EnsureCharacter(name);
        store.SaveSnapshot("Alpha","Bank",[new Item(1,"Chest",1,15,IsStackable:true)],true);
        store.SaveSnapshot("Bravo","Bank",[new Item(1,"Chest",1,15,IsStackable:true)],true);
        store.Put("category/chest","Chests");
        var role=store.AddStorageRole("StorageOne","Chests");
        store.AddStorageRule(role.Id,StorageMatchKind.Category,"Chests");
        Assert.Equal("StorageOne",Assert.Single(OrganizationPlanner.Build(store.ReadOrganizationState()).Groups).ProposedHolder);
    }

    [Fact]
    public void JunkRuleDoesNotGenerateAConsolidationOpportunity()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Old Chest",1,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item],true);
        store.SaveSnapshot("Bravo","Bank",[item],true);
        new ItemRules(store).Set(item,ItemAction.Junk);
        Assert.Empty(OrganizationPlanner.Build(store.ReadOrganizationState()).Groups);
    }

    [Fact]
    public void UnscannedChosenHolderIsCalledOut()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Chest",1,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item],true);
        store.SaveSnapshot("Bravo","Bank",[item],true);
        store.EnsureCharacter("StorageOne");
        store.SetItemOverride(item,new ItemOverride(null,"StorageOne",false));
        var group=Assert.Single(OrganizationPlanner.Build(store.ReadOrganizationState()).Groups);
        Assert.Contains(group.Blockers,x=>x.Contains("StorageOne bank needs scan"));
    }

    [Fact]
    public void InventoryOwnerAppearsInRouteWithoutInflatingBankSavings()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Chest",1,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item],true);
        store.SaveSnapshot("Bravo","Bank",[item],true);
        store.SaveSnapshot("Charlie","Inventory",[item],true);
        var group=Assert.Single(OrganizationPlanner.Build(store.ReadOrganizationState()).Groups);
        Assert.Equal(3,group.Owners.Count);
        Assert.Equal(2,group.CurrentBankSlots);
        Assert.Equal(1,group.PotentialSlotsFreed);
        Assert.Contains(group.Routes,x=>x.Source=="Charlie"&&x.SourceLocation=="Inventory");
    }

    [Fact]
    public void StorageCategoryRuleCanTargetExistingItemFamily()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Water Dungeon Chest",1,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item],true);
        store.SaveSnapshot("Bravo","Bank",[item],true);
        store.EnsureCharacter("StorageOne");
        var role=store.AddStorageRole("StorageOne","Chests");
        store.AddStorageRule(role.Id,StorageMatchKind.Category,"Chests & gifts");
        Assert.Equal("StorageOne",Assert.Single(OrganizationPlanner.Build(store.ReadOrganizationState()).Groups).ProposedHolder);
    }

    [Fact]
    public void ConflictingEqualRolesNeedUserHolderChoice()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Chest",1,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item],true);store.SaveSnapshot("Bravo","Bank",[item],true);
        foreach(var name in new[]{"StorageOne","StorageTwo"})
        {
            store.EnsureCharacter(name);
            var role=store.AddStorageRole(name,"Everything");store.AddStorageRule(role.Id,StorageMatchKind.All,"");
        }
        var group=Assert.Single(OrganizationPlanner.Build(store.ReadOrganizationState()).Groups);
        Assert.Null(group.ProposedHolder);
        Assert.Contains(group.Blockers,x=>x.Contains("Conflicting storage roles"));
    }

    [Fact]
    public void SpecificItemRoleOutranksEverythingRole()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Chest",1,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item],true);store.SaveSnapshot("Bravo","Bank",[item],true);
        store.EnsureCharacter("StorageOne");store.EnsureCharacter("StorageTwo");
        var all=store.AddStorageRole("StorageOne","Everything",100);
        store.AddStorageRule(all.Id,StorageMatchKind.All,"");
        var exact=store.AddStorageRole("StorageTwo","Exact",0);
        store.AddStorageRule(exact.Id,StorageMatchKind.Item,ItemGroups.Key(item));
        Assert.Equal("StorageTwo",Assert.Single(OrganizationPlanner.Build(store.ReadOrganizationState()).Groups).ProposedHolder);
    }

    [Fact]
    public void ExplicitTradeRejectionRequiresManualReview()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Chest",1,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item],true);store.SaveSnapshot("Bravo","Bank",[item],true);
        var a=store.CreateGameAccount("One");var b=store.CreateGameAccount("Two");
        store.AssignCharacter("Alpha",a.Id);store.AssignCharacter("Bravo",b.Id);
        store.SetPairCoexistence(a.Id,b.Id,CoexistencePolicy.Yes);
        store.AddTradeObservation(item,TradeOutcome.ExplicitRejection,"ItemDenied","1.0","hash");
        var group=Assert.Single(OrganizationPlanner.Build(store.ReadOrganizationState()).Groups);
        Assert.Equal(TransferRouteKind.ManualOnly,Assert.Single(group.Routes).Kind);
        Assert.Contains(group.Blockers,x=>x.Contains("rejection",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PinnedInventorySlotCannotBeOfferedByTransferRoute()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(4,"Chest",3,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item],true);
        store.SaveSnapshot("Bravo","Bank",[item],true);
        store.SaveSnapshot("Charlie","Inventory",[item],true);
        var a=store.CreateGameAccount("First");var b=store.CreateGameAccount("Second");
        store.AssignCharacter("Alpha",a.Id);store.AssignCharacter("Charlie",b.Id);
        store.SetPairCoexistence(a.Id,b.Id,CoexistencePolicy.Yes);
        store.SetItemOverride(item,new ItemOverride(null,"Alpha",false));
        store.Put("pins/charlie",new HashSet<int>{4});

        var group=Assert.Single(OrganizationPlanner.Build(store.ReadOrganizationState()).Groups);
        var route=Assert.Single(group.Routes,x=>x.Source=="Charlie");
        Assert.Equal(TransferRouteKind.ManualOnly,route.Kind);
        Assert.Contains("pin",route.Reason,StringComparison.OrdinalIgnoreCase);
        Assert.Contains(group.Blockers,x=>x.Contains("Charlie",StringComparison.OrdinalIgnoreCase)&&
            x.Contains("pin",StringComparison.OrdinalIgnoreCase));
    }
}
