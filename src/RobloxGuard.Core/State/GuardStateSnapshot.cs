namespace RobloxGuard.Core.State;

/// <summary>state.json 의 모양.</summary>
public sealed record GuardStateSnapshot(GuardMode Mode, DateTimeOffset? AllowUntil, int FailedAttempts, DateTimeOffset? LockedUntil)
{
    public static GuardStateSnapshot Default => new(GuardMode.Blocked, null, 0, null);
}
