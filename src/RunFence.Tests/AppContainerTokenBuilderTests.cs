using System.ComponentModel;
using Moq;
using RunFence.Core;
using RunFence.Launch.Container;
using Xunit;

namespace RunFence.Tests;

public class AppContainerTokenBuilderTests
{
    [Fact]
    public void Build_ContainerSidConversionFailure_DoesNotCleanupUnallocatedResources()
    {
        var nativeApi = new FakeAppContainerTokenNativeApi
        {
            ContainerSidConversionException = new Win32Exception(1337, "container sid failed")
        };
        var builder = CreateBuilder(nativeApi);

        var exception = Assert.Throws<Win32Exception>(() => builder.Build((IntPtr)10, "S-1-15-2-42", null));

        Assert.Equal(1337, exception.NativeErrorCode);
        Assert.Empty(nativeApi.LocalFreedPointers);
        Assert.Empty(nativeApi.FreedCapabilityArrays);
        Assert.Empty(nativeApi.ClosedHandles);
    }

    [Fact]
    public void Build_InvalidCapabilitySid_SkipsInvalidSid_AndDisposesOnlyValidAllocations()
    {
        var nativeApi = new FakeAppContainerTokenNativeApi
        {
            NamedObjectPath = @"\Sessions\1\AppContainerNamedObjects\Test"
        };
        nativeApi.CapabilitySidConversions["S-1-15-3-valid"] = ((IntPtr)201, 0);
        nativeApi.CapabilitySidConversions["S-1-15-3-invalid"] = (IntPtr.Zero, 1337);

        var builder = CreateBuilder(nativeApi, out var log);

        using var context = builder.Build(
            (IntPtr)10,
            "S-1-15-2-42",
            ["S-1-15-3-valid", "S-1-15-3-invalid"]);

        Assert.Equal(new[] { (IntPtr)201 }, context.CapabilitySidPointers);
        log.Verify(
            l => l.Warn("AppContainerProcessLauncher: Could not convert capability SID 'S-1-15-3-invalid', skipping"),
            Times.Once);
        Assert.Empty(nativeApi.LocalFreedPointers);

        context.Dispose();

        Assert.Equal(new[] { (IntPtr)201, (IntPtr)101 }, nativeApi.LocalFreedPointers);
        Assert.Equal(new[] { (IntPtr)301 }, nativeApi.FreedCapabilityArrays);
        Assert.Equal(new[] { (IntPtr)401, (IntPtr)3010 }, nativeApi.ClosedHandles);
    }

    [Theory]
    [InlineData(AppContainerTokenBuilderFailurePoint.CapabilityArrayAllocation)]
    [InlineData(AppContainerTokenBuilderFailurePoint.DuplicateToken)]
    [InlineData(AppContainerTokenBuilderFailurePoint.CreateToken)]
    [InlineData(AppContainerTokenBuilderFailurePoint.DefaultDacl)]
    [InlineData(AppContainerTokenBuilderFailurePoint.TokenSidLookup)]
    public void Build_FailurePaths_CleanupAllocatedResourcesExactlyOnce(AppContainerTokenBuilderFailurePoint failurePoint)
    {
        var nativeApi = new FakeAppContainerTokenNativeApi
        {
            FailurePoint = failurePoint
        };
        nativeApi.CapabilitySidConversions["S-1-15-3-valid"] = ((IntPtr)201, 0);
        var builder = CreateBuilder(nativeApi);

        Assert.ThrowsAny<Exception>(() => builder.Build(
            (IntPtr)10,
            "S-1-15-2-42",
            ["S-1-15-3-valid"]));

        Assert.Equal(ExpectedLocalFrees(failurePoint), nativeApi.LocalFreedPointers);
        Assert.Equal(ExpectedCapabilityArrayFrees(failurePoint), nativeApi.FreedCapabilityArrays);
        Assert.Equal(ExpectedClosedHandles(failurePoint), nativeApi.ClosedHandles);
    }

    [Fact]
    public void Build_NamedObjectLookupFailure_LogsWarning_AndStillReturnsContext()
    {
        var nativeApi = new FakeAppContainerTokenNativeApi
        {
            NamedObjectPathErrorCode = 5
        };
        var builder = CreateBuilder(nativeApi, out var log);

        using var context = builder.Build((IntPtr)10, "S-1-15-2-42", null);

        Assert.Equal((IntPtr)3010, context.DuplicatedExplorerToken);
        Assert.Equal((IntPtr)401, context.AppContainerToken);
        log.Verify(
            l => l.Warn("AppContainerProcessLauncher: GetAppContainerNamedObjectPath failed (error 5) - named objects may not work"),
            Times.Once);
    }

