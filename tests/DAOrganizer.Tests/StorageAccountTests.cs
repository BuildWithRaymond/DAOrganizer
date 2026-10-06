using DAOrganizer.Core;
using Xunit;

namespace DAOrganizer.Tests;

public class StorageAccountTests
{
    [Fact]
    public void CharactersCanShareAnExplicitGameAccount()
    {
        using var store=new InventoryStore(":memory:");
        store.EnsureCharacter("Alpha");store.EnsureCharacter("Beta");
        var accounts=new AccountCatalog(store);
        var gameAccount=accounts.CreateGameAccount("Main");
        accounts.AssignCharacter("Alpha",gameAccount.Id);
        accounts.AssignCharacter("Beta",gameAccount.Id);

        Assert.Equal(gameAccount.Id,accounts.AccountFor("alpha")?.Id);
        Assert.Equal(gameAccount.Id,accounts.AccountFor("Beta")?.Id);
        Assert.Equal("Main",Assert.Single(accounts.GameAccounts()).Label);
    }

    [Fact]
    public void CoexistenceNeedsAnExplicitPolicyForEachAccountRelationship()
    {
        using var store=new InventoryStore(":memory:");
        foreach(var name in new[]{"Alpha","Beta","Middle"})store.EnsureCharacter(name);
        var accounts=new AccountCatalog(store);
        var main=accounts.CreateGameAccount("Main");var other=accounts.CreateGameAccount("Other");
        accounts.AssignCharacter("Alpha",main.Id);accounts.AssignCharacter("Beta",main.Id);
        accounts.AssignCharacter("Middle",other.Id);

        Assert.Equal(CoexistencePolicy.Unknown,accounts.CanCoexist("Alpha","Beta"));
        Assert.Equal(CoexistencePolicy.Unknown,accounts.CanCoexist("Alpha","Middle"));
        accounts.SetSameAccountCoexistence(main.Id,CoexistencePolicy.No);
        accounts.SetPairCoexistence(main.Id,other.Id,CoexistencePolicy.Yes);
        Assert.Equal(CoexistencePolicy.No,accounts.CanCoexist("Alpha","Beta"));
        Assert.Equal(CoexistencePolicy.Yes,accounts.CanCoexist("Alpha","Middle"));
        Assert.Equal(CoexistencePolicy.Yes,accounts.CanCoexist("Middle","Alpha"));
    }

    [Fact]
    public void StorageCharacterKeepsMultipleRolesAndRuleKinds()
    {
        using var store=new InventoryStore(":memory:");store.EnsureCharacter("StorageOne");
        var accounts=new AccountCatalog(store);
        var chests=accounts.AddStorageRole("StorageOne","Chests",10);
        var eventItems=accounts.AddStorageRole("StorageOne","Events",5);
        accounts.AddStorageRule(chests.Id,StorageMatchKind.Category,"Chests");
        accounts.AddStorageRule(chests.Id,StorageMatchKind.Item,"15:0:WATER DUNGEON CHEST");
        accounts.AddStorageRule(eventItems.Id,StorageMatchKind.All,"");

        Assert.Equal(2,accounts.StorageRoles("StorageOne").Count);
        Assert.Equal(2,accounts.StorageRules(chests.Id).Count);
        Assert.Equal(StorageMatchKind.All,Assert.Single(accounts.StorageRules(eventItems.Id)).MatchKind);
    }

    [Fact]
    public void MiddlemanDesignationIsPerCharacter()
    {
        using var store=new InventoryStore(":memory:");
        store.EnsureCharacter("Middle");store.EnsureCharacter("Other");
        var accounts=new AccountCatalog(store);
        accounts.SetMiddlemanCapable("Middle",true);
        Assert.True(accounts.MiddlemanCapable("Middle"));
        Assert.False(accounts.MiddlemanCapable("Other"));
    }

    [Fact]
    public void StorageRulesAndRolesCanBeDisabledOrRemoved()
    {
        using var store=new InventoryStore(":memory:");store.EnsureCharacter("StorageOne");
        var accounts=new AccountCatalog(store);
        var role=accounts.AddStorageRole("StorageOne","Chests");
        var rule=accounts.AddStorageRule(role.Id,StorageMatchKind.Category,"Consumables");
        accounts.SetStorageRoleEnabled(role.Id,false);
        Assert.False(Assert.Single(accounts.StorageRoles("StorageOne")).Enabled);
        accounts.RemoveStorageRule(rule.Id);
        Assert.Empty(accounts.StorageRules(role.Id));
        accounts.RemoveStorageRole(role.Id);
        Assert.Empty(accounts.StorageRoles("StorageOne"));
    }

    [Fact]
    public void PairPolicyCanBeReadBackAndAssignmentRemoved()
    {
        using var store=new InventoryStore(":memory:");store.EnsureCharacter("Alpha");
        var accounts=new AccountCatalog(store);
        var first=accounts.CreateGameAccount("Main");var second=accounts.CreateGameAccount("Other");
        accounts.AssignCharacter("Alpha",first.Id);
        accounts.SetPairCoexistence(first.Id,second.Id,CoexistencePolicy.Yes);
        Assert.Equal(CoexistencePolicy.Yes,accounts.PairCoexistence(second.Id,first.Id));
        accounts.UnassignCharacter("Alpha");
        Assert.Null(accounts.AccountFor("Alpha"));
    }
}
