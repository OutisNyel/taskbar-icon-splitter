using TaskbarIconSplitter.Native.Icons;
using TaskbarIconSplitter.Native.Protocol;

namespace TaskbarIconSplitter.Native.Windows;

internal sealed class Win32WindowIdentityPlatform : IWindowIdentityPlatform
{
    private const int CorrelationAttempts = 300;
    private const int CorrelationPollMilliseconds = 10;

    public async Task<IntPtr> FindEdgeWindowAsync(
        string titleToken,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < CorrelationAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = IntPtr.Zero;
            _ = Win32.EnumWindows((hwnd, _) =>
            {
                if (!Win32.IsWindowVisible(hwnd) ||
                    !Win32.GetWindowTitle(hwnd).Contains(
                        titleToken,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                if (IsValidEdgeWindow(hwnd))
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            if (found != IntPtr.Zero)
            {
                return found;
            }
            await Task.Delay(
                CorrelationPollMilliseconds,
                cancellationToken);
        }

        throw new TimeoutException(
            "Could not correlate the Edge bootstrap window with a Win32 HWND.");
    }

    public bool IsValidEdgeWindow(IntPtr hwnd)
    {
        return hwnd != IntPtr.Zero &&
            EdgeWindowValidator.IsEdgeTopLevelWindow(
                Win32.IsWindow(hwnd),
                Win32.GetWindowClass(hwnd),
                Win32.GetProcessPath(hwnd));
    }

    public bool IsWindow(IntPtr hwnd)
    {
        return Win32.IsWindow(hwnd);
    }

    public NativeBindingSnapshot CaptureOriginal(IntPtr hwnd)
    {
        return new NativeBindingSnapshot(
            Win32.HandleToString(hwnd),
            WindowPropertyStore.GetAppUserModelId(hwnd),
            new NativeIconSnapshot(
                Win32.HandleToString(
                    Win32.GetWindowIcon(hwnd, Win32.IconSmall)),
                Win32.HandleToString(
                    Win32.GetWindowIcon(hwnd, Win32.IconBig)),
                Win32.HandleToString(
                    Win32.GetWindowIcon(hwnd, Win32.IconSmall2))),
            WindowPropertyStore.GetRelaunchIconResource(hwnd));
    }

    public void ApplyIdentityProperties(
        IntPtr hwnd,
        string appUserModelId,
        IconLease icons)
    {
        WindowPropertyStore.SetIdentity(
            hwnd,
            appUserModelId,
            icons.IconResourcePath);
    }

    public void ApplyWindowIcons(
        IntPtr hwnd,
        IconLease icons)
    {
        SetIcons(hwnd, icons);
    }

    public void RestoreIdentity(
        IntPtr hwnd,
        NativeBindingSnapshot original)
    {
        WindowPropertyStore.RestoreIdentity(
            hwnd,
            original.OriginalAppId,
            original.OriginalRelaunchIconResource);
        if (Win32.TryParseHandle(
                original.OriginalIcons.Small,
                out var small))
        {
            _ = Win32.TrySendWindowIcon(
                hwnd,
                Win32.IconSmall,
                small,
                out _);
        }
        if (Win32.TryParseHandle(
                original.OriginalIcons.Small2,
                out var small2))
        {
            _ = Win32.TrySendWindowIcon(
                hwnd,
                Win32.IconSmall2,
                small2,
                out _);
        }
        if (Win32.TryParseHandle(
                original.OriginalIcons.Big,
                out var big))
        {
            _ = Win32.TrySendWindowIcon(
                hwnd,
                Win32.IconBig,
                big,
                out _);
        }
    }

    private static void SetIcons(IntPtr hwnd, IconLease icons)
    {
        if (!Win32.TrySendWindowIcon(
                hwnd,
                Win32.IconSmall,
                icons.Small,
                out _))
        {
            throw new InvalidOperationException("Edge did not accept the small icon.");
        }
        _ = Win32.TrySendWindowIcon(
            hwnd,
            Win32.IconSmall2,
            icons.Small2,
            out _);
        if (!Win32.TrySendWindowIcon(
                hwnd,
                Win32.IconBig,
                icons.Big,
                out _))
        {
            throw new InvalidOperationException("Edge did not accept the large icon.");
        }
    }
}
