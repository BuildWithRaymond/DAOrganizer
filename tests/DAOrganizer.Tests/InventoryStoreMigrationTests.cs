using DAOrganizer.Core;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace DAOrganizer.Tests;

public class InventoryStoreMigrationTests
{
    [Fact]
    public void VersionOneProfileGetsRecoverableBackupBeforeUpgrade()
    {
        var path=Path.Combine(Path.GetTempPath(),"DAOrganizer-migration-"+Guid.NewGuid()+".db");
        try
        {
            using(var old=new SqliteConnection($"Data Source={path}"))
            {
                old.Open();using var command=old.CreateCommand();
                command.CommandText="""
                    CREATE TABLE characters(name TEXT PRIMARY KEY COLLATE NOCASE,last_seen TEXT);
                    CREATE TABLE snapshots(character TEXT COLLATE NOCASE,location TEXT,state TEXT,updated TEXT,PRIMARY KEY(character,location));
                    CREATE TABLE items(character TEXT COLLATE NOCASE,location TEXT,slot INTEGER,data TEXT,PRIMARY KEY(character,location,slot));
                    CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT);
                    INSERT INTO characters VALUES('Alpha',NULL);
                    INSERT INTO snapshots VALUES('Alpha','Bank','Current','2026-09-29T00:00:00+00:00');
                    PRAGMA user_version=1;
                    """;command.ExecuteNonQuery();
                command.CommandText="INSERT INTO items VALUES('Alpha','Bank',1,$item)";
                command.Parameters.AddWithValue("$item",JsonSerializer.Serialize(new Item(1,"Emerald",3)));
                command.ExecuteNonQuery();
            }
            using(var upgraded=new InventoryStore(path))
                Assert.Equal(3,Assert.Single(upgraded.Items("Alpha","Bank")).Quantity);
            Assert.True(File.Exists(path+".v1.backup"));
            using(var backup=new SqliteConnection($"Data Source={path}.v1.backup"))
            {
                backup.Open();using var query=backup.CreateCommand();query.CommandText="PRAGMA user_version";
                Assert.Equal(1L,(long)query.ExecuteScalar()!);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach(var suffix in new[]{"","-wal","-shm",".v1.backup"})File.Delete(path+suffix);
        }
    }
    [Fact]
    public void ExistingProfileUpgradesWithoutLosingSnapshotsOrSettings()
    {
        var path=Path.Combine(Path.GetTempPath(),"DAOrganizer-migration-"+Guid.NewGuid()+".db");
        try
        {
            using(var store=new InventoryStore(path))
            {
                store.SaveSnapshot("Alpha","Bank",[new(1,"Emerald",3)],true);
                store.Put("category/emerald","Materials");
            }
            using(var upgraded=new InventoryStore(path))
            {
                Assert.Equal(3,Assert.Single(upgraded.Items("Alpha","Bank")).Quantity);
                Assert.Equal("Materials",upgraded.Get<string>("category/emerald"));
            }
            using(var db=new SqliteConnection($"Data Source={path}"))
            {
                db.Open();using var query=db.CreateCommand();query.CommandText="PRAGMA user_version";
                Assert.Equal(2L,(long)query.ExecuteScalar()!);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach(var suffix in new[]{"","-wal","-shm"})File.Delete(path+suffix);
        }
    }

    [Fact]
    public void NewerDatabaseVersionIsRejectedWithoutDowngradingIt()
    {
        var path=Path.Combine(Path.GetTempPath(),"DAOrganizer-migration-"+Guid.NewGuid()+".db");
        try
        {
            using(var store=new InventoryStore(path))store.SaveSnapshot("Alpha","Bank",[new(1,"Emerald",3)],true);
            using(var db=new SqliteConnection($"Data Source={path}"))
            {
                db.Open();using var set=db.CreateCommand();set.CommandText="PRAGMA user_version=99";set.ExecuteNonQuery();
            }
            var error=Record.Exception(()=>{using var unexpected=new InventoryStore(path);});
            Assert.IsType<InvalidDataException>(error);
            using(var check=new SqliteConnection($"Data Source={path}"))
            {
                check.Open();using var query=check.CreateCommand();query.CommandText="PRAGMA user_version";
                Assert.Equal(99L,(long)query.ExecuteScalar()!);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach(var suffix in new[]{"","-wal","-shm"})File.Delete(path+suffix);
        }
    }
}
