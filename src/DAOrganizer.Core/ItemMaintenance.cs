namespace DAOrganizer.Core;

public enum ItemAction { Keep, Junk, AutoDeposit }
public sealed record ItemGroup(string Key,Item Item,long Quantity,IReadOnlyList<StoredItem> Owners);
public static class ItemGroups
{
    public static string Key(Item item)=>$"{item.Sprite}:{item.Color}:{item.Name.Trim().ToUpperInvariant()}";
    public static IReadOnlyList<ItemGroup> Build(IEnumerable<StoredItem> items)=>items.GroupBy(x=>Key(x.Item))
        .Select(g=>new ItemGroup(g.Key,g.First().Item,g.Sum(x=>x.Item.Quantity),g.OrderBy(x=>x.Character).ThenBy(x=>x.Location).ThenBy(x=>x.Item.Slot).ToArray()))
        .OrderBy(x=>ItemCategories.Order(x.Item.Category)).ThenBy(x=>x.Item.Category,StringComparer.OrdinalIgnoreCase)
        .ThenBy(x=>ItemCategories.Family(x.Item),StringComparer.OrdinalIgnoreCase).ThenBy(x=>x.Item.Name,StringComparer.OrdinalIgnoreCase).ToArray();
}
public sealed class ItemRules(InventoryStore store)
{
    public ItemAction Get(Item item)=>store.Get<ItemAction>("itemRule/"+ItemGroups.Key(item));
    public void Set(Item item,ItemAction action)=>store.Put("itemRule/"+ItemGroups.Key(item),action);
}
public sealed record MaintenanceEntry(string Character,string Location,Item Item,ItemAction Action);
public sealed record MaintenanceResult(string Character,string Item,string Action,string Result);
public static class MaintenancePlan
{
    public static bool Protected(Item item,ISet<int> pins)=>pins.Contains(item.Slot);
    public static IReadOnlyList<MaintenanceEntry> Build(IEnumerable<StoredItem> items,ItemRules rules,ItemAction action,Func<string,HashSet<int>> pins)
    {
        if(action==ItemAction.Keep)return [];
        return items.Where(x=>x.Item.Quantity>0&&rules.Get(x.Item)==action&&
                (x.Location=="Inventory"&&!Protected(x.Item,pins(x.Character))||action==ItemAction.Junk&&x.Location=="Bank"))
            .Select(x=>new MaintenanceEntry(x.Character,x.Location,x.Item,action))
            .OrderBy(x=>x.Character,StringComparer.OrdinalIgnoreCase).ThenBy(x=>x.Location=="Bank"?1:0).ThenBy(x=>x.Item.Slot).ToArray();
    }
}

public interface IItemMaintenanceClient
{
    Item[] Inventory();
    IReadOnlyList<Item> BankItems();
    Task<bool> DropItem(Item item,long quantity,CancellationToken token);
    Task<bool> DepositItem(Item item,long quantity,CancellationToken token);
    Task<bool> WithdrawOne(Item item,CancellationToken token);
}
public static class MaintenanceRunner
{
    public static async Task Run(IItemMaintenanceClient client,IReadOnlyList<MaintenanceEntry> plan,ItemRules rules,
        Func<HashSet<int>> pins,Action<MaintenanceResult> report,CancellationToken token)
    {
        var rejected=new HashSet<string>();
        foreach(var entry in plan)
        {
            token.ThrowIfCancellationRequested();
            var key=ItemGroups.Key(entry.Item);
            void Result(string text)=>report(new(entry.Character,entry.Item.Name,entry.Action.ToString(),text));
            bool Allowed()=>rules.Get(entry.Item)==entry.Action;
            if(!Allowed()){Result("Skipped: rule changed");continue;}
            if(rejected.Contains(key)){Result("Skipped: earlier attempt not confirmed");continue;}
            if(entry.Location=="Inventory")
            {
                var item=client.Inventory().FirstOrDefault(x=>x.Slot==entry.Item.Slot&&ItemGroups.Key(x)==key);
                if(item==null||MaintenancePlan.Protected(item,pins())){Result("Skipped: moved, missing or pinned");continue;}
                if(entry.Action==ItemAction.AutoDeposit&&item.Quantity>entry.Item.Quantity){Result("Skipped: stack grew since preview");continue;}
                var quantity=Math.Min(item.Quantity,entry.Item.Quantity);
                var ok=entry.Action==ItemAction.Junk?await client.DropItem(item,quantity,token):await client.DepositItem(item,quantity,token);
                if(!ok)rejected.Add(key);
                Result(ok?$"{quantity:N0} {(entry.Action==ItemAction.Junk?"dropped":"deposited")}":"Not confirmed; no retry");
            }
            else if(entry.Location=="Bank"&&entry.Action==ItemAction.Junk)
            {
                var matches=client.BankItems().Where(x=>ItemGroups.Key(x)==key).ToArray();
                // The native withdrawal request selects by name: ambiguous variants cannot be selected safely.
                if(matches.Length!=1||client.BankItems().Any(x=>x.Name.Equals(entry.Item.Name,StringComparison.OrdinalIgnoreCase)&&ItemGroups.Key(x)!=key))
                {Result("Skipped: missing or ambiguous bank item");continue;}
                var quantity=Math.Min(matches[0].Quantity,entry.Item.Quantity);long dropped=0;
                for(;dropped<quantity;dropped++)
                {
                    token.ThrowIfCancellationRequested();
                    if(!Allowed()){Result("Stopped item: rule changed");break;}
                    var before=client.Inventory();
                    if(before.Length>=59){Result("Skipped: inventory full");break;}
                    if(before.Any(x=>ItemGroups.Key(x)==key&&MaintenancePlan.Protected(x,pins())))
                    {Result("Skipped: matching inventory item is pinned");break;}
                    if(!await client.WithdrawOne(matches[0],token)){rejected.Add(key);Result("Withdrawal not confirmed; no retry");break;}
                    var added=client.Inventory().Where(x=>ItemGroups.Key(x)==key&&x.Quantity-(before.FirstOrDefault(b=>b.Slot==x.Slot&&ItemGroups.Key(b)==key)?.Quantity??0)==1).ToArray();
                    if(added.Length!=1||!Allowed()||MaintenancePlan.Protected(added[0],pins()))
                    {Result("Withdrawn item left in inventory: contents or rule changed");break;}
                    if(!await client.DropItem(added[0],1,token))
                    {rejected.Add(key);Result("Drop not confirmed; withdrawn item left in inventory; no retry");break;}
                }
                if(dropped>0)Result($"{dropped:N0} withdrawn and dropped");
            }
        }
    }
}
