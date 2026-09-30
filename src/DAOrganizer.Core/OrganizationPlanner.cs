namespace DAOrganizer.Core;

public sealed record OrganizationState(
    IReadOnlyList<CharacterSummary> Characters,
    IReadOnlyList<StoredItem> Items,
    IReadOnlyList<GameAccount> GameAccounts,
    IReadOnlyDictionary<string,long?> AccountAssignments,
    IReadOnlyList<StorageRole> Roles,
    IReadOnlyList<StorageRoleRule> RoleRules,
    IReadOnlyDictionary<string,ItemMetadata> Metadata,
    IReadOnlyDictionary<string,ItemOverride> Overrides,
    IReadOnlyDictionary<string,TradeEvidence> TradeEvidence,
    IReadOnlyDictionary<string,string> Settings,
    IReadOnlyDictionary<string,CoexistencePolicy> Coexistence,
    IReadOnlyList<string> Middlemen,
    string Fingerprint);

public enum TransferRouteKind { Direct, Middleman, ManualOnly, Local }
public sealed record PlannedRoute(string Source,string Destination,TransferRouteKind Kind,string? Middleman,string Reason,string SourceLocation="Bank");
public sealed record ConsolidationOpportunity(Item Item,IReadOnlyList<string> Owners,string? ProposedHolder,
    int CurrentBankSlots,int PotentialBankSlots,int PotentialSlotsFreed,int VerifiedSlotsFreed,
    IReadOnlyList<PlannedRoute> Routes,IReadOnlyList<string> Blockers);
public sealed record OrganizationPlan(string InputFingerprint,IReadOnlyList<ConsolidationOpportunity> Groups)
{
    public int CurrentBankSlots=>Groups.Sum(x=>x.CurrentBankSlots);
    public int PotentialSlotsFreed=>Groups.Sum(x=>x.PotentialSlotsFreed);
    public int VerifiedSlotsFreed=>Groups.Sum(x=>x.VerifiedSlotsFreed);
}

