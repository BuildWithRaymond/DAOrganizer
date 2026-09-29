namespace DAOrganizer.Core;

// Sorting preferences only. Manual moves, pins and saved layouts remain under user control.
public static class PotionSlots
{
    public static readonly string[] Names=["Komadium","Red Potion","Exkuranum","Dibenomum","Hemloch"];
    public static Dictionary<int,Item> Select(IEnumerable<Item> items,ISet<int>? pinned=null)
    {
        pinned??=new HashSet<int>();
        var source=items.ToArray();var result=new Dictionary<int,Item>();
        var names=new[]{"Dual Crystal Arrows"}.Concat(Names);
        foreach(var name in names)
        {
            if(source.Any(x=>pinned.Contains(x.Slot)&&x.Name.Trim().Equals(name,StringComparison.OrdinalIgnoreCase)))continue;
            var target=Enumerable.Range(1,12).FirstOrDefault(x=>!pinned.Contains(x)&&!result.ContainsKey(x));
            if(target==0)break;
            var item=source.Where(x=>!pinned.Contains(x.Slot)&&x.Name.Trim().Equals(name,StringComparison.OrdinalIgnoreCase))
                .OrderBy(x=>x.Slot==target?0:1).ThenBy(x=>x.Slot).FirstOrDefault();
            if(item!=null)result[target]=item;
        }
        return result;
    }
}
