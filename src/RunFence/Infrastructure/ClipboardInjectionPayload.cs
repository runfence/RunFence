namespace RunFence.Infrastructure;

public sealed class ClipboardInjectionPayload(byte[] shellcode, byte[] dataBlock)
{
    public byte[] Shellcode { get; } = shellcode;
    public byte[] DataBlock { get; } = dataBlock;
}
