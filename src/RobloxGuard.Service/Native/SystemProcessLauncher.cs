using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxGuard.Service.Native;

/// <summary>
/// 서비스(SYSTEM)의 토큰을 복제해 대상 세션으로 바꾼 뒤, 그 세션의 데스크톱에 프로세스를 띄운다.
/// 아이 권한이 아니라 SYSTEM 권한으로 뜨므로 아이가 작업 관리자로 끝낼 수 없다.
/// </summary>
public static class SystemProcessLauncher
{
    public static Process? Launch(string exePath, string arguments, int sessionId, ILogger? log = null)
    {
        var currentToken = IntPtr.Zero;
        var dupToken = IntPtr.Zero;
        var envBlock = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID, out currentToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken 실패");

            if (!DuplicateTokenEx(currentToken, TOKEN_ALL_ACCESS, IntPtr.Zero, SecurityIdentification, TokenPrimary, out dupToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx 실패");

            var sid = (uint)sessionId;
            if (!SetTokenInformation(dupToken, TokenSessionId, ref sid, sizeof(uint)))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation(SessionId) 실패");

            CreateEnvironmentBlock(out envBlock, dupToken, false); // 실패해도 null 환경으로 진행

            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
            var cmdLine = $"\"{exePath}\" {arguments}";

            var ok = CreateProcessAsUser(dupToken, exePath, cmdLine, IntPtr.Zero, IntPtr.Zero, false,
                CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW, envBlock, Path.GetDirectoryName(exePath),
                ref si, out var pi);
            if (!ok)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser 실패");

            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            try
            {
                var proc = Process.GetProcessById((int)pi.dwProcessId);
                return proc;
            }
            catch (ArgumentException)
            {
                return null; // 이미 끝났으면 null
            }
            finally
            {
                if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            }
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "세션 {Session} 에 {Exe} 실행 실패", sessionId, exePath);
            return null;
        }
        finally
        {
            if (envBlock != IntPtr.Zero) DestroyEnvironmentBlock(envBlock);
            if (dupToken != IntPtr.Zero) CloseHandle(dupToken);
            if (currentToken != IntPtr.Zero) CloseHandle(currentToken);
        }
    }

    // ── 상수 ───────────────────────────────────────────────────────────
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    private const uint TOKEN_ADJUST_SESSIONID = 0x0100;
    private const uint TOKEN_ALL_ACCESS = 0xF01FF;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const int SecurityIdentification = 1;
    private const int TokenPrimary = 1;
    private const int TokenSessionId = 12;

    // ── P/Invoke ───────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr existing, uint desiredAccess, IntPtr attrs, int impersonationLevel, int tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(IntPtr token, int infoClass, ref uint info, int length);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr env);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(IntPtr token, string? appName, string cmdLine, IntPtr procAttrs, IntPtr threadAttrs,
        bool inherit, uint flags, IntPtr env, string? curDir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
