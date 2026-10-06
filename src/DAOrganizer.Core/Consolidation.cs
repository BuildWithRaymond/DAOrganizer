namespace DAOrganizer.Core;

public sealed record ConsolidationSource(string Character,long InventoryQuantity,long BankQuantity,long ProtectedQuantity)
{
    public long EligibleQuantity=>InventoryQuantity+BankQuantity;
}

public sealed record ConsolidationSummary(Item Item,string ItemKey,string DestinationCharacter,
    IReadOnlyList<ConsolidationSource> Sources,long DestinationBankQuantity,string? Blocker)
{
    public long EligibleQuantity=>Sources.Sum(x=>x.EligibleQuantity);
    public bool CanStart=>Blocker is null&&EligibleQuantity>0;
}

public static class ConsolidationPlanner
{
    public static ConsolidationSummary Build(OrganizationState state,Item selected,string destination,
        IReadOnlySet<string>? reservedCharacters=null)
    {
        var key=ItemGroups.Key(selected);
        var matching=state.Items.Where(x=>ItemGroups.Key(x.Item)==key&&x.Item.Quantity>0).ToArray();
        var exemplar=matching.FirstOrDefault()?.Item??selected;
        string? blocker=null;
        if(!state.Characters.Any(x=>x.Name.Equals(destination,StringComparison.OrdinalIgnoreCase)))
            blocker="Choose a saved destination character.";
        else if(state.Settings.TryGetValue("itemRule/"+key,out var rule)&&
            System.Text.Json.JsonSerializer.Deserialize<ItemAction>(rule)==ItemAction.Junk)
            blocker="This item is marked Junk. Change its item rule before consolidating.";
        else if(state.Overrides.GetValueOrDefault(key)?.NeverMove==true)
            blocker="This item is protected by Never move.";
        else if(state.Metadata.GetValueOrDefault(key)?.Tradeability is
            Tradeability.NonTradeable or Tradeability.EvidenceNonTradeable||
            state.TradeEvidence.GetValueOrDefault(key)?.ExplicitRejections>0)
            blocker="This item has evidence that it cannot be exchanged.";

        var sources=new List<ConsolidationSource>();
        foreach(var character in matching.Select(x=>x.Character).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x=>!x.Equals(destination,StringComparison.OrdinalIgnoreCase))
            .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
        {
            var rows=matching.Where(x=>x.Character.Equals(character,StringComparison.OrdinalIgnoreCase)).ToArray();
            var protectedQuantity=rows.Where(x=>IsPinned(state,x)||x.Location=="Equipment").Sum(x=>x.Item.Quantity);
            var inventory=rows.Where(x=>x.Location=="Inventory"&&!IsPinned(state,x)).Sum(x=>x.Item.Quantity);
            var bank=rows.Where(x=>x.Location=="Bank").Sum(x=>x.Item.Quantity);
            if(inventory+bank+protectedQuantity==0)continue;
            sources.Add(new(character,inventory,bank,protectedQuantity));
        }

        if(blocker is null&&sources.All(x=>x.EligibleQuantity==0))
            blocker="No eligible copies are held by another character.";
        if(blocker is null)
        {
            var reserved=sources.FirstOrDefault(x=>reservedCharacters?.Contains(x.Character)==true);
            if(reserved is not null)blocker=$"{reserved.Character} has an unfinished transfer. Review its last verified holder first.";
        }
        if(blocker is null)
        {
            var unsupported=sources.FirstOrDefault(x=>x.EligibleQuantity>0&&Policy(state,x.Character,destination)!=CoexistencePolicy.Yes);
            if(unsupported is not null)
                blocker=$"{unsupported.Character} and {destination} cannot be online together with a proven direct handoff.";
        }
        var destinationBank=matching.Where(x=>x.Character.Equals(destination,StringComparison.OrdinalIgnoreCase)&&x.Location=="Bank")
            .Sum(x=>x.Item.Quantity);
        return new(exemplar,key,destination,sources,destinationBank,blocker);
    }

    private static CoexistencePolicy Policy(OrganizationState state,string first,string second)=>
        state.Coexistence.GetValueOrDefault(OrganizationPlanner.PairKey(first,second),CoexistencePolicy.Unknown);

    private static bool IsPinned(OrganizationState state,StoredItem entry)
    {
        if(entry.Location!="Inventory"||
           !state.Settings.TryGetValue("pins/"+entry.Character.ToLowerInvariant(),out var value))return false;
        try{return System.Text.Json.JsonSerializer.Deserialize<HashSet<int>>(value)?.Contains(entry.Item.Slot)??true;}
        catch(System.Text.Json.JsonException){return true;}
    }
}
