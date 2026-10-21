using RobloxGuard.Core.Logging;

namespace RobloxGuard.Tray.Forms;

/// <summary>시도 기록을 날짜별 트리로 보여준다. 기본 7일, 토글로 30일.</summary>
internal sealed class LogViewerForm : Form
{
    // 아이 입장에서 의미 있는 사건만 보여준다. blocked_kill 은 잠금 시작과 중복이라 숨긴다.
    private static readonly HashSet<string> VisibleTypes = new()
    {
        AttemptTypes.LockoutStart, AttemptTypes.WrongPassword, AttemptTypes.PasswordLockout,
        AttemptTypes.AllowStart, AttemptTypes.AllowEnd, AttemptTypes.BlockManual,
    };

    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, Font = new Font("Malgun Gothic", 10f) };
    private readonly CheckBox _show30 = new() { Text = Strings.LogShow30, Dock = DockStyle.Fill };
    private readonly Label _empty = new() { Text = Strings.LogEmpty, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Visible = false, Font = new Font("Malgun Gothic", 11f), ForeColor = Color.Gray };
    private readonly Func<int, Task<IReadOnlyList<AttemptEntry>?>> _reload;

    public LogViewerForm(IReadOnlyList<AttemptEntry> entries, Func<int, Task<IReadOnlyList<AttemptEntry>?>> reload)
    {
        _reload = reload;
        Text = Strings.LogTitle;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 520);
        Font = new Font("Malgun Gothic", 10f);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _show30.CheckedChanged += async (_, _) => await ReloadAsync(_show30.Checked ? 30 : 7);
        layout.Controls.Add(_show30, 0, 0);
        var host = new Panel { Dock = DockStyle.Fill };
        host.Controls.Add(_tree);
        host.Controls.Add(_empty);
        layout.Controls.Add(host, 0, 1);
        Controls.Add(layout);

        Populate(entries);
    }

    private async Task ReloadAsync(int days)
    {
        var entries = await _reload(days);
        if (entries is not null) Populate(entries);
    }

    private void Populate(IReadOnlyList<AttemptEntry> entries)
    {
        _tree.Nodes.Clear();
        var days = LogSummary.GroupByDay(entries, TimeZoneInfo.Local);
        var any = false;
        foreach (var day in days)
        {
            var dayNode = new TreeNode(string.Format(Strings.LogDayFmt, day.Day.ToDateTime(TimeOnly.MinValue), day.AttemptCount));
            foreach (var e in day.Entries.Where(e => VisibleTypes.Contains(e.Type)).OrderByDescending(e => e.Ts))
            {
                var local = TimeZoneInfo.ConvertTime(e.Ts, TimeZoneInfo.Local);
                dayNode.Nodes.Add(new TreeNode($"{local:HH:mm}  {Describe(e)}"));
                any = true;
            }
            if (dayNode.Nodes.Count > 0) _tree.Nodes.Add(dayNode);
        }
        _tree.ExpandAll();
        _empty.Visible = !any;
        _tree.Visible = any;
    }

    private static string Describe(AttemptEntry e) => e.Type switch
    {
        AttemptTypes.LockoutStart => $"로블록스 차단 ({e.Process}){Session(e)}",
        AttemptTypes.WrongPassword => $"비밀번호 틀림{Session(e)}",
        AttemptTypes.PasswordLockout => $"비밀번호 잠금{Session(e)}",
        AttemptTypes.AllowStart => $"허용 시작 ({e.Detail}분)",
        AttemptTypes.AllowEnd => "허용 종료",
        AttemptTypes.BlockManual => "수동 차단",
        _ => e.Type,
    };

    private static string Session(AttemptEntry e) => string.IsNullOrEmpty(e.Session) ? "" : $"  [{e.Session}]";
}
