using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Text;
using System.Text.Json;
using TaskbarIconSplitter.Native.Core;
using TaskbarIconSplitter.Native.Icons;
using TaskbarIconSplitter.Native.Protocol;
using TaskbarIconSplitter.Native.Windows;

namespace TaskbarIconSplitter.Native.Tests;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Test)> Tests =
    [
        ("AUMID is stable, case-insensitive and bounded", TestDomainIdentity),
        ("native protocol reads fragmented frames", TestProtocolReadAsync),
        ("native protocol writes little-endian frames", TestProtocolWriteAsync),
        ("native protocol rejects invalid lengths", TestProtocolInvalidLengthAsync),
        ("Edge HWND description validation is strict", TestEdgeWindowValidation),
        ("favicon candidate protocol and size validation", TestIconCandidatePolicy),
        ("favicon candidates race in parallel", TestParallelIconCandidatesAsync),
        ("PNG-compressed ICO preserves transparency", TestPngIconResource),
        ("HICON lease releases each owned handle once", TestIconLease),
        ("reset restores the original identity and releases icons", TestWindowResetAsync),
        ("release skips restore for a closed window", TestWindowReleaseAsync),
        ("shutdown restores all live window identities", TestWindowDisposeAsync)
    ];

    public static async Task<int> Main(string[] args)
    {
        if (args is ["--probe-edge-pid", var processId] &&
            uint.TryParse(processId, out var parsedProcessId))
        {
            return ProbeEdgeWindows(parsedProcessId);
        }

        var failures = 0;
        foreach (var (name, test) in Tests)
        {
            try
            {
                await test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception error)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}");
                Console.Error.WriteLine(error);
            }
        }

        Console.WriteLine(
            $"{Tests.Count - failures}/{Tests.Count} native tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static int ProbeEdgeWindows(uint targetProcessId)
    {
        var rows = new List<object>();
        _ = Win32.EnumWindows((hwnd, ignored) =>
        {
            _ = Win32.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId != targetProcessId ||
                !Win32.IsWindowVisible(hwnd) ||
                !string.Equals(
                    Win32.GetWindowClass(hwnd),
                    "Chrome_WidgetWin_1",
                    StringComparison.Ordinal))
            {
                return true;
            }

            rows.Add(new
            {
                hwnd = Win32.HandleToString(hwnd),
                title = Win32.GetWindowTitle(hwnd),
                appUserModelId = WindowPropertyStore.GetAppUserModelId(hwnd),
                relaunchIconResource =
                    WindowPropertyStore.GetRelaunchIconResource(hwnd),
                smallIcon = Win32.HandleToString(
                    Win32.GetWindowIcon(hwnd, Win32.IconSmall)),
                bigIcon = Win32.HandleToString(
                    Win32.GetWindowIcon(hwnd, Win32.IconBig))
            });
            return true;
        }, IntPtr.Zero);

        Console.WriteLine(
            JsonSerializer.Serialize(
                rows,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                }));
        return 0;
    }

    private static Task TestDomainIdentity()
    {
        var lower = DomainIdentity.ComputeAppUserModelId("github.com");
        var upper = DomainIdentity.ComputeAppUserModelId("GITHUB.COM");
        Assert.Equal(lower, upper);
        Assert.True(
            lower.StartsWith(
                "Outis.TaskbarIconSplitter.Edge.",
                StringComparison.Ordinal));
        Assert.Equal(55, lower.Length);
        Assert.NotEqual(
            lower,
            DomainIdentity.ComputeAppUserModelId("example.org"));
        Assert.Equal(64, DomainIdentity.ComputeCacheKey("github.com").Length);
        return Task.CompletedTask;
    }

    private static async Task TestProtocolReadAsync()
    {
        var payload = Encoding.UTF8.GetBytes(
            """{"type":"hello","requestId":"read-1"}""");
        var frame = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame.AsSpan(sizeof(int)));

        await using var input = new ChunkedReadStream(frame, 2);
        await using var output = new MemoryStream();
        var protocol = new NativeMessageProtocol(input, output);
        using var message = await protocol.ReadAsync();

        Assert.NotNull(message);
        Assert.Equal(
            "hello",
            message!.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "read-1",
            message.RootElement.GetProperty("requestId").GetString());
    }

    private static async Task TestProtocolWriteAsync()
    {
        await using var input = new MemoryStream();
        await using var output = new MemoryStream();
        var protocol = new NativeMessageProtocol(
            input,
            output,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await protocol.WriteAsync(
            new HostResponse("write-1", true, new { version = "0.1.0" }));
        var bytes = output.ToArray();
        var length = BinaryPrimitives.ReadInt32LittleEndian(
            bytes.AsSpan(0, sizeof(int)));
        Assert.Equal(bytes.Length - sizeof(int), length);

        using var json = JsonDocument.Parse(bytes.AsMemory(sizeof(int)));
        Assert.Equal(
            "write-1",
            json.RootElement.GetProperty("requestId").GetString());
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
    }

    private static async Task TestProtocolInvalidLengthAsync()
    {
        var frame = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(frame, -1);
        await using var input = new MemoryStream(frame);
        await using var output = new MemoryStream();
        var protocol = new NativeMessageProtocol(input, output);

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => _ = await protocol.ReadAsync());
    }

    private static Task TestEdgeWindowValidation()
    {
        Assert.True(
            EdgeWindowValidator.IsEdgeTopLevelWindow(
                true,
                "Chrome_WidgetWin_1",
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"));
        Assert.False(
            EdgeWindowValidator.IsEdgeTopLevelWindow(
                false,
                "Chrome_WidgetWin_1",
                "msedge.exe"));
        Assert.False(
            EdgeWindowValidator.IsEdgeTopLevelWindow(
                true,
                "Chrome_WidgetWin_0",
                "msedge.exe"));
        Assert.False(
            EdgeWindowValidator.IsEdgeTopLevelWindow(
                true,
                "Chrome_WidgetWin_1",
                "chrome.exe"));
        Assert.True(Win32.TryParseHandle("123456", out var handle));
        Assert.Equal(new IntPtr(123456), handle);
        Assert.False(Win32.TryParseHandle("-1", out _));
        return Task.CompletedTask;
    }

    private static Task TestIconCandidatePolicy()
    {
        Assert.True(
            IconCandidatePolicy.TryCreateNetworkUri(
                "https://example.org/favicon.ico",
                out var uri));
        Assert.Equal("https", uri.Scheme);
        Assert.True(
            IconCandidatePolicy.TryCreateNetworkUri(
                "http://localhost:8080/icon.png",
                out _));
        Assert.False(
            IconCandidatePolicy.TryCreateNetworkUri(
                "file:///C:/secret.ico",
                out _));
        Assert.False(
            IconCandidatePolicy.TryCreateNetworkUri(
                "javascript:alert(1)",
                out _));
        Assert.True(
            IconCandidatePolicy.TryDecodeDataImage(
                "data:image/png;base64,AQID",
                3,
                out var bytes));
        Assert.SequenceEqual(new byte[] { 1, 2, 3 }, bytes);
        Assert.False(
            IconCandidatePolicy.TryDecodeDataImage(
                "data:image/png;base64,AQID",
                2,
                out _));
        Assert.False(
            IconCandidatePolicy.TryDecodeDataImage(
                "data:image/png;base64,not-base64",
                100,
                out _));
        return Task.CompletedTask;
    }

    private static async Task TestParallelIconCandidatesAsync()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "TaskbarIconSplitter.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var handler = new ParallelIconHandler(CreateTestPng());
            using var service = new IconService(
                handler,
                directory,
                TimeSpan.FromSeconds(2));
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(500));
            using var icons = await service.CreateIconsAsync(
                IntPtr.Zero,
                "parallel.example",
                [
                    "https://parallel.example/slow.ico",
                    "https://parallel.example/fast.ico"
                ],
                cancellation.Token);

            Assert.Equal(2, handler.RequestCount);
            Assert.True(icons.Small != IntPtr.Zero);
            Assert.True(icons.Big != IntPtr.Zero);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task TestIconLease()
    {
        var destroyed = new List<IntPtr>();
        var lease = new IconLease(
            new IntPtr(11),
            new IntPtr(22),
            handle =>
            {
                destroyed.Add(handle);
                return true;
            });

        lease.Dispose();
        lease.Dispose();
        Assert.SequenceEqual(
            new[] { new IntPtr(11), new IntPtr(22) },
            destroyed);

        destroyed.Clear();
        using (var shared = new IconLease(
            new IntPtr(33),
            new IntPtr(33),
            handle =>
            {
                destroyed.Add(handle);
                return true;
            }))
        {
        }
        Assert.SequenceEqual(new[] { new IntPtr(33) }, destroyed);
        return Task.CompletedTask;
    }

    private static Task TestPngIconResource()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "TaskbarIconSplitter.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "transparent.ico");
        try
        {
            using (var bitmap = new Bitmap(
                16,
                16,
                PixelFormat.Format32bppArgb))
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.Transparent);
                bitmap.SetPixel(8, 8, Color.Black);
                IconResourceWriter.WritePngIcon(bitmap, path);
            }

            var bytes = File.ReadAllBytes(path);
            Assert.Equal((byte)0, bytes[0]);
            Assert.Equal((byte)1, bytes[2]);
            Assert.Equal((byte)0x89, bytes[22]);
            Assert.Equal((byte)0x50, bytes[23]);

            using var icon = new Icon(path, 64, 64);
            using var rendered = icon.ToBitmap();
            Assert.Equal((byte)0, rendered.GetPixel(0, 0).A);
            Assert.True(rendered.GetPixel(32, 32).A > 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static byte[] CreateTestPng()
    {
        using var bitmap = new Bitmap(
            16,
            16,
            PixelFormat.Format32bppArgb);
        bitmap.SetPixel(8, 8, Color.Black);
        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);
        return memory.ToArray();
    }

    private static async Task TestWindowResetAsync()
    {
        var fixture = new WindowFixture();
        using var manager = new WindowIdentityManager(
            fixture.Icons,
            fixture.Platform);

        var result = await manager.BindAsync(
            7,
            "TIS:token",
            "github.com",
            ["https://github.com/favicon.ico"],
            CancellationToken.None);
        Assert.Equal(fixture.Original, result.Binding);
        Assert.True(result.Timings.HwndCorrelationMs >= 0);
        Assert.True(result.Timings.AppUserModelIdMs >= 0);
        Assert.True(result.Timings.IconProcessingMs >= 0);
        Assert.Equal(1, fixture.Platform.Applied.Count);
        Assert.Equal(1, fixture.Platform.AppliedIcons.Count);
        Assert.Equal(
            DomainIdentity.ComputeAppUserModelId("github.com"),
            fixture.Platform.Applied[0].AppId);

        manager.Reset(7);

        Assert.SequenceEqual(
            new[] { fixture.Original },
            fixture.Platform.Restored.Select(item => item.Original));
        Assert.SequenceEqual(
            new[] { new IntPtr(91), new IntPtr(92) },
            fixture.DestroyedIcons);
    }

    private static async Task TestWindowReleaseAsync()
    {
        var fixture = new WindowFixture();
        using var manager = new WindowIdentityManager(
            fixture.Icons,
            fixture.Platform);
        await manager.BindAsync(
            8,
            "TIS:token",
            "example.org",
            [],
            CancellationToken.None);

        manager.Release(8);

        Assert.Equal(0, fixture.Platform.Restored.Count);
        Assert.SequenceEqual(
            new[] { new IntPtr(91), new IntPtr(92) },
            fixture.DestroyedIcons);
    }

    private static async Task TestWindowDisposeAsync()
    {
        var fixture = new WindowFixture();
        var manager = new WindowIdentityManager(
            fixture.Icons,
            fixture.Platform);
        await manager.RestoreAsync(
            9,
            "example.net",
            [],
            fixture.Original,
            CancellationToken.None);

        manager.Dispose();

        Assert.Equal(1, fixture.Platform.Restored.Count);
        Assert.Equal(fixture.Original, fixture.Platform.Restored[0].Original);
        Assert.SequenceEqual(
            new[] { new IntPtr(91), new IntPtr(92) },
            fixture.DestroyedIcons);
    }

    private sealed class WindowFixture
    {
        public WindowFixture()
        {
            Original = new NativeBindingSnapshot(
                "123",
                "Microsoft.MicrosoftEdge.Stable",
                new NativeIconSnapshot("1", "2", "3"),
                "edge-default.ico");
            Platform = new FakeWindowPlatform(Original);
            Icons = new FakeIconService(DestroyedIcons);
        }

        public NativeBindingSnapshot Original { get; }

        public FakeWindowPlatform Platform { get; }

        public FakeIconService Icons { get; }

        public List<IntPtr> DestroyedIcons { get; } = [];
    }

    private sealed class FakeIconService : IIconService
    {
        private readonly List<IntPtr> _destroyed;

        public FakeIconService(List<IntPtr> destroyed)
        {
            _destroyed = destroyed;
        }

        public Task<IconLease> CreateIconsAsync(
            IntPtr hwnd,
            string domain,
            IReadOnlyList<string> candidates,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new IconLease(
                    new IntPtr(91),
                    new IntPtr(92),
                    handle =>
                    {
                        _destroyed.Add(handle);
                        return true;
                    }));
        }
    }

    private sealed class ParallelIconHandler : HttpMessageHandler
    {
        private readonly byte[] _image;
        private int _requestCount;

        public ParallelIconHandler(byte[] image)
        {
            _image = image;
        }

        public int RequestCount => _requestCount;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _requestCount);
            if (request.RequestUri?.AbsolutePath.Contains(
                    "slow",
                    StringComparison.Ordinal) == true)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_image)
            };
        }
    }

    private sealed class FakeWindowPlatform : IWindowIdentityPlatform
    {
        private readonly NativeBindingSnapshot _original;

        public FakeWindowPlatform(NativeBindingSnapshot original)
        {
            _original = original;
        }

        public List<(IntPtr Hwnd, string AppId, IconLease Icons)> Applied
            { get; } = [];

        public List<(IntPtr Hwnd, IconLease Icons)> AppliedIcons
            { get; } = [];

        public List<(IntPtr Hwnd, NativeBindingSnapshot Original)> Restored
            { get; } = [];

        public Task<IntPtr> FindEdgeWindowAsync(
            string titleToken,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new IntPtr(123));
        }

        public bool IsValidEdgeWindow(IntPtr hwnd)
        {
            return hwnd == new IntPtr(123);
        }

        public bool IsWindow(IntPtr hwnd)
        {
            return hwnd == new IntPtr(123);
        }

        public NativeBindingSnapshot CaptureOriginal(IntPtr hwnd)
        {
            return _original;
        }

        public void ApplyIdentityProperties(
            IntPtr hwnd,
            string appUserModelId,
            IconLease icons)
        {
            Applied.Add((hwnd, appUserModelId, icons));
        }

        public void ApplyWindowIcons(IntPtr hwnd, IconLease icons)
        {
            AppliedIcons.Add((hwnd, icons));
        }

        public void RestoreIdentity(
            IntPtr hwnd,
            NativeBindingSnapshot original)
        {
            Restored.Add((hwnd, original));
        }
    }

    private sealed class ChunkedReadStream : MemoryStream
    {
        private readonly int _maximumChunkSize;

        public ChunkedReadStream(byte[] bytes, int maximumChunkSize)
            : base(bytes, writable: false)
        {
            _maximumChunkSize = maximumChunkSize;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return base.ReadAsync(
                buffer[..Math.Min(buffer.Length, _maximumChunkSize)],
                cancellationToken);
        }
    }

    private static class Assert
    {
        public static void True(bool value, string? message = null)
        {
            if (!value)
            {
                throw new InvalidOperationException(
                    message ?? "Expected true but found false.");
            }
        }

        public static void False(bool value, string? message = null)
        {
            True(!value, message ?? "Expected false but found true.");
        }

        public static void NotNull<T>(T? value)
        {
            if (value is null)
            {
                throw new InvalidOperationException("Expected a non-null value.");
            }
        }

        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Expected {expected}, found {actual}.");
            }
        }

        public static void NotEqual<T>(T unexpected, T actual)
        {
            if (EqualityComparer<T>.Default.Equals(unexpected, actual))
            {
                throw new InvalidOperationException(
                    $"Did not expect {unexpected}.");
            }
        }

        public static void SequenceEqual<T>(
            IEnumerable<T> expected,
            IEnumerable<T> actual)
        {
            if (!expected.SequenceEqual(actual))
            {
                throw new InvalidOperationException(
                    "Sequences were not equal.");
            }
        }

        public static async Task ThrowsAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Expected {typeof(TException).Name}.");
        }
    }
}
