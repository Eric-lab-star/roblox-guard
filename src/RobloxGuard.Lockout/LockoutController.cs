namespace RobloxGuard.Lockout;

/// <summary>
/// 모든 모니터에 잠금 창을 띄우고, 1초마다 카운트다운을 갱신하며, 끝나면 입력을 풀고 종료한다.
/// </summary>
internal sealed class LockoutController : IDisposable
{
    private readonly int _seconds;
    private readonly List<LockoutForm> _forms = new();
    private InputBlocker? _blocker;
    private System.Windows.Forms.Timer? _timer;
    private int _remaining;

    public LockoutController(int seconds)
    {
        _seconds = seconds;
        _remaining = seconds;
    }

    public void Run()
    {
        var screens = Screen.AllScreens;
        var primaryIndex = Array.FindIndex(screens, s => s.Primary);
        if (primaryIndex < 0) primaryIndex = 0;

        for (var i = 0; i < screens.Length; i++)
            _forms.Add(new LockoutForm(screens[i], _seconds, primary: i == primaryIndex));

        // 입력 차단은 창이 뜬 다음에 건다.
        _forms[0].Shown += (_, _) => _blocker = new InputBlocker();

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTick;
        _timer.Start();

        for (var i = 1; i < _forms.Count; i++) _forms[i].Show();
        Application.Run(_forms[0]);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remaining--;
        if (_remaining <= 0)
        {
            Finish();
            return;
        }
        foreach (var f in _forms) f.SetRemaining(_remaining);
    }

    private void Finish()
    {
        _timer?.Stop();
        _blocker?.Dispose();
        _blocker = null;
        foreach (var f in _forms.ToArray())
        {
            f.AllowClose();
            f.Close();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _blocker?.Dispose();
        foreach (var f in _forms) f.Dispose();
    }
}
