namespace RobloxGuard.Core;

/// <summary>설치·데이터·파이프 경로 상수.</summary>
public static class GuardPaths
{
    public const string PipeName = "RobloxGuard";
    public const string AppFolderName = "RobloxGuard";

    public static string InstallDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppFolderName);

    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppFolderName);

    public static string ConfigPath => Path.Combine(DataDir, "config.json");
    public static string StatePath => Path.Combine(DataDir, "state.json");
    public static string LogPath => Path.Combine(DataDir, "attempts.log");

    public static string HostsPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
}
