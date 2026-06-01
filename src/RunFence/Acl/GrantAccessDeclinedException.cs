namespace RunFence.Acl;

public sealed class GrantAccessDeclinedException(string message) : OperationCanceledException(message);
