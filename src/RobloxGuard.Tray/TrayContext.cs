using RobloxGuard.Core.Protocol;
using RobloxGuard.Tray.Forms;

namespace RobloxGuard.Tray;

/// <summary>트레이 아이콘과 메뉴. 상태에 따라 아이콘·툴팁·메뉴를 갱신한다.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly ServiceConnection _conn;
    private readonly Icon _locked = IconFactory.Locked();
    private readonly Icon _unlocked = IconFactory.Unlocked();
    private readonly Icon _disconnected = IconFactory.Disconnected();

    private bool _connected;
    private string _mode = "Blocked";
    private DateTimeOffset? _allowUntil;

    public TrayContext()
    {
        _icon = new NotifyIcon
        {
            Icon = _disconnected,
            Text = Strings.StatusDisconnected,
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();

        _conn = new ServiceConnection(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
        _conn.StatusUpdated += OnStatus;
        _conn.EventReceived += OnEvent;
        _conn.Start();
    }

    private void OnStatus(PipeResponse? res)
    {
        _connected = res is not null;
        if (res is not null)
        {
            _mode = res.Mode ?? "Blocked";
            _allowUntil = res.AllowUntil;
        }
        UpdateIcon();
    }

    private void OnEvent(PipeEvent ev)
    {
        switch (ev.Event)
        {
            case EventNames.Expiring when ev.Minutes is { } m:
                _icon.ShowBalloonTip(5000, Strings.AppName, string.Format(Strings.ExpiringFmt, m), ToolTipIcon.Info);
                break;
            case EventNames.ModeChanged:
                _mode = ev.Mode ?? _mode;
                _allowUntil = ev.AllowUntil;
                UpdateIcon();
                break;
        }
    }

    private int MinutesLeft() => _allowUntil is { } u
        ? Math.Max(0, (int)Math.Ceiling((u - DateTimeOffset.UtcNow).TotalMinutes))
        : 0;

    private void UpdateIcon()
    {
        if (!_connected)
        {
            _icon.Icon = _disconnected;
            _icon.Text = Strings.StatusDisconnected;
            return;
        }
        if (_mode == "Allowed")
        {
            _icon.Icon = _unlocked;
            _icon.Text = string.Format(Strings.StatusAllowedFmt, MinutesLeft());
        }
        else
        {
            _icon.Icon = _locked;
            _icon.Text = Strings.StatusBlocked;
        }
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();

        var status = !_connected ? Strings.StatusDisconnected
            : _mode == "Allowed" ? string.Format(Strings.StatusAllowedFmt, MinutesLeft())
            : Strings.StatusBlocked;
        menu.Items.Add(new ToolStripMenuItem(status) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(Strings.MenuAllow, null, (_, _) => DoAllow());
        if (_mode == "Allowed")
            menu.Items.Add(Strings.MenuBlockNow, null, async (_, _) => await _conn.SendAsync(new PipeRequest { Type = RequestTypes.Block }));
        menu.Items.Add(Strings.MenuLog, null, (_, _) => DoLog());
        menu.Items.Add(Strings.MenuChangePassword, null, (_, _) => DoChangePassword());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.MenuAbout, null, (_, _) => MessageBox.Show(Strings.About, Strings.AppName));
        // 종료 메뉴는 두지 않는다.
    }

    private async void DoAllow()
    {
        var pw = PasswordDialog.Ask(null, Strings.AppName);
        if (pw is null) return;
        var minutes = AllowDialog.AskMinutes(null);
        if (minutes is null) return;
        var res = await _conn.SendAsync(new PipeRequest { Type = RequestTypes.Allow, Password = pw, Minutes = minutes });
        if (!res.Ok) ShowError(res);
    }

    private async void DoLog()
    {
        var pw = PasswordDialog.Ask(null, Strings.AppName);
        if (pw is null) return;
        var res = await _conn.SendAsync(new PipeRequest { Type = RequestTypes.GetLog, Password = pw, Days = 7 });
        if (!res.Ok) { ShowError(res); return; }

        using var form = new LogViewerForm(res.Entries ?? new(), async days =>
        {
            var r = await _conn.SendAsync(new PipeRequest { Type = RequestTypes.GetLog, Password = pw, Days = days });
            return r.Ok ? r.Entries : null;
        });
        form.ShowDialog();
    }

    private async void DoChangePassword()
    {
        var input = ChangePasswordDialog.Ask(null);
        if (input is null) return;
        var res = await _conn.SendAsync(new PipeRequest
        {
            Type = RequestTypes.ChangePassword,
            OldPassword = input.Value.Old,
            NewPassword = input.Value.New,
        });
        if (res.Ok) MessageBox.Show(Strings.ChangePwOk, Strings.AppName);
        else ShowError(res);
    }

    private static void ShowError(PipeResponse res)
    {
        var msg = res.Error switch
        {
            ErrorCodes.WrongPassword => Strings.ErrWrongPassword,
            ErrorCodes.NotConfigured => Strings.ErrNotConfigured,
            ErrorCodes.Locked => string.Format(Strings.ErrLockedFmt, LockMinutes(res)),
            "disconnected" => Strings.ErrDisconnected,
            _ => Strings.ErrGeneric,
        };
        MessageBox.Show(msg, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static int LockMinutes(PipeResponse res) => res.LockedUntil is { } u
        ? Math.Max(1, (int)Math.Ceiling((u - DateTimeOffset.UtcNow).TotalMinutes))
        : 5;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _conn.Dispose();
            _locked.Dispose();
            _unlocked.Dispose();
            _disconnected.Dispose();
        }
        base.Dispose(disposing);
    }
}
