using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DAOrganizer.Core;

public sealed partial class InventoryStore
{
    public OrganizationState ReadOrganizationState()
    {
        lock(_gate)
        {
            // One connection-level read transaction covers snapshots and every rule used by the proposal.
            Execute("BEGIN DEFERRED TRANSACTION");
            try
            {
                var characters=Characters();
                var items=Search("").OrderBy(x=>x.Character,StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x=>x.Location,StringComparer.Ordinal).ThenBy(x=>x.Item.Slot).ToArray();
                var accounts=new AccountCatalog(this);
                var gameAccounts=GameAccounts();
                var assignments=new SortedDictionary<string,long?>(StringComparer.OrdinalIgnoreCase);
                foreach(var character in characters)assignments[character.Name]=AccountFor(character.Name)?.Id;
                var roles=characters.SelectMany(x=>StorageRoles(x.Name)).OrderBy(x=>x.Id).ToArray();
                var rules=roles.SelectMany(x=>StorageRules(x.Id)).OrderBy(x=>x.Id).ToArray();
                var metadata=new SortedDictionary<string,ItemMetadata>(StringComparer.Ordinal);
                var overrides=new SortedDictionary<string,ItemOverride>(StringComparer.Ordinal);
                var tradeEvidence=new SortedDictionary<string,TradeEvidence>(StringComparer.Ordinal);
                foreach(var item in items.Select(x=>x.Item).DistinctBy(ItemGroups.Key))
                {
                    var key=ItemGroups.Key(item);
                    if(GetItemMetadata(item) is { } facts)metadata[key]=facts;
                    if(GetItemOverride(item) is { } preference)overrides[key]=preference;
                    var observed=TradeEvidence(item);
                    if(observed.Successes+observed.ExplicitRejections+observed.Inconclusive>0)tradeEvidence[key]=observed;
                }
                var settings=new SortedDictionary<string,string>(StringComparer.Ordinal);
                using(var command=_db.CreateCommand())
                {
                    command.CommandText="SELECT key,value FROM settings WHERE key LIKE 'pins/%' OR key LIKE 'category/%' OR key LIKE 'itemRule/%' ORDER BY key";
                    using var reader=command.ExecuteReader();
                    while(reader.Read())settings[reader.GetString(0)]=reader.GetString(1);
                }
                var coexist=new SortedDictionary<string,CoexistencePolicy>(StringComparer.Ordinal);
                for(var i=0;i<characters.Count;i++)for(var j=i+1;j<characters.Count;j++)
                    coexist[OrganizationPlanner.PairKey(characters[i].Name,characters[j].Name)]=accounts.CanCoexist(characters[i].Name,characters[j].Name);
                var middlemen=characters.Where(x=>MiddlemanCapable(x.Name)).Select(x=>x.Name).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
                var payload=JsonSerializer.Serialize(new{characters,items,gameAccounts,assignments,roles,rules,metadata,overrides,tradeEvidence,settings,coexist,middlemen});
                var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
                Execute("COMMIT");
                return new(characters,items,gameAccounts,assignments,roles,rules,metadata,overrides,tradeEvidence,settings,coexist,middlemen,fingerprint);
            }
            catch
            {
                Execute("ROLLBACK");
                throw;
            }
        }
    }
}
