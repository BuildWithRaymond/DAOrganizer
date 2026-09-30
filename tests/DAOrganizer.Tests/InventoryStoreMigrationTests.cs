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
    public void ExistingProfileKeepsSnapshotsAndSettings()
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
                Assert.Equal(5L,(long)query.ExecuteScalar()!);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach(var suffix in new[]{"","-wal","-shm"})File.Delete(path+suffix);
        }
    }

    [Fact]
    public void VersionTwoProfileGetsBackupAndKeepsAccountData()
    {
        var path=Path.Combine(Path.GetTempPath(),"DAOrganizer-migration-"+Guid.NewGuid()+".db");
        try
        {
            using(var store=new InventoryStore(path))
            {
                store.SaveSnapshot("Alpha","Bank",[new Item(1,"Chest",2)],true);
                var account=store.CreateGameAccount("Primary");store.AssignCharacter("Alpha",account.Id);
            }
            using(var old=new SqliteConnection($"Data Source={path}"))
            {
                old.Open();using var command=old.CreateCommand();
                command.CommandText="DROP TABLE transfer_events; DROP TABLE transfer_runs; DROP TABLE plan_steps; DROP TABLE organization_plans; PRAGMA user_version=2";
                command.ExecuteNonQuery();
            }
            using(var upgraded=new InventoryStore(path))
            {
                Assert.Equal(2,Assert.Single(upgraded.Items("Alpha","Bank")).Quantity);
                Assert.Equal("Primary",upgraded.AccountFor("Alpha")?.Label);
            }
            using(var backup=new SqliteConnection($"Data Source={path}.v2.backup"))
            {
                backup.Open();using var query=backup.CreateCommand();query.CommandText="PRAGMA user_version";
                Assert.Equal(2L,(long)query.ExecuteScalar()!);
                query.CommandText="SELECT count(*) FROM items";Assert.Equal(1L,(long)query.ExecuteScalar()!);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach(var suffix in new[]{"","-wal","-shm",".v2.backup"})File.Delete(path+suffix);
        }
    }

    [Fact]
    public void VersionThreeProfileGetsBackupBeforeTransferJournalUpgrade()
    {
        var path=Path.Combine(Path.GetTempPath(),"DAOrganizer-migration-"+Guid.NewGuid()+".db");
        try
        {
            using(var store=new InventoryStore(path))
                store.SaveSnapshot("Alpha","Bank",[new Item(1,"Chest",2)],true);
            using(var old=new SqliteConnection($"Data Source={path}"))
            {
                old.Open();using var command=old.CreateCommand();
                command.CommandText="DROP TABLE transfer_events; DROP TABLE transfer_runs; PRAGMA user_version=3";
                command.ExecuteNonQuery();
            }
            using(var upgraded=new InventoryStore(path))
                Assert.Equal(2,Assert.Single(upgraded.Items("Alpha","Bank")).Quantity);
            using(var backup=new SqliteConnection($"Data Source={path}.v3.backup"))
            {
                backup.Open();using var query=backup.CreateCommand();query.CommandText="PRAGMA user_version";
                Assert.Equal(3L,(long)query.ExecuteScalar()!);
                query.CommandText="SELECT count(*) FROM items";Assert.Equal(1L,(long)query.ExecuteScalar()!);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach(var suffix in new[]{"","-wal","-shm",".v3.backup"})File.Delete(path+suffix);
        }
    }

    [Fact]
    public void VersionFourProfileBacksUpBeforeSourceReservationUpgrade()
    {
        var path=Path.Combine(Path.GetTempPath(),"DAOrganizer-migration-"+Guid.NewGuid()+".db");
        try
        {
            using(var store=new InventoryStore(path))store.SaveSnapshot("Alpha","Bank",[new Item(1,"Chest",2)],true);
            using(var old=new SqliteConnection($"Data Source={path}"))
            {
                old.Open();using var command=old.CreateCommand();
                command.CommandText="""
                    DROP INDEX active_transfer_source;
                    CREATE UNIQUE INDEX active_transfer_source ON transfer_runs(source_character)
                        WHERE state NOT IN ('Complete','Failed');
                    PRAGMA user_version=4;
                    """;
                command.ExecuteNonQuery();
            }
            using(var upgraded=new InventoryStore(path))
                Assert.Equal(2,Assert.Single(upgraded.Items("Alpha","Bank")).Quantity);
            using(var db=new SqliteConnection($"Data Source={path}"))
            {
                db.Open();using var query=db.CreateCommand();query.CommandText="PRAGMA user_version";
                Assert.Equal(5L,(long)query.ExecuteScalar()!);
                query.CommandText="SELECT sql FROM sqlite_master WHERE type='index' AND name='active_transfer_source'";
                Assert.Contains("last_verified_holder=source_character",(string)query.ExecuteScalar()!);
            }
            using(var backup=new SqliteConnection($"Data Source={path}.v4.backup"))
            {
                backup.Open();using var query=backup.CreateCommand();query.CommandText="PRAGMA user_version";
                Assert.Equal(4L,(long)query.ExecuteScalar()!);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach(var suffix in new[]{"","-wal","-shm",".v4.backup"})File.Delete(path+suffix);
        }
    }

    [Fact]
    public void FailedVersionTwoMigrationLeavesVersionAndDataRecoverable()
    {
        var path=Path.Combine(Path.GetTempPath(),"DAOrganizer-migration-"+Guid.NewGuid()+".db");
        try
        {
            using(var store=new InventoryStore(path))
                store.SaveSnapshot("Alpha","Bank",[new Item(1,"Chest",2)],true);
            using(var old=new SqliteConnection($"Data Source={path}"))
            {
                old.Open();using var command=old.CreateCommand();
                command.CommandText="DROP TABLE transfer_events; DROP TABLE transfer_runs; DROP TABLE plan_steps; DROP TABLE organization_plans; CREATE TABLE plan_steps(broken TEXT); PRAGMA user_version=2";
                command.ExecuteNonQuery();
            }
            Assert.Throws<SqliteException>(()=>{using var ignored=new InventoryStore(path);});
            using(var check=new SqliteConnection($"Data Source={path}"))
            {
                check.Open();using var query=check.CreateCommand();query.CommandText="PRAGMA user_version";
                Assert.Equal(2L,(long)query.ExecuteScalar()!);
                query.CommandText="SELECT count(*) FROM items";Assert.Equal(1L,(long)query.ExecuteScalar()!);
                query.CommandText="SELECT count(*) FROM sqlite_master WHERE name='organization_plans'";
                Assert.Equal(0L,(long)query.ExecuteScalar()!);
                query.CommandText="DROP TABLE plan_steps";query.ExecuteNonQuery();
            }
            using var repaired=new InventoryStore(path);
            Assert.Equal(2,Assert.Single(repaired.Items("Alpha","Bank")).Quantity);
            Assert.True(File.Exists(path+".v2.backup"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach(var suffix in new[]{"","-wal","-shm",".v2.backup"})File.Delete(path+suffix);
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
