namespace RunFence.Infrastructure;

internal interface IObjectTypeNameReader
{
    bool TryGetObjectTypeName(IntPtr handle, out string typeName);
}
