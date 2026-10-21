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
