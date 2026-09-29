namespace DAOrganizer.Core;

public static class PotionSlots
{
    private static readonly string[] Names=["Komadium","Red Potion","Exkuranum","Dibenomum","Hemloch"];
    public static string? Name(int slot)=>slot is >=1 and <=5?Names[slot-1]:null;
    public static bool Locked(Item item)=>string.Equals(Name(item.Slot),item.Name.Trim(),StringComparison.OrdinalIgnoreCase);

    // Select one stack per hotkey, preferring the stack already in its correct slot.
    public static Dictionary<int,Item> Select(IEnumerable<Item> items)
    {
        var source=items.ToArray();var result=new Dictionary<int,Item>();
        for(var slot=1;slot<=Names.Length;slot++)
        {
            var item=source.Where(x=>x.Name.Trim().Equals(Name(slot),StringComparison.OrdinalIgnoreCase))
                .OrderBy(x=>x.Slot==slot?0:1).ThenBy(x=>x.Slot).FirstOrDefault();
            if(item!=null)result[slot]=item;
        }
        return result;
    }

    // Layout values retain original slots. Swap occupants; never overwrite a displaced item.
    public static void Place(Dictionary<int,Item> layout)
    {
        foreach(var (target,item) in Select(layout.Values))
        {
            var from=layout.First(x=>x.Value.Slot==item.Slot).Key;
            if(from==target)continue;
            layout.Remove(from);layout.Remove(target,out var displaced);
            layout[target]=item;if(displaced!=null)layout[from]=displaced;
        }
    }

    public static void ValidateLocks(IEnumerable<Item> current,IReadOnlyDictionary<int,Item> desired)
    {
        foreach(var item in current.Where(Locked))
            if(!desired.TryGetValue(item.Slot,out var target)||target.Slot!=item.Slot)
                throw new InvalidOperationException($"Slot {item.Slot} is locked for {item.Name}.");
    }
}
