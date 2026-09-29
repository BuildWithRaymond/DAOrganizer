using System.Text.Json;
namespace DAOrganizer.Core;

public sealed class WorldGraph(IEnumerable<WorldMap> maps)
{
    public IReadOnlyDictionary<int,WorldMap> Maps {get;}=maps.ToDictionary(x=>x.Id);
    public IReadOnlyList<Portal> Route(int start,int goal)
    {
        if(start==goal)return [];
        var queue=new Queue<int>();var seen=new HashSet<int>{start};var previous=new Dictionary<int,Portal>();queue.Enqueue(start);
        while(queue.TryDequeue(out var id))
        {
            if(!Maps.TryGetValue(id,out var map))continue;
            foreach(var edge in map.Portals)
            {
                if(!Maps.ContainsKey(edge.To)||!seen.Add(edge.To))continue;
                previous[edge.To]=edge;
                if(edge.To==goal)
                {
                    var result=new List<Portal>();var current=goal;
                    while(current!=start){var p=previous[current];result.Add(p);current=p.From;}
                    result.Reverse();return result;
                }
                queue.Enqueue(edge.To);
            }
        }
        throw new InvalidOperationException("No known walking/world-map route to this bank.");
    }
    public static WorldGraph Load(string folder)
    {
        var maps=new List<WorldMap>();
        foreach(var file in Directory.EnumerateFiles(folder,"*.json"))
        {
            using var json=JsonDocument.Parse(File.ReadAllText(file));var r=json.RootElement;
            var id=r.GetProperty("Number").GetInt32();var size=r.GetProperty("Size");var w=size.GetProperty("Width").GetInt32();var h=size.GetProperty("Height").GetInt32();
            var portals=new List<Portal>();
            foreach(var p in r.GetProperty("Portals").EnumerateObject())
            {
                var t=ParseTile(p.Name);var v=p.Value;
                // One-tile boundary exits are retained; unrelated out-of-range observations are excluded.
                if(t.X< -1||t.Y< -1||t.X>w||t.Y>h)continue;
                portals.Add(new(id,t.X,t.Y,v.GetProperty("MapId").GetInt32(),v.GetProperty("X").GetInt32(),v.GetProperty("Y").GetInt32()));
            }
            if(r.TryGetProperty("WorldMaps",out var world))foreach(var p in world.EnumerateObject())
            {
                var t=ParseTile(p.Name);
                foreach(var dest in p.Value.EnumerateObject())
                {var d=ParseTile(dest.Value.GetString()!);portals.Add(new(id,t.X,t.Y,int.Parse(dest.Name),d.X,d.Y,true));}
            }
            var name=r.GetProperty("DisplayName").GetString()??r.GetProperty("Name").GetString()??id.ToString();
            maps.Add(new(id,name,w,h,portals));
        }
        return new(maps);
    }
    private static Tile ParseTile(string value){var p=value.Split(',');return new(int.Parse(p[0]),int.Parse(p[1]));}
}
public static class TilePath
{
    public static IReadOnlyList<Tile> Find(int width,int height,Tile start,Tile goal,Func<Tile,bool> passable)
    {
        var queue=new PriorityQueue<Tile,int>();var cost=new Dictionary<Tile,int>{{start,0}};var previous=new Dictionary<Tile,Tile>();queue.Enqueue(start,0);
        Tile[] delta=[new(0,-1),new(1,0),new(0,1),new(-1,0)];
        while(queue.TryDequeue(out var p,out _))
        {
            if(p==goal){var path=new List<Tile>();while(p!=start){path.Add(p);p=previous[p];}path.Reverse();return path;}
            foreach(var d in delta)
            {
                var n=new Tile(p.X+d.X,p.Y+d.Y);
                if(n.X<0||n.Y<0||n.X>=width||n.Y>=height||!passable(n))continue;
                var next=cost[p]+1;if(cost.TryGetValue(n,out var known)&&known<=next)continue;
                cost[n]=next;previous[n]=p;queue.Enqueue(n,next+Math.Abs(n.X-goal.X)+Math.Abs(n.Y-goal.Y));
            }
        }
        throw new InvalidOperationException("No clear tile path. Move past the obstruction and try again.");
    }
}
