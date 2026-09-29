using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Types;
namespace DAOrganizer.Game;

public sealed partial class GameSession
{
    private uint _expectedCloseNpc;
    private int _expectedCloseCount;
    private DateTimeOffset _expectedCloseUntil;
    private string _lastManualItemInput="item input";

    private void ObserveManualItemInput(ClientPacket packet)
    {
        if(packet.Command is not (ClientCommand.Get or ClientCommand.Drop or ClientCommand.Give or ClientCommand.Eat or ClientCommand.Use or ClientCommand.ChangeSlot or ClientCommand.RemoveEquipment or ClientCommand.Exchange or ClientCommand.Merchant or ClientCommand.Pursuit))return;
        lock(_gate)
        {
            // The native client may close the same popup after our close request.
            // Only this exact, harmless close is exempt; selections and quantities still stop work.
            if(packet.Command==ClientCommand.Pursuit&&_expectedCloseCount>0&&DateTimeOffset.UtcNow<=_expectedCloseUntil&&
                (packet.Data.Length==9||packet.Data.Length==10&&packet.Data[9]==0)&&
                ClientMessageFactory.Default.TryCreate(packet,out var message)&&message is ClientPursuitMessage close&&
                close.EntityType==EntityTypeFlags.Creature&&close.EntityId==_expectedCloseNpc&&close.PursuitId==0&&close.StepId==1&&close.ArgsType==DialogArgsType.None)
            {_expectedCloseCount--;return;}
            _lastManualItemInput=$"{packet.Command} (0x{(byte)packet.Command:X2})";
            Interlocked.Increment(ref _manualItemRevision);
        }
    }
}
