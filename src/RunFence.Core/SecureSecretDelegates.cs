namespace RunFence.Core;

public delegate void SecureSecretInitializer(Span<byte> data);

public delegate void SecureSecretAction(ReadOnlySpan<byte> data);

public delegate T SecureSecretFunc<out T>(ReadOnlySpan<byte> data);
