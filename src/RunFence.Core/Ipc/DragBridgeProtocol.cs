using System.Text;
using System.Text.Json;

namespace RunFence.Core.Ipc;

/// <summary>
/// Length-prefixed JSON read/write helpers for DragBridge named pipe communication.
/// Format: [4 bytes: int32 LE length] [UTF-8 JSON payload]
/// </summary>
public static class DragBridgeProtocol
{
    private const int MaxMessageSize = 256 * 1024; // 256 KB

    public static async Task WriteAsync(Stream stream, DragBridgeData data, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(data);
        var payload = Encoding.UTF8.GetBytes(json);

        if (payload.Length > MaxMessageSize)
            throw new InvalidOperationException($"DragBridge message too large: {payload.Length} bytes.");

        var lengthPrefix = BitConverter.GetBytes(payload.Length); // 4 bytes LE
        await stream.WriteAsync(lengthPrefix, ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<DragBridgeData?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var lengthBuf = new byte[4];
        if (!await ReadExactAsync(stream, lengthBuf, ct))
            return null;

        var length = BitConverter.ToInt32(lengthBuf, 0);
        if (length is <= 0 or > MaxMessageSize)
            throw new InvalidDataException($"Invalid DragBridge message length: {length}.");

        var payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, ct))
            return null;

        var json = Encoding.UTF8.GetString(payload);
        return JsonSerializer.Deserialize<DragBridgeData>(json);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, ct);
            if (read == 0)
                return false; // stream closed
            offset += read;
        }

        return true;
    }
}