using Arbiter.Net.Annotations;
using Arbiter.Net.Serialization;
using Arbiter.Net.Types;

namespace Arbiter.Net.Client.Messages;

[NetworkCommand(ClientCommand.Merchant)]
public class ClientMerchantMessage : ClientMessage
{
    public EntityTypeFlags EntityType { get; set; }
    public uint EntityId { get; set; }
    public ushort PursuitId { get; set; }
    public byte? Slot { get; set; }
    // Native deposit quantity (pursuit 0x54): argument count 1, slot, string8 quantity.
    public byte? QuantitySlot { get; set; }
    public List<string> Arguments { get; set; } = [];
    
    public override void Deserialize(NetworkPacketReader reader)
    {
        base.Deserialize(reader);

        EntityType = (EntityTypeFlags)reader.ReadByte();
        EntityId = reader.ReadUInt32();
        PursuitId = reader.ReadUInt16();

        if (PursuitId == 0x54 && reader.Remaining > 1)
        {
            if (reader.ReadByte() != 1) throw new InvalidDataException("Invalid deposit argument count.");
            QuantitySlot = reader.ReadByte();
            Arguments = reader.ReadStringArgs8().ToList();
        }
        else if (reader.Remaining == 1)
        {
            Slot = reader.ReadByte();
        }
        else
        {
            Arguments = reader.ReadStringArgs8().ToList();
        }
    }

    public override void Serialize(ref NetworkPacketBuilder builder)
    {
        base.Serialize(ref builder);
        
        builder.AppendByte((byte)EntityType);
        builder.AppendUInt32(EntityId);
        builder.AppendUInt16(PursuitId);
        
        if (Slot.HasValue)
        {
            builder.AppendByte(Slot.Value);
        }
        else
        {
            if (QuantitySlot.HasValue)
            {
                builder.AppendByte(1);
                builder.AppendByte(QuantitySlot.Value);
            }
            foreach (var arg in Arguments)
            {
                builder.AppendString8(arg);
            }
        }
    }
}
