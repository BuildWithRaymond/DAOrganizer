using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    private const int MaxExactSteps=256;
    private const long MaxExchangeQuantity=255;
    /// <summary>
    /// Builds reviewable exact candidates without reading the clock or persistent store.
    /// IDs and ordering are derived from the state fingerprint, supplied timestamps, and the
    /// ordinal candidate key, so identical arguments produce byte-for-byte identical plans.
    /// The frozen state contract has no capacity observations; consequently bank/exchange
    /// candidates are deliberately never <see cref="OrganizationReadiness.Ready"/>.
    /// </summary>
    public static ExactOrganizationPlan? BuildExact(OrganizationState state,DateTimeOffset createdAt,DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        if(createdAt>=expiresAt)throw new ArgumentException("Plan expiry must be after creation.",nameof(expiresAt));

        var equipment=ItemCategoryResolver.EquipmentCategories(state.Items);
        var candidates=new List<ExactCandidate>();
        foreach(var identityGroup in state.Items.Where(x=>x.Location is "Bank" or "Inventory"&&x.Item.Quantity>0)
            .GroupBy(x=>new ExactIdentity(ItemGroups.Key(x.Item),OrganizationPlanContract.ItemFingerprint(x.Item)))
            .OrderBy(x=>x.Key.ItemKey,StringComparer.Ordinal).ThenBy(x=>x.Key.Fingerprint,StringComparer.Ordinal))
        {
            var entries=identityGroup.OrderBy(x=>x.Character,StringComparer.OrdinalIgnoreCase)
                .ThenBy(x=>x.Location,StringComparer.Ordinal).ThenBy(x=>x.Item.Slot).ToArray();
            if(entries.Length<2||entries.Select(x=>x.Character).Distinct(StringComparer.OrdinalIgnoreCase).Count()<2&&
                !entries.Any(x=>x.Location=="Inventory"))continue;
            var key=identityGroup.Key.ItemKey;var exemplar=entries[0].Item;
            if(IsJunk(state,key)||state.Overrides.GetValueOrDefault(key)?.NeverMove==true)continue;
            state.Metadata.TryGetValue(key,out var metadata);state.Overrides.TryGetValue(key,out var preference);
            var category=ResolvedCategory(state,exemplar,preference,metadata,equipment);
            var holder=PreferredHolder(state,key,category,ItemCategories.Family(exemplar with{Category=category}),preference,entries,[]);
            if(holder is null)continue;

            var matchingRule=MatchingRule(state,holder,key,category,ItemCategories.Family(exemplar with{Category=category}));
            var destinationEntries=entries.Where(x=>x.Character.Equals(holder,StringComparison.OrdinalIgnoreCase)&&x.Location=="Bank").ToArray();
            var destinationTotal=SaturatingSum(destinationEntries.Select(x=>x.Item.Quantity));
            var cap=metadata?.StackLimit is >0?metadata.StackLimit.Value:(long?)null;
            // Existing stacks give a known upper bound. New bank slots have no capacity observation
            // in OrganizationState, so empty destinations remain tentative NeedsScan proposals.
            var stackRooms=cap is long stackCap&&destinationEntries.Length>0&&
                metadata?.Stackable!=false&&exemplar.IsStackable!=false?
                destinationEntries.OrderBy(x=>x.Item.Slot).Select(x=>Math.Max(0,stackCap-x.Item.Quantity)).ToArray():null;
            var destinationRoom=stackRooms is not null?SaturatingSum(stackRooms):
                long.MaxValue-destinationTotal;
            var roomIndex=0;
            var chunkLimit=metadata?.Stackable==false||exemplar.IsStackable==false?1:
                Math.Min(MaxExchangeQuantity,cap??MaxExchangeQuantity);
            foreach(var sourceGroup in entries.Where(x=>!x.Character.Equals(holder,StringComparison.OrdinalIgnoreCase)||x.Location=="Inventory")
                .GroupBy(x=>x.Character,StringComparer.OrdinalIgnoreCase).OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase))
            {
                var keep=matchingRule?.MinimumToKeep??0;
                var movable=Math.Max(0,SaturatingSum(sourceGroup.Select(x=>x.Item.Quantity))-keep);
                foreach(var source in sourceGroup.OrderBy(x=>x.Location,StringComparer.Ordinal).ThenBy(x=>x.Item.Slot))
                {
                    if(movable==0||destinationRoom==0||candidates.Count==MaxExactSteps)break;
                    if(IsPinned(state,source))continue;
                    var remaining=Math.Min(Math.Min(source.Item.Quantity,movable),destinationRoom);
                    for(var chunkIndex=0;chunkIndex<MaxExactSteps&&remaining>0&&candidates.Count<MaxExactSteps;chunkIndex++)
                    {
                        if(stackRooms is not null)
                        {
                            while(roomIndex<stackRooms.Length&&stackRooms[roomIndex]==0)roomIndex++;
                            if(roomIndex==stackRooms.Length)break;
                        }
                        var chunk=Math.Min(Math.Min(remaining,chunkLimit),stackRooms is null?long.MaxValue:stackRooms[roomIndex]);
                        candidates.Add(new(source,holder,key,chunk,metadata,preference,category));
                        remaining-=chunk;
                        movable-=chunk;
                        destinationRoom-=chunk;
                        if(stackRooms is not null)stackRooms[roomIndex]-=chunk;
                    }
                }
            }
        }
        if(candidates.Count==0)return null;

        var planSeed=$"plan|{state.Fingerprint}|{createdAt:O}|{expiresAt:O}";
        var planId=DeterministicGuid(planSeed);
        var sourceRemaining=new Dictionary<(string Character,string Location,int Slot,string Fingerprint),long>();
        var destinationTotals=new Dictionary<(string Character,string ItemKey,string Fingerprint),long>();
        var steps=ImmutableArray.CreateBuilder<PlannedOrganizationStep>();
        foreach(var candidate in candidates.OrderBy(x=>x.ItemKey,StringComparer.Ordinal)
            .ThenBy(x=>x.Destination,StringComparer.OrdinalIgnoreCase)
            .ThenBy(x=>x.Source.Character,StringComparer.OrdinalIgnoreCase)
            .ThenBy(x=>x.Source.Location,StringComparer.Ordinal).ThenBy(x=>x.Source.Item.Slot))
        {
            var shape=OrganizationPlanContract.ItemFingerprint(candidate.Source.Item);
            var sourceKey=(candidate.Source.Character.ToUpperInvariant(),candidate.Source.Location,candidate.Source.Item.Slot,shape);
            var beforeSource=sourceRemaining.GetValueOrDefault(sourceKey,candidate.Source.Item.Quantity);
            var destinationKey=(candidate.Destination.ToUpperInvariant(),candidate.ItemKey,shape);
            var beforeDestination=destinationTotals.GetValueOrDefault(destinationKey,
                SaturatingSum(state.Items.Where(x=>x.Character.Equals(candidate.Destination,StringComparison.OrdinalIgnoreCase)&&x.Location=="Bank"&&
                    ItemGroups.Key(x.Item)==candidate.ItemKey&&OrganizationPlanContract.ItemFingerprint(x.Item)==shape).Select(x=>x.Item.Quantity)));
            var route=ExactRoute(state,candidate.Source.Character,candidate.Destination,candidate.Source.Location,
                TradeBlocked(state,candidate.ItemKey,candidate.Metadata));
            var readiness=route.Kind==TransferRouteKind.ManualOnly?OrganizationReadiness.ManualOnly:OrganizationReadiness.NeedsScan;
            var order=steps.Count;
            var stepId=DeterministicGuid($"{planSeed}|step|{order}|{candidate.ItemKey}|{shape}|{candidate.Source.Character.ToUpperInvariant()}|{candidate.Source.Location}|{candidate.Source.Item.Slot}|{candidate.Quantity}|{candidate.Destination.ToUpperInvariant()}");
            var before=ImmutableArray.Create(
                new OrganizationItemExpectation(candidate.Source.Character,candidate.Source.Location,candidate.ItemKey,
                    candidate.Source.Item.Slot,beforeSource,shape),
                new OrganizationItemExpectation(candidate.Destination,"Bank",candidate.ItemKey,null,beforeDestination,shape));
            var after=ImmutableArray.Create(
                new OrganizationItemExpectation(candidate.Source.Character,candidate.Source.Location,candidate.ItemKey,
                    candidate.Source.Item.Slot,beforeSource-candidate.Quantity,shape),
                new OrganizationItemExpectation(candidate.Destination,"Bank",candidate.ItemKey,null,beforeDestination+candidate.Quantity,shape));
            steps.Add(new(stepId,order,candidate.Source.Character,candidate.Destination,candidate.ItemKey,candidate.Source.Item,
                candidate.Source.Location,candidate.Source.Item.Slot,candidate.Quantity,route.Kind,route.Legs,
                order==0?ImmutableArray<Guid>.Empty:ImmutableArray.Create(steps[^1].Id),readiness,before,after));
            sourceRemaining[sourceKey]=beforeSource-candidate.Quantity;
            destinationTotals[destinationKey]=beforeDestination+candidate.Quantity;
        }
        if(steps.Count==0)return null;
        var plan=new ExactOrganizationPlan(planId,createdAt,expiresAt,state.Fingerprint,steps.ToImmutable());
        OrganizationPlanContract.Validate(plan);
        return plan;
    }

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
            var total=SaturatingSum(bankEntries.Select(x=>x.Item.Quantity));
            var predicted=stackable==true?metadata?.StackLimit is long cap&&cap>0?
                (int)Math.Min(int.MaxValue,Math.Ceiling((decimal)total/cap)):1:bankEntries.Length;
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
        var preferredPool=entries.Any(x=>x.Location=="Bank")?entries.Where(x=>x.Location=="Bank"):entries;
        return preferredPool.GroupBy(x=>x.Character,StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x=>SaturatingSum(x.Select(y=>y.Item.Quantity))).ThenBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).First().Key;
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

    private static bool IsJunk(OrganizationState state,string key)=>state.Settings.TryGetValue("itemRule/"+key,out var value)&&
        JsonSerializer.Deserialize<ItemAction>(value)==ItemAction.Junk;

    private static string ResolvedCategory(OrganizationState state,Item item,ItemOverride? preference,ItemMetadata? metadata,
        IReadOnlyDictionary<string,string> equipment)
    {
        state.Settings.TryGetValue("category/"+item.Name.ToLowerInvariant(),out var setting);
        var legacy=setting is null?null:JsonSerializer.Deserialize<string>(setting);
        return ItemCategoryResolver.Resolve(item,preference?.Category,legacy,metadata?.CanonicalCategory,
            metadata?.CommunityCategory,equipment.GetValueOrDefault(item.Name));
    }

    private static StorageRoleRule? MatchingRule(OrganizationState state,string character,string itemKey,string category,string family)=>
        (from role in state.Roles where role.Enabled&&role.Character.Equals(character,StringComparison.OrdinalIgnoreCase)
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
         orderby specificity descending,role.Priority descending,rule.Id
         select rule).FirstOrDefault();

    private static bool TradeBlocked(OrganizationState state,string key,ItemMetadata? metadata)=>
        metadata?.Tradeability is Tradeability.NonTradeable or Tradeability.EvidenceNonTradeable||
        state.TradeEvidence.GetValueOrDefault(key)?.ExplicitRejections>0;

    private static (TransferRouteKind Kind,ImmutableArray<OrganizationRouteLeg> Legs) ExactRoute(OrganizationState state,
        string source,string destination,string sourceLocation,bool tradeBlocked)
    {
        if(source.Equals(destination,StringComparison.OrdinalIgnoreCase))
            return (TransferRouteKind.Local,
                [new OrganizationRouteLeg(OrganizationLegKind.Deposit,source,destination,sourceLocation,"Bank")]);
        if(tradeBlocked)return (TransferRouteKind.ManualOnly,ImmutableArray<OrganizationRouteLeg>.Empty);
        if(Policy(state,source,destination)==CoexistencePolicy.Yes)
            return (TransferRouteKind.Direct,TransferLegs(source,destination,sourceLocation));
        var middleman=state.Middlemen.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).FirstOrDefault(x=>
            !x.Equals(source,StringComparison.OrdinalIgnoreCase)&&!x.Equals(destination,StringComparison.OrdinalIgnoreCase)&&
            Policy(state,source,x)==CoexistencePolicy.Yes&&Policy(state,x,destination)==CoexistencePolicy.Yes);
        if(middleman is null)return (TransferRouteKind.ManualOnly,ImmutableArray<OrganizationRouteLeg>.Empty);
        var legs=ImmutableArray.CreateBuilder<OrganizationRouteLeg>();
        if(sourceLocation=="Bank")legs.Add(new(OrganizationLegKind.Withdraw,source,source,"Bank","Inventory"));
        legs.Add(new(OrganizationLegKind.Exchange,source,middleman,"Inventory","Inventory"));
        legs.Add(new(OrganizationLegKind.Exchange,middleman,destination,"Inventory","Inventory"));
        legs.Add(new(OrganizationLegKind.Deposit,destination,destination,"Inventory","Bank"));
        return (TransferRouteKind.Middleman,legs.ToImmutable());
    }

    private static ImmutableArray<OrganizationRouteLeg> TransferLegs(string source,string destination,string sourceLocation)
    {
        var legs=ImmutableArray.CreateBuilder<OrganizationRouteLeg>();
        if(sourceLocation=="Bank")legs.Add(new(OrganizationLegKind.Withdraw,source,source,"Bank","Inventory"));
        legs.Add(new(OrganizationLegKind.Exchange,source,destination,"Inventory","Inventory"));
        legs.Add(new(OrganizationLegKind.Deposit,destination,destination,"Inventory","Bank"));
        return legs.ToImmutable();
    }

    private static long SaturatingSum(IEnumerable<long> quantities)
    {
        long total=0;
        foreach(var quantity in quantities)
        {
            if(quantity<=0)continue;
            if(quantity>long.MaxValue-total)return long.MaxValue;
            total+=quantity;
        }
        return total;
    }

    private static Guid DeterministicGuid(string value)
    {
        var bytes=SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0,16));
    }

    private sealed record ExactIdentity(string ItemKey,string Fingerprint);
    private sealed record ExactCandidate(StoredItem Source,string Destination,string ItemKey,long Quantity,
        ItemMetadata? Metadata,ItemOverride? Preference,string Category);
}
