namespace RobloxGuard.Lockout;

internal static class Program
{
    private const int DefaultSeconds = 15;
    private const int MaxSeconds = 60;

    [STAThread]
    private static int Main(string[] args)
    {
        // 미리보기 모드: 입력을 잠그지 않고 창만 띄워 문구를 확인한다(부모가 모양만 보는 용도).
        // --shot <경로> 를 주면 창을 PNG 로 저장한다. 운영 모드(기본)는 영향받지 않는다.
        var preview = HasFlag(args, "--preview");
        var shot = ParseValue(args, "--shot");
        var seconds = Math.Clamp(ParseSeconds(args, preview ? 5 : DefaultSeconds), 1, MaxSeconds);

        // 안전장치: 창 로직이 어떤 이유로든 멈춰도 반드시 프로세스가 끝나도록 (seconds+3)초 뒤 강제 종료.
        var bail = new System.Threading.Timer(_ => Environment.Exit(0), null,
            TimeSpan.FromSeconds(seconds + 3), System.Threading.Timeout.InfiniteTimeSpan);

        ApplicationConfiguration.Initialize();
        using var controller = new LockoutController(seconds, preview, shot);
        controller.Run();

        bail.Dispose();
        return 0;
    }

    private static int ParseSeconds(string[] args, int fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--seconds", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var v))
                return v;
        return fallback;
    }

    private static bool HasFlag(string[] args, string flag)
        => Array.Exists(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static string? ParseValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
