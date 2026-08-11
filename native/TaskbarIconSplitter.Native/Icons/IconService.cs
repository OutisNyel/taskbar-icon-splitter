using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using TaskbarIconSplitter.Native.Core;
using TaskbarIconSplitter.Native.Windows;

namespace TaskbarIconSplitter.Native.Icons;

internal sealed class IconService : IIconService, IDisposable
{
    private const int MaximumIconBytes = 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly TimeSpan _candidateTimeout;

    public IconService()
        : this(
            CreateDefaultHandler(),
            cacheDirectory: null,
            candidateTimeout: TimeSpan.FromSeconds(3))
    {
    }

    internal IconService(
        HttpMessageHandler handler,
        string? cacheDirectory,
        TimeSpan candidateTimeout)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (candidateTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateTimeout));
        }

        _candidateTimeout = candidateTimeout;
        _httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = MaximumIconBytes
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "TaskbarIconSplitter/0.1");

        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "TaskbarIconSplitter",
            "icons");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<IconLease> CreateIconsAsync(
        IntPtr hwnd,
        string domain,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        using var bitmap = await LoadOrCreateBitmapAsync(
            domain,
            candidates,
            cancellationToken);

        var dpi = Win32.GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }
        var smallWidth = SafeMetric(Win32.SmCxSmallIcon, dpi, 16);
        var smallHeight = SafeMetric(Win32.SmCySmallIcon, dpi, 16);
        var bigWidth = SafeMetric(Win32.SmCxIcon, dpi, 32);
        var bigHeight = SafeMetric(Win32.SmCyIcon, dpi, 32);
        var iconResourcePath = TryCreateIconResource(
            bitmap,
            Path.Combine(
                _cacheDirectory,
                $"{DomainIdentity.ComputeCacheKey(domain)}.ico"));

        var small = CreateIcon(bitmap, smallWidth, smallHeight);
        try
        {
            var big = CreateIcon(bitmap, bigWidth, bigHeight);
            return new IconLease(
                small,
                big,
                iconResourcePath: iconResourcePath);
        }
        catch
        {
            _ = Win32.DestroyIcon(small);
            throw;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task<Bitmap> LoadOrCreateBitmapAsync(
        string domain,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        var cachePath = Path.Combine(
            _cacheDirectory,
            $"{DomainIdentity.ComputeCacheKey(domain)}.png");
        var cached = TryLoadBitmap(cachePath);
        if (cached is not null)
        {
            return cached;
        }

        var distinctCandidates = candidates
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var candidate in distinctCandidates)
        {
            if (!IconCandidatePolicy.TryDecodeDataImage(
                    candidate,
                    MaximumIconBytes,
                    out var dataImage))
            {
                continue;
            }
            try
            {
                var bitmap = DecodeBitmap(dataImage);
                TrySaveBitmap(bitmap, cachePath);
                return bitmap;
            }
            catch (Exception error) when (
                error is InvalidDataException or
                ArgumentException or
                ExternalException)
            {
                Console.Error.WriteLine(
                    $"favicon candidate failed for {domain}: {error.Message}");
            }
        }

        var networkBitmap = await LoadFirstNetworkBitmapAsync(
            domain,
            distinctCandidates,
            cancellationToken);
        if (networkBitmap is not null)
        {
            TrySaveBitmap(networkBitmap, cachePath);
            return networkBitmap;
        }

        var fallback = CreateFallback(domain);
        TrySaveBitmap(fallback, cachePath);
        return fallback;
    }

    private async Task<Bitmap?> LoadFirstNetworkBitmapAsync(
        string domain,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        var networkCandidates = candidates
            .Where(candidate =>
                IconCandidatePolicy.TryCreateNetworkUri(candidate, out _))
            .ToArray();
        if (networkCandidates.Length == 0)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_candidateTimeout);
        var pending = networkCandidates
            .Select(candidate => TryReadCandidateAsync(
                domain,
                candidate,
                timeout.Token))
            .ToList();

        Bitmap? winner = null;
        while (pending.Count > 0 && winner is null)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);
            var bytes = await completed;
            if (bytes is null)
            {
                continue;
            }

            try
            {
                winner = DecodeBitmap(bytes);
            }
            catch (Exception error) when (
                error is InvalidDataException or
                ArgumentException or
                ExternalException)
            {
                Console.Error.WriteLine(
                    $"favicon candidate failed for {domain}: {error.Message}");
            }
        }

        if (winner is not null)
        {
            timeout.Cancel();
        }
        if (pending.Count > 0)
        {
            _ = await Task.WhenAll(pending);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return winner;
    }

    private async Task<byte[]?> TryReadCandidateAsync(
        string domain,
        string candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ReadCandidateAsync(candidate, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception error) when (
            error is HttpRequestException or
            InvalidDataException or
            ArgumentException)
        {
            Console.Error.WriteLine(
                $"favicon candidate failed for {domain}: {error.Message}");
            return null;
        }
    }

    private async Task<byte[]?> ReadCandidateAsync(
        string candidate,
        CancellationToken cancellationToken)
    {
        if (IconCandidatePolicy.TryDecodeDataImage(
                candidate,
                MaximumIconBytes,
                out var dataImage))
        {
            return dataImage;
        }

        if (!IconCandidatePolicy.TryCreateNetworkUri(candidate, out var uri))
        {
            return null;
        }

        for (var redirect = 0; redirect < 5; redirect++)
        {
            using var response = await _httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
            {
                uri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(uri, response.Headers.Location);
                if (!IconCandidatePolicy.IsAllowedNetworkUri(uri))
                {
                    return null;
                }
                continue;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumIconBytes)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            await using var memory = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }
                if (memory.Length + read > MaximumIconBytes)
                {
                    return null;
                }
                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            return memory.ToArray();
        }

        return null;
    }

    private static Bitmap DecodeBitmap(byte[] bytes)
    {
        using var memory = new MemoryStream(bytes, writable: false);
        try
        {
            using var image = Image.FromStream(
                memory,
                useEmbeddedColorManagement: false,
                validateImageData: true);
            return new Bitmap(image);
        }
        catch (ArgumentException)
        {
            memory.Position = 0;
            using var icon = new Icon(memory);
            return icon.ToBitmap();
        }
    }

    private static Bitmap? TryLoadBitmap(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            using var image = Image.FromFile(path);
            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
    }

    private static void TrySaveBitmap(Bitmap bitmap, string path)
    {
        try
        {
            bitmap.Save(path, ImageFormat.Png);
        }
        catch
        {
            // Cache writes are optional.
        }
    }

    private static Bitmap CreateFallback(string domain)
    {
        var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var colorBytes = SHA256.HashData(Encoding.UTF8.GetBytes(domain));
        using var background = new SolidBrush(
            Color.FromArgb(
                255,
                64 + colorBytes[0] % 128,
                64 + colorBytes[1] % 128,
                64 + colorBytes[2] % 128));
        graphics.FillEllipse(background, 2, 2, 60, 60);

        var letter = domain.FirstOrDefault(char.IsLetterOrDigit);
        var text = letter == default ? "?" : char.ToUpperInvariant(letter).ToString();
        using var font = new Font(
            "Segoe UI",
            30,
            FontStyle.Bold,
            GraphicsUnit.Pixel);
        using var foreground = new SolidBrush(Color.White);
        var size = graphics.MeasureString(text, font);
        graphics.DrawString(
            text,
            font,
            foreground,
            (64 - size.Width) / 2,
            (64 - size.Height) / 2);
        return bitmap;
    }

    private static IntPtr CreateIcon(
        Bitmap source,
        int width,
        int height)
    {
        using var resized = new Bitmap(
            Math.Max(width, 1),
            Math.Max(height, 1),
            PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, 0, 0, resized.Width, resized.Height);
        return resized.GetHicon();
    }

    private static string? TryCreateIconResource(
        Bitmap bitmap,
        string path)
    {
        try
        {
            IconResourceWriter.WritePngIcon(bitmap, path);
            return path;
        }
        catch (Exception error) when (
            error is IOException or
            UnauthorizedAccessException or
            ExternalException)
        {
            Console.Error.WriteLine(
                $"Could not create taskbar icon resource: {error.Message}");
            return null;
        }
    }

    private static int SafeMetric(int metric, uint dpi, int fallback)
    {
        try
        {
            var value = Win32.GetSystemMetricsForDpi(metric, dpi);
            return value > 0 ? value : fallback;
        }
        catch (EntryPointNotFoundException)
        {
            return fallback;
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is
            HttpStatusCode.Moved or
            HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;
    }

    private static HttpMessageHandler CreateDefaultHandler()
    {
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = true
        };
    }
}
