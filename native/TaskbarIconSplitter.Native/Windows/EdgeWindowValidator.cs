namespace TaskbarIconSplitter.Native.Windows;

internal static class EdgeWindowValidator
{
    internal static bool IsEdgeTopLevelWindow(
        bool isWindow,
        string windowClass,
        string? processPath)
    {
        return isWindow &&
            string.Equals(
                windowClass,
                "Chrome_WidgetWin_1",
                StringComparison.Ordinal) &&
            string.Equals(
                Path.GetFileName(processPath),
                "msedge.exe",
                StringComparison.OrdinalIgnoreCase);
    }
}
