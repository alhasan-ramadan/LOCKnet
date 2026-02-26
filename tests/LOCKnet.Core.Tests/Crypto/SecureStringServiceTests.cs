using System.Security;
using System.Text;
using LOCKnet.Core.Crypto;

namespace LOCKnet.Core.Tests.Crypto;

public class SecureStringServiceTests
{
    private readonly ISecureStringService _sut = new SecureStringService();

    private static SecureString MakeSecure(string value)
    {
        var s = new SecureString();
        foreach (var c in value)
            s.AppendChar(c);
        s.MakeReadOnly();
        return s;
    }

    // ── ToByteArray ───────────────────────────────────────────────────────────

    [Fact]
    public void ToByteArray_ConvertsToUtf8Bytes()
    {
        using var secure = MakeSecure("hello");
        var expected = Encoding.UTF8.GetBytes("hello");

        var result = _sut.ToByteArray(secure);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToByteArray_EmptySecureString_ReturnsEmptyArray()
    {
        using var secure = new SecureString();
        secure.MakeReadOnly();

        var result = _sut.ToByteArray(secure);

        Assert.Empty(result);
    }

    [Fact]
    public void ToByteArray_UnicodePassword_RoundtripsCorrectly()
    {
        const string password = "Pässwört!€";
        using var secure = MakeSecure(password);
        var expected = Encoding.UTF8.GetBytes(password);

        var result = _sut.ToByteArray(secure);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToByteArray_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.ToByteArray(null!));
    }

    // ── FromByteArray ─────────────────────────────────────────────────────────

    [Fact]
    public void FromByteArray_ConvertsFromUtf8Bytes()
    {
        var bytes = Encoding.UTF8.GetBytes("test123");

        using var result = _sut.FromByteArray(bytes);

        Assert.Equal(7, result.Length); // "test123" = 7 chars
    }

    [Fact]
    public void FromByteArray_IsReadOnly()
    {
        var bytes = Encoding.UTF8.GetBytes("locked");

        using var result = _sut.FromByteArray(bytes);

        Assert.True(result.IsReadOnly());
    }

    [Fact]
    public void FromByteArray_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.FromByteArray(null!));
    }

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public void ToByteArray_ThenFromByteArray_PreservesPassword()
    {
        const string original = "CorrectHorseBatteryStaple";
        using var secure = MakeSecure(original);

        var bytes = _sut.ToByteArray(secure);
        using var recovered = _sut.FromByteArray(bytes);

        // Compare round-tripped bytes
        var recoveredBytes = _sut.ToByteArray(recovered);
        Assert.Equal(Encoding.UTF8.GetBytes(original), recoveredBytes);
    }

    // ── ZeroMemory ────────────────────────────────────────────────────────────

    [Fact]
    public void ZeroMemory_ClearsAllBytes()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };

        _sut.ZeroMemory(data);

        Assert.All(data, b => Assert.Equal(0, b));
    }

    [Fact]
    public void ZeroMemory_EmptyArray_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.ZeroMemory([]));
        Assert.Null(ex);
    }

    [Fact]
    public void ZeroMemory_NullArray_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.ZeroMemory(null!));
        Assert.Null(ex);
    }
}