    [Fact]
    public void Build_NamedObjectLookupThrows_LogsWarning_AndStillReturnsContext()
    {
        var nativeApi = new FakeAppContainerTokenNativeApi
        {
            NamedObjectPathException = new InvalidOperationException("named object lookup blew up")
        };
        var builder = CreateBuilder(nativeApi, out var log);

        using var context = builder.Build((IntPtr)10, "S-1-15-2-42", null);

        Assert.Equal((IntPtr)3010, context.DuplicatedExplorerToken);
        Assert.Equal((IntPtr)401, context.AppContainerToken);
        log.Verify(
            l => l.Warn("AppContainerProcessLauncher: Named object path check failed: named object lookup blew up"),
            Times.Once);
    }

    [Fact]
    public void Build_Success_DisposeTwice_DoesNotDoubleCleanup()
    {
        var nativeApi = new FakeAppContainerTokenNativeApi();
        nativeApi.CapabilitySidConversions["S-1-15-3-valid"] = ((IntPtr)201, 0);
        var builder = CreateBuilder(nativeApi);

        var context = builder.Build(
            (IntPtr)10,
            "S-1-15-2-42",
            ["S-1-15-3-valid"]);

        context.Dispose();
        context.Dispose();

        Assert.Equal(new[] { (IntPtr)201, (IntPtr)101 }, nativeApi.LocalFreedPointers);
        Assert.Equal(new[] { (IntPtr)301 }, nativeApi.FreedCapabilityArrays);
        Assert.Equal(new[] { (IntPtr)401, (IntPtr)3010 }, nativeApi.ClosedHandles);
    }

    private static AppContainerTokenBuilder CreateBuilder(
        FakeAppContainerTokenNativeApi nativeApi,
        out Mock<ILoggingService> log)
    {
        log = new Mock<ILoggingService>();
        return new AppContainerTokenBuilder(log.Object, nativeApi);
    }

    private static AppContainerTokenBuilder CreateBuilder(FakeAppContainerTokenNativeApi nativeApi)
        => new(new Mock<ILoggingService>().Object, nativeApi);

    private static IntPtr[] ExpectedLocalFrees(AppContainerTokenBuilderFailurePoint failurePoint)
        => failurePoint switch
        {
            AppContainerTokenBuilderFailurePoint.CapabilityArrayAllocation => [(IntPtr)201, (IntPtr)101],
            AppContainerTokenBuilderFailurePoint.DuplicateToken => [(IntPtr)201, (IntPtr)101],
            AppContainerTokenBuilderFailurePoint.CreateToken => [(IntPtr)201, (IntPtr)101],
            AppContainerTokenBuilderFailurePoint.DefaultDacl => [(IntPtr)201, (IntPtr)101],
            AppContainerTokenBuilderFailurePoint.TokenSidLookup => [(IntPtr)201, (IntPtr)101],
            _ => throw new ArgumentOutOfRangeException(nameof(failurePoint), failurePoint, null)
        };

    private static IntPtr[] ExpectedCapabilityArrayFrees(AppContainerTokenBuilderFailurePoint failurePoint)
        => failurePoint switch
        {
            AppContainerTokenBuilderFailurePoint.CapabilityArrayAllocation => Array.Empty<IntPtr>(),
            AppContainerTokenBuilderFailurePoint.DuplicateToken => [(IntPtr)301],
            AppContainerTokenBuilderFailurePoint.CreateToken => [(IntPtr)301],
            AppContainerTokenBuilderFailurePoint.DefaultDacl => [(IntPtr)301],
            AppContainerTokenBuilderFailurePoint.TokenSidLookup => [(IntPtr)301],
            _ => throw new ArgumentOutOfRangeException(nameof(failurePoint), failurePoint, null)
        };

