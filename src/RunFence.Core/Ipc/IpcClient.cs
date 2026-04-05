using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace RunFence.Core.Ipc;

public static class IpcClient
{
    public static IpcResponse? SendMessage(IpcMessage message, int connectTimeoutMs = Constants.PipeConnectTimeoutMs)
    {
        var pipeName = Constants.PipeName;

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            client.Connect(connectTimeoutMs);
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
        catch
        {
            return null;
        }
    }

    public static bool PingServer()
    {
        var response = SendMessage(new IpcMessage { Command = IpcCommands.Ping });
        return response?.Success == true;
    }
}