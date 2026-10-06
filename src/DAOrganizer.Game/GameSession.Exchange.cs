using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Serialization;
using Arbiter.Net.Types;

namespace DAOrganizer.Game;

public sealed partial class GameSession
{
    public async Task<ExchangeLease> AcquireExchangeLease(CancellationToken token)
    {
        await _actionGate.WaitAsync(token);
        try
        {
            lock(_gate)
            {
                if(!Ready||Error!=null||_manualTradeTrace!=null)
                    throw new InvalidOperationException("Session is not ready for an exclusive exchange.");
                return new ExchangeLease(this,MaintenanceGuard());
            }
        }
        catch{_actionGate.Release();throw;}
    }

    public sealed class ExchangeLease:IDisposable
    {
        private readonly GameSession _session;
        private readonly Action _guard;
        private bool _disposed;
        internal ExchangeLease(GameSession session,Action guard){_session=session;_guard=guard;}

        public void Check()
        {
            if(_disposed)throw new ObjectDisposedException(nameof(ExchangeLease));
            lock(_session._gate)_guard();
        }

        public void Send(ExchangeClientActionType action,uint partnerId,byte? slot=null,byte? quantity=null)
        {
            var itemAction=action is ExchangeClientActionType.AddItem or ExchangeClientActionType.AddStackableItem;
            if(action is not (ExchangeClientActionType.BeginExchange or ExchangeClientActionType.AddItem or
                ExchangeClientActionType.AddStackableItem or ExchangeClientActionType.Accept)||partnerId==0||
                itemAction&&(slot is null or <1 or >59)||
                action==ExchangeClientActionType.AddStackableItem&&(quantity is null or 0))
                throw new ArgumentException("Unsupported exchange action, partner, slot or quantity.");
            lock(_session._gate)
            {
                Check();
                var message=new ClientExchangeMessage{Action=action,TargetId=partnerId,Slot=slot,Quantity=quantity};
                var builder=new NetworkPacketBuilder(ClientCommand.Exchange);
                try
                {
                    message.Serialize(ref builder);
                    var packet=builder.ToPacket();
                    _session.Send(message);
                    // Proxy PacketReceived observes native input only. Record organizer sends here so
                    // the same strict two-party analyzer can validate automated and manual exchanges.
                    _session._manualTradeTrace?.Add(new ClientPacket((byte)ClientCommand.Exchange,packet.Data),_session.Name);
                }
                finally{builder.Dispose();}
            }
        }

        public void Dispose()
        {
            if(_disposed)return;
            _disposed=true;
            _session._actionGate.Release();
        }
    }
}
