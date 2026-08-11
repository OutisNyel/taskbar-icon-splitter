using System.Text;

namespace TaskbarIconSplitter.Native.Diagnostics;

internal sealed class NativeLogWriter : TextWriter
{
    private const long MaximumLogBytes = 2 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly TextWriter _fallback;
    private readonly StreamWriter? _file;

    private NativeLogWriter(
        TextWriter fallback,
        StreamWriter? file)
    {
        _fallback = fallback;
        _file = file;
    }

    public override Encoding Encoding => Encoding.UTF8;

    internal static NativeLogWriter Create(TextWriter fallback)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "TaskbarIconSplitter",
                "logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "native.log");
            RotateIfNeeded(path);
            var file = new StreamWriter(
                new FileStream(
                    path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
            return new NativeLogWriter(fallback, file);
        }
        catch (Exception error)
        {
            fallback.WriteLine(
                $"Could not initialize Native Host file logging: {error.Message}");
            return new NativeLogWriter(fallback, null);
        }
    }

    public override void WriteLine(string? value)
    {
        lock (_gate)
        {
            _fallback.WriteLine(value);
            _file?.WriteLine(
                $"{DateTimeOffset.Now:O} {value}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                _file?.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) ||
            new FileInfo(path).Length <= MaximumLogBytes)
        {
            return;
        }

        File.Move(
            path,
            Path.Combine(
                Path.GetDirectoryName(path)!,
                "native.previous.log"),
            overwrite: true);
    }
}
