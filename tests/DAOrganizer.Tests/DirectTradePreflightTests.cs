using DAOrganizer.Core;
using DAOrganizer.Game;
using Xunit;

namespace DAOrganizer.Tests;

public class DirectTradePreflightTests
{
    private static readonly Item Chest=new(4,"Chest",2,15,IsStackable:true);

    private static (DirectTradeEndpoint Sender,DirectTradeEndpoint Recipient,PlannedOrganizationStep Step) Ready()
    {
        var now=DateTimeOffset.UtcNow;
        var sender=new DirectTradeEndpoint("Alpha",1,true,7,101,new Tile(10,10),[Chest],0,
            new VisibleTradeTarget(202,"Bravo",new Tile(11,10),7,now),[],now);
        var recipient=new DirectTradeEndpoint("Bravo",2,true,7,202,new Tile(11,10),[],0,
            new VisibleTradeTarget(101,"Alpha",new Tile(10,10),7,now),[],now);
        var step=new PlannedOrganizationStep(Guid.NewGuid(),0,"Alpha","Bravo",ItemGroups.Key(Chest),Chest,
            "Inventory",4,1,TransferRouteKind.Direct,[],[],OrganizationReadiness.Ready,[],[]);
        return (sender,recipient,step);
    }

    [Fact]
    public void MatchingTwoWayServerIdsAndFreeRecipientSlotPass()
    {
        var (sender,recipient,step)=Ready();
        var result=DirectTradePreflight.Check(sender,recipient,step,Chest,DateTimeOffset.UtcNow);
        Assert.Equal(202u,result.SenderTargetId);
        Assert.Equal(101u,result.RecipientTargetId);
    }

    [Fact]
    public void BankSourceMayGainPreviouslyUnknownDurabilityAndStackability()
    {
        var (sender,recipient,step)=Ready();
        var bankItem=Chest with{Slot=9,Durability=null,MaxDurability=null,IsStackable=null};
        var carried=Chest with{Durability=0,MaxDurability=0};
        sender=sender with{Inventory=[carried]};
        step=step with{SourceLocation="Bank",SourceSlot=9,SourceItem=bankItem};
        Assert.Equal(202u,DirectTradePreflight.Check(sender,recipient,step,carried,DateTimeOffset.UtcNow).SenderTargetId);
        Assert.False(OrganizationPlanContract.MatchesObservedItem(carried with{Sprite=99},bankItem));
    }

    [Fact]
    public void StationaryVisiblePartnersRemainValidAfterThreeMinutes()
    {
        var (sender,recipient,step)=Ready();
        var old=DateTimeOffset.UtcNow.AddMinutes(-3);
        sender=sender with{Partner=sender.Partner with{ObservedAt=old}};
        recipient=recipient with{Partner=recipient.Partner with{ObservedAt=old}};
        Assert.Equal(202u,DirectTradePreflight.Check(sender,recipient,step,Chest,DateTimeOffset.UtcNow).SenderTargetId);
    }

    [Theory]
    [InlineData("wrong id")]
    [InlineData("not adjacent")]
    [InlineData("full recipient")]
    [InlineData("source pinned")]
    public void MismatchOrMissingRoomRejectsBeforeAnySend(string failure)
    {
        var (sender,recipient,step)=Ready();
        switch(failure)
        {
            case "wrong id":sender=sender with{Partner=sender.Partner with{Id=999}};break;
            case "not adjacent":recipient=recipient with{Position=new Tile(20,20)};break;
            case "full recipient":recipient=recipient with{Inventory=Enumerable.Range(1,59)
                .Select(slot=>new Item(slot,"Other",1,16)).ToArray()};break;
            case "source pinned":sender=sender with{PinnedSlots=new HashSet<int>{4}};break;
        }
        Assert.Throws<InvalidOperationException>(()=>DirectTradePreflight.Check(sender,recipient,step,Chest,DateTimeOffset.UtcNow));
    }
}
