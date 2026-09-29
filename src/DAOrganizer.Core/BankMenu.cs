using System.Buffers.Binary;
using System.Text;
namespace DAOrganizer.Core;
public sealed record BankMenu(uint NpcId,ushort PursuitId,string Speaker,string Content,IReadOnlyList<Item> Items)
{
    // Payload excludes opcode. Called only for a correlated Withdraw Items response.
    public static BankMenu Parse(byte[] data)
    {
        var r=new Reader(data);var type=r.Byte();
        if(type is not (4 or 10))throw new InvalidDataException("Bank did not return an item list.");
        r.Byte();var npc=r.UInt32();r.Byte();r.UInt16();r.Byte();r.Byte();r.UInt16();r.Byte();r.Byte();
        var speaker=r.String8();var content=r.String16();var pursuit=r.UInt16();var count=r.UInt16();
        if(count>512)throw new InvalidDataException("Bank list exceeds supported client capacity; old snapshot retained.");
        var items=new List<Item>();
        for(var i=0;i<count;i++)
        {
            if(pursuit==0x004B)r.UInt32();
            var sprite=(ushort)(r.UInt16()&0x3fff);var color=r.Byte();var value=r.UInt32();
            var quantity=pursuit==0x004B?r.Byte():value;var name=r.String8();
            if(pursuit==0x004B){if(r.Byte()==1)r.String8();r.UInt32();r.UInt32();}else r.String8();
            items.Add(new(i+1,name,quantity,sprite,color));
        }
        return new(npc,pursuit,speaker,content,items);
    }
    private sealed class Reader(byte[] data)
    {
        private int _offset;
        private ReadOnlySpan<byte> Take(int count)
        {
            if(count<0||_offset>data.Length-count)throw new InvalidDataException("Incomplete bank packet; old snapshot retained.");
            var result=data.AsSpan(_offset,count);_offset+=count;return result;
        }
        public byte Byte()=>Take(1)[0];
        public ushort UInt16()=>BinaryPrimitives.ReadUInt16BigEndian(Take(2));
        public uint UInt32()=>BinaryPrimitives.ReadUInt32BigEndian(Take(4));
        public string String8()=>Encoding.ASCII.GetString(Take(Byte()));
        public string String16()=>Encoding.ASCII.GetString(Take(UInt16()));
    }
}
