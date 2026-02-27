using LOCKnet.Data;
using LOCKnet.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Tests.Repositories;

/// <summary>
/// Tests for <see cref="SettingsRepository"/> using an in-memory SQLite database.
/// </summary>
public class SettingsRepositoryTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SettingsRepository _sut;

    public SettingsRepositoryTests()
    {
        var dbName = $"settings_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();

        new Database(connectionString, true).Initialize();

        _sut = new SettingsRepository(connectionString);
    }

    public void Dispose() => _keepAlive.Dispose();

    // ── Set / Get ──────────────────────────────────────────────────────────────

    [Fact]
    public void Set_ThenGet_RoundTripsValue()
    {
        _sut.Set("theme", "dark");

        var result = _sut.Get("theme");

        Assert.Equal("dark", result);
    }

    [Fact]
    public void Get_NonExistentKey_ReturnsNull()
    {
        var result = _sut.Get("does_not_exist");

        Assert.Null(result);
    }

    // ── Upsert behaviour ─────────────────────────────────────────────────────

    [Fact]
    public void Set_SameKeyTwice_OverwritesValue()
    {
        _sut.Set("lang", "en");
        _sut.Set("lang", "de");

        var result = _sut.Get("lang");

        Assert.Equal("de", result);
    }

    [Fact]
    public void Set_SameKeyTwice_DoesNotCreateDuplicateRow()
    {
        _sut.Set("key", "v1");
        _sut.Set("key", "v2");

        var all = _sut.GetAll();

        Assert.Single(all);
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_MultipleEntries_ReturnsAll()
    {
        _sut.Set("a", "1");
        _sut.Set("b", "2");
        _sut.Set("c", "3");

        var all = _sut.GetAll();

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void GetAll_EmptyDatabase_ReturnsEmptyDictionary()
    {
        var all = _sut.GetAll();

        Assert.Empty(all);
    }

    [Fact]
    public void GetAll_ContainsCorrectKeyValuePairs()
    {
        _sut.Set("timeout", "60");
        _sut.Set("theme", "light");

        var all = _sut.GetAll();

        Assert.True(all.ContainsKey("timeout"));
        Assert.Equal("60", all["timeout"]);
        Assert.True(all.ContainsKey("theme"));
        Assert.Equal("light", all["theme"]);
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_ExistingKey_GetReturnsNull()
    {
        _sut.Set("delete_me", "value");

        _sut.Remove("delete_me");

        Assert.Null(_sut.Get("delete_me"));
    }

    [Fact]
    public void Remove_NonExistentKey_IsNoOp()
    {
        var ex = Record.Exception(() => _sut.Remove("ghost"));

        Assert.Null(ex);
    }

    [Fact]
    public void Remove_OnlyRemovesTargetKey()
    {
        _sut.Set("keep", "yes");
        _sut.Set("delete", "no");

        _sut.Remove("delete");

        var all = _sut.GetAll();
        Assert.Single(all);
        Assert.True(all.ContainsKey("keep"));
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Set_EmptyStringValue_IsStoredAndRetrieved()
    {
        _sut.Set("empty", string.Empty);

        var result = _sut.Get("empty");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Set_ValueWithSpecialCharacters_RoundTrips()
    {
        const string special = "value with spaces & <symbols> \"quotes\"";
        _sut.Set("special", special);

        var result = _sut.Get("special");

        Assert.Equal(special, result);
    }
}
