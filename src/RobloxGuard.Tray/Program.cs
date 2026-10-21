namespace RobloxGuard.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 세션당 하나만. Local\ 접두사라 세션(로그인 사용자)별로 독립적이다.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\RobloxGuard.Tray", out var isNew);
        if (!isNew) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
