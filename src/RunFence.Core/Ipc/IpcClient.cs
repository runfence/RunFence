using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace RunFence.Core.Ipc;

public static class IpcClient
{
    private const int MaxResponseSize = 10 * 1024 * 1024;

    public static IpcResponse? SendMessage(IpcMessage message, int connectTimeoutMs = Constants.PipeConnectTimeoutMs)
    {
        message.CallerName ??= Environment.UserName;
        var pipeName = Constants.PipeName;

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            try
            {
                client.Connect(connectTimeoutMs);
            }
            catch (TimeoutException)
            {
                Trace.TraceInformation($"IpcClient: RunFence not running (pipe '{pipeName}' connect timed out).");
                return null;
            }
            catch (IOException ex)
            {
                Trace.TraceInformation($"IpcClient: RunFence not running (pipe '{pipeName}' connection failed: {ex.Message}).");
                return null;
            }

            client.ReadMode = PipeTransmissionMode.Message;

            var json = JsonSerializer.Serialize(message, JsonDefaults.Options);
            var bytes = Encoding.UTF8.GetBytes(json);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();

            var buffer = new byte[Constants.MaxPipeMessageSize];
            int bytesRead = client.Read(buffer, 0, buffer.Length);

            // Assemble multi-chunk responses when the pipe signals incomplete delivery.
            if (!client.IsMessageComplete)
            {
                using var ms = new MemoryStream();
                ms.Write(buffer, 0, bytesRead);
                while (!client.IsMessageComplete)
                {
                    int chunk = client.Read(buffer, 0, buffer.Length);
                    if (chunk == 0)
                        break;
                    ms.Write(buffer, 0, chunk);
                    if (ms.Length > MaxResponseSize)
                        throw new InvalidOperationException($"IPC response exceeded maximum size of {MaxResponseSize} bytes.");
                }

                var assembled = ms.ToArray();
                var responseJson = Encoding.UTF8.GetString(assembled);
                return JsonSerializer.Deserialize<IpcResponse>(responseJson, JsonDefaults.Options);
            }

            if (bytesRead > 0)
            {
                var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                return JsonSerializer.Deserialize<IpcResponse>(responseJson, JsonDefaults.Options);
            }

            return null;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"IpcClient: Protocol error communicating with RunFence: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public static bool PingServer()
    {
        var response = SendMessage(new IpcMessage { Command = IpcCommands.Ping });
        return response?.Success == true;
    }
}
