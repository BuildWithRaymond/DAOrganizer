using System.Net;
using System.Threading.Channels;
using Arbiter.Net;
using Arbiter.Net.Client;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Proxy;
using Arbiter.Net.Server;
using Arbiter.Net.Server.Messages;
using Arbiter.Net.Server.Types;
using Arbiter.Net.Types;
using DAOrganizer.Core;
namespace DAOrganizer.Game;

public sealed partial class GameSession:IDisposable
{
    private readonly InventoryStore _store;
    private readonly ProxyServer _proxy=new();
    private readonly Lock _gate=new();
    private readonly Channel<Action> _events=Channel.CreateUnbounded<Action>(new(){SingleReader=true});
    private readonly Dictionary<int,Item> _inventory=[],_equipment=[];
    private readonly Dictionary<uint,ServerCreatureEntity> _creatures=[];
    private ProxyConnection? _connection;
    private bool _ready,_dirty,_disposed;
    private DateTimeOffset _lastInventoryChange;
    private uint? _pendingBank;
    private DateTimeOffset _bankRequested;
    private long _bankDialogRevision;
    private long _manualItemRevision;
    private ManualTradeTrace? _manualTradeTrace;
    public BankMenu? LastBankMenu {get;private set;}
    public uint? BankNpcId {get;private set;}
    private readonly Timer _flush;
    private readonly Task _worker;
    private LoginBaseline _baseline=new();
    public int ProcessId {get;private set;}
    public string Name {get;private set;}="";
    public string Status {get;private set;}="Waiting for login";
    public string? Error {get;private set;}
    public bool Online {get;private set;}
    public bool Ready {get{lock(_gate)return _ready&&Online;}}
    public int MapId {get;private set;}
    public int Width {get;private set;}
    public int Height {get;private set;}
    public string MapName {get;private set;}="";
    public Tile Position {get;private set;}
    public uint Gold {get;private set;}
    public uint Health {get;private set;}
    public ServerScreenMenuMessage? Dialog {get;private set;}
    public long DialogRevision {get;private set;}
    public ServerFieldMapMessage? FieldMap {get;private set;}
    public DateTimeOffset? LastBankScan {get;private set;}
    public event Action? Changed;
    public event Action? Damaged;

