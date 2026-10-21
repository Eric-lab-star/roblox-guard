namespace RobloxGuard.Lockout;

/// <summary>
/// 모니터 하나를 덮는 검은 전체 화면. primary 창만 문구와 카운트다운을 그린다.
/// </summary>
internal sealed class LockoutForm : Form
{
    private readonly bool _primary;
    private readonly Label? _countdown;
    private bool _closeAllowed;

    public void AllowClose() => _closeAllowed = true;

    public LockoutForm(Screen screen, int seconds, bool primary)
    {
        _primary = primary;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        TopMost = true;
        ShowInTaskbar = false;
        ControlBox = false;
        BackColor = Color.Black;
        Cursor = Cursors.Default;
        KeyPreview = true;

        if (!primary) return;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            ColumnCount = 1,
            RowCount = 3,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 30));

        var title = new Label
        {
            Text = "로블록스 실행이 감지되었습니다",
            ForeColor = Color.White,
            Font = new Font("Malgun Gothic", 24f, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomCenter,
            Dock = DockStyle.Fill,
        };
        _countdown = new Label
        {
            Text = seconds.ToString(),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 72f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
        };
        var hint = new Label
        {
            Text = "부모님의 허용이 필요합니다",
            ForeColor = Color.Gray,
            Font = new Font("Malgun Gothic", 14f),
            TextAlign = ContentAlignment.TopCenter,
            Dock = DockStyle.Fill,
        };
        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(_countdown, 0, 1);
        panel.Controls.Add(hint, 0, 2);
        Controls.Add(panel);
    }

    public void SetRemaining(int seconds)
    {
        if (_countdown is not null) _countdown.Text = Math.Max(0, seconds).ToString();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Cursor.Hide();
        if (_primary) NativeMethods.SetForegroundWindow(Handle);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Cursor.Show();
        base.OnFormClosed(e);
    }

    // Alt+F4 등으로 닫히지 않게
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_closeAllowed) e.Cancel = true;
        base.OnFormClosing(e);
    }

    protected override bool ProcessDialogKey(Keys keyData) => true;
}
