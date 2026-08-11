using System.Buffers.Binary;
using System.Text.Json;

namespace TaskbarIconSplitter.Native.Protocol;

public sealed class NativeMessageProtocol
{
    public const int MaximumIncomingMessageBytes = 4 * 1024 * 1024;

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly JsonSerializerOptions _jsonOptions;

    public NativeMessageProtocol(
        Stream input,
        Stream output,
        JsonSerializerOptions? jsonOptions = null)
    {
        _input = input;
        _output = output;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public async ValueTask<JsonDocument?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var lengthBuffer = new byte[sizeof(int)];
        var firstRead = await _input.ReadAsync(
            lengthBuffer.AsMemory(0, lengthBuffer.Length),
            cancellationToken);
        if (firstRead == 0)
        {
            return null;
        }

        await ReadRemainingAsync(
            lengthBuffer,
            firstRead,
            cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        if (length <= 0 || length > MaximumIncomingMessageBytes)
        {
            throw new InvalidDataException(
                $"Native message length {length} is outside the accepted range.");
        }

        var payload = new byte[length];
        await ReadRemainingAsync(payload, 0, cancellationToken);
        return JsonDocument.Parse(payload);
    }

    public async ValueTask WriteAsync<T>(
        T message,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, _jsonOptions);
        var lengthBuffer = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, payload.Length);

        await _output.WriteAsync(lengthBuffer, cancellationToken);
        await _output.WriteAsync(payload, cancellationToken);
        await _output.FlushAsync(cancellationToken);
    }

    private async ValueTask ReadRemainingAsync(
        byte[] buffer,
        int alreadyRead,
        CancellationToken cancellationToken)
    {
        var offset = alreadyRead;
        while (offset < buffer.Length)
        {
            var read = await _input.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException(
                    "Native messaging stream ended in the middle of a message.");
            }

            offset += read;
        }
    }
}
