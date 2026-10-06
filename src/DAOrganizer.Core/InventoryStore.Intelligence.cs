using Microsoft.Data.Sqlite;

namespace DAOrganizer.Core;

public sealed partial class InventoryStore
{
    public void SetItemMetadata(Item item,ItemMetadata metadata)
    {
        if(metadata.Tradeability is Tradeability.EvidenceTradeable or Tradeability.EvidenceNonTradeable)
            throw new ArgumentException("Observation evidence is stored separately from canonical metadata.");
        if(metadata.StackLimit is <=0)throw new ArgumentException("Stack limit must be positive.");
        lock(_gate)Execute("""
            INSERT INTO item_metadata(item_key,canonical_category,stackable,stack_limit,trade_state,provenance,updated)
            VALUES($key,$category,$stackable,$limit,$trade,$source,$updated)
            ON CONFLICT(item_key) DO UPDATE SET canonical_category=excluded.canonical_category,
              stackable=excluded.stackable,stack_limit=excluded.stack_limit,
              trade_state=excluded.trade_state,provenance=excluded.provenance,updated=excluded.updated
            """,("$key",ItemGroups.Key(item)),("$category",metadata.CanonicalCategory),
            ("$stackable",metadata.Stackable is bool stackable?stackable?1:0:null),("$limit",metadata.StackLimit),
            ("$trade",metadata.Tradeability.ToString()),("$source",metadata.Provenance),("$updated",DateTimeOffset.UtcNow.ToString("O")));
    }

    public void SetCommunityCategory(Item item,string? category)
    {
        lock(_gate)Execute("""
            INSERT INTO item_metadata(item_key,community_category,updated) VALUES($key,$category,$updated)
            ON CONFLICT(item_key) DO UPDATE SET community_category=excluded.community_category,updated=excluded.updated
            """,("$key",ItemGroups.Key(item)),("$category",category),("$updated",DateTimeOffset.UtcNow.ToString("O")));
    }

    public ItemMetadata? GetItemMetadata(Item item)
    {
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="SELECT canonical_category,community_category,stackable,stack_limit,trade_state,provenance FROM item_metadata WHERE item_key=$key";
            command.Parameters.AddWithValue("$key",ItemGroups.Key(item));
            using var reader=command.ExecuteReader();
            if(!reader.Read())return null;
            return new(reader.IsDBNull(0)?null:reader.GetString(0),reader.IsDBNull(2)?null:reader.GetInt32(2)==1,
                reader.IsDBNull(3)?null:reader.GetInt64(3),Enum.Parse<Tradeability>(reader.GetString(4)),reader.GetString(5),
                reader.IsDBNull(1)?null:reader.GetString(1));
        }
    }

    public void SetItemOverride(Item item,ItemOverride preference)
    {
        lock(_gate)Execute("""
            INSERT INTO item_overrides(item_key,category,destination_character,never_move)
            VALUES($key,$category,$destination,$never)
            ON CONFLICT(item_key) DO UPDATE SET category=excluded.category,
              destination_character=excluded.destination_character,never_move=excluded.never_move
            """,("$key",ItemGroups.Key(item)),("$category",preference.Category),
            ("$destination",preference.DestinationCharacter),("$never",preference.NeverMove?1:0));
    }

    public ItemOverride? GetItemOverride(Item item)
    {
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="SELECT category,destination_character,never_move FROM item_overrides WHERE item_key=$key";
            command.Parameters.AddWithValue("$key",ItemGroups.Key(item));
            using var reader=command.ExecuteReader();
            return reader.Read()?new(reader.IsDBNull(0)?null:reader.GetString(0),reader.IsDBNull(1)?null:reader.GetString(1),reader.GetInt32(2)==1):null;
        }
    }

    public void AddTradeObservation(Item item,TradeOutcome outcome,string evidenceKind,string appVersion,string? gameClientHash)
    {
        if(string.IsNullOrWhiteSpace(evidenceKind)||string.IsNullOrWhiteSpace(appVersion))throw new ArgumentException("Trade evidence needs its source and app version.");
        lock(_gate)Execute("""
            INSERT INTO trade_observations(item_key,outcome,evidence_kind,observed_at,app_version,game_client_hash)
            VALUES($key,$outcome,$kind,$time,$app,$client)
            """,("$key",ItemGroups.Key(item)),("$outcome",outcome.ToString()),("$kind",evidenceKind),
            ("$time",DateTimeOffset.UtcNow.ToString("O")),("$app",appVersion),("$client",gameClientHash));
    }

    public bool AddVerifiedManualTrade(Item item,string operationId,string appVersion)
    {
        if(string.IsNullOrWhiteSpace(operationId)||operationId.Length>128||string.IsNullOrWhiteSpace(appVersion))
            throw new ArgumentException("Manual trade evidence needs an operation ID and app version.");
        var evidenceKind="ManualCapture:"+operationId;
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="""
                INSERT INTO trade_observations(item_key,outcome,evidence_kind,observed_at,app_version,game_client_hash)
                SELECT $key,'Success',$kind,$time,$app,NULL
                WHERE NOT EXISTS(SELECT 1 FROM trade_observations WHERE evidence_kind=$kind)
                """;
            command.Parameters.AddWithValue("$key",ItemGroups.Key(item));
            command.Parameters.AddWithValue("$kind",evidenceKind);
            command.Parameters.AddWithValue("$time",DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$app",appVersion);
            return command.ExecuteNonQuery()==1;
        }
    }

    public TradeEvidence TradeEvidence(Item item)
    {
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="SELECT outcome,COUNT(*),MAX(observed_at) FROM trade_observations WHERE item_key=$key GROUP BY outcome";
            command.Parameters.AddWithValue("$key",ItemGroups.Key(item));
            using var reader=command.ExecuteReader();
            var successes=0;var rejected=0;var inconclusive=0;DateTimeOffset? last=null;
            while(reader.Read())
            {
                var count=reader.GetInt32(1);
                switch(Enum.Parse<TradeOutcome>(reader.GetString(0)))
                {
                    case TradeOutcome.Success:successes=count;break;
                    case TradeOutcome.ExplicitRejection:rejected=count;break;
                    case TradeOutcome.Inconclusive:inconclusive=count;break;
                }
                var time=DateTimeOffset.Parse(reader.GetString(2));
                if(last==null||time>last)last=time;
            }
            var state=successes>0&&rejected==0?Tradeability.EvidenceTradeable:
                rejected>0&&successes==0?Tradeability.EvidenceNonTradeable:Tradeability.Unknown;
            return new(state,successes,rejected,inconclusive,last);
        }
    }
}
