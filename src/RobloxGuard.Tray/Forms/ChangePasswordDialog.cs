namespace RobloxGuard.Tray.Forms;

/// <summary>현재/새/새확인 입력. 새 비밀번호 일치와 4자 이상을 확인한 뒤 반환.</summary>
internal sealed class ChangePasswordDialog : Form
{
    private readonly TextBox _old = NewBox();
    private readonly TextBox _new = NewBox();
    private readonly TextBox _confirm = NewBox();

    private ChangePasswordDialog()
    {
        Text = Strings.ChangePwTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(360, 230);
        Font = new Font("Malgun Gothic", 10f);

        AddRow(Strings.ChangePwOld, _old, 14);
        AddRow(Strings.ChangePwNew, _new, 76);
        AddRow(Strings.ChangePwConfirm, _confirm, 138);

        var ok = new Button { Text = Strings.Ok, DialogResult = DialogResult.None, Location = new Point(168, 188), Width = 84 };
        var cancel = new Button { Text = Strings.Cancel, DialogResult = DialogResult.Cancel, Location = new Point(260, 188), Width = 84 };
        ok.Click += OnOk;
        Controls.AddRange(new Control[] { ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void AddRow(string label, TextBox box, int y)
    {
        Controls.Add(new Label { Text = label, Location = new Point(16, y), AutoSize = true });
        box.Location = new Point(16, y + 22);
        box.Width = 328;
        Controls.Add(box);
    }

    private void OnOk(object? sender, EventArgs e)
    {
        if (_new.Text.Length < 4) { MessageBox.Show(this, Strings.ChangePwTooShort, Strings.AppName); return; }
        if (_new.Text != _confirm.Text) { MessageBox.Show(this, Strings.ChangePwMismatch, Strings.AppName); return; }
        DialogResult = DialogResult.OK;
        Close();
    }

    private static TextBox NewBox() => new() { UseSystemPasswordChar = true };

    public static (string Old, string New)? Ask(IWin32Window? owner)
    {
        using var dlg = new ChangePasswordDialog();
        var result = owner is null ? dlg.ShowDialog() : dlg.ShowDialog(owner);
        return result == DialogResult.OK ? (dlg._old.Text, dlg._new.Text) : null;
    }
}
