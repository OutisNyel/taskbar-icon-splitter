using TaskbarIconSplitter.Native.Icons;
using TaskbarIconSplitter.Native.Protocol;

namespace TaskbarIconSplitter.Native.Windows;

internal interface IWindowIdentityPlatform
{
    Task<IntPtr> FindEdgeWindowAsync(
        string titleToken,
        CancellationToken cancellationToken);

    bool IsValidEdgeWindow(IntPtr hwnd);

    bool IsWindow(IntPtr hwnd);

    NativeBindingSnapshot CaptureOriginal(IntPtr hwnd);

    void ApplyIdentityProperties(
        IntPtr hwnd,
        string appUserModelId,
        IconLease icons);

    void ApplyWindowIcons(
        IntPtr hwnd,
        IconLease icons);

    void RestoreIdentity(
        IntPtr hwnd,
        NativeBindingSnapshot original);
}
