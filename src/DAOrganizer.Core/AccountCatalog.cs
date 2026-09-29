namespace DAOrganizer.Core;
public sealed class AccountCatalog(InventoryStore store)
{
    public bool Displayed(string name)=>store.Get<bool?>("display/"+name.ToLowerInvariant())??true;
    public void SetDisplay(string name,bool value)=>store.Put("display/"+name.ToLowerInvariant(),value);
    public bool UpdateEnabled(string name)=>store.Get<bool?>("update/"+name.ToLowerInvariant())??false;
    public void SetUpdate(string name,bool value)=>store.Put("update/"+name.ToLowerInvariant(),value);
    private Dictionary<string,string> EquipmentCategories()=>store.Search("").Where(x=>x.Location=="Equipment")
        .GroupBy(x=>x.Item.Name,StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>ItemCategories.Equipment(x.First().Item.Slot),StringComparer.OrdinalIgnoreCase);
    private string Resolve(Item item,Dictionary<string,string> known)=>store.Get<string>("category/"+item.Name.ToLowerInvariant())??
        known.GetValueOrDefault(item.Name)??AutomaticCategory(item);
    private static string AutomaticCategory(Item item)
    {
        var inferred=ItemCategories.Infer(item.Name);
        return inferred!="Other"?inferred:item.Category;
    }
    public IReadOnlyList<StoredItem> Items(string search,string? location=null)
    {
        var known=EquipmentCategories();
        return store.Search(search).Where(x=>Displayed(x.Character)&&(location==null||x.Location==location))
            .Select(x=>x with{Item=x.Item with{Category=Resolve(x.Item,known)}}).ToArray();
    }
    public Item[] Categorize(IEnumerable<Item> items)
    {
        var known=EquipmentCategories();return items.Select(x=>x with{Category=Resolve(x,known)}).ToArray();
    }
    public string Category(Item item)=>Resolve(item,EquipmentCategories());
}
