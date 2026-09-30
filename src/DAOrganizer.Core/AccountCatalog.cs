namespace DAOrganizer.Core;
public sealed class AccountCatalog(InventoryStore store)
{
    public GameAccount CreateGameAccount(string label)=>store.CreateGameAccount(label);
    public IReadOnlyList<GameAccount> GameAccounts()=>store.GameAccounts();
    public void AssignCharacter(string character,long accountId)=>store.AssignCharacter(character,accountId);
    public void UnassignCharacter(string character)=>store.UnassignCharacter(character);
    public GameAccount? AccountFor(string character)=>store.AccountFor(character);
    public void SetSameAccountCoexistence(long accountId,CoexistencePolicy policy)=>store.SetSameAccountCoexistence(accountId,policy);
    public void SetPairCoexistence(long first,long second,CoexistencePolicy policy)=>store.SetPairCoexistence(first,second,policy);
    public CoexistencePolicy PairCoexistence(long first,long second)=>store.PairCoexistence(first,second);
    public CoexistencePolicy CanCoexist(string first,string second)=>store.CanCoexist(first,second);
    public StorageRole AddStorageRole(string character,string label,int priority=0)=>store.AddStorageRole(character,label,priority);
    public IReadOnlyList<StorageRole> StorageRoles(string character)=>store.StorageRoles(character);
    public void SetStorageRoleEnabled(long roleId,bool enabled)=>store.SetStorageRoleEnabled(roleId,enabled);
    public void RemoveStorageRole(long roleId)=>store.RemoveStorageRole(roleId);
    public StorageRoleRule AddStorageRule(long roleId,StorageMatchKind matchKind,string matchValue,long minimumToKeep=0)=>store.AddStorageRule(roleId,matchKind,matchValue,minimumToKeep);
    public IReadOnlyList<StorageRoleRule> StorageRules(long roleId)=>store.StorageRules(roleId);
    public void RemoveStorageRule(long ruleId)=>store.RemoveStorageRule(ruleId);
    public void SetMiddlemanCapable(string character,bool enabled)=>store.SetMiddlemanCapable(character,enabled);
    public bool MiddlemanCapable(string character)=>store.MiddlemanCapable(character);
    public bool Displayed(string name)=>store.Get<bool?>("display/"+name.ToLowerInvariant())??true;
    public void SetDisplay(string name,bool value)=>store.Put("display/"+name.ToLowerInvariant(),value);
    public bool UpdateEnabled(string name)=>store.Get<bool?>("update/"+name.ToLowerInvariant())??false;
    public void SetUpdate(string name,bool value)=>store.Put("update/"+name.ToLowerInvariant(),value);
    private Dictionary<string,string> EquipmentCategories()=>ItemCategoryResolver.EquipmentCategories(store.Search(""));
    private string Resolve(Item item,Dictionary<string,string> known)
    {
        var metadata=store.GetItemMetadata(item);
        return ItemCategoryResolver.Resolve(item,store.GetItemOverride(item)?.Category,
            store.Get<string>("category/"+item.Name.ToLowerInvariant()),metadata?.CanonicalCategory,
            metadata?.CommunityCategory,known.GetValueOrDefault(item.Name));
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
