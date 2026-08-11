namespace TaskbarIconSplitter.Native.Icons;

internal interface IIconService
{
    Task<IconLease> CreateIconsAsync(
        IntPtr hwnd,
        string domain,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken);
}
