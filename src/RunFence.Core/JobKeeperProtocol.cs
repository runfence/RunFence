using System.Text.Json;

namespace RunFence.Core;

public static class JobKeeperProtocol
{
    private const int MaxMessageSize = 10 * 1024 * 1024;

    public static void WriteMessage<T>(Stream stream, T message)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message);
        stream.Write(BitConverter.GetBytes(json.Length));
        stream.Write(json);
        stream.Flush();
    }

    public static T? ReadMessage<T>(Stream stream)
    {
        var lenBytes = new byte[4];
        ReadExact(stream, lenBytes);
        var len = BitConverter.ToInt32(lenBytes);
        if (len <= 0 || len > MaxMessageSize)
            throw new IOException($"Invalid message length: {len}");
        var buf = new byte[len];
        ReadExact(stream, buf);
        return JsonSerializer.Deserialize<T>(buf);
    }

    public static async Task WriteMessageAsync<T>(Stream stream, T message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message);
        await stream.WriteAsync(BitConverter.GetBytes(json.Length), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T?> ReadMessageAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var lenBytes = new byte[4];
        await ReadExactAsync(stream, lenBytes, cancellationToken).ConfigureAwait(false);
        var len = BitConverter.ToInt32(lenBytes);
        if (len <= 0 || len > MaxMessageSize)
            throw new IOException($"Invalid message length: {len}");
        var buf = new byte[len];
        await ReadExactAsync(stream, buf, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(buf);
    }

    private static void ReadExact(Stream stream, byte[] buf)
    {
        var offset = 0;
        while (offset < buf.Length)
        {
            var read = stream.Read(buf, offset, buf.Length - offset);
            if (read == 0) throw new IOException("Pipe closed");
            offset += read;
        }
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buf, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buf.Length)
        {
            var read = await stream.ReadAsync(buf.AsMemory(offset, buf.Length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new IOException("Pipe closed");
            offset += read;
        }
    }
}

public record JobKeeperLaunchRequest(
    string ExePath,
    string? Arguments,
    string? WorkingDirectory,
    bool HideWindow,
    bool SuppressStartupFeedback = false,
    Dictionary<string, string>? EnvOverrides = null
);

public record JobKeeperLaunchResponse(int Pid, int Error);
