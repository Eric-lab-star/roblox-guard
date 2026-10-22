namespace RobloxGuard.Lockout;

/// <summary>
/// 모든 모니터에 잠금 창을 띄우고, 1초마다 카운트다운을 갱신하며, 끝나면 입력을 풀고 종료한다.
/// </summary>
internal sealed class LockoutController : IDisposable
{
    private readonly int _seconds;
    private readonly bool _preview;
    private readonly string? _shotPath;
    private readonly List<LockoutForm> _forms = new();
    private InputBlocker? _blocker;
    private System.Windows.Forms.Timer? _timer;
    private int _remaining;

    public LockoutController(int seconds, bool preview = false, string? screenshotPath = null)
    {
        _seconds = seconds;
        _remaining = seconds;
        _preview = preview;
        _shotPath = screenshotPath;
    }

    public void Run()
    {
        var screens = Screen.AllScreens;
        var primaryIndex = Array.FindIndex(screens, s => s.Primary);
        if (primaryIndex < 0) primaryIndex = 0;

        for (var i = 0; i < screens.Length; i++)
            _forms.Add(new LockoutForm(screens[i], _seconds, primary: i == primaryIndex, preview: _preview));

        if (!_preview)
            // 입력 차단은 창이 뜬 다음에 건다. (미리보기에서는 걸지 않는다)
            _forms[0].Shown += (_, _) => _blocker = new InputBlocker();
        else if (_shotPath is not null)
            _forms[0].Shown += (_, _) => ScheduleScreenshot(_forms[0]);

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

    // 미리보기: 창이 그려진 뒤 primary 화면을 PNG 로 한 번 저장한다.
    private void ScheduleScreenshot(LockoutForm form)
    {
        var t = new System.Windows.Forms.Timer { Interval = 800 };
        t.Tick += (_, _) =>
        {
            t.Stop();
            t.Dispose();
            try
            {
                // 화면 픽셀(CopyFromScreen)은 포그라운드/z-순서에 좌우된다. 폼 자체를
                // 비트맵으로 렌더링하면 창이 앞에 없어도 내용 그대로 저장된다.
                var size = form.ClientSize;
                using var bmp = new Bitmap(size.Width, size.Height);
                form.DrawToBitmap(bmp, new Rectangle(System.Drawing.Point.Empty, size));
                bmp.Save(_shotPath!, System.Drawing.Imaging.ImageFormat.Png);
            }
            catch { /* 미리보기 캡처 실패는 무시 */ }
        };
        t.Start();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _blocker?.Dispose();
        foreach (var f in _forms) f.Dispose();
    }
}
