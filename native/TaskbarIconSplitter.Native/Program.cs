using System.Text.Json;
using TaskbarIconSplitter.Native.Diagnostics;
using TaskbarIconSplitter.Native.Icons;
using TaskbarIconSplitter.Native.Protocol;
using TaskbarIconSplitter.Native.Windows;

namespace TaskbarIconSplitter.Native;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<int> Main()
    {
        using var log = NativeLogWriter.Create(Console.Error);
        Console.SetError(log);
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var protocol = new NativeMessageProtocol(
            Console.OpenStandardInput(),
            Console.OpenStandardOutput(),
            JsonOptions);
        using var iconService = new IconService();
        using var windows = new WindowIdentityManager(iconService);

        try
        {
            while (true)
            {
                using var request = await protocol.ReadAsync();
                if (request is null)
                {
                    return 0;
                }

                var response = await HandleAsync(request.RootElement, windows);
                await protocol.WriteAsync(response);
            }
        }
        catch (EndOfStreamException)
        {
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static async Task<HostResponse> HandleAsync(
        JsonElement request,
        WindowIdentityManager windows)
    {
        var requestId = ReadRequiredString(request, "requestId");
        try
        {
            var type = ReadRequiredString(request, "type");
            switch (type)
            {
                case "hello":
                    return new HostResponse(
                        requestId,
                        true,
                        new { version = "0.1.0" });

                case "bind_window":
                {
                    var result = await windows.BindAsync(
                        ReadRequiredInt32(request, "edgeWindowId"),
                        ReadRequiredString(request, "token"),
                        ReadRequiredString(request, "domain"),
                        ReadStringArray(request, "faviconCandidates"),
                        CancellationToken.None);
                    return new HostResponse(
                        requestId,
                        true,
                        result);
                }

                case "restore_window":
                {
                    var binding = request.GetProperty("binding")
                        .Deserialize<NativeBindingSnapshot>(JsonOptions)
                        ?? throw new InvalidDataException(
                            "restore_window requires a binding snapshot.");
                    await windows.RestoreAsync(
                        ReadRequiredInt32(request, "edgeWindowId"),
                        ReadRequiredString(request, "domain"),
                        ReadStringArray(request, "faviconCandidates"),
                        binding,
                        CancellationToken.None);
                    return new HostResponse(requestId, true);
                }

                case "reset_window":
                    windows.Reset(ReadRequiredInt32(request, "edgeWindowId"));
                    return new HostResponse(requestId, true);

                case "release_window":
                    windows.Release(ReadRequiredInt32(request, "edgeWindowId"));
                    return new HostResponse(requestId, true);

                default:
                    throw new InvalidDataException(
                        $"Unsupported native message type: {type}");
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                $"request {requestId} failed: {error}");
            return new HostResponse(
                requestId,
                false,
                Error: error.Message);
        }
    }

    private static string ReadRequiredString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException(
                $"Missing or invalid string property: {propertyName}");
        }
        return property.GetString()!;
    }

    private static int ReadRequiredInt32(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            !property.TryGetInt32(out var value))
        {
            throw new InvalidDataException(
                $"Missing or invalid integer property: {propertyName}");
        }
        return value;
    }

    private static string[] ReadStringArray(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .Take(8)
            .ToArray();
    }
}
