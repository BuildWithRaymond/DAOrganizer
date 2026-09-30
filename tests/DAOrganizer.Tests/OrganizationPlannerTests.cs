using DAOrganizer.Core;
using Xunit;

namespace DAOrganizer.Tests;

public class OrganizationPlannerTests
{
    private static readonly DateTimeOffset Created=new(2026,9,30,12,0,0,TimeSpan.Zero);

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

    [Fact]
    public void ExactPlanIsDeterministicAndUsesExplicitDirectLegs()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(7,"Chest",4,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item],true);store.SaveSnapshot("Bravo","Bank",[item with{Slot=2}],true);
        var first=store.CreateGameAccount("First");var second=store.CreateGameAccount("Second");
        store.AssignCharacter("Alpha",first.Id);store.AssignCharacter("Bravo",second.Id);
        store.SetPairCoexistence(first.Id,second.Id,CoexistencePolicy.Yes);
        store.SetItemOverride(item,new ItemOverride(null,"Bravo",false));
        var state=store.ReadOrganizationState();

        var plan=OrganizationPlanner.BuildExact(state,Created,Created.AddHours(1));
        var repeated=OrganizationPlanner.BuildExact(state,Created,Created.AddHours(1));

        Assert.NotNull(plan);Assert.NotNull(repeated);
        Assert.Equal(plan.Id,repeated.Id);
        Assert.Equal(plan.Steps.Select(x=>x.Id),repeated.Steps.Select(x=>x.Id));
        Assert.Equal(state.Fingerprint,plan.InputFingerprint);
        var step=Assert.Single(plan.Steps);
        Assert.Equal(TransferRouteKind.Direct,step.RouteKind);
        Assert.Equal(OrganizationReadiness.NeedsScan,step.Readiness);
        Assert.Equal(new[]{OrganizationLegKind.Withdraw,OrganizationLegKind.Exchange,OrganizationLegKind.Deposit},
            step.RouteLegs.Select(x=>x.Kind));
        Assert.Equal(0,step.Order);Assert.Empty(step.DependsOn);
        Assert.Equal(0,step.ExpectedAfter.Single(x=>x.Character=="Alpha").Quantity);
        Assert.Equal(8,step.ExpectedAfter.Single(x=>x.Character=="Bravo").Quantity);
    }

    [Fact]
    public void ExactPlanIncludesInventoryOnlyDuplicatesAndLocalDeposit()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(3,"Chest",2,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Inventory",[item],true);
        store.SaveSnapshot("Bravo","Inventory",[item with{Slot=4}],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        store.SetItemOverride(item,new ItemOverride(null,"Bravo",false));

        var plan=OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddMinutes(30));

        Assert.NotNull(plan);Assert.Equal(2,plan.Steps.Length);
        Assert.Contains(plan.Steps,x=>x.SourceCharacter=="Alpha"&&x.RouteKind==TransferRouteKind.ManualOnly);
        var local=Assert.Single(plan.Steps,x=>x.SourceCharacter=="Bravo");
        Assert.Equal(TransferRouteKind.Local,local.RouteKind);
        Assert.Equal(OrganizationLegKind.Deposit,Assert.Single(local.RouteLegs).Kind);
    }

    [Fact]
    public void InventoryOnlyDuplicatesChooseDeterministicHolderWithoutOverride()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Chest",2,15,IsStackable:true);
        store.SaveSnapshot("Zulu","Inventory",[item],true);
        store.SaveSnapshot("Alpha","Inventory",[item with{Slot=2}],true);

        var plan=OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddHours(1));

        Assert.NotNull(plan);
        Assert.All(plan.Steps,x=>Assert.Equal("Alpha",x.DestinationCharacter));
    }

    [Fact]
    public void ExactPlanSkipsPinnedAndKeepQuantityAndSplitsTradeLimit()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Chest",300,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Inventory",[item,item with{Slot=2,Quantity=20}],true);
        store.SaveSnapshot("Bravo","Bank",[item with{Slot=4,Quantity=1}],true);
        store.SetItemOverride(item,new ItemOverride(null,"Bravo",false));
        store.Put("pins/alpha",new HashSet<int>{2});
        var role=store.AddStorageRole("Bravo","Chest keeper");
        store.AddStorageRule(role.Id,StorageMatchKind.Item,ItemGroups.Key(item),65);

        var plan=OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddHours(1));

        Assert.NotNull(plan);Assert.Equal(new long[]{255},plan.Steps.Select(x=>x.Quantity));
        Assert.All(plan.Steps,x=>Assert.Equal(1,x.SourceSlot));
        Assert.Equal(45,plan.Steps[0].ExpectedAfter.Single(x=>x.Character=="Alpha").Quantity);
    }

    [Fact]
    public void ExactPlanKeepsDurabilityVariantsSeparate()
    {
        using var store=new InventoryStore(":memory:");
        var worn=new Item(1,"Sword",1,22,Durability:5,MaxDurability:10,IsStackable:false);
        var newSword=worn with{Slot=2,Durability=10};
        store.SaveSnapshot("Alpha","Inventory",[worn,newSword],true);
        store.SaveSnapshot("Bravo","Bank",[worn with{Slot=3}],true);
        store.SetItemOverride(worn,new ItemOverride(null,"Bravo",false));

        var plan=OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddHours(1));

        Assert.NotNull(plan);
        var step=Assert.Single(plan.Steps);
        Assert.Equal(5,step.SourceItem.Durability);
        Assert.Equal(OrganizationPlanContract.ItemFingerprint(worn),
            step.ExpectedAfter.Single(x=>x.Character=="Bravo").ItemFingerprint);
    }

    [Fact]
    public void ExactPlanUsesExplicitMiddlemanLegsAndUnknownCoexistenceIsManual()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Chest",2,15,IsStackable:true);
        foreach(var name in new[]{"Alpha","Bravo","Middle"})store.EnsureCharacter(name);
        store.SaveSnapshot("Alpha","Bank",[item],true);store.SaveSnapshot("Bravo","Bank",[item],true);
        var source=store.CreateGameAccount("Source");var destination=store.CreateGameAccount("Destination");
        var relay=store.CreateGameAccount("Relay");
        store.AssignCharacter("Alpha",source.Id);store.AssignCharacter("Bravo",destination.Id);
        store.AssignCharacter("Middle",relay.Id);store.SetMiddlemanCapable("Middle",true);
        store.SetItemOverride(item,new ItemOverride(null,"Bravo",false));

        var manual=Assert.Single(OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddHours(1))!.Steps);
        Assert.Equal(TransferRouteKind.ManualOnly,manual.RouteKind);
        store.SetPairCoexistence(source.Id,relay.Id,CoexistencePolicy.Yes);
        store.SetPairCoexistence(destination.Id,relay.Id,CoexistencePolicy.Yes);

        var routed=Assert.Single(OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddHours(1))!.Steps);
        Assert.Equal(TransferRouteKind.Middleman,routed.RouteKind);
        Assert.Equal(2,routed.RouteLegs.Count(x=>x.Kind==OrganizationLegKind.Exchange));
        Assert.Equal("Middle",routed.RouteLegs[1].DestinationCharacter);
    }

    [Fact]
    public void StaleBankUnknownCapAndConflictingEvidenceNeverBecomeReady()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Water Dungeon Chest",8,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item],true);store.MarkStale("Alpha","Bank");
        store.SaveSnapshot("Bravo","Bank",[item],true);
        var first=store.CreateGameAccount("First");var second=store.CreateGameAccount("Second");
        store.AssignCharacter("Alpha",first.Id);store.AssignCharacter("Bravo",second.Id);
        store.SetPairCoexistence(first.Id,second.Id,CoexistencePolicy.Yes);
        store.SetItemOverride(item,new ItemOverride(null,"Bravo",false));
        store.AddTradeObservation(item,TradeOutcome.Success,"Delivered","1.0","success");
        store.AddTradeObservation(item,TradeOutcome.ExplicitRejection,"Denied","1.0","denied");

        var step=Assert.Single(OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddHours(1))!.Steps);

        Assert.Equal(OrganizationReadiness.ManualOnly,step.Readiness);
        Assert.Equal(TransferRouteKind.ManualOnly,step.RouteKind);
        Assert.Empty(step.RouteLegs);
    }

    [Fact]
    public void FullDestinationStackCannotAcceptAnotherExactQuantity()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Chest",10,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item with{Quantity=3}],true);
        store.SaveSnapshot("Bravo","Bank",[item],true);
        store.SetItemMetadata(item,new ItemMetadata(null,true,10,Tradeability.Tradeable,"Observed"));
        store.SetItemOverride(item,new ItemOverride(null,"Bravo",false));
        var first=store.CreateGameAccount("First");var second=store.CreateGameAccount("Second");
        store.AssignCharacter("Alpha",first.Id);store.AssignCharacter("Bravo",second.Id);
        store.SetPairCoexistence(first.Id,second.Id,CoexistencePolicy.Yes);

        Assert.Null(OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddHours(1)));
    }

    [Fact]
    public void ExistingDestinationStackLimitsExactQuantityToKnownRoom()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Chest",8,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item],true);
        store.SaveSnapshot("Bravo","Bank",[item with{Quantity=9}],true);
        store.SetItemMetadata(item,new ItemMetadata(null,true,10,Tradeability.Tradeable,"Observed"));
        store.SetItemOverride(item,new ItemOverride(null,"Bravo",false));
        var first=store.CreateGameAccount("First");var second=store.CreateGameAccount("Second");
        store.AssignCharacter("Alpha",first.Id);store.AssignCharacter("Bravo",second.Id);
        store.SetPairCoexistence(first.Id,second.Id,CoexistencePolicy.Yes);

        var step=Assert.Single(OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddHours(1))!.Steps);
        Assert.Equal(1,step.Quantity);
        Assert.Equal(10,step.ExpectedAfter.Single(x=>x.Character=="Bravo").Quantity);
        Assert.Equal(OrganizationReadiness.NeedsScan,step.Readiness);
    }

    [Fact]
    public void MultipleDestinationStacksContributeOnlyTheirKnownRoom()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Chest",8,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item],true);
        store.SaveSnapshot("Bravo","Bank",[item with{Quantity=9},item with{Slot=2,Quantity=8}],true);
        store.SetItemMetadata(item,new ItemMetadata(null,true,10,Tradeability.Tradeable,"Observed"));
        store.SetItemOverride(item,new ItemOverride(null,"Bravo",false));

        var plan=OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddHours(1));
        Assert.NotNull(plan);
        Assert.Equal(new long[]{1,2},plan.Steps.Select(x=>x.Quantity));
        Assert.Equal(3,plan.Steps.Sum(x=>x.Quantity));
        Assert.Equal(20,plan.Steps[^1].ExpectedAfter.Single(x=>x.Character=="Bravo").Quantity);
    }

    [Fact]
    public void HugeQuantityHasFiniteBoundedCandidateCount()
    {
        using var store=new InventoryStore(":memory:");var item=new Item(1,"Chest",long.MaxValue,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Inventory",[item],true);
        store.SaveSnapshot("Bravo","Bank",[item with{Quantity=1}],true);
        store.SetItemOverride(item,new ItemOverride(null,"Bravo",false));

        var plan=OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddHours(1));
        Assert.NotNull(plan);
        Assert.InRange(plan.Steps.Length,1,256);
        Assert.All(plan.Steps,x=>Assert.InRange(x.Quantity,1,255));
        Assert.NotEqual(OrganizationReadiness.Ready,plan.Steps[0].Readiness);
    }

    [Fact]
    public void ExtremeSourceAndDestinationSumsDoNotOverflow()
    {
        using var store=new InventoryStore(":memory:");
        var item=new Item(1,"Chest",long.MaxValue,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item,item with{Slot=2}],true);
        store.SaveSnapshot("Bravo","Bank",[item with{Quantity=long.MaxValue-1}],true);
        store.SetItemOverride(item,new ItemOverride(null,"Bravo",false));

        var plan=OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddHours(1));
        Assert.NotNull(plan);
        Assert.Single(plan.Steps);
        Assert.Equal(1,plan.Steps[0].Quantity);
        Assert.Equal(long.MaxValue,plan.Steps[0].ExpectedAfter.Single(x=>x.Character=="Bravo").Quantity);
        Assert.Equal(0,OrganizationPlanner.Build(store.ReadOrganizationState()).VerifiedSlotsFreed);
    }

    [Fact]
    public void SameNameDifferentSpritesRemainSeparateExactIdentities()
    {
        using var store=new InventoryStore(":memory:");
        var first=new Item(1,"Chest",2,15,IsStackable:true);
        var second=first with{Slot=2,Sprite=16};
        store.SaveSnapshot("Alpha","Inventory",[first,second],true);
        store.SaveSnapshot("Bravo","Bank",[first with{Quantity=1},second with{Quantity=1}],true);
        store.SetItemOverride(first,new ItemOverride(null,"Bravo",false));
        store.SetItemOverride(second,new ItemOverride(null,"Bravo",false));

        var plan=OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddHours(1));
        Assert.NotNull(plan);
        Assert.Equal(2,plan.Steps.Length);
        Assert.Equal(2,plan.Steps.Select(x=>x.ItemKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void NonstackableQuantityUsesSingleItemChunks()
    {
        using var store=new InventoryStore(":memory:");
        var item=new Item(1,"Sword",3,22,IsStackable:false);
        store.SaveSnapshot("Alpha","Inventory",[item],true);
        store.SaveSnapshot("Bravo","Inventory",[item with{Quantity=1}],true);
        store.SaveSnapshot("Bravo","Bank",[],true);
        store.SetItemOverride(item,new ItemOverride(null,"Bravo",false));

        var plan=OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddHours(1));
        Assert.NotNull(plan);
        Assert.All(plan.Steps,x=>Assert.Equal(1,x.Quantity));
        Assert.All(plan.Steps,x=>Assert.NotEqual(OrganizationReadiness.Ready,x.Readiness));
    }

    [Fact]
    public void ExactPlanExcludesJunkAndNeverMove()
    {
        using var store=new InventoryStore(":memory:");var junk=new Item(1,"Junk",1,15);var fixedItem=new Item(2,"Fixed",1,16);
        foreach(var name in new[]{"Alpha","Bravo"})store.SaveSnapshot(name,"Bank",[junk,fixedItem],true);
        new ItemRules(store).Set(junk,ItemAction.Junk);
        store.SetItemOverride(fixedItem,new ItemOverride(null,"Bravo",true));
        Assert.Null(OrganizationPlanner.BuildExact(store.ReadOrganizationState(),Created,Created.AddHours(1)));
    }

    [Fact]
    public void ExactPlanRejectsInvalidTimesAndReturnsNullWithoutCandidates()
    {
        using var store=new InventoryStore(":memory:");store.SaveSnapshot("Alpha","Bank",[new Item(1,"Chest",1)],true);
        var state=store.ReadOrganizationState();
        Assert.Null(OrganizationPlanner.BuildExact(state,Created,Created.AddHours(1)));
        Assert.Throws<ArgumentException>(()=>OrganizationPlanner.BuildExact(state,Created,Created));
    }
}
