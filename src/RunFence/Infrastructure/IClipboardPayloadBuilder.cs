namespace RunFence.Infrastructure;

public interface IClipboardPayloadBuilder
{
    bool TryBuild(IntPtr processHandle, IntPtr hWnd, IReadOnlyList<ClipboardFormatData> formats, out ClipboardInjectionPayload payload);
}
