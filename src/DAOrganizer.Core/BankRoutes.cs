namespace DAOrganizer.Core;
public sealed record BankRoute(WorldMap Map,IReadOnlyList<Portal> Portals,int EstimatedDistance);
public static class BankRoutes
{
    public static bool IsBank(string name)=>System.Text.RegularExpressions.Regex.IsMatch(name,@"\b(bank|storage)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase)&&
        !System.Text.RegularExpressions.Regex.IsMatch(name,@"\b(inn|weapons?)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    public static BankRoute Closest(WorldGraph world,int start,Tile position)
    {
        var origin=(Map:start,Point:position);
        var costs=new Dictionary<(int Map,Tile Point),int>{{origin,0}};
        var previous=new Dictionary<(int Map,Tile Point),((int Map,Tile Point) Parent,Portal Portal)>();
        var queue=new PriorityQueue<(int Map,Tile Point),int>();queue.Enqueue(origin,0);
        while(queue.TryDequeue(out var node,out var distance))
        {
            if(costs[node]!=distance||!world.Maps.TryGetValue(node.Map,out var map))continue;
            if(IsBank(map.Name))
            {
                var portals=new List<Portal>();var cursor=node;
                while(previous.TryGetValue(cursor,out var step)){portals.Add(step.Portal);cursor=step.Parent;}
                portals.Reverse();return new(map,portals,distance);
            }
            foreach(var portal in map.Portals)
            {
                if(!world.Maps.ContainsKey(portal.To))continue;
                var next=(Map:portal.To,Point:new Tile(portal.ToX,portal.ToY));
                var cost=distance+Math.Abs(node.Point.X-portal.X)+Math.Abs(node.Point.Y-portal.Y)+8;
                if(costs.TryGetValue(next,out var old)&&old<=cost)continue;
                costs[next]=cost;previous[next]=(node,portal);queue.Enqueue(next,cost);
            }
        }
        throw new InvalidOperationException("No reachable bank or storage in WorldLogs. Move to a known town and retry.");
    }
}