    private static IntPtr[] ExpectedClosedHandles(AppContainerTokenBuilderFailurePoint failurePoint)
        => failurePoint switch
        {
            AppContainerTokenBuilderFailurePoint.CapabilityArrayAllocation => Array.Empty<IntPtr>(),
            AppContainerTokenBuilderFailurePoint.DuplicateToken => Array.Empty<IntPtr>(),
            AppContainerTokenBuilderFailurePoint.CreateToken => [(IntPtr)3010],
            AppContainerTokenBuilderFailurePoint.DefaultDacl => [(IntPtr)401, (IntPtr)3010],
            AppContainerTokenBuilderFailurePoint.TokenSidLookup => [(IntPtr)401, (IntPtr)3010],
            _ => throw new ArgumentOutOfRangeException(nameof(failurePoint), failurePoint, null)
        };

    private sealed class FakeAppContainerTokenNativeApi : IAppContainerTokenNativeApi
    {
        public Dictionary<string, (IntPtr Pointer, int ErrorCode)> CapabilitySidConversions { get; } = [];

        public Win32Exception? ContainerSidConversionException { get; set; }

        public AppContainerTokenBuilderFailurePoint? FailurePoint { get; set; }

        public Exception? NamedObjectPathException { get; set; }

        public int? NamedObjectPathErrorCode { get; set; }

        public string NamedObjectPath { get; set; } = @"\Sessions\1\AppContainerNamedObjects\Default";

        public List<IntPtr> LocalFreedPointers { get; } = [];

        public List<IntPtr> FreedCapabilityArrays { get; } = [];

        public List<IntPtr> ClosedHandles { get; } = [];

        public IntPtr ConvertRequiredStringSidToSid(string sid)
            => ContainerSidConversionException is null
                ? (IntPtr)101
                : throw ContainerSidConversionException;

        public bool TryConvertStringSidToSid(string sid, out IntPtr pointer, out int errorCode)
        {
            if (CapabilitySidConversions.TryGetValue(sid, out var result))
            {
                pointer = result.Pointer;
                errorCode = result.ErrorCode;
                return result.Pointer != IntPtr.Zero;
            }

            pointer = IntPtr.Zero;
            errorCode = 87;
            return false;
        }

        public void LocalFree(IntPtr pointer)
            => LocalFreedPointers.Add(pointer);

        public IntPtr DuplicateToken(IntPtr token)
        {
            if (FailurePoint == AppContainerTokenBuilderFailurePoint.DuplicateToken)
                throw new Win32Exception(5, "duplicate failed");

            return (IntPtr)3010;
        }

        public IntPtr CreateAppContainerToken(
            IntPtr duplicatedExplorerToken,
            ref AppContainerProcessLauncherNative.SECURITY_CAPABILITIES capabilities)
        {
            if (FailurePoint == AppContainerTokenBuilderFailurePoint.CreateToken)
                throw new Win32Exception(8, "CreateAppContainerToken failed");

            return (IntPtr)401;
        }

        public void SetRestrictiveDefaultDacl(IntPtr appContainerToken, string containerSid, string interactiveUserSid)
        {
            if (FailurePoint == AppContainerTokenBuilderFailurePoint.DefaultDacl)
                throw new InvalidOperationException("dacl failed");
        }

        public string GetRequiredTokenSidValue(IntPtr token)
        {
            if (FailurePoint == AppContainerTokenBuilderFailurePoint.TokenSidLookup)
                throw new InvalidOperationException("sid lookup failed");

            return "S-1-5-21-100-100-100-1000";
        }

        public IntPtr AllocateCapabilityArray(IReadOnlyList<IntPtr> capabilitySidPointers)
        {
            if (FailurePoint == AppContainerTokenBuilderFailurePoint.CapabilityArrayAllocation)
                throw new InvalidOperationException("capability array failed");

            return capabilitySidPointers.Count == 0 ? IntPtr.Zero : (IntPtr)301;
        }

        public void FreeCapabilityArray(IntPtr pointer)
            => FreedCapabilityArrays.Add(pointer);

        public bool TryGetAppContainerNamedObjectPath(IntPtr appContainerToken, out string path, out int errorCode)
        {
            if (NamedObjectPathException is not null)
                throw NamedObjectPathException;

            path = NamedObjectPath;
            errorCode = NamedObjectPathErrorCode ?? 0;
            return NamedObjectPathErrorCode is null;
        }

        public void CloseHandle(IntPtr handle)
            => ClosedHandles.Add(handle);
    }

    public enum AppContainerTokenBuilderFailurePoint
    {
        CapabilityArrayAllocation,
        DuplicateToken,
        CreateToken,
        DefaultDacl,
        TokenSidLookup
    }
}
