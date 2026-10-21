using RobloxGuard.Core.State;

namespace RobloxGuard.Tray.Forms;

/// <summary>허용 시간 선택. 30분 / 1시간 / 2시간 / 오늘 하루. 선택한 분을 반환.</summary>
internal sealed class AllowDialog : Form
{
    private int? _minutes;

    private AllowDialog()
    {
        Text = Strings.AllowTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(300, 190);
        Font = new Font("Malgun Gothic", 10f);

        AddButton(Strings.Allow30, 16, 30);
        AddButton(Strings.Allow60, 16, 60);
        AddButton(Strings.Allow120, 16, 120);

        var todayMinutes = (int)Math.Ceiling(AllowDurations.UntilEndOfDay(DateTimeOffset.UtcNow, TimeZoneInfo.Local).TotalMinutes);
        AddButton(Strings.AllowToday, 16, todayMinutes, y: 142);
    }

    private int _nextY = 16;

    private void AddButton(string text, int x, int minutes, int? y = null)
    {
        var by = y ?? _nextY;
        var btn = new Button { Text = text, Location = new Point(x, by), Size = new Size(268, 34) };
        btn.Click += (_, _) => { _minutes = minutes; DialogResult = DialogResult.OK; Close(); };
        Controls.Add(btn);
        _nextY = by + 42;
    }

    public static int? AskMinutes(IWin32Window? owner)
    {
        using var dlg = new AllowDialog();
        var result = owner is null ? dlg.ShowDialog() : dlg.ShowDialog(owner);
        return result == DialogResult.OK ? dlg._minutes : null;
    }
}