public static class OrganizationPlanner
{
    public static OrganizationPlan Build(OrganizationState state)
    {
        var groups=new List<ConsolidationOpportunity>();
        var equipment=ItemCategoryResolver.EquipmentCategories(state.Items);
        foreach(var group in state.Items.Where(x=>x.Location is "Bank" or "Inventory"&&x.Item.Quantity>0)
            .GroupBy(x=>ItemGroups.Key(x.Item),StringComparer.Ordinal)
            .Where(x=>x.Any(y=>y.Location=="Bank")&&x.Select(y=>y.Character).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1))
        {
            var entries=group.ToArray();var exemplar=entries[0].Item;var key=group.Key;
            var bankEntries=entries.Where(x=>x.Location=="Bank").ToArray();
            if(state.Settings.TryGetValue("itemRule/"+key,out var actionSetting)&&
                System.Text.Json.JsonSerializer.Deserialize<ItemAction>(actionSetting)==ItemAction.Junk)continue;
            var owners=entries.Select(x=>x.Character).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
            state.Metadata.TryGetValue(key,out var metadata);state.Overrides.TryGetValue(key,out var preference);
            state.TradeEvidence.TryGetValue(key,out var evidence);
            state.Settings.TryGetValue("category/"+exemplar.Name.ToLowerInvariant(),out var categorySetting);
            var legacyCategory=categorySetting==null?null:System.Text.Json.JsonSerializer.Deserialize<string>(categorySetting);
            var category=ItemCategoryResolver.Resolve(exemplar,preference?.Category,legacyCategory,
                metadata?.CanonicalCategory,metadata?.CommunityCategory,equipment.GetValueOrDefault(exemplar.Name));
            var blockers=new List<string>();
            var family=ItemCategories.Family(exemplar with{Category=category});
            var holder=PreferredHolder(state,key,category,family,preference,entries,blockers);
            var stackable=metadata?.Stackable??(bankEntries.All(x=>x.Item.IsStackable==true)?true:
                bankEntries.Any(x=>x.Item.IsStackable==false)?false:null);
            var total=bankEntries.Sum(x=>x.Item.Quantity);
            var predicted=stackable==true?metadata?.StackLimit is long cap&&cap>0?(int)Math.Ceiling((decimal)total/cap):1:bankEntries.Length;
            var potential=Math.Max(0,bankEntries.Length-predicted);
            if(stackable==null)blockers.Add("Stackability unknown");
            if(entries.Any(x=>x.Item.IsStackable==false)&&entries.Any(x=>x.Item.IsStackable==true))
                blockers.Add("Conflicting stackability observations");
            if(stackable==true&&metadata?.StackLimit is not >0)blockers.Add("Stack limit unknown; savings are an upper bound");
            if(metadata?.Tradeability is Tradeability.NonTradeable)blockers.Add("Canonical non-tradeable");
            else if(evidence?.ExplicitRejections>0)blockers.Add("Explicit trade rejection observed; review before transfer");
            else if(metadata?.Tradeability is not Tradeability.Tradeable&&evidence?.Successes is not >0)
                blockers.Add("Tradeability not verified");
            if(preference?.NeverMove==true)blockers.Add("User set never move");
            foreach(var protectedCharacter in entries.Where(x=>IsPinned(state,x)).Select(x=>x.Character)
                .Distinct(StringComparer.OrdinalIgnoreCase))
                blockers.Add($"{protectedCharacter} has a pinned inventory slot");
            foreach(var entry in entries.DistinctBy(x=>(x.Character.ToUpperInvariant(),x.Location)))
            {
                var summary=state.Characters.FirstOrDefault(x=>x.Name.Equals(entry.Character,StringComparison.OrdinalIgnoreCase));
                var freshness=entry.Location=="Bank"?summary?.BankState:summary?.InventoryState;
                if(freshness!="Current")blockers.Add($"{entry.Character} {entry.Location.ToLowerInvariant()} needs scan");
            }
            if(holder!=null&&!bankEntries.Any(x=>x.Character.Equals(holder,StringComparison.OrdinalIgnoreCase))&&
                state.Characters.FirstOrDefault(x=>x.Name.Equals(holder,StringComparison.OrdinalIgnoreCase))?.BankState!="Current")
                blockers.Add($"{holder} bank needs scan");
            blockers.Add("Destination bank capacity and exchange flow not verified");
            var routes=holder==null?Array.Empty<PlannedRoute>():entries
                .Where(x=>!x.Character.Equals(holder,StringComparison.OrdinalIgnoreCase))
                .DistinctBy(x=>(x.Character.ToUpperInvariant(),x.Location))
                .Select(x=>Route(state,x.Character,holder,preference?.NeverMove==true,
                    entries.Any(y=>y.Character.Equals(x.Character,StringComparison.OrdinalIgnoreCase)&&
                        y.Location==x.Location&&IsPinned(state,y)),
                    metadata?.Tradeability is Tradeability.NonTradeable||evidence?.ExplicitRejections>0,x.Location)).ToArray();
            groups.Add(new(exemplar,owners,holder,bankEntries.Length,predicted,potential,0,routes,blockers));
        }
        return new(state.Fingerprint,groups.OrderByDescending(x=>x.PotentialSlotsFreed).ThenBy(x=>x.Item.Name,StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string? PreferredHolder(OrganizationState state,string itemKey,string category,string family,ItemOverride? preference,
        StoredItem[] entries,List<string> blockers)
    {
        if(preference?.DestinationCharacter is {Length:>0} chosen)
        {
            if(state.Characters.Any(x=>x.Name.Equals(chosen,StringComparison.OrdinalIgnoreCase)))return chosen;
            blockers.Add("Preferred character is unknown");return null;
        }
        var matches=(from role in state.Roles where role.Enabled
            from rule in state.RoleRules where rule.RoleId==role.Id
            let specificity=rule.MatchKind switch
            {
                StorageMatchKind.Item when rule.MatchValue.Equals(itemKey,StringComparison.OrdinalIgnoreCase)=>3,
                StorageMatchKind.Category when rule.MatchValue.Equals(category,StringComparison.OrdinalIgnoreCase)||
                    rule.MatchValue.Equals(family,StringComparison.OrdinalIgnoreCase)=>2,
                StorageMatchKind.All=>1,
                _=>0
            }
            where specificity>0
            select (role.Character,role.Priority,Specificity:specificity)).OrderByDescending(x=>x.Specificity)
            .ThenByDescending(x=>x.Priority).ToArray();
        if(matches.Length>0)
        {
            var best=matches[0];
            if(matches.Any(x=>x.Specificity==best.Specificity&&x.Priority==best.Priority&&!x.Character.Equals(best.Character,StringComparison.OrdinalIgnoreCase)))
            {blockers.Add("Conflicting storage roles need holder choice");return null;}
            return best.Character;
        }
        return entries.Where(x=>x.Location=="Bank").GroupBy(x=>x.Character,StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x=>x.Sum(y=>y.Item.Quantity)).ThenBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).First().Key;
    }

    private static bool IsPinned(OrganizationState state,StoredItem entry)
    {
        if(entry.Location!="Inventory"||
           !state.Settings.TryGetValue("pins/"+entry.Character.ToLowerInvariant(),out var pins))return false;
        try{return System.Text.Json.JsonSerializer.Deserialize<HashSet<int>>(pins)?.Contains(entry.Item.Slot)??true;}
        catch(System.Text.Json.JsonException){return true;}
    }

    private static PlannedRoute Route(OrganizationState state,string source,string destination,bool neverMove,bool pinned,bool tradeBlocked,string sourceLocation)
    {
        if(neverMove)return new(source,destination,TransferRouteKind.ManualOnly,null,"User set never move",sourceLocation);
        if(pinned)return new(source,destination,TransferRouteKind.ManualOnly,null,"Pinned inventory slot needs manual review",sourceLocation);
        if(tradeBlocked)return new(source,destination,TransferRouteKind.ManualOnly,null,"Tradeability needs review",sourceLocation);
        if(Policy(state,source,destination)==CoexistencePolicy.Yes)
            return new(source,destination,TransferRouteKind.Direct,null,"Accounts can coexist",sourceLocation);
        var middleman=state.Middlemen.FirstOrDefault(x=>!x.Equals(source,StringComparison.OrdinalIgnoreCase)&&
            !x.Equals(destination,StringComparison.OrdinalIgnoreCase)&&
            Policy(state,source,x)==CoexistencePolicy.Yes&&Policy(state,x,destination)==CoexistencePolicy.Yes);
        return middleman!=null?new(source,destination,TransferRouteKind.Middleman,middleman,"Two declared coexistence links",sourceLocation):
            new(source,destination,TransferRouteKind.ManualOnly,null,"No verified coexistence route",sourceLocation);
    }

    private static CoexistencePolicy Policy(OrganizationState state,string first,string second)=>
        state.Coexistence.TryGetValue(PairKey(first,second),out var policy)?policy:CoexistencePolicy.Unknown;
    public static string PairKey(string first,string second)=>string.Compare(first,second,StringComparison.OrdinalIgnoreCase)<0?
        first.ToUpperInvariant()+"|"+second.ToUpperInvariant():second.ToUpperInvariant()+"|"+first.ToUpperInvariant();
}
