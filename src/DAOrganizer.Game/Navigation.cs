using System.Buffers.Binary;
using Arbiter.IO.Archives;
using Arbiter.Net.Client.Messages;
using Arbiter.Net.Types;
using DAOrganizer.Core;
namespace DAOrganizer.Game;

public sealed record BankDestination(int MapId,string Name,string? NpcName=null,int? X=null,int? Y=null)
{
    public override string ToString()=>$"{Name} · {MapId}";
}
public sealed class Navigation(WorldGraph world,string gameDirectory)
{
    private byte[]? _flags;
    public async Task Travel(GameSession session,BankDestination bank,CancellationToken token,IReadOnlyList<Portal>? plannedRoute=null)
    {
        if(!session.Online)throw new InvalidOperationException("Log in before travelling.");
        if(!session.Ready)throw new InvalidOperationException("Wait for login and home inn travel to finish before travelling.");
        var edges=plannedRoute??world.Route(session.MapId,bank.MapId);
        foreach(var edge in edges)
        {
            CheckControl(session,token);
            if(session.MapId!=edge.From)throw new InvalidOperationException("Unexpected map transition. Scan stopped.");
            session.SetStatus($"Walking to {world.Maps[edge.To].Name}");
            await Walk(session,new(edge.X,edge.Y),true,token);
            if(edge.WorldMap)
            {
                await GameSession.WaitUntil(()=>session.FieldMap!=null,TimeSpan.FromSeconds(5),token);
                var destination=session.FieldMap!.Locations.FirstOrDefault(x=>x.MapId==edge.To)
                    ??throw new InvalidOperationException("World-map destination is unavailable for this character.");
                session.Send(new ClientFieldMapMessage{MapId=destination.MapId,X=destination.MapX,Y=destination.MapY,Checksum=destination.Checksum});
            }
            await GameSession.WaitUntil(()=>session.MapId==edge.To,TimeSpan.FromSeconds(8),token);
            await Task.Delay(700,token);
        }
        if(bank.X.HasValue&&bank.Y.HasValue)await Walk(session,new(bank.X.Value,bank.Y.Value),false,token);
        var npc=session.Mundanes().Where(x=>bank.NpcName==null||string.Equals(x.Name,bank.NpcName,StringComparison.OrdinalIgnoreCase))
            .OrderBy(x=>Math.Abs(x.X-session.Position.X)+Math.Abs(x.Y-session.Position.Y)).FirstOrDefault();
        if(npc!=null)
        {
            var nearby=new[]{new Tile(npc.X,npc.Y+1),new Tile(npc.X-1,npc.Y),new Tile(npc.X+1,npc.Y),new Tile(npc.X,npc.Y-1)};
            foreach(var p in nearby.OrderBy(x=>Math.Abs(x.X-session.Position.X)+Math.Abs(x.Y-session.Position.Y)))
            {
                if(p.X<0||p.Y<0||p.X>=session.Width||p.Y>=session.Height)continue;
                try{await Walk(session,p,false,token);break;}
                catch(InvalidOperationException ex)when(ex.Message.StartsWith("No clear tile path")){}
            }
        }
        CheckControl(session,token);await session.ScanNearbyBank(bank.NpcName,token);
    }
    private async Task Walk(GameSession session,Tile goal,bool allowExit,CancellationToken token)
    {
        var mapId=session.MapId;var w=session.Width;var h=session.Height;
        var cells=File.ReadAllBytes(Path.Combine(gameDirectory,"maps",$"lod{mapId}.map"));
        if(cells.Length<w*h*6)throw new InvalidDataException("Map file is incomplete. Travel stopped.");
        _flags??=ReadFlags();
        var blocked=new HashSet<Tile>();var retries=0;
        var insideGoal=new Tile(Math.Clamp(goal.X,0,w-1),Math.Clamp(goal.Y,0,h-1));
        bool StaticOpen(Tile p)
        {
            var offset=(p.Y*w+p.X)*6;
            for(var i=2;i<=4;i+=2)
            {
                var tile=BinaryPrimitives.ReadUInt16LittleEndian(cells.AsSpan(offset+i,2));
                if(tile==0||tile==0x2710)continue;
                if(tile>_flags.Length||(_flags[tile-1]&15)!=0)return false;
            }
            return true;
        }
        while(session.MapId==mapId&&session.Position!=insideGoal)
        {
            CheckControl(session,token);
            var occupied=session.Occupied();
            var exits=world.Maps.TryGetValue(mapId,out var map)?map.Portals.Select(x=>new Tile(x.X,x.Y)).ToHashSet():[];
            var route=TilePath.Find(w,h,session.Position,insideGoal,p=>StaticOpen(p)&&!blocked.Contains(p)&&!occupied.Contains(p)&&(!exits.Contains(p)||(allowExit&&p==insideGoal)));
            foreach(var next in route.Take(8))
            {
                CheckControl(session,token);if(session.MapId!=mapId)return;
                var before=session.Position;var dx=next.X-before.X;var dy=next.Y-before.Y;
                if(Math.Abs(dx)+Math.Abs(dy)!=1)throw new InvalidOperationException("Position changed outside route. Travel stopped.");
                var manualRevision=session.ManualMovementRevision;
                var revision=session.SendWalk(dx<0?WorldDirection.Left:dx>0?WorldDirection.Right:dy<0?WorldDirection.Up:WorldDirection.Down);
                await GameSession.WaitUntil(()=>
                {
                    CheckControl(session,token);
                    if(session.ManualMovementRevision!=manualRevision)throw new OperationCanceledException("Manual movement detected. Travel stopped.");
                    return session.PositionRevision>revision||session.MapId!=mapId||session.FieldMap!=null;
                },TimeSpan.FromSeconds(2),token);
                if(session.MapId!=mapId||session.FieldMap!=null)return;
                if(session.Position==before)
                {
                    if(++retries>3)throw new InvalidOperationException("Server rejected repeated walk steps. Travel stopped.");
                    blocked.Add(next);break;
                }
                if(session.Position!=next)throw new InvalidOperationException("Server corrected position. Travel stopped.");
                await Task.Delay(300,token);
            }
        }
        if(session.MapId==mapId&&goal!=insideGoal)
        {
            CheckControl(session,token);
            session.SendWalk(goal.X<0?WorldDirection.Left:goal.X>=w?WorldDirection.Right:goal.Y<0?WorldDirection.Up:WorldDirection.Down);
        }
    }
    private byte[] ReadFlags()
    {
        foreach(var file in Directory.EnumerateFiles(gameDirectory,"*.dat"))
        {
            DatArchive archive;try{archive=DatArchive.Open(file);}catch(InvalidDataException){continue;}
            var entry=archive.Entries.FirstOrDefault(x=>x.Name.Equals("sotp.dat",StringComparison.OrdinalIgnoreCase));
            if(entry==null)continue;using var input=archive.OpenRead(entry);using var output=new MemoryStream();input.CopyTo(output);return output.ToArray();
        }
        throw new FileNotFoundException("SOTP collision data not found in game archives.");
    }
    private static void CheckControl(GameSession session,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!session.Online||session.Health==0)throw new InvalidOperationException("Character unavailable. Travel stopped.");
        // Walking uses the session's packets; it does not need keyboard focus.
        // Only treat global key states as game input while this game has focus.
        if(!NativeInput.HasFocus(session.ProcessId))return;
        if((NativeInput.GetAsyncKeyState(0x1B)&0x8000)!=0)throw new OperationCanceledException("Travel stopped with Escape.");
        if(new[]{0x25,0x26,0x27,0x28}.Any(k=>(NativeInput.GetAsyncKeyState(k)&0x8000)!=0))throw new OperationCanceledException("Manual movement detected. Travel stopped.");
    }
}
