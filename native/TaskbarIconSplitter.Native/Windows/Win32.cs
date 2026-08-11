using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarIconSplitter.Native.Windows;

internal static class Win32
{
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const int WmGetIcon = 0x007F;
    internal const int WmSetIcon = 0x0080;
    internal const int IconSmall = 0;
    internal const int IconBig = 1;
    internal const int IconSmall2 = 2;
    internal const uint SmtoAbortIfHung = 0x0002;
    internal const int SmCxIcon = 11;
    internal const int SmCyIcon = 12;
    internal const int SmCxSmallIcon = 49;
    internal const int SmCySmallIcon = 50;

    internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(
        IntPtr hwnd,
        StringBuilder text,
        int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(
        IntPtr hwnd,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(
        IntPtr hwnd,
        out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(
        IntPtr process,
        uint flags,
        StringBuilder executableName,
        ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessage(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetricsForDpi(int index, uint dpi);

    internal static bool TrySendWindowIcon(
        IntPtr hwnd,
        int kind,
        IntPtr icon,
        out IntPtr previous)
    {
        var returned = SendMessageTimeout(
            hwnd,
            WmSetIcon,
            (IntPtr)kind,
            icon,
            SmtoAbortIfHung,
            1000,
            out previous);
        return returned != IntPtr.Zero;
    }

    internal static IntPtr GetWindowIcon(IntPtr hwnd, int kind)
    {
        return SendMessage(hwnd, WmGetIcon, (IntPtr)kind, IntPtr.Zero);
    }

    internal static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    internal static string GetWindowClass(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        _ = GetClassName(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    internal static string? GetProcessPath(IntPtr hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return null;
        }

        var process = OpenProcess(
            ProcessQueryLimitedInformation,
            false,
            processId);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var capacity = 1024u;
            var builder = new StringBuilder((int)capacity);
            return QueryFullProcessImageName(process, 0, builder, ref capacity)
                ? builder.ToString()
                : null;
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    internal static string HandleToString(IntPtr handle)
    {
        return ((nuint)handle).ToString();
    }

    internal static bool TryParseHandle(string? value, out IntPtr handle)
    {
        if (nuint.TryParse(value, out var parsed))
        {
            handle = (IntPtr)parsed;
            return true;
        }

        handle = IntPtr.Zero;
        return false;
    }
}
