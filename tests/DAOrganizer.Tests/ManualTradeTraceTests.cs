using Arbiter.Net.Client;
using Arbiter.Net.Server;
using DAOrganizer.Core;
using DAOrganizer.Game;
using Xunit;

namespace DAOrganizer.Tests;

public class ManualTradeTraceTests
{
    [Fact]
    public void CaptureKeepsRelevantFullPayloadAndInventorySnapshots()
    {
        var trace=new ManualTradeTrace("operation-1","Alpha",123,[new Item(1,"Chest",3,15)],10);
        Assert.False(trace.Add(new ClientPacket(0x03,[1,2,3])));
        Assert.True(trace.Add(new ClientPacket(0x4A,[0,0,0,1])));
        Assert.True(trace.Add(new ServerPacket(0x42,[2,4,5])));
        Assert.True(trace.Add(new ServerPacket(0x0F,[1,2,3,4])));
        Assert.True(trace.Add(new ServerPacket(0x10,[1])));
        Assert.True(trace.Add(new ServerPacket(0x37,[1,2])));
        var result=trace.Finish([new Item(1,"Chest",2,15)],10);
        Assert.Equal(5,result.Packets.Count);
        Assert.Equal("00000001",result.Packets[0].PayloadHex);
        Assert.Equal("Client",result.Packets[0].Direction);
        Assert.Equal("Server",result.Packets[1].Direction);
        Assert.Equal(3,Assert.Single(result.BeforeInventory).Quantity);
        Assert.Equal(2,Assert.Single(result.AfterInventory).Quantity);
    }

    [Fact]
    public void CaptureStopsAtBoundInsteadOfRetainingUnlimitedPackets()
    {
        var trace=new ManualTradeTrace("operation-1","Alpha",123,[],0,maxPackets:2);
        for(var i=0;i<5;i++)trace.Add(new ClientPacket(0x4A,[1]));
        var result=trace.Finish([],0);
        Assert.Equal(2,result.Packets.Count);
        Assert.True(result.Truncated);
    }
}
