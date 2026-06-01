using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Tests.Helpers;

public sealed class InMemoryRegistryKey : IRegistryKey
{
    private readonly Node _node;
    private readonly bool _writable;

    private InMemoryRegistryKey(Node node, bool writable)
    {
        _node = node;
        _writable = writable;
    }

    public string Name => _node.Path;

    public int SubKeyCount => _node.Children.Count;

    public int ValueCount => _node.Values.Count;

    public static InMemoryRegistryKey CreateRoot(string name = "HKU")
        => new(new Node(name), writable: true);

    public InMemoryRegistryKey AsReadOnly()
        => new(_node, writable: false);

    public IRegistryKey? OpenSubKey(string name, bool writable = false)
    {
        var node = FindNode(name, create: false);
        return node == null ? null : new InMemoryRegistryKey(node, _writable && writable);
    }

    public IRegistryKey CreateSubKey(string subkey)
    {
        EnsureWritable();
        return new InMemoryRegistryKey(FindNode(subkey, create: true)!, writable: true);
    }

    public void DeleteSubKey(string subkey, bool throwOnMissingSubKey = true)
    {
        EnsureWritable();
        var (parent, leaf) = FindParent(subkey);
        if (parent == null || !parent.Children.TryGetValue(leaf, out var child))
        {
            if (throwOnMissingSubKey)
                throw new IOException($"Registry key not found: {subkey}");
            return;
        }

        if (child.Children.Count > 0)
            throw new InvalidOperationException($"Registry key has subkeys: {subkey}");

        parent.Children.Remove(leaf);
    }

    public void DeleteSubKeyTree(string subkey, bool throwOnMissingSubKey = true)
    {
        EnsureWritable();
        var (parent, leaf) = FindParent(subkey);
        if (parent == null || !parent.Children.Remove(leaf))
        {
            if (throwOnMissingSubKey)
                throw new IOException($"Registry key not found: {subkey}");
        }
    }

    public object? GetValue(string? name)
        => _node.Values.TryGetValue(NormalizeValueName(name), out var value) ? value.Value : null;

    public RegistryValueKind GetValueKind(string? name)
        => _node.Values.TryGetValue(NormalizeValueName(name), out var value)
            ? value.Kind
            : throw new IOException($"Registry value not found: {name}");

    public string[] GetValueNames()
        => _node.Values.Keys.ToArray();

    public string[] GetSubKeyNames()
        => _node.Children.Keys.ToArray();

    public void SetValue(string? name, object value, RegistryValueKind valueKind = RegistryValueKind.String)
    {
        EnsureWritable();
        _node.Values[NormalizeValueName(name)] = new ValueEntry(value, valueKind);
    }

    public void DeleteValue(string? name, bool throwOnMissingValue = true)
    {
        EnsureWritable();
        if (!_node.Values.Remove(NormalizeValueName(name)) && throwOnMissingValue)
            throw new IOException($"Registry value not found: {name}");
    }

    public void Flush()
    {
    }

    public void Dispose()
    {
    }

    private Node? FindNode(string path, bool create)
    {
        var current = _node;
        foreach (var segment in SplitPath(path))
        {
            if (!current.Children.TryGetValue(segment, out var child))
            {
                if (!create)
                    return null;

                child = new Node($@"{current.Path}\{segment}");
                current.Children.Add(segment, child);
            }

            current = child;
        }

        return current;
    }

    private (Node? Parent, string Leaf) FindParent(string path)
    {
        var segments = SplitPath(path).ToArray();
        if (segments.Length == 0)
            return (null, string.Empty);

        var parentPath = string.Join('\\', segments.Take(segments.Length - 1));
        return (parentPath.Length == 0 ? _node : FindNode(parentPath, create: false), segments[^1]);
    }

    private void EnsureWritable()
    {
        if (!_writable)
            throw new UnauthorizedAccessException("Access denied.");
    }

    private static IEnumerable<string> SplitPath(string path)
        => path.Split('\\', StringSplitOptions.RemoveEmptyEntries);

    private static string NormalizeValueName(string? name)
        => name ?? string.Empty;

    private sealed class Node(string path)
    {
        public string Path { get; } = path;

        public Dictionary<string, Node> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ValueEntry> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ValueEntry(object Value, RegistryValueKind Kind);
}
