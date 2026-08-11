using TaskbarIconSplitter.Native.Windows;

namespace TaskbarIconSplitter.Native.Icons;

internal sealed class IconLease : IDisposable
{
    private readonly Func<IntPtr, bool> _destroyIcon;
    private bool _disposed;

    public IconLease(
        IntPtr small,
        IntPtr big,
        Func<IntPtr, bool>? destroyIcon = null,
        string? iconResourcePath = null)
    {
        Small = small;
        Big = big;
        _destroyIcon = destroyIcon ?? Win32.DestroyIcon;
        IconResourcePath = iconResourcePath;
    }

    public IntPtr Small { get; }

    public IntPtr Big { get; }

    public IntPtr Small2 => Small;

    public string? IconResourcePath { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Small != IntPtr.Zero)
        {
            _ = _destroyIcon(Small);
        }
        if (Big != IntPtr.Zero && Big != Small)
        {
            _ = _destroyIcon(Big);
        }
    }
}
