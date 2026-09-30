namespace DAOrganizer.Core;

public sealed partial class InventoryStore
{
    public GameAccount CreateGameAccount(string label)
    {
        label=label.Trim();
        if(label.Length is <1 or >64)throw new ArgumentException("Enter an account label between 1 and 64 characters.");
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="INSERT INTO game_accounts(label) VALUES($label) RETURNING id";
            command.Parameters.AddWithValue("$label",label);
            var id=(long)command.ExecuteScalar()!;
            return new(id,label,CoexistencePolicy.Unknown);
        }
    }

    public IReadOnlyList<GameAccount> GameAccounts()
    {
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="SELECT id,label,same_account_coexistence FROM game_accounts ORDER BY label COLLATE NOCASE";
            using var reader=command.ExecuteReader();var result=new List<GameAccount>();
            while(reader.Read())result.Add(new(reader.GetInt64(0),reader.GetString(1),Enum.Parse<CoexistencePolicy>(reader.GetString(2))));
            return result;
        }
    }

    public void AssignCharacter(string character,long accountId)
    {
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="""
                INSERT INTO character_accounts(character,account_id) VALUES($character,$account)
                ON CONFLICT(character) DO UPDATE SET account_id=excluded.account_id
                """;
            command.Parameters.AddWithValue("$character",character);
            command.Parameters.AddWithValue("$account",accountId);
            command.ExecuteNonQuery();
        }
    }

    public void UnassignCharacter(string character)
    {
        lock(_gate)Execute("DELETE FROM character_accounts WHERE character=$character",("$character",character));
    }

    public GameAccount? AccountFor(string character)
    {
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="""
                SELECT a.id,a.label,a.same_account_coexistence FROM game_accounts a
                JOIN character_accounts c ON c.account_id=a.id WHERE c.character=$character
                """;
            command.Parameters.AddWithValue("$character",character);
            using var reader=command.ExecuteReader();
            return reader.Read()?new(reader.GetInt64(0),reader.GetString(1),Enum.Parse<CoexistencePolicy>(reader.GetString(2))):null;
        }
    }

    public void SetSameAccountCoexistence(long accountId,CoexistencePolicy policy)
    {
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="UPDATE game_accounts SET same_account_coexistence=$policy WHERE id=$id";
            command.Parameters.AddWithValue("$policy",policy.ToString());command.Parameters.AddWithValue("$id",accountId);
            if(command.ExecuteNonQuery()!=1)throw new ArgumentException("Game account not found.");
        }
    }

    public void SetPairCoexistence(long first,long second,CoexistencePolicy policy)
    {
        if(first==second)throw new ArgumentException("Choose two different game accounts.");
        var a=Math.Min(first,second);var b=Math.Max(first,second);
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="""
                INSERT INTO account_coexistence_overrides(account_a,account_b,policy) VALUES($a,$b,$policy)
                ON CONFLICT(account_a,account_b) DO UPDATE SET policy=excluded.policy
                """;
            command.Parameters.AddWithValue("$a",a);command.Parameters.AddWithValue("$b",b);
            command.Parameters.AddWithValue("$policy",policy.ToString());command.ExecuteNonQuery();
        }
    }

    public CoexistencePolicy PairCoexistence(long first,long second)
    {
        if(first==second)throw new ArgumentException("Choose two different game accounts.");
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="SELECT policy FROM account_coexistence_overrides WHERE account_a=$a AND account_b=$b";
            command.Parameters.AddWithValue("$a",Math.Min(first,second));
            command.Parameters.AddWithValue("$b",Math.Max(first,second));
            return command.ExecuteScalar() is string policy?Enum.Parse<CoexistencePolicy>(policy):CoexistencePolicy.Unknown;
        }
    }

    public CoexistencePolicy CanCoexist(string first,string second)
    {
        if(string.Equals(first,second,StringComparison.OrdinalIgnoreCase))return CoexistencePolicy.No;
        lock(_gate)
        {
            var a=AccountFor(first);var b=AccountFor(second);
            if(a==null||b==null)return CoexistencePolicy.Unknown;
            if(a.Id==b.Id)return a.SameAccountCoexistence;
            return PairCoexistence(a.Id,b.Id);
        }
    }

    public StorageRole AddStorageRole(string character,string label,int priority=0)
    {
        label=label.Trim();
        if(label.Length is <1 or >64)throw new ArgumentException("Enter a storage role label between 1 and 64 characters.");
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="INSERT INTO storage_roles(character,label,priority) VALUES($character,$label,$priority) RETURNING id";
            command.Parameters.AddWithValue("$character",character);command.Parameters.AddWithValue("$label",label);
            command.Parameters.AddWithValue("$priority",priority);
            return new((long)command.ExecuteScalar()!,character,label,priority,true);
        }
    }

    public IReadOnlyList<StorageRole> StorageRoles(string character)
    {
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="SELECT id,character,label,priority,enabled FROM storage_roles WHERE character=$character ORDER BY priority DESC,label COLLATE NOCASE";
            command.Parameters.AddWithValue("$character",character);
            using var reader=command.ExecuteReader();var result=new List<StorageRole>();
            while(reader.Read())result.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetInt32(3),reader.GetInt32(4)==1));
            return result;
        }
    }

    public void SetStorageRoleEnabled(long roleId,bool enabled)
    {
        lock(_gate)Execute("UPDATE storage_roles SET enabled=$enabled WHERE id=$id",("$enabled",enabled?1:0),("$id",roleId));
    }

    public void RemoveStorageRole(long roleId)
    {
        lock(_gate)Execute("DELETE FROM storage_roles WHERE id=$id",("$id",roleId));
    }

    public StorageRoleRule AddStorageRule(long roleId,StorageMatchKind matchKind,string matchValue,long minimumToKeep=0)
    {
        matchValue=matchValue.Trim();
        if(minimumToKeep<0||matchKind==StorageMatchKind.All&&matchValue.Length>0||matchKind!=StorageMatchKind.All&&matchValue.Length==0)
            throw new ArgumentException("Choose a valid match and nonnegative keep quantity.");
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="""
                INSERT INTO storage_role_rules(role_id,match_kind,match_value,minimum_to_keep)
                VALUES($role,$kind,$value,$keep) RETURNING id
                """;
            command.Parameters.AddWithValue("$role",roleId);command.Parameters.AddWithValue("$kind",matchKind.ToString());
            command.Parameters.AddWithValue("$value",matchValue);command.Parameters.AddWithValue("$keep",minimumToKeep);
            return new((long)command.ExecuteScalar()!,roleId,matchKind,matchValue,minimumToKeep);
        }
    }

    public IReadOnlyList<StorageRoleRule> StorageRules(long roleId)
    {
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="SELECT id,role_id,match_kind,match_value,minimum_to_keep FROM storage_role_rules WHERE role_id=$role ORDER BY id";
            command.Parameters.AddWithValue("$role",roleId);
            using var reader=command.ExecuteReader();var result=new List<StorageRoleRule>();
            while(reader.Read())result.Add(new(reader.GetInt64(0),reader.GetInt64(1),Enum.Parse<StorageMatchKind>(reader.GetString(2)),reader.GetString(3),reader.GetInt64(4)));
            return result;
        }
    }

    public void RemoveStorageRule(long ruleId)
    {
        lock(_gate)Execute("DELETE FROM storage_role_rules WHERE id=$id",("$id",ruleId));
    }

    public void SetMiddlemanCapable(string character,bool enabled)
    {
        lock(_gate)Execute("""
            INSERT INTO character_transfer_settings(character,middleman_capable) VALUES($character,$enabled)
            ON CONFLICT(character) DO UPDATE SET middleman_capable=excluded.middleman_capable
            """,("$character",character),("$enabled",enabled?1:0));
    }

    public bool MiddlemanCapable(string character)
    {
        lock(_gate)
        {
            using var command=_db.CreateCommand();
            command.CommandText="SELECT middleman_capable FROM character_transfer_settings WHERE character=$character";
            command.Parameters.AddWithValue("$character",character);
            return command.ExecuteScalar() is long enabled&&enabled==1;
        }
    }
}
