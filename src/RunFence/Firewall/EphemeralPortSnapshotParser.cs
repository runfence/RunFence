using System.Buffers.Binary;

namespace RunFence.Firewall;

public class EphemeralPortSnapshotParser
{
    public IReadOnlyList<(int Port, int Pid)> Parse(ReadOnlySpan<byte> tableBytes, EphemeralPortTableKind tableKind)
    {
        if (tableBytes.Length < sizeof(int))
            return [];

        var (stride, portOffset, pidOffset) = GetLayout(tableKind);
        var entryCount = BinaryPrimitives.ReadInt32LittleEndian(tableBytes);
        if (entryCount <= 0)
            return [];

        var maxEntryCount = (tableBytes.Length - sizeof(int)) / stride;
        var boundedEntryCount = Math.Min(entryCount, maxEntryCount);
        if (boundedEntryCount <= 0)
            return [];

        var result = new List<(int Port, int Pid)>(boundedEntryCount);

        for (var i = 0; i < boundedEntryCount; i++)
        {
            var rowOffset = sizeof(int) + ((long)i * stride);
            if (!TryReadRow(tableBytes, rowOffset, portOffset, pidOffset, out var row))
                break;

            result.Add(row);
        }

        return result;
    }

    private static (int Stride, int PortOffset, int PidOffset) GetLayout(EphemeralPortTableKind tableKind) =>
        tableKind switch
        {
            EphemeralPortTableKind.TcpIpv4 => (24, 8, 20),
            EphemeralPortTableKind.TcpIpv6 => (56, 20, 52),
            EphemeralPortTableKind.UdpIpv4 => (12, 4, 8),
            EphemeralPortTableKind.UdpIpv6 => (28, 20, 24),
            _ => throw new ArgumentOutOfRangeException(nameof(tableKind), tableKind, null)
        };

    private static bool TryReadRow(
        ReadOnlySpan<byte> tableBytes,
        long rowOffset,
        int portOffset,
        int pidOffset,
        out (int Port, int Pid) row)
    {
        row = default;

        if (rowOffset < sizeof(int)
            || rowOffset + portOffset + sizeof(ushort) > tableBytes.Length
            || rowOffset + pidOffset + sizeof(int) > tableBytes.Length)
            return false;

        var port = BinaryPrimitives.ReadUInt16BigEndian(tableBytes.Slice((int)rowOffset + portOffset, sizeof(ushort)));
        var pid = BinaryPrimitives.ReadInt32LittleEndian(tableBytes.Slice((int)rowOffset + pidOffset, sizeof(int)));
        row = (port, pid);
        return true;
    }
}
