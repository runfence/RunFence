namespace RunFence.Core;

internal delegate void ProtectedStringBufferAction(ProtectedStringBufferAccess access);

internal delegate T ProtectedStringBufferFunc<T>(ProtectedStringBufferAccess access);
