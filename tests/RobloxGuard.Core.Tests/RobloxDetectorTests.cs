using RobloxGuard.Core.Detection;

namespace RobloxGuard.Core.Tests;

public class RobloxDetectorTests
{
    private static ProcessInfo P(string name, string? path = null, string? company = null)
        => new(1234, name, path, company, 2);

    [Theory]
    [InlineData("RobloxPlayerBeta")]
    [InlineData("robloxplayerbeta")]
    [InlineData("RobloxPlayerBeta.exe")]
    [InlineData("RobloxStudioBeta")]
    [InlineData("RobloxPlayerLauncher")]
    [InlineData("RobloxStudioInstaller")]
    public void KnownName_IsRoblox(string name) => Assert.True(RobloxDetector.IsRoblox(P(name)));

    [Fact]
    public void RenamedExe_WithRobloxCompany_IsRoblox()
        => Assert.True(RobloxDetector.IsRoblox(P("game", @"C:\Users\kid\AppData\Local\Roblox\Versions\v1\game.exe", "Roblox Corporation")));

    [Fact]
    public void StorePath_IsRoblox()
        => Assert.True(RobloxDetector.IsRoblox(P("Windows10Universal",
            @"C:\Program Files\WindowsApps\ROBLOXCORPORATION.ROBLOX_2.0_x64__55nm5eh3cm0pr\Windows10Universal.exe")));

    [Fact]
    public void UnrelatedNameContainingRoblox_IsNot()
        => Assert.False(RobloxDetector.IsRoblox(P("roblox_notes", @"C:\Tools\roblox_notes.exe", "Acme")));

    [Fact]
    public void SelfProcesses_AreNeverRoblox()
        => Assert.False(RobloxDetector.IsRoblox(P("RobloxGuard.Tray", @"C:\Program Files\RobloxGuard\RobloxGuard.Tray.exe", "Roblox Corporation")));

    [Fact]
    public void ExtraName_IsRoblox()
        => Assert.True(RobloxDetector.IsRoblox(P("Bloxstrap"), new[] { "Bloxstrap" }));

    [Fact]
    public void NullEverything_IsNot() => Assert.False(RobloxDetector.IsRoblox(P("")));
}
