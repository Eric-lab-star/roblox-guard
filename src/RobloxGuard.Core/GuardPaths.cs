namespace RobloxGuard.Core;

/// <summary>설치·데이터·파이프 경로 상수.</summary>
public static class GuardPaths
{
    public const string PipeName = "RobloxGuard";
    public const string AppFolderName = "RobloxGuard";

    public static string InstallDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppFolderName);

    /// <summary>개발·테스트용 오버라이드. 환경변수 ROBLOXGUARD_DATA_DIR 이 있으면 그곳을 쓴다. 설치된 서비스에는 없다.</summary>
    public const string DataDirEnvVar = "ROBLOXGUARD_DATA_DIR";

    public static string DataDir { get; } =
        Environment.GetEnvironmentVariable(DataDirEnvVar) is { Length: > 0 } o
            ? Path.GetFullPath(o)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppFolderName);

    public static string ConfigPath => Path.Combine(DataDir, "config.json");
    public static string StatePath => Path.Combine(DataDir, "state.json");
    public static string LogPath => Path.Combine(DataDir, "attempts.log");

    public static string HostsPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
}
