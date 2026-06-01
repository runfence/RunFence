using System.Security.Principal;
using Moq;
using RunFence.DragBridge;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowOwnerProcessTokenReaderTests
{
    private readonly Mock<IProcessOwnerSidReader> _ownerSidReader = new();
    private readonly Mock<IProcessAppContainerSidReader> _appContainerSidReader = new();
    private readonly Mock<IProcessPrivilegeStateReader> _privilegeStateReader = new();
    private readonly WindowOwnerProcessTokenReader _reader;

    public WindowOwnerProcessTokenReaderTests()
    {
        _reader = new WindowOwnerProcessTokenReader(
            _ownerSidReader.Object,
            _appContainerSidReader.Object,
            _privilegeStateReader.Object);
    }

    [Fact]
    public void TryGetTokenInfo_ComposesInfoFromInjectedReaders()
    {
        _ownerSidReader.Setup(r => r.TryGetProcessOwnerSid(123)).Returns("S-1-5-21-1-2-3-1001");
        _appContainerSidReader.Setup(r => r.TryGetProcessAppContainerSid(123)).Returns("S-1-15-2-1");
        _privilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(123, out It.Ref<int>.IsAny))
            .Returns((uint _, out int integrityLevel) =>
            {
                integrityLevel = 0x2000;
                return true;
            });
        _privilegeStateReader.Setup(r => r.TryGetProcessElevation(123, out It.Ref<bool>.IsAny))
            .Returns((uint _, out bool isElevated) =>
            {
                isElevated = true;
                return true;
            });

        var result = _reader.TryGetTokenInfo(123, out var info);

        Assert.True(result);
        Assert.Equal("S-1-5-21-1-2-3-1001", info.OwnerSid.Value);
        Assert.Equal("S-1-15-2-1", info.AppContainerSid?.Value);
        Assert.Equal(0x2000, info.IntegrityLevel);
        Assert.True(info.IsElevated);
    }

    [Fact]
    public void TryGetTokenInfo_InvalidOwnerSid_ReturnsFalse()
    {
        _ownerSidReader.Setup(r => r.TryGetProcessOwnerSid(123)).Returns("not-a-sid");

        var result = _reader.TryGetTokenInfo(123, out var info);

        Assert.False(result);
        Assert.Equal(default, info);
    }

    [Fact]
    public void TryGetTokenInfo_InvalidAppContainerSid_IgnoresIt()
    {
        _ownerSidReader.Setup(r => r.TryGetProcessOwnerSid(123)).Returns("S-1-5-21-1-2-3-1001");
        _appContainerSidReader.Setup(r => r.TryGetProcessAppContainerSid(123)).Returns("not-a-sid");
        _privilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(123, out It.Ref<int>.IsAny))
            .Returns((uint _, out int integrityLevel) =>
            {
                integrityLevel = 0x2000;
                return true;
            });

        var result = _reader.TryGetTokenInfo(123, out var info);

        Assert.True(result);
        Assert.Equal("S-1-5-21-1-2-3-1001", info.OwnerSid.Value);
        Assert.Null(info.AppContainerSid);
        Assert.Equal(0x2000, info.IntegrityLevel);
    }

    [Fact]
    public void TryGetTokenInfo_UnavailableIntegrity_ReturnsNullIntegrity()
    {
        _ownerSidReader.Setup(r => r.TryGetProcessOwnerSid(123)).Returns("S-1-5-21-1-2-3-1001");
        _appContainerSidReader.Setup(r => r.TryGetProcessAppContainerSid(123)).Returns((string?)null);
        _privilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(123, out It.Ref<int>.IsAny))
            .Returns((uint _, out int integrityLevel) =>
            {
                integrityLevel = 0;
                return false;
            });

        var result = _reader.TryGetTokenInfo(123, out var info);

        Assert.True(result);
        Assert.Equal("S-1-5-21-1-2-3-1001", info.OwnerSid.Value);
        Assert.Null(info.AppContainerSid);
        Assert.Null(info.IntegrityLevel);
    }

    [Fact]
    public void TryGetTokenInfo_UnavailableOwnerSid_ReturnsFalse()
    {
        _ownerSidReader.Setup(r => r.TryGetProcessOwnerSid(123)).Returns((string?)null);

        var result = _reader.TryGetTokenInfo(123, out var info);

        Assert.False(result);
        Assert.Equal(default, info);
    }

    [Fact]
    public void TryGetTokenInfo_UnavailableAppContainerSid_KeepsOwnerAndIntegrity()
    {
        _ownerSidReader.Setup(r => r.TryGetProcessOwnerSid(123)).Returns("S-1-5-21-1-2-3-1001");
        _appContainerSidReader.Setup(r => r.TryGetProcessAppContainerSid(123)).Returns((string?)null);
        _privilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(123, out It.Ref<int>.IsAny))
            .Returns((uint _, out int integrityLevel) =>
            {
                integrityLevel = 0x2000;
                return true;
            });

        var result = _reader.TryGetTokenInfo(123, out var info);

        Assert.True(result);
        Assert.Equal("S-1-5-21-1-2-3-1001", info.OwnerSid.Value);
        Assert.Null(info.AppContainerSid);
        Assert.Equal(0x2000, info.IntegrityLevel);
    }
}
