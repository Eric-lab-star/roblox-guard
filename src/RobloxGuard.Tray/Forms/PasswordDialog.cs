using System.Security.Principal;

namespace RobloxGuard.Tray.Forms;

/// <summary>부모 비밀번호 입력. 현재 세션이 관리자가 아니면 가로채기 경고를 강조한다.</summary>
internal sealed class PasswordDialog : Form
{
    private readonly TextBox _box;

    private PasswordDialog(string title)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(360, 170);
        Font = new Font("Malgun Gothic", 10f);

        var prompt = new Label { Text = Strings.PasswordPrompt, Location = new Point(16, 14), AutoSize = true };
        _box = new TextBox { UseSystemPasswordChar = true, Location = new Point(16, 40), Width = 328 };

        var isAdmin = IsCurrentUserAdmin();
        var hint = new Label
        {
            Text = isAdmin ? Strings.PasswordHint : Strings.PasswordHintStrong,
            ForeColor = isAdmin ? Color.Gray : Color.FromArgb(180, 60, 60),
            Location = new Point(16, 72),
            Size = new Size(328, 36),
        };

        var ok = new Button { Text = Strings.Ok, DialogResult = DialogResult.OK, Location = new Point(168, 124), Width = 84 };
        var cancel = new Button { Text = Strings.Cancel, DialogResult = DialogResult.Cancel, Location = new Point(260, 124), Width = 84 };

        Controls.AddRange(new Control[] { prompt, _box, hint, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public static string? Ask(IWin32Window? owner, string title)
    {
        using var dlg = new PasswordDialog(title);
        var result = owner is null ? dlg.ShowDialog() : dlg.ShowDialog(owner);
        return result == DialogResult.OK && dlg._box.Text.Length > 0 ? dlg._box.Text : null;
    }

    private static bool IsCurrentUserAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception) { return false; }
    }
}
