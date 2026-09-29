namespace DAOrganizer.Core;

public static class SlotPlanner
{
    public static IEnumerable<int> BottomFirstSlots()=>Enumerable.Range(1,59).OrderByDescending(x=>(x-1)/12).ThenBy(x=>x);
    public static Dictionary<int,Item> Sort(IEnumerable<Item> items,ISet<int> pinned,bool byCategory=false)
    {
        var source = items.ToDictionary(x=>x.Slot);
        if(source.Keys.Any(x=>x is <1 or >59)) throw new ArgumentException("Only item slots 1–59 can move.");
        var result=PotionSlots.Select(source.Values);
        var supplies=result.Values.Select(x=>x.Slot).ToHashSet();
        var protectedSlots=pinned.Except(supplies).Except(result.Keys).ToHashSet();
        foreach(var entry in source.Where(x=>protectedSlots.Contains(x.Key)))result[entry.Key]=entry.Value;
        var ordered = source.Values.Where(x=>!supplies.Contains(x.Slot)&&!protectedSlots.Contains(x.Slot))
            .OrderBy(x=>byCategory?ItemCategories.Order(x.Category):0)
            .ThenBy(x=>byCategory?x.Category:"",StringComparer.OrdinalIgnoreCase)
            .ThenBy(x=>byCategory?ItemCategories.Family(x):"",StringComparer.OrdinalIgnoreCase)
            .ThenBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ThenBy(x=>x.Slot).ToArray();
        var slots = BottomFirstSlots().Where(x=>!protectedSlots.Contains(x)&&!result.ContainsKey(x)).ToArray();
        for(var i=0;i<ordered.Length;i++) result[slots[i]]=ordered[i];
        return result;
    }
    // Values retain their original slot, so identical items remain distinguishable during a plan.
    public static IReadOnlyList<(int From,int To)> Swaps(IReadOnlyList<Item> current,Dictionary<int,Item> desired)
    {
        var originals=current.ToDictionary(x=>x.Slot);
        if(desired.Count!=current.Count||desired.Keys.Any(x=>x is <1 or >59)||
           desired.Values.Select(x=>x.Slot).Distinct().Count()!=current.Count||
           desired.Values.Any(x=>!originals.TryGetValue(x.Slot,out var actual)||(x with{Category=actual.Category})!=actual))
            throw new InvalidOperationException("Inventory changed or layout is invalid. Review layout and try again.");
        var occupants=current.ToDictionary(x=>x.Slot,x=>x.Slot);
        var moves=new List<(int,int)>();
        foreach(var (target,item) in desired.OrderBy(x=>x.Key))
        {
            var from=occupants.First(x=>x.Value==item.Slot).Key;
            if(from==target) continue;
            moves.Add((from,target));
            if(occupants.Remove(target,out var displaced)) occupants[from]=displaced;
            else occupants.Remove(from);
            occupants[target]=item.Slot;
        }
        return moves;
    }
}
