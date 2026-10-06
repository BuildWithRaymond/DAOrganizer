namespace DAOrganizer.Core;

public static class ItemCategoryResolver
{
    public static Dictionary<string,string> EquipmentCategories(IEnumerable<StoredItem> items)=>items
        .Where(x=>x.Location=="Equipment")
        .GroupBy(x=>x.Item.Name,StringComparer.OrdinalIgnoreCase)
        .ToDictionary(x=>x.Key,x=>ItemCategories.Equipment(x.First().Item.Slot),StringComparer.OrdinalIgnoreCase);

    public static string Resolve(Item item,string? exactOverride,string? legacyOverride,string? canonical,
        string? community,string? equipment)=>exactOverride??legacyOverride??canonical??community??equipment??
        (ItemCategories.Infer(item.Name) is { } inferred&&inferred!="Other"?inferred:item.Category);
}
