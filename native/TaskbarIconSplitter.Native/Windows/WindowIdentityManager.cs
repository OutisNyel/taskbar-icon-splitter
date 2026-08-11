using System.Diagnostics;
using TaskbarIconSplitter.Native.Core;
using TaskbarIconSplitter.Native.Icons;
using TaskbarIconSplitter.Native.Protocol;

namespace TaskbarIconSplitter.Native.Windows;

internal sealed class WindowIdentityManager : IDisposable
{
    private readonly IIconService _iconService;
    private readonly IWindowIdentityPlatform _platform;
    private readonly Dictionary<int, ManagedWindow> _windows = [];

    public WindowIdentityManager(
        IIconService iconService,
        IWindowIdentityPlatform? platform = null)
    {
        _iconService = iconService;
        _platform = platform ?? new Win32WindowIdentityPlatform();
    }

    public async Task<BindResponseData> BindAsync(
        int edgeWindowId,
        string token,
        string domain,
        IReadOnlyList<string> faviconCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        var correlationTimer = Stopwatch.StartNew();
        var hwnd = await _platform.FindEdgeWindowAsync(
            token,
            cancellationToken);
        correlationTimer.Stop();
        var original = _platform.CaptureOriginal(hwnd);
        var applyTimings = await ApplyAsync(
            edgeWindowId,
            hwnd,
            domain,
            faviconCandidates,
            original,
            cancellationToken);
        var timings = applyTimings with
        {
            HwndCorrelationMs = correlationTimer.Elapsed.TotalMilliseconds
        };
        Console.Error.WriteLine(
            $"Timing {domain}: HWND={timings.HwndCorrelationMs:F2} ms, " +
            $"AUMID={timings.AppUserModelIdMs:F2} ms, " +
            $"icon={timings.IconProcessingMs:F2} ms.");
        return new BindResponseData(original, timings);
    }

    public async Task RestoreAsync(
        int edgeWindowId,
        string domain,
        IReadOnlyList<string> faviconCandidates,
        NativeBindingSnapshot binding,
        CancellationToken cancellationToken)
    {
        if (!Win32.TryParseHandle(binding.Hwnd, out var hwnd) ||
            !_platform.IsValidEdgeWindow(hwnd))
        {
            throw new InvalidOperationException(
                "Stored window handle is no longer a Microsoft Edge window.");
        }

        _ = await ApplyAsync(
            edgeWindowId,
            hwnd,
            domain,
            faviconCandidates,
            binding,
            cancellationToken);
    }

    public void Reset(int edgeWindowId)
    {
        if (!_windows.Remove(edgeWindowId, out var managed))
        {
            return;
        }

        try
        {
            if (_platform.IsWindow(managed.Hwnd))
            {
                _platform.RestoreIdentity(managed.Hwnd, managed.Original);
            }
        }
        finally
        {
            managed.Icons.Dispose();
        }
    }

    public void Release(int edgeWindowId)
    {
        if (_windows.Remove(edgeWindowId, out var managed))
        {
            managed.Icons.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var managed in _windows.Values)
        {
            try
            {
                if (_platform.IsWindow(managed.Hwnd))
                {
                    _platform.RestoreIdentity(
                        managed.Hwnd,
                        managed.Original);
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(
                    $"Could not restore Edge window during shutdown: {error.Message}");
            }
            finally
            {
                managed.Icons.Dispose();
            }
        }
        _windows.Clear();
    }

    private async Task<NativeStageTimings> ApplyAsync(
        int edgeWindowId,
        IntPtr hwnd,
        string domain,
        IReadOnlyList<string> faviconCandidates,
        NativeBindingSnapshot original,
        CancellationToken cancellationToken)
    {
        if (!_platform.IsValidEdgeWindow(hwnd))
        {
            throw new InvalidOperationException(
                "Refusing to modify a window that does not belong to Microsoft Edge.");
        }

        var iconTimer = Stopwatch.StartNew();
        var icons = await _iconService.CreateIconsAsync(
            hwnd,
            domain,
            faviconCandidates,
            cancellationToken);
        iconTimer.Stop();
        var iconProcessingMs = iconTimer.Elapsed.TotalMilliseconds;
        _windows.TryGetValue(edgeWindowId, out var previous);
        try
        {
            var identityTimer = Stopwatch.StartNew();
            _platform.ApplyIdentityProperties(
                hwnd,
                DomainIdentity.ComputeAppUserModelId(domain),
                icons);
            identityTimer.Stop();

            iconTimer.Restart();
            _platform.ApplyWindowIcons(hwnd, icons);
            iconTimer.Stop();
            iconProcessingMs += iconTimer.Elapsed.TotalMilliseconds;

            _windows[edgeWindowId] = new ManagedWindow(
                hwnd,
                domain,
                original,
                icons);
            previous?.Icons.Dispose();
            Console.Error.WriteLine(
                $"Applied taskbar identity for {domain} to HWND " +
                $"{Win32.HandleToString(hwnd)}.");
            return new NativeStageTimings(
                0,
                identityTimer.Elapsed.TotalMilliseconds,
                iconProcessingMs);
        }
        catch (Exception applyError)
        {
            try
            {
                if (previous is not null && _platform.IsWindow(previous.Hwnd))
                {
                    _platform.ApplyIdentityProperties(
                        previous.Hwnd,
                        DomainIdentity.ComputeAppUserModelId(previous.Domain),
                        previous.Icons);
                    _platform.ApplyWindowIcons(previous.Hwnd, previous.Icons);
                }
                else if (_platform.IsWindow(hwnd))
                {
                    _platform.RestoreIdentity(hwnd, original);
                }
            }
            catch (Exception rollbackError)
            {
                Console.Error.WriteLine(
                    $"Could not roll back window identity: {rollbackError.Message}");
            }
            icons.Dispose();
            throw new InvalidOperationException(
                "Could not apply the domain window identity.",
                applyError);
        }
    }

    private sealed record ManagedWindow(
        IntPtr Hwnd,
        string Domain,
        NativeBindingSnapshot Original,
        IconLease Icons);
}