    public GameSession(InventoryStore store)
    {
        _store=store;
        _proxy.AddFilter<ClientMoveMessage>(RewriteManualWalk,"WalkCounter",100);
        _proxy.PacketReceived+=(_,e)=>
        {
            lock(_gate)_manualTradeTrace?.Add(e.Decrypted,e.Connection.Name);
            if(e.Decrypted is ClientPacket manual)ObserveManualItemInput(manual);
            // Never retain credentials or chat in the work queue.
            if(e.Decrypted is ClientPacket client && client.Command is not (ClientCommand.Merchant or ClientCommand.RequestObjectInfo))return;
            _events.Writer.TryWrite(()=>
            {
                try{Observe(e.Connection,e.Decrypted);}
                catch(Exception ex){throw new InvalidDataException($"Tracking packet 0x{e.Decrypted.Command:X2}: {ex.Message}",ex);}
            });
        };
        _proxy.ClientDisconnected+=(_,e)=>_events.Writer.TryWrite(()=>
        {
            lock(_gate)
            {
                if(_connection!=e.Connection||e.Connection.IsTransferring)return;
                Flush();Online=false;_ready=false;Status="Offline";if(Name.Length>0)_store.MarkStale(Name);
            }
            Changed?.Invoke();
        });
        _flush=new Timer(_=>_events.Writer.TryWrite(Flush),null,500,500);
        _worker=Task.Run(async()=>
        {
            await foreach(var action in _events.Reader.ReadAllAsync())
            {
                try{action();}
                catch(Exception ex){Error=ex.Message;Status="Tracking needs attention";Changed?.Invoke();}
            }
        });
    }
    public async Task LaunchAsync(string executable)
    {
        ClientLauncher.Validate(executable);
        var ip=(await Dns.GetHostAddressesAsync("da0.kru.com")).First(x=>x.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork);
        _proxy.Start(0,ip,2610);
        try{ProcessId=ClientLauncher.Launch(executable,_proxy.LocalEndpoint!.Port);using var owned=System.Diagnostics.Process.GetProcessById(ProcessId);_ownedStarted=owned.StartTime;}
        catch{_proxy.Dispose();throw;}
    }
    public Item[] Inventory(){lock(_gate)return _inventory.Values.OrderBy(x=>x.Slot).ToArray();}
    public bool CapturingManualTrade{get{lock(_gate)return _manualTradeTrace!=null;}}
    public void StartManualTradeCapture(string operationId)
    {
        lock(_gate)
        {
            if(!Ready||Name.Length==0)throw new InvalidOperationException("Wait for a ready, named character before capture.");
            if(_manualTradeTrace!=null)throw new InvalidOperationException("A manual trade capture is already running.");
            _manualTradeTrace=new(operationId,Name,ProcessId,_inventory.Values,Gold);
        }
    }
    public ManualTradeResult StopManualTradeCapture()
    {
        lock(_gate)
        {
            var trace=_manualTradeTrace??throw new InvalidOperationException("No manual trade capture is running.");
            _manualTradeTrace=null;
            return trace.Finish(_inventory.Values,Gold);
        }
    }
    public Item[] Equipment(){lock(_gate)return _equipment.Values.OrderBy(x=>x.Slot).ToArray();}
    public ServerCreatureEntity[] Mundanes(){lock(_gate)return _creatures.Values.Where(x=>x.CreatureType==CreatureType.Mundane).ToArray();}
    public HashSet<Tile> Occupied(){lock(_gate)return _creatures.Values.Select(x=>new Tile(x.X,x.Y)).ToHashSet();}
    private void Observe(ProxyConnection connection,NetworkPacket packet)
    {
        lock(_gate)
        {
            if(packet is ClientPacket client)
            {
                if(ClientMessageFactory.Default.TryCreate(client,out var outgoing)&&outgoing is ClientMerchantMessage merchant)
                    TrackMerchant(merchant);
                return;
            }
            if(packet is not ServerPacket server)return;
            if(!string.IsNullOrWhiteSpace(connection.Name))
            {
                if(!string.Equals(Name,connection.Name,StringComparison.OrdinalIgnoreCase))
                {Name=connection.Name;ResetBaseline();_store.EnsureCharacter(Name);}
                else if(_connection!=connection&&!Online)ResetBaseline();
                _connection=connection;
            }
            // Sound/music has no inventory or navigation state. Some 0x19 variants
            // are shorter than Arbiter's assumed FF + track + ushort format.
            // Leave the original packet flowing to the client without decoding it here.
            if(server.Command==ServerCommand.SoundEffect)return;
            if(server.Command==ServerCommand.Quit)
            {
                // Native SQuit consumes one approval byte; the observed two padding bytes are optional.
                if(server.Data.Length==0)throw new InvalidDataException("Incomplete logout approval.");
                _quitApproval=server.Data[0];Interlocked.Increment(ref _quitRevision);return;
            }
            if(server.Command==ServerCommand.ScreenMenu)
            {
                DialogRevision++;
                if(server.Data.Length>0&&server.Data[0] is 4 or 10)
                {
                    Dialog=null;
                    if(_pendingBank.HasValue&&DateTimeOffset.UtcNow-_bankRequested<TimeSpan.FromSeconds(15)&&Name.Length>0)
                    {
                        try
                        {
                            var bank=BankMenu.Parse(server.Data);
                            if(bank.NpcId!=_pendingBank)throw new InvalidDataException("Bank response belongs to another NPC.");
                            LastBankMenu=bank;_store.SaveSnapshot(Name,"Bank",bank.Items,true);LastBankScan=DateTimeOffset.UtcNow;
                            Status=$"Bank saved: {bank.Items.Count} entries";
                        }
                        catch{_store.SaveSnapshot(Name,"Bank",[],false);throw;}
                        finally{_pendingBank=null;}
                    }
                    Changed?.Invoke();return;
                }
            }
            IServerMessage? message;
            try
            {
                message=ServerMessageFactory.Default.Create(server);if(message==null)return;
            }
            catch
            {
                if(server.Command is ServerCommand.AddInventory or ServerCommand.RemoveInventory or ServerCommand.AddEquip or ServerCommand.RemoveEquip)
                {
                    _baseline.Failed=true;_ready=false;
                    if(Name.Length>0){_store.SaveSnapshot(Name,"Inventory",[],false);_store.SaveSnapshot(Name,"Equipment",[],false);}
                }
                throw;
            }
            switch(message)
            {
                case ServerLoginCheckMessage login:
                    Error=login.Result==LoginResult.Success?null:$"Login rejected ({login.Result}).";
                    if(Error!=null)Status=Error;
                    break;
                case ServerUserAppearanceMessage appearance:
                    _playerId=appearance.UserId;
                    if(!Online){Online=true;Status="Reading inventory";}
                    _baseline.AppearanceSeen=true;
                    break;
                case ServerDrawHumanObjectsMessage human:
                    if(human.EntityId==_playerId&&_playerId!=0&&Name.Length>0&&human.MonsterSprite==null)
                    {
                        var look=new CharacterAppearance(human.HeadSprite,human.FaceShape,(byte)(human.BodySprite??BodySprite.None),
                            (byte)(human.HairColor??DyeColor.Default),(byte)(human.SkinColor??SkinColor.Default),
                            human.ArmsSprite??0,human.ArmorSprite??0,human.OvercoatSprite??0,
                            human.Accessory1Sprite??0,(byte)(human.Accessory1Color??DyeColor.Default),
                            human.Accessory2Sprite??0,(byte)(human.Accessory2Color??DyeColor.Default),
                            human.Accessory3Sprite??0,(byte)(human.Accessory3Color??DyeColor.Default));
                        _store.Put("appearance/"+Name.ToLowerInvariant(),look);
                    }
                    else if(human.EntityId!=0&&human.EntityId!=_playerId&&human.MonsterSprite==null&&
                        !human.IsHidden&&!string.IsNullOrWhiteSpace(human.Name))
                        _visibleTradeTargets[human.EntityId]=new(human.EntityId,human.Name,
                            new(human.X,human.Y),MapId,DateTimeOffset.UtcNow);
                    else _visibleTradeTargets.Remove(human.EntityId);
                    break;
                case ServerUserReadyMessage:
                    _baseline.ControlSeen=true;_baseline.Changed(DateTimeOffset.UtcNow);_dirty=true;
                    break;
                case ServerAddInventoryMessage item:
                    if(item.Slot is >=1 and <=59){_inventory[item.Slot]=new(item.Slot,item.Name,item.Quantity,item.Sprite,(byte)item.Color,item.Durability,item.MaxDurability,IsStackable:item.IsStackable);InventoryChanged();}
                    break;
                case ServerRemoveInventoryMessage item:_inventory.Remove(item.Slot);InventoryChanged();break;
                case ServerAddEquipMessage item:_equipment[(int)item.Slot]=new((int)item.Slot,item.Name,1,item.Sprite,(byte)item.Color,item.Durability,item.MaxDurability);InventoryChanged();break;
                case ServerRemoveEquipMessage item:_equipment.Remove((int)item.Slot);InventoryChanged();break;
                case ServerStatusMessage stats:
                    _baseline.StatusSeen=true;
                    if(stats.Gold is uint gold){Gold=gold;_dirty=true;}
                    if(stats.Health is uint hp){if(Health>hp)Damaged?.Invoke();Health=hp;}
                    break;
                case ServerMapSizeMessage map:
                    _baseline.MapSeen=true;
                    _baseline.Changed(DateTimeOffset.UtcNow);_pendingWalk=null;
                    if(_homeInnPending)_homeInnMapSeen=true;
                    MapId=map.MapId;Width=map.Width;Height=map.Height;MapName=map.Name;_creatures.Clear();_visibleTradeTargets.Clear();Dialog=null;FieldMap=null;_pendingBank=null;BankNpcId=null;LastBankMenu=null;break;
                case ServerUserPositionMessage position:
                    Position=new(position.X,position.Y);PositionRevision++;_pendingWalk=null;ObserveHomeInnArrival();break;
                case ServerMoveMessage movement:
                    ObserveWalk(movement);break;
                case ServerDrawObjectsMessage objects:
                    foreach(var creature in objects.Entities.OfType<ServerCreatureEntity>())_creatures[creature.Id]=creature;break;
                case ServerRemoveObjectsMessage remove:_creatures.Remove(remove.EntityId);_visibleTradeTargets.Remove(remove.EntityId);break;
                case ServerMoveObjectMessage move:
                    if(_creatures.TryGetValue(move.EntityId,out var moving))
                    {
                        moving.X=(ushort)(move.OriginX+(move.Direction==WorldDirection.Right?1:move.Direction==WorldDirection.Left?-1:0));
                        moving.Y=(ushort)(move.OriginY+(move.Direction==WorldDirection.Down?1:move.Direction==WorldDirection.Up?-1:0));
                    }
                    if(_visibleTradeTargets.TryGetValue(move.EntityId,out var target))
                        _visibleTradeTargets[move.EntityId]=target with
                        {
                            Position=new Tile(move.OriginX+(move.Direction==WorldDirection.Right?1:move.Direction==WorldDirection.Left?-1:0),
                                move.OriginY+(move.Direction==WorldDirection.Down?1:move.Direction==WorldDirection.Up?-1:0)),
                            ObservedAt=DateTimeOffset.UtcNow
                        };
                    break;
                case ServerScreenMenuMessage menu:Dialog=menu;ObserveHomeInn(menu);break;
                case ServerPursuitMessage pursuit:ObserveHomeInn(pursuit);break;
                case ServerFieldMapMessage field:FieldMap=field;break;
            }
            AcceptHomeInn();
        }
        Changed?.Invoke();
    }
    private void InventoryChanged()
    {
        _dirty=true;_lastInventoryChange=DateTimeOffset.UtcNow;
        _baseline.Changed(_lastInventoryChange);
        if(Name.Length>0)_store.MarkStale(Name,"Bank");
    }
    private void TrackMerchant(ClientMerchantMessage request)
    {
        var choice=Dialog?.MenuChoices.FirstOrDefault(x=>x.PursuitId==request.PursuitId);
        if(Dialog?.EntityId!=request.EntityId||choice==null)return;
        if(IsWithdrawal(choice.Text)){BankNpcId=request.EntityId;_pendingBank=request.EntityId;_bankRequested=DateTimeOffset.UtcNow;_bankDialogRevision=DialogRevision;LastBankMenu=null;}
        else if(Name.Length>0)_store.MarkStale(Name,"Bank");
    }
    public static bool IsWithdrawal(string text)=>text.Contains("withdraw",StringComparison.OrdinalIgnoreCase)&&!text.Contains("gold",StringComparison.OrdinalIgnoreCase)&&!text.Contains("coin",StringComparison.OrdinalIgnoreCase);
    private void Flush()
    {
        lock(_gate)
        {
            CompleteSilentBank();
            if(_homeInnPending&&DateTimeOffset.UtcNow>=_homeInnDeadline)
            {
                _baseline.Failed=true;Error="Home inn travel was not confirmed. Handle the login prompt in the game.";Status=Error;
            }
            if(!_ready&&Online&&!_homeInnPending&&_baseline.CanCommit(DateTimeOffset.UtcNow)){_ready=true;_dirty=true;Status="Connected";}
            if(!_ready||!_dirty||Name.Length==0||DateTimeOffset.UtcNow-_lastInventoryChange<TimeSpan.FromMilliseconds(200))return;
            _store.SaveSnapshot(Name,"Inventory",_inventory.Values,true);
            _store.SaveSnapshot(Name,"Equipment",_equipment.Values,true);
            _store.Put("gold/"+Name.ToLowerInvariant(),Gold);_dirty=false;
        }
        Changed?.Invoke();
    }
    public void Send(IClientMessage message)
    {
        lock(_gate)
        {
            if(_connection is not {IsConnected:true}||!Online)throw new InvalidOperationException("Character is not connected.");
            if(message is ClientMerchantMessage merchant)TrackMerchant(merchant);
            if(!_connection.EnqueueMessage(message))throw new InvalidOperationException("Game connection closed.");
        }
    }
    public void SetStatus(string status){Status=status;Changed?.Invoke();}
    private void ResetBaseline()
    {
        _inventory.Clear();_equipment.Clear();_visibleTradeTargets.Clear();_ready=false;_dirty=false;_baseline=new();
        Gold=0;Health=0;Online=false;Dialog=null;FieldMap=null;_pendingBank=null;LastBankScan=null;LastBankMenu=null;BankNpcId=null;
        _safeQuitApproved=false;_safeQuitRequested=false;
        _walkCounter=0;_playerId=0;_pendingWalk=null;PositionRevision=0;ManualMovementRevision=0;
        _homeInnAccepted=false;_homeInnPending=false;_homeInnMapSeen=false;_homeInnReply=null;
        _expectedCloseCount=0;
        _loginPromptExpires=DateTimeOffset.UtcNow.AddSeconds(35);
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_flush.Dispose();_proxy.Dispose();_events.Writer.TryComplete();
        _worker.GetAwaiter().GetResult();Flush();
    }
}
