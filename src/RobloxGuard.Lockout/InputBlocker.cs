namespace RobloxGuard.Lockout;

/// <summary>
/// 키보드·마우스 입력을 막는다. BlockInput 이 주 수단, 저수준 훅이 보조 수단.
/// 이 객체를 Dispose 하거나 프로세스가 죽으면 Windows 가 BlockInput 을 자동 해제한다.
/// </summary>
internal sealed class InputBlocker : IDisposable
{
    // GC 가 델리게이트를 수거하지 않도록 필드로 보관
    private readonly NativeMethods.HookProc _keyboardProc;
    private readonly NativeMethods.HookProc _mouseProc;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private bool _blockInputActive;
    private bool _disposed;

    public InputBlocker()
    {
        _keyboardProc = Swallow;
        _mouseProc = Swallow;
        var module = NativeMethods.GetModuleHandle(null);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        _blockInputActive = NativeMethods.BlockInput(true);
    }

    public bool BlockInputActive => _blockInputActive;
    public bool HooksActive => _keyboardHook != IntPtr.Zero && _mouseHook != IntPtr.Zero;

    private IntPtr Swallow(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0) return (IntPtr)1; // 모든 입력 삼킴
        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_keyboardHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        if (_blockInputActive) { NativeMethods.BlockInput(false); _blockInputActive = false; }
    }
}
