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
}

public record JobKeeperLaunchRequest(
    string ExePath,
    string? Arguments,
    string? WorkingDirectory,
    bool HideWindow,
    Dictionary<string, string>? EnvOverrides
);

public record JobKeeperLaunchResponse(int Pid, int Error);
