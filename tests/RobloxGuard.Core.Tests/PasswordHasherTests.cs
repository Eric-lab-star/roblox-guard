using RobloxGuard.Core.Security;

namespace RobloxGuard.Core.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_Succeeds()
        => Assert.True(PasswordHasher.Verify("abc123", PasswordHasher.Hash("abc123")));

    [Fact]
    public void Verify_WrongPassword_Fails()
        => Assert.False(PasswordHasher.Verify("abc124", PasswordHasher.Hash("abc123")));

    [Fact]
    public void Hash_UsesFreshSaltEachTime()
    {
        var a = PasswordHasher.Hash("x");
        var b = PasswordHasher.Hash("x");
        Assert.NotEqual(a.Salt, b.Salt);
        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void Hash_Parameters()
    {
        var r = PasswordHasher.Hash("x");
        Assert.Equal(100_000, r.Iterations);
        Assert.Equal(16, Convert.FromBase64String(r.Salt).Length);
        Assert.Equal(32, Convert.FromBase64String(r.Hash).Length);
    }
}

public class PasswordHasherHardeningTests
{
    [Fact]
    public void Verify_EmptyHash_Fails()
    {
        var r = PasswordHasher.Hash("x") with { Hash = "" };
        Assert.False(PasswordHasher.Verify("x", r));
    }

    [Fact]
    public void Verify_TruncatedHash_Fails()
    {
        var r = PasswordHasher.Hash("x");
        var truncated = r with { Hash = Convert.ToBase64String(Convert.FromBase64String(r.Hash)[..8]) };
        Assert.False(PasswordHasher.Verify("x", truncated));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99_999)]
    [InlineData(5_000_000)]
    public void Verify_IterationsOutOfRange_Fails(int iterations)
    {
        var r = PasswordHasher.Hash("x") with { Iterations = iterations };
        Assert.False(PasswordHasher.Verify("x", r));
    }

    [Fact]
    public void Verify_InvalidBase64_Fails()
        => Assert.False(PasswordHasher.Verify("x", new PasswordRecord("!!", "!!", 100_000)));
}
