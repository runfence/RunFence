namespace RunFence.Infrastructure;

public readonly record struct ForegroundWindowInfo(IntPtr HWnd, uint ProcessId, string ClassName);
