using Arbiter.Net.Client;
using Arbiter.Net.Server;
using DAOrganizer.Core;
using DAOrganizer.Game;
using System.Text.Json;
using Xunit;

namespace DAOrganizer.Tests;

public class ManualTradeTraceTests
{
    [Fact]
    public void SnapshotLeavesCaptureOpenForLaterDelivery()
    {
        var trace=new ManualTradeTrace("operation","Alpha",1,[new Item(4,"Chest",2,15)],0);
        trace.Add(new ClientPacket(0x4A,[0,1,2,3,4]));
        var pending=trace.Snapshot([new Item(4,"Chest",1,15)],0);
        Assert.Single(pending.Packets);
        trace.Add(new ServerPacket(0x42,[5,0,0]));
        var finished=trace.Finish([new Item(4,"Chest",1,15)],0);
        Assert.Equal(2,finished.Packets.Count);
        Assert.Single(pending.Packets);
    }

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
        var filtered=Assert.Single(result.FilteredTimeline!);
        Assert.Equal((byte)0x03,filtered.Opcode);
        Assert.Equal("Client",filtered.Direction);
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

    [Fact]
    public void PayloadAndMetadataTimelinesHaveIndependentBounds()
    {
        var trace=new ManualTradeTrace("operation-1","Alpha",123,[],0,maxPackets:1,maxTimelineEntries:2);
        trace.Add(new ServerPacket(0x42,[5]));trace.Add(new ServerPacket(0x42,[5]));
        trace.Add(new ServerPacket(0x4C,[1]));trace.Add(new ServerPacket(0x22,[2]));trace.Add(new ServerPacket(0x33,[3]));
        var result=trace.Finish([],0);
        Assert.Single(result.Packets);Assert.True(result.Truncated);
        Assert.Equal(2,result.FilteredTimeline!.Count);Assert.True(result.TimelineTruncated);
        Assert.All(result.FilteredTimeline,x=>Assert.DoesNotContain("Payload",x.ToString()));
    }

    [Fact]
    public void ExistingCaptureJsonShapesRemainReadable()
    {
        const string oldest="""{"OperationId":"old","Character":"Alpha","ProcessId":1,"StartedAt":"2026-01-01T00:00:00Z","FinishedAt":"2026-01-01T00:00:01Z","BeforeInventory":[],"AfterInventory":[],"BeforeGold":0,"AfterGold":0,"Packets":[],"Truncated":false}""";
        const string packetShape="""{"OperationId":"old","Character":"Alpha","ProcessId":1,"StartedAt":"2026-01-01T00:00:00Z","FinishedAt":"2026-01-01T00:00:01Z","BeforeInventory":[],"AfterInventory":[],"BeforeGold":0,"AfterGold":0,"Packets":[{"ObservedAt":"2026-01-01T00:00:00Z","Direction":"Server","Opcode":66,"PayloadHex":"050000","SessionName":"Alpha"}],"Truncated":false}""";
        foreach(var json in new[]{oldest,packetShape})
        {
            var capture=JsonSerializer.Deserialize<ManualTradeResult>(json);
            Assert.NotNull(capture);Assert.Null(capture.FilteredTimeline);Assert.False(capture.TimelineTruncated);
        }
    }
}
