using RobloxGuard.Core.Logging;
using RobloxGuard.Core.Protocol;

namespace RobloxGuard.Core.Tests;

public class ProtocolTests
{
    [Fact]
    public void Request_RoundTrip()
    {
        var req = new PipeRequest { Type = RequestTypes.Allow, Password = "p", Minutes = 60 };
        var back = ProtocolJson.Deserialize<PipeRequest>(ProtocolJson.Serialize(req));
        Assert.NotNull(back);
        Assert.Equal("Allow", back!.Type);
        Assert.Equal("p", back.Password);
        Assert.Equal(60, back.Minutes);
        Assert.Null(back.Days);
    }

    [Fact]
    public void Response_RoundTrip_WithEntries()
    {
        var ts = new DateTimeOffset(2026, 10, 22, 1, 2, 3, TimeSpan.Zero);
        var res = PipeResponse.Success();
        res.Mode = "Allowed";
        res.AllowUntil = ts;
        res.Entries = new List<AttemptEntry> { new(ts, AttemptTypes.BlockedKill, "kid", "RobloxPlayerBeta", null, null) };
        var back = ProtocolJson.Deserialize<PipeResponse>(ProtocolJson.Serialize(res));
        Assert.NotNull(back);
        Assert.True(back!.Ok);
        Assert.Equal("Allowed", back.Mode);
        Assert.Equal(ts, back.AllowUntil);
        Assert.Single(back.Entries!);
        Assert.Equal("kid", back.Entries![0].Session);
    }

    [Fact]
    public void Fail_CarriesErrorCode()
    {
        var back = ProtocolJson.Deserialize<PipeResponse>(ProtocolJson.Serialize(PipeResponse.Fail(ErrorCodes.Locked)));
        Assert.False(back!.Ok);
        Assert.Equal("locked", back.Error);
    }

    [Fact]
    public void Event_RoundTrip()
    {
        var ev = new PipeEvent { Event = EventNames.Expiring, Minutes = 5 };
        var back = ProtocolJson.Deserialize<PipeEvent>(ProtocolJson.Serialize(ev));
        Assert.Equal("expiring", back!.Event);
        Assert.Equal(5, back.Minutes);
    }

    [Fact]
    public void Serialize_HasNoNewline()
    {
        var res = PipeResponse.Success();
        res.Entries = new List<AttemptEntry> { new(DateTimeOffset.UtcNow, "x", "a\nb", null, null, null) };
        Assert.DoesNotContain('\n', ProtocolJson.Serialize(res));
    }

    [Fact]
    public void Deserialize_Garbage_ReturnsNull()
    {
        Assert.Null(ProtocolJson.Deserialize<PipeRequest>("{nope"));
        Assert.Null(ProtocolJson.Deserialize<PipeRequest>(""));
    }

    [Fact]
    public void Serialize_UsesCamelCase()
        => Assert.Contains("\"type\":\"Allow\"", ProtocolJson.Serialize(new PipeRequest { Type = RequestTypes.Allow }));
}
