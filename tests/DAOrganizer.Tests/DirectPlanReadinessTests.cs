using System.Collections.Immutable;
using System.Text.Json;
using DAOrganizer.App;
using DAOrganizer.Core;
using DAOrganizer.Game;
using Xunit;

namespace DAOrganizer.Tests;

public class DirectPlanReadinessTests
{
    private static readonly Item Chest=new(4,"Chest",2,15,IsStackable:true);
    private static readonly string Key=ItemGroups.Key(Chest);

    private static (ExactOrganizationPlan Plan,OrganizationState State,DirectTradeEndpoint Sender,
        DirectTradeEndpoint Recipient) Fixture()
    {
        var now=DateTimeOffset.UtcNow;
        var shape=OrganizationPlanContract.ItemFingerprint(Chest);
        var step=new PlannedOrganizationStep(Guid.NewGuid(),0,"Alpha","Bravo",Key,Chest,"Inventory",4,1,
            TransferRouteKind.Direct,
            [new(OrganizationLegKind.Exchange,"Alpha","Bravo","Inventory","Inventory"),
             new(OrganizationLegKind.Deposit,"Bravo","Bravo","Inventory","Bank")],
            [],OrganizationReadiness.NeedsScan,
            [new("Alpha","Inventory",Key,4,2,shape),new("Bravo","Bank",Key,null,2,shape)],
            [new("Alpha","Inventory",Key,4,1,shape),new("Bravo","Bank",Key,null,3,shape)]);
        var plan=new ExactOrganizationPlan(Guid.NewGuid(),now,now.AddMinutes(30),new string('A',64),[step]);
        var state=new OrganizationState(
            [new("Alpha","Current","Current",now),new("Bravo","Current","Current",now)],
            [new("Alpha","Inventory",Chest,now),new("Bravo","Bank",Chest with{Slot=1},now)],
            [],new Dictionary<string,long?>(),[],[],
            new Dictionary<string,ItemMetadata>{{Key,new(null,true,5,Tradeability.Tradeable,"local")}},
            new Dictionary<string,ItemOverride>(),new Dictionary<string,TradeEvidence>(),
            new Dictionary<string,string>(),new Dictionary<string,CoexistencePolicy>(),[],plan.InputFingerprint);
        var sender=new DirectTradeEndpoint("Alpha",1,true,7,101,new Tile(10,10),[Chest],0,
            new VisibleTradeTarget(202,"Bravo",new Tile(11,10),7,now),[],now);
        var recipient=new DirectTradeEndpoint("Bravo",2,true,7,202,new Tile(11,10),[],0,
            new VisibleTradeTarget(101,"Alpha",new Tile(10,10),7,now),[],now);
        return (plan,state,sender,recipient);
    }

    [Fact]
    public void ExistingBankStackWithKnownHeadroomCanBecomeReady()
    {
        var (plan,state,sender,recipient)=Fixture();
        var ready=DirectPlanReadiness.Promote(plan,state,sender,recipient,DateTimeOffset.UtcNow);
        Assert.Equal(OrganizationReadiness.Ready,ready.Steps[0].Readiness);
    }

    [Fact]
    public void MissingBankHeadroomOrRecipientSlotStaysBlocked()
    {
        var (plan,state,sender,recipient)=Fixture();
        var capped=state with{Metadata=new Dictionary<string,ItemMetadata>{{Key,new(null,true,2,Tradeability.Tradeable,"local")}}};
        Assert.Throws<InvalidOperationException>(()=>DirectPlanReadiness.Promote(plan,capped,sender,recipient,DateTimeOffset.UtcNow));
        recipient=recipient with{Inventory=Enumerable.Range(1,59).Select(x=>new Item(x,"Other",1,16)).ToArray()};
        Assert.Throws<InvalidOperationException>(()=>DirectPlanReadiness.Promote(plan,state,sender,recipient,DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SingleBankUnitWithUniqueNameAndInventoryRoomCanBecomeReady()
    {
        var (plan,state,sender,recipient)=Fixture();
        var step=plan.Steps[0];
        step=step with
        {
            SourceLocation="Bank",
            RouteLegs=[new(OrganizationLegKind.Withdraw,"Alpha","Alpha","Bank","Inventory"),
                ..step.RouteLegs],
            ExpectedBefore=[step.ExpectedBefore[0] with{Location="Bank"},step.ExpectedBefore[1]],
            ExpectedAfter=[step.ExpectedAfter[0] with{Location="Bank"},step.ExpectedAfter[1]]
        };
        plan=plan with{Steps=[step]};
        state=state with{Items=[new("Alpha","Bank",Chest,DateTimeOffset.UtcNow),
            new("Bravo","Bank",Chest with{Slot=1},DateTimeOffset.UtcNow)]};
        sender=sender with{Inventory=[]};
        var ready=DirectPlanReadiness.Promote(plan,state,sender,recipient,DateTimeOffset.UtcNow);
        Assert.Equal(OrganizationReadiness.Ready,ready.Steps[0].Readiness);
    }

    [Fact]
    public void OneUnitControlledTrialCanProceedWithoutCommunityKnowledge()
    {
        var (plan,state,sender,recipient)=Fixture();
        state=state with{Metadata=new Dictionary<string,ItemMetadata>(),TradeEvidence=new Dictionary<string,TradeEvidence>()};
        var trial=DirectPlanReadiness.Promote(plan,state,sender,recipient,DateTimeOffset.UtcNow,controlledTrial:true);
        Assert.True(trial.Steps[0].ControlledTrial);
        Assert.Throws<InvalidOperationException>(()=>DirectPlanReadiness.Promote(plan,state,sender,recipient,DateTimeOffset.UtcNow));
        var larger=plan with{Steps=[plan.Steps[0] with{Quantity=2}]};
        Assert.Throws<InvalidOperationException>(()=>DirectPlanReadiness.Promote(larger,state,sender,recipient,
            DateTimeOffset.UtcNow,controlledTrial:true));
    }

    [Fact]
    public void OldSavedStepWithoutTrialFlagLoadsAsStrict()
    {
        var (plan,_,_,_)=Fixture();
        var oldJson=JsonSerializer.Serialize(plan.Steps[0]).Replace(",\"ControlledTrial\":false","",
            StringComparison.Ordinal);
        var loaded=JsonSerializer.Deserialize<PlannedOrganizationStep>(oldJson);
        Assert.NotNull(loaded);
        Assert.False(loaded.ControlledTrial);
    }
}
