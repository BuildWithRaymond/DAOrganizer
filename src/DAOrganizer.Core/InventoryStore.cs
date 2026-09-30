using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace DAOrganizer.Core;

public sealed partial class InventoryStore:IDisposable
{
    private readonly SqliteConnection _db;
    private readonly Lock _gate=new();
    public InventoryStore(string path)
    {
        if(path!=":memory:") Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path}.ToString());
        _db.Open();
        try
        {
            using var versionCommand=_db.CreateCommand();versionCommand.CommandText="PRAGMA user_version";
            var version=Convert.ToInt32(versionCommand.ExecuteScalar());
            if(version>4)throw new InvalidDataException($"Database schema {version} is newer than this app supports.");
            if(version is 1 or 2 or 3&&path!=":memory:")
            {
                var backup=Path.GetFullPath(path)+$".v{version}.backup";
                if(!File.Exists(backup))
                {
                    var temporary=backup+".tmp";
                    if(File.Exists(temporary))File.Delete(temporary);
                    try
                    {
                        using(var destination=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=temporary,Pooling=false}.ToString()))
                        {
                            destination.Open();_db.BackupDatabase(destination);
                        }
                        File.Move(temporary,backup);
                    }
                    finally{if(File.Exists(temporary))File.Delete(temporary);}
                }
            }
            Execute("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;");
            Execute("""
                CREATE TABLE IF NOT EXISTS characters(name TEXT PRIMARY KEY COLLATE NOCASE,last_seen TEXT);
                CREATE TABLE IF NOT EXISTS snapshots(character TEXT COLLATE NOCASE,location TEXT,state TEXT,updated TEXT,PRIMARY KEY(character,location));
                CREATE TABLE IF NOT EXISTS items(character TEXT COLLATE NOCASE,location TEXT,slot INTEGER,data TEXT,PRIMARY KEY(character,location,slot));
                CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,value TEXT);
                """);
            if(version<2)
            {
                using var migration=_db.BeginTransaction();
                Execute("""
                    CREATE TABLE game_accounts(
                        id INTEGER PRIMARY KEY,
                        label TEXT NOT NULL UNIQUE COLLATE NOCASE,
                        same_account_coexistence TEXT NOT NULL DEFAULT 'Unknown'
                            CHECK(same_account_coexistence IN ('Yes','No','Unknown')));
                    CREATE TABLE character_accounts(
                        character TEXT PRIMARY KEY COLLATE NOCASE REFERENCES characters(name) ON DELETE CASCADE,
                        account_id INTEGER NOT NULL REFERENCES game_accounts(id));
                    CREATE TABLE account_coexistence_overrides(
                        account_a INTEGER NOT NULL REFERENCES game_accounts(id) ON DELETE CASCADE,
                        account_b INTEGER NOT NULL REFERENCES game_accounts(id) ON DELETE CASCADE,
                        policy TEXT NOT NULL CHECK(policy IN ('Yes','No','Unknown')),
                        evidence TEXT NOT NULL DEFAULT 'User',
                        PRIMARY KEY(account_a,account_b),CHECK(account_a<account_b));
                    CREATE TABLE character_transfer_settings(
                        character TEXT PRIMARY KEY COLLATE NOCASE REFERENCES characters(name) ON DELETE CASCADE,
                        middleman_capable INTEGER NOT NULL DEFAULT 0 CHECK(middleman_capable IN (0,1)));
                    CREATE TABLE storage_roles(
                        id INTEGER PRIMARY KEY,
                        character TEXT NOT NULL COLLATE NOCASE REFERENCES characters(name) ON DELETE CASCADE,
                        label TEXT NOT NULL,
                        priority INTEGER NOT NULL DEFAULT 0,
                        enabled INTEGER NOT NULL DEFAULT 1 CHECK(enabled IN (0,1)),
                        UNIQUE(character,label));
                    CREATE TABLE storage_role_rules(
                        id INTEGER PRIMARY KEY,
                        role_id INTEGER NOT NULL REFERENCES storage_roles(id) ON DELETE CASCADE,
                        match_kind TEXT NOT NULL CHECK(match_kind IN ('All','Category','Item')),
                        match_value TEXT NOT NULL DEFAULT '',
                        minimum_to_keep INTEGER NOT NULL DEFAULT 0 CHECK(minimum_to_keep>=0),
                        UNIQUE(role_id,match_kind,match_value));
                    CREATE TABLE item_metadata(
                        item_key TEXT PRIMARY KEY,
                        canonical_category TEXT,
                        community_category TEXT,
                        stackable INTEGER CHECK(stackable IN (0,1)),
                        stack_limit INTEGER CHECK(stack_limit>0),
                        trade_state TEXT NOT NULL DEFAULT 'Unknown'
                            CHECK(trade_state IN ('Unknown','Tradeable','NonTradeable')),
                        provenance TEXT NOT NULL DEFAULT 'Local',
                        updated TEXT NOT NULL);
                    CREATE TABLE item_overrides(
                        item_key TEXT PRIMARY KEY,
                        category TEXT,
                        destination_character TEXT COLLATE NOCASE REFERENCES characters(name) ON DELETE SET NULL,
                        never_move INTEGER NOT NULL DEFAULT 0 CHECK(never_move IN (0,1)));
                    CREATE TABLE trade_observations(
                        id INTEGER PRIMARY KEY,
                        item_key TEXT NOT NULL,
                        outcome TEXT NOT NULL CHECK(outcome IN ('Success','ExplicitRejection','Inconclusive')),
                        evidence_kind TEXT NOT NULL,
                        observed_at TEXT NOT NULL,
                        app_version TEXT NOT NULL,
                        game_client_hash TEXT);
                    """);
                Execute("PRAGMA user_version=2");
                migration.Commit();
            }
            if(version<3)
            {
                using var migration=_db.BeginTransaction();
                Execute("""
                    CREATE TABLE organization_plans(
                        id TEXT PRIMARY KEY,
                        created_at TEXT NOT NULL,
                        expires_at TEXT NOT NULL,
                        input_fingerprint TEXT NOT NULL,
                        approval TEXT NOT NULL DEFAULT 'Draft'
                            CHECK(approval IN ('Draft','Approved','Completed')),
                        checkpoint_fingerprint TEXT,
                        checkpoint_state TEXT,
                        next_step_ordinal INTEGER NOT NULL DEFAULT 0 CHECK(next_step_ordinal>=0));
                    CREATE TABLE plan_steps(
                        plan_id TEXT NOT NULL REFERENCES organization_plans(id) ON DELETE CASCADE,
                        ordinal INTEGER NOT NULL CHECK(ordinal>=0),
                        step_id TEXT NOT NULL,
                        data TEXT NOT NULL,
                        PRIMARY KEY(plan_id,ordinal),UNIQUE(plan_id,step_id));
                    """);
                Execute("PRAGMA user_version=3");
                migration.Commit();
            }
            if(version<4)
            {
                using var migration=_db.BeginTransaction();
                Execute("""
                    CREATE TABLE transfer_runs(
                        id TEXT PRIMARY KEY,
                        plan_id TEXT NOT NULL REFERENCES organization_plans(id),
                        step_id TEXT NOT NULL,
                        source_character TEXT NOT NULL COLLATE NOCASE,
                        destination_character TEXT NOT NULL COLLATE NOCASE,
                        state TEXT NOT NULL CHECK(state IN ('Preparing','InSourceInventory','ExchangeOpen',
                            'Offered','Accepting','RecipientVerified','Banking','Complete',
                            'NeedsReconciliation','Failed')),
                        last_verified_holder TEXT NOT NULL COLLATE NOCASE,
                        quantity INTEGER NOT NULL CHECK(quantity>0),
                        before_fingerprint TEXT NOT NULL,
                        reason TEXT,
                        created_at TEXT NOT NULL,
                        updated_at TEXT NOT NULL,
                        UNIQUE(plan_id,step_id));
                    CREATE UNIQUE INDEX active_transfer_source ON transfer_runs(source_character)
                        WHERE state NOT IN ('Complete','Failed');
                    CREATE TABLE transfer_events(
                        run_id TEXT NOT NULL REFERENCES transfer_runs(id) ON DELETE CASCADE,
                        ordinal INTEGER NOT NULL CHECK(ordinal>=0),
                        state TEXT NOT NULL,
                        observed_at TEXT NOT NULL,
                        detail TEXT NOT NULL,
                        PRIMARY KEY(run_id,ordinal));
                    """);
                Execute("PRAGMA user_version=4");
                migration.Commit();
            }
        }
        catch{_db.Dispose();throw;}
    }
    private void Execute(string sql,params (string,object?)[] parameters)
    {
        using var command=_db.CreateCommand(); command.CommandText=sql;
        foreach(var (key,value) in parameters) command.Parameters.AddWithValue(key,value??DBNull.Value);
        command.ExecuteNonQuery();
    }
    public void EnsureCharacter(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>32||!name.All(char.IsAsciiLetter)) throw new ArgumentException("Enter a character name using letters only.");
        lock(_gate) Execute("INSERT OR IGNORE INTO characters(name) VALUES($n)",("$n",name));
    }
    public void SaveSnapshot(string character,string location,IEnumerable<Item> items,bool complete)
    {
        var rows=items.ToArray();
        lock(_gate)
        {
            EnsureCharacter(character);
            using var transaction=_db.BeginTransaction();
            var now=DateTimeOffset.UtcNow.ToString("O");
            if(complete)
            {
                Execute("DELETE FROM items WHERE character=$c AND location=$l",("$c",character),("$l",location));
                foreach(var item in rows) Execute("INSERT INTO items VALUES($c,$l,$s,$d)",("$c",character),("$l",location),("$s",item.Slot),("$d",JsonSerializer.Serialize(item)));
            }
            Execute("""
                INSERT INTO snapshots VALUES($c,$l,$s,$t)
                ON CONFLICT(character,location) DO UPDATE SET state=$s, updated=CASE WHEN $s='Current' THEN $t ELSE updated END
                """,("$c",character),("$l",location),("$s",complete?"Current":"Incomplete"),("$t",now));
            Execute("UPDATE characters SET last_seen=$t WHERE name=$c",("$c",character),("$t",now));
            transaction.Commit();
        }
    }
    public void MarkStale(string character,string? location=null)
    {
        lock(_gate) Execute("UPDATE snapshots SET state='Stale' WHERE character=$c AND ($l IS NULL OR location=$l)",("$c",character),("$l",location));
    }
    public IReadOnlyList<Item> Items(string character,string location)
    {
        lock(_gate)
        {
            using var cmd=_db.CreateCommand();cmd.CommandText="SELECT data FROM items WHERE character=$c AND location=$l ORDER BY slot";
            cmd.Parameters.AddWithValue("$c",character);cmd.Parameters.AddWithValue("$l",location);
            using var reader=cmd.ExecuteReader();var rows=new List<Item>();
            while(reader.Read()) rows.Add(JsonSerializer.Deserialize<Item>(reader.GetString(0))!);
            return rows;
        }
    }
    public string Freshness(string character,string location)
    {
        lock(_gate)
        {
            using var cmd=_db.CreateCommand();cmd.CommandText="SELECT state FROM snapshots WHERE character=$c AND location=$l";
            cmd.Parameters.AddWithValue("$c",character);cmd.Parameters.AddWithValue("$l",location);
            return cmd.ExecuteScalar() as string??"Never scanned";
        }
    }
    public IReadOnlyList<CharacterSummary> Characters()
    {
        lock(_gate)
        {
            using var cmd=_db.CreateCommand();cmd.CommandText="SELECT name,last_seen FROM characters ORDER BY name COLLATE NOCASE";
            using var r=cmd.ExecuteReader(); var result=new List<(string,DateTimeOffset?)>();
            while(r.Read()) result.Add((r.GetString(0),r.IsDBNull(1)?null:DateTimeOffset.Parse(r.GetString(1))));
            r.Close();return result.Select(x=>new CharacterSummary(x.Item1,Freshness(x.Item1,"Inventory"),Freshness(x.Item1,"Bank"),x.Item2)).ToArray();
        }
    }
    public IReadOnlyList<StoredItem> Search(string text)
    {
        lock(_gate)
        {
            using var cmd=_db.CreateCommand();cmd.CommandText="SELECT i.character,i.location,i.data,s.updated FROM items i JOIN snapshots s ON i.character=s.character AND i.location=s.location";
            using var r=cmd.ExecuteReader();var result=new List<StoredItem>();
            while(r.Read())
            {
                var item=JsonSerializer.Deserialize<Item>(r.GetString(2))!;
                if(item.Name.Contains(text,StringComparison.OrdinalIgnoreCase)) result.Add(new(r.GetString(0),r.GetString(1),item,DateTimeOffset.Parse(r.GetString(3))));
            }
            return result.OrderBy(x=>x.Item.Name).ThenBy(x=>x.Character).ToArray();
        }
    }
    public void Put<T>(string key,T value)
    {
        lock(_gate) Execute("INSERT INTO settings VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v",("$k",key),("$v",JsonSerializer.Serialize(value)));
    }
    public T? Get<T>(string key)
    {
        lock(_gate)
        {
            using var cmd=_db.CreateCommand();cmd.CommandText="SELECT value FROM settings WHERE key=$k";cmd.Parameters.AddWithValue("$k",key);
            return cmd.ExecuteScalar() is string value?JsonSerializer.Deserialize<T>(value):default;
        }
    }
    public void Dispose(){lock(_gate) _db.Dispose();}
}
