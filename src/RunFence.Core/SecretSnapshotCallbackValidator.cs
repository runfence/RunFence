namespace RunFence.Core;

internal static class SecretSnapshotCallbackValidator
{
    private static readonly HashSet<Type> CommonRejectedReturnTypes =
    [
        typeof(Task),
        typeof(ValueTask),
        typeof(IntPtr)
    ];

    public static void RejectUnsupportedReturnType<T>(string ownerName, IReadOnlySet<Type> additionalRejectedTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerName);
        ArgumentNullException.ThrowIfNull(additionalRejectedTypes);

        Type returnType = typeof(T);
        if (CommonRejectedReturnTypes.Contains(returnType) ||
            additionalRejectedTypes.Contains(returnType) ||
            (returnType.IsGenericType &&
             (returnType.GetGenericTypeDefinition() == typeof(Task<>) ||
              returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))))
        {
            throw new NotSupportedException(
                $"{ownerName} cannot return {returnType.Name}. Use a synchronous non-pointer result type instead.");
        }
    }
}
