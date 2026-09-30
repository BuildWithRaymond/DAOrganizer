using DAOrganizer.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DAOrganizer.Tests;

public class OrganizationPlanSelectionTests
{
    [Fact]
    public void SavedDraftsCanBeListedAfterReopen()
    {
        var path=Path.Combine(Path.GetTempPath(),$"organizer-plans-{Guid.NewGuid():N}.db");
        try
        {
            Guid id;
            using(var store=new InventoryStore(path))
            {
                var item=new Item(1,"Chest",1,15,IsStackable:true);
                store.SaveSnapshot("Alpha","Bank",[item],true);
                store.SaveSnapshot("Bravo","Bank",[item],true);
                store.SetItemOverride(item,new ItemOverride(null,"Bravo",false));
                var now=DateTimeOffset.UtcNow;
                var plan=OrganizationPlanner.BuildExact(store.ReadOrganizationState(),now,now.AddMinutes(30))!;
                store.SaveOrganizationPlan(plan);id=plan.Id;
            }
            using var reopened=new InventoryStore(path);
            Assert.Equal(id,Assert.Single(reopened.ListOrganizationPlans()).Plan.Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach(var suffix in new[]{"","-wal","-shm"})File.Delete(path+suffix);
        }
    }

    [Fact]
    public void SelectingLaterStepRebasesSourceDestinationAndDependency()
    {
        using var store=new InventoryStore(":memory:");
        var item=new Item(1,"Chest",1,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item],true);
        store.SaveSnapshot("Bravo","Bank",[item],true);
        store.SaveSnapshot("Charlie","Bank",[item],true);
        store.SetItemOverride(item,new ItemOverride(null,"Bravo",false));
        var state=store.ReadOrganizationState();
        var now=DateTimeOffset.UtcNow;
        var original=OrganizationPlanner.BuildExact(state,now,now.AddMinutes(30))!;
        Assert.Equal(2,original.Steps.Length);

        var selected=OrganizationPlanSelection.Select(original,state,[original.Steps[1].Id]);

        var step=Assert.Single(selected.Steps);
        Assert.Equal(0,step.Order);
        Assert.Empty(step.DependsOn);
        Assert.Equal(1,step.ExpectedBefore.Single(x=>x.Character=="Bravo").Quantity);
        Assert.Equal(2,step.ExpectedAfter.Single(x=>x.Character=="Bravo").Quantity);
        Assert.Equal(1,step.ExpectedBefore.Single(x=>x.Character=="Charlie").Quantity);
        Assert.Equal(selected.Id,OrganizationPlanSelection.Select(original,state,[step.Id]).Id);
        OrganizationPlanContract.Validate(selected);
    }

    [Fact]
    public void SelectionRejectsUnknownOrEmptySteps()
    {
        using var store=new InventoryStore(":memory:");
        var item=new Item(1,"Chest",1,15,IsStackable:true);
        store.SaveSnapshot("Alpha","Bank",[item],true);
        store.SaveSnapshot("Bravo","Bank",[item],true);
        store.SetItemOverride(item,new ItemOverride(null,"Bravo",false));
        var state=store.ReadOrganizationState();var now=DateTimeOffset.UtcNow;
        var plan=OrganizationPlanner.BuildExact(state,now,now.AddMinutes(30))!;
        Assert.Throws<ArgumentException>(()=>OrganizationPlanSelection.Select(plan,state,[]));
        Assert.Throws<ArgumentException>(()=>OrganizationPlanSelection.Select(plan,state,[Guid.NewGuid()]));
    }
}
