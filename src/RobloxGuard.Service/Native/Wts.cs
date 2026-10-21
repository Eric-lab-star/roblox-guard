using System.Runtime.InteropServices;

namespace RobloxGuard.Service.Native;

/// <summary>터미널 서비스(세션) 조회 P/Invoke.</summary>
public static class Wts
{
    private static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;
    private const int WTSUserName = 5;

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    public static string? GetSessionUserName(int sessionId)
    {
        if (!WTSQuerySessionInformation(WTS_CURRENT_SERVER_HANDLE, sessionId, WTSUserName, out var buf, out var len))
            return null;
        try
        {
            var s = len > 2 ? Marshal.PtrToStringUni(buf) : null;
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        finally
        {
            WTSFreeMemory(buf);
        }
    }
}
