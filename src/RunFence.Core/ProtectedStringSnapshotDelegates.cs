namespace RunFence.Core;

public delegate void ProtectedStringBytesAction(ReadOnlySpan<byte> utf16Bytes);

public delegate T ProtectedStringBytesFunc<out T>(ReadOnlySpan<byte> utf16Bytes);

public delegate void ProtectedStringUnicodeAction(ProtectedUtf16ZSnapshot snapshot);

public delegate T ProtectedStringUnicodeFunc<out T>(ProtectedUtf16ZSnapshot snapshot);
