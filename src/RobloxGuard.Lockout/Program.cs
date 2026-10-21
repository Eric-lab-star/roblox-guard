namespace RobloxGuard.Lockout;

internal static class Program
{
    private const int DefaultSeconds = 15;
    private const int MaxSeconds = 60;

    [STAThread]
    private static int Main(string[] args)
    {
        var seconds = Math.Clamp(ParseSeconds(args), 1, MaxSeconds);

        // 안전장치: 창 로직이 어떤 이유로든 멈춰도 반드시 프로세스가 끝나도록 (seconds+3)초 뒤 강제 종료.
        var bail = new System.Threading.Timer(_ => Environment.Exit(0), null,
            TimeSpan.FromSeconds(seconds + 3), System.Threading.Timeout.InfiniteTimeSpan);

        ApplicationConfiguration.Initialize();
        using var controller = new LockoutController(seconds);
        controller.Run();

        bail.Dispose();
        return 0;
    }

    private static int ParseSeconds(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--seconds", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var v))
                return v;
        return DefaultSeconds;
    }
}
