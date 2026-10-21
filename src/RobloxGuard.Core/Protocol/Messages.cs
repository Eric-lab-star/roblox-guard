using RobloxGuard.Core.Logging;

namespace RobloxGuard.Core.Protocol;

public static class RequestTypes
{
    public const string GetStatus = "GetStatus";
    public const string Allow = "Allow";
    public const string Block = "Block";
    public const string GetLog = "GetLog";
    public const string ChangePassword = "ChangePassword";
    public const string Subscribe = "Subscribe";
}

public static class ErrorCodes
{
    public const string WrongPassword = "wrong_password";
    public const string Locked = "locked";
    public const string BadRequest = "bad_request";
    public const string NotConfigured = "not_configured";
}

public static class EventNames
{
    public const string ModeChanged = "mode_changed";
    public const string Expiring = "expiring";
    public const string Blocked = "blocked";
}

public sealed class PipeRequest
{
    public string Type { get; set; } = "";
    public string? Password { get; set; }
    public int? Minutes { get; set; }
    public int? Days { get; set; }
    public string? OldPassword { get; set; }
    public string? NewPassword { get; set; }
}

public sealed class PipeResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? Mode { get; set; }
    public DateTimeOffset? AllowUntil { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public List<AttemptEntry>? Entries { get; set; }

    public static PipeResponse Success() => new() { Ok = true };
    public static PipeResponse Fail(string error) => new() { Ok = false, Error = error };
}

public sealed class PipeEvent
{
    public string Event { get; set; } = "";
    public string? Mode { get; set; }
    public DateTimeOffset? AllowUntil { get; set; }
    public int? Minutes { get; set; }
    public string? Process { get; set; }
}
