using RobloxGuard.Core.Network;

namespace RobloxGuard.Core.Tests;

public class HostsFileEditorTests
{
    private static readonly string[] Hosts = { "roblox.com", "www.roblox.com" };

    [Fact]
    public void InsertBlock_IntoEmpty_ProducesMarkedBlock()
    {
        var r = HostsFileEditor.InsertBlock("", Hosts);
        var b = r.IndexOf(HostsFileEditor.BeginMarker, StringComparison.Ordinal);
        var h = r.IndexOf("0.0.0.0 roblox.com", StringComparison.Ordinal);
        var e = r.IndexOf(HostsFileEditor.EndMarker, StringComparison.Ordinal);
        Assert.True(b >= 0 && h > b && e > h, r);
    }

    [Fact]
    public void InsertBlock_Twice_IsIdempotent()
    {
        var once = HostsFileEditor.InsertBlock("127.0.0.1 localhost\r\n", Hosts);
        Assert.Equal(once, HostsFileEditor.InsertBlock(once, Hosts));
    }

    [Fact]
    public void RemoveBlock_RestoresOriginalExactly()
    {
        const string original = "127.0.0.1 localhost\r\n";
        Assert.Equal(original, HostsFileEditor.RemoveBlock(HostsFileEditor.InsertBlock(original, Hosts)));
    }

    [Fact]
    public void InsertBlock_NoTrailingNewline_StartsOnNewLine()
    {
        var r = HostsFileEditor.InsertBlock("127.0.0.1 localhost", Hosts);
        Assert.Contains("localhost\r\n# RobloxGuard BEGIN", r);
        Assert.DoesNotContain("localhost# RobloxGuard", r);
    }

    [Fact]
    public void RemoveBlock_NoTrailingNewline_RestoresOriginal()
    {
        const string original = "127.0.0.1 localhost";
        Assert.Equal(original, HostsFileEditor.RemoveBlock(HostsFileEditor.InsertBlock(original, Hosts)));
    }

    [Fact]
    public void MixedLineEndings_RoundTrip()
    {
        const string original = "a\nb\r\nc";
        Assert.Equal(original, HostsFileEditor.RemoveBlock(HostsFileEditor.InsertBlock(original, Hosts)));
    }

    [Fact]
    public void RemoveBlock_WithoutBlock_IsNoop()
    {
        const string original = "127.0.0.1 localhost\r\n# comment\r\n";
        Assert.Equal(original, HostsFileEditor.RemoveBlock(original));
    }

    [Fact]
    public void Apply_WritesAndLeavesNoTempFile()
    {
        var dir = Directory.CreateTempSubdirectory("rg-hosts-");
        try
        {
            var path = Path.Combine(dir.FullName, "hosts");
            File.WriteAllText(path, "127.0.0.1 localhost\r\n");
            HostsFileEditor.Apply(path, true, Hosts);
            Assert.Contains(HostsFileEditor.BeginMarker, File.ReadAllText(path));
            HostsFileEditor.Apply(path, false, Hosts);
            Assert.DoesNotContain(HostsFileEditor.BeginMarker, File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir.FullName));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Apply_OnMissingFile_CreatesIt()
    {
        var dir = Directory.CreateTempSubdirectory("rg-hosts-");
        try
        {
            var path = Path.Combine(dir.FullName, "hosts");
            HostsFileEditor.Apply(path, true, Hosts);
            Assert.True(File.Exists(path));
            Assert.Contains("0.0.0.0 www.roblox.com", File.ReadAllText(path));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void DefaultBlockedHosts_HasExpectedDomains()
    {
        Assert.Equal(10, HostsFileEditor.DefaultBlockedHosts.Length);
        Assert.Contains("roblox.com", HostsFileEditor.DefaultBlockedHosts);
        Assert.Contains("now.gg", HostsFileEditor.DefaultBlockedHosts);
        Assert.Contains("setup.rbxcdn.com", HostsFileEditor.DefaultBlockedHosts);
    }
}
