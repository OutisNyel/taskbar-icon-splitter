namespace TaskbarIconSplitter.Native.Protocol;

public sealed record HostResponse(
    string RequestId,
    bool Ok,
    object? Data = null,
    string? Error = null);

public sealed record NativeIconSnapshot(
    string Small,
    string Big,
    string Small2);

public sealed record NativeBindingSnapshot(
    string Hwnd,
    string? OriginalAppId,
    NativeIconSnapshot OriginalIcons,
    string? OriginalRelaunchIconResource = null);

public sealed record NativeStageTimings(
    double HwndCorrelationMs,
    double AppUserModelIdMs,
    double IconProcessingMs);

public sealed record BindResponseData(
    NativeBindingSnapshot Binding,
    NativeStageTimings Timings);
