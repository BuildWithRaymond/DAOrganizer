using DAOrganizer.Core;
using System.Buffers.Binary;
using System.Text;
using Xunit;
namespace DAOrganizer.Tests;
public class BankTests
{
    internal static byte[] Packet(int count,bool extended=false)
    {
        using var s=new MemoryStream();
        void B(int n)=>s.WriteByte((byte)n);
        void U16(int n){Span<byte>b=stackalloc byte[2];BinaryPrimitives.WriteUInt16BigEndian(b,(ushort)n);s.Write(b);}
        void U32(int n){Span<byte>b=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(b,(uint)n);s.Write(b);}
        void Str(string v){var b=Encoding.ASCII.GetBytes(v);B(b.Length);s.Write(b);}
        B(extended?10:4);B(1);U32(42);B(1);U16(0x4001);B(0);B(1);U16(0x4001);B(0);B(0);Str("Banker");U16(0);U16(extended?0x4B:0x42);U16(count);
        for(var i=0;i<count;i++)
        {
            if(extended)U32(i+1);
            U16(0x8001);B(0);U32(12);
            if(extended)B(7);
            Str("Ruby "+i);
            if(extended){B(0);U32(0);U32(0);}else Str("");
        }
        return s.ToArray();
    }
    [Fact] public void ReadsRowsBeyondFirstPage()=>Assert.Equal(300,BankMenu.Parse(Packet(300)).Items.Count);
    [Fact] public void OrdinaryWithdrawalUsesStoredCount()=>Assert.Equal(12,BankMenu.Parse(Packet(1)).Items[0].Quantity);
    [Fact] public void ExtendedListKeepsQuantity()=>Assert.Equal(7,BankMenu.Parse(Packet(1,true)).Items[0].Quantity);
    [Fact] public void TruncatedScanCannotBecomeComplete()=>Assert.Throws<InvalidDataException>(()=>BankMenu.Parse(Packet(2)[..^2]));
    [Fact] public void SwapPlanPreservesDuplicates()
    {
        Item[] items=[new(1,"Z",2),new(2,"A",2),new(3,"A",3)];
        var desired=SlotPlanner.Sort(items,new HashSet<int>());var slots=items.ToDictionary(x=>x.Slot);
        foreach(var (from,to) in SlotPlanner.Swaps(items,desired))
        {slots.Remove(from,out var a);slots.Remove(to,out var b);slots[to]=a!;if(b!=null)slots[from]=b;}
        Assert.Equal("A",slots[49].Name);Assert.Equal(2,slots[49].Quantity);Assert.Equal(3,slots[50].Quantity);Assert.Equal("Z",slots[51].Name);
    }
}
