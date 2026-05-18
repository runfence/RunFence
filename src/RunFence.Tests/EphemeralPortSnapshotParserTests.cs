using System.Buffers.Binary;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class EphemeralPortSnapshotParserTests
{
    private readonly EphemeralPortSnapshotParser _parser = new();

    [Theory]
    [InlineData(EphemeralPortTableKind.TcpIpv4, 24, 8, 20)]
    [InlineData(EphemeralPortTableKind.TcpIpv6, 56, 20, 52)]
    [InlineData(EphemeralPortTableKind.UdpIpv4, 12, 4, 8)]
    [InlineData(EphemeralPortTableKind.UdpIpv6, 28, 20, 24)]
    public void Parse_AllTableLayouts_UsesExpectedOffsets(EphemeralPortTableKind tableKind, int stride, int portOffset, int pidOffset)
    {
        var bytes = BuildTableBytes(stride, row =>
        {
            WritePort(row, portOffset, 49160);
            WritePid(row, pidOffset, 1234);
        });

        var result = _parser.Parse(bytes, tableKind);

        Assert.Equal([(49160, 1234)], result);
    }

    [Fact]
    public void Parse_PortBytesAreNetworkOrdered()
    {
        var bytes = BuildTableBytes(24, row =>
        {
            WritePort(row, 8, 80);
            WritePid(row, 20, 99);
        });

        var result = _parser.Parse(bytes, EphemeralPortTableKind.TcpIpv4);

        Assert.Equal([(80, 99)], result);
    }

    [Fact]
    public void Parse_ShortHeader_ReturnsEmpty()
    {
        var result = _parser.Parse([0x01, 0x02, 0x03], EphemeralPortTableKind.UdpIpv4);

        Assert.Empty(result);
    }

    [Fact]
    public void Parse_TruncatedRow_StopsWithoutThrowing()
    {
        var bytes = new byte[4 + 8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), 1);

        var result = _parser.Parse(bytes, EphemeralPortTableKind.UdpIpv4);

        Assert.Empty(result);
    }

    [Fact]
    public void Parse_HeaderCountLargerThanBuffer_ClampsToAvailableRows()
    {
        var bytes = BuildTableBytes(24, row =>
        {
            WritePort(row, 8, 443);
            WritePid(row, 20, 321);
        });
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), 1000);

        var result = _parser.Parse(bytes, EphemeralPortTableKind.TcpIpv4);

        Assert.Equal([(443, 321)], result);
    }

    private static byte[] BuildTableBytes(int stride, Action<byte[]> writeRow)
    {
        var bytes = new byte[4 + stride];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), 1);
        var row = new byte[stride];
        writeRow(row);
        row.CopyTo(bytes, 4);
        return bytes;
    }

    private static void WritePort(byte[] row, int offset, ushort port)
    {
        BinaryPrimitives.WriteUInt16BigEndian(row.AsSpan(offset, 2), port);
    }

    private static void WritePid(byte[] row, int offset, int pid)
    {
        BinaryPrimitives.WriteInt32LittleEndian(row.AsSpan(offset, 4), pid);
    }
}
