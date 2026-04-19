using System.Text;

namespace RunFence.Core.Ipc;

public static class IpcCommands
{
    public static readonly byte[] PingBytes = Encoding.ASCII.GetBytes("PING");
    public static readonly byte[] PongBytes = Encoding.ASCII.GetBytes("PONG");
    public const byte RateLimitedSignal = 1;
    public const string Launch = "Launch";
    public const string Shutdown = "Shutdown";
    public const string Unlock = "Unlock";
    public const string LoadApps = "LoadApps";
    public const string UnloadApps = "UnloadApps";
    public const string OpenFolder = "OpenFolder";
    public const string HandleAssociation = "HandleAssociation";
}