using LOCKnet.Data;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Tests;

public class DatabaseTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString;

    public DatabaseTests()
    {
        var dbName = $"test_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    public void Dispose() => _keepAlive.Dispose();

    // ── NotNull after Initialize ───────────────────────────────────────────────

    [Fact]
    public void Initialize_DatabaseObjectIsNotNull()
    {
        var db = new Database(_connectionString, true);
        db.Initialize();
        Assert.NotNull(db);
    }

    // ── Idempotent (double-call must not throw) ────────────────────────────────

    [Fact]
    public void Initialize_CalledTwice_DoesNotThrow()
    {
        var db = new Database(_connectionString, true);
        db.Initialize();

        var ex = Record.Exception(() => db.Initialize());

        Assert.Null(ex);
    }

    // ── Tables exist after Initialize ─────────────────────────────────────────

    [Fact]
    public void Initialize_CreatesCredentialsTable()
    {
        var db = new Database(_connectionString, true);
        db.Initialize();

        Assert.True(TableExists("Credentials"));
    }

    [Fact]
    public void Initialize_CreatesMasterKeyTable()
    {
        var db = new Database(_connectionString, true);
        db.Initialize();

        Assert.True(TableExists("MasterKey"));
    }

    [Fact]
    public void Initialize_CreatesSettingsTable()
    {
        var db = new Database(_connectionString, true);
        db.Initialize();

        Assert.True(TableExists("Settings"));
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private bool TableExists(string tableName)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt64(cmd.ExecuteScalar()!) > 0;
    }
}
