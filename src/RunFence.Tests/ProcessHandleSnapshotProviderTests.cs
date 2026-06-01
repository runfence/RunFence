using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Moq;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class ProcessHandleSnapshotProviderTests
{
    private static readonly SafeProcessHandle OwnerProcessHandle = new(new IntPtr(5000), ownsHandle: false);

    [Fact]
    public void GetJobHandleCandidates_GrowsBufferAndFiltersNonJobHandles()
    {
        var native = new FakeProcessHandleSnapshotNative();
        var jobApi = new Mock<IJobObjectApi>(MockBehavior.Strict);
        var typeNameReader = new FakeObjectTypeNameReader();
        var sut = new ProcessHandleSnapshotProvider(jobApi.Object, native, typeNameReader);

        native.QueryBehavior = (IntPtr buffer, int bufferSize, out int returnLength) =>
        {
            if (bufferSize == 4096)
            {
                returnLength = 8192;
                return SystemHandleNative.StatusInfoLengthMismatch;
            }

            returnLength = bufferSize;
            FakeProcessHandleSnapshotNative.WriteSnapshot(buffer, [new IntPtr(101), new IntPtr(102)]);
            return SystemHandleNative.StatusSuccess;
        };
        native.SameAccessDuplicates[new IntPtr(101)] = new IntPtr(201);
        native.SameAccessDuplicates[new IntPtr(102)] = new IntPtr(202);
        native.AccessDuplicates[new IntPtr(101)] = new IntPtr(301);

        typeNameReader.TypeNames[new IntPtr(201)] = "Job";
        typeNameReader.TypeNames[new IntPtr(202)] = "File";

        jobApi.Setup(a => a.CloseHandle(new IntPtr(201)));
        jobApi.Setup(a => a.CloseHandle(new IntPtr(202)));
        jobApi.Setup(a => a.CloseHandle(new IntPtr(301)));

        var result = sut.GetJobHandleCandidates(OwnerProcessHandle);

        Assert.Single(result);
        Assert.Equal(new IntPtr(301), result[0].Handle);
        Assert.Equal([4096, 8192], native.QueryBufferSizes);
        Assert.Equal(
            [ProcessJobManager.JobObjectReconnectAccess],
            native.DuplicateWithAccessDesiredAccesses);
        result[0].Dispose();
    }

    [Fact]
    public void GetJobHandleCandidates_SameAccessDuplicateFailure_SkipsEntry()
    {
        var native = CreateNativeWithHandles([new IntPtr(101)]);
        var jobApi = new Mock<IJobObjectApi>(MockBehavior.Strict);
        var typeNameReader = new FakeObjectTypeNameReader();
        var sut = new ProcessHandleSnapshotProvider(jobApi.Object, native, typeNameReader);

        var result = sut.GetJobHandleCandidates(OwnerProcessHandle);

        Assert.Empty(result);
        Assert.Empty(native.DuplicateWithAccessDesiredAccesses);
    }

    [Fact]
    public void GetJobHandleCandidates_ReconnectDuplicateFailure_SkipsEntry()
    {
        var native = CreateNativeWithHandles([new IntPtr(101)]);
        var jobApi = new Mock<IJobObjectApi>(MockBehavior.Strict);
        var typeNameReader = new FakeObjectTypeNameReader();
        var sut = new ProcessHandleSnapshotProvider(jobApi.Object, native, typeNameReader);

        native.SameAccessDuplicates[new IntPtr(101)] = new IntPtr(201);
        typeNameReader.TypeNames[new IntPtr(201)] = "Job";
        jobApi.Setup(a => a.CloseHandle(new IntPtr(201)));

        var result = sut.GetJobHandleCandidates(OwnerProcessHandle);

        Assert.Empty(result);
    }

    [Fact]
    public void GetJobHandleCandidates_DeduplicatesSameJobAndDisposesExtraHandle()
    {
        var native = CreateNativeWithHandles([new IntPtr(101), new IntPtr(102)]);
        var jobApi = new Mock<IJobObjectApi>(MockBehavior.Strict);
        var typeNameReader = new FakeObjectTypeNameReader();
        var sut = new ProcessHandleSnapshotProvider(jobApi.Object, native, typeNameReader);
        var sequence = new MockSequence();

        native.SameAccessDuplicates[new IntPtr(101)] = new IntPtr(201);
        native.SameAccessDuplicates[new IntPtr(102)] = new IntPtr(202);
        native.AccessDuplicates[new IntPtr(101)] = new IntPtr(301);
        native.AccessDuplicates[new IntPtr(102)] = new IntPtr(302);

        typeNameReader.TypeNames[new IntPtr(201)] = "Job";
        typeNameReader.TypeNames[new IntPtr(202)] = "Job";

        jobApi.InSequence(sequence).Setup(a => a.CloseHandle(new IntPtr(201)));
        jobApi.InSequence(sequence).Setup(a => a.CloseHandle(new IntPtr(202)));
        jobApi.Setup(a => a.AreSameJobObject(new IntPtr(301), new IntPtr(302))).Returns(true);
        jobApi.InSequence(sequence).Setup(a => a.CloseHandle(new IntPtr(302)));
        jobApi.InSequence(sequence).Setup(a => a.CloseHandle(new IntPtr(301)));

        var result = sut.GetJobHandleCandidates(OwnerProcessHandle);

        Assert.Single(result);
        Assert.Equal(new IntPtr(301), result[0].Handle);
        Assert.Equal(
            [ProcessJobManager.JobObjectReconnectAccess, ProcessJobManager.JobObjectReconnectAccess],
            native.DuplicateWithAccessDesiredAccesses);
        jobApi.Verify(a => a.CloseHandle(new IntPtr(302)), Times.Once);
        jobApi.Verify(a => a.CloseHandle(new IntPtr(301)), Times.Never);

        result[0].Dispose();
        jobApi.Verify(a => a.CloseHandle(new IntPtr(201)), Times.Once);
        jobApi.Verify(a => a.CloseHandle(new IntPtr(202)), Times.Once);
        jobApi.Verify(a => a.CloseHandle(new IntPtr(302)), Times.Once);
        jobApi.Verify(a => a.CloseHandle(new IntPtr(301)), Times.Once);
    }

    [Fact]
    public void GetJobHandleCandidates_WhenEnumerationThrows_DisposesAccumulatedCandidates()
    {
        var native = CreateNativeWithHandles([new IntPtr(101), new IntPtr(102)]);
        var jobApi = new Mock<IJobObjectApi>(MockBehavior.Strict);
        var typeNameReader = new FakeObjectTypeNameReader();
        var sut = new ProcessHandleSnapshotProvider(jobApi.Object, native, typeNameReader);

        native.SameAccessDuplicates[new IntPtr(101)] = new IntPtr(201);
        native.SameAccessDuplicates[new IntPtr(102)] = new IntPtr(202);
        native.AccessDuplicates[new IntPtr(101)] = new IntPtr(301);

        typeNameReader.TypeNames[new IntPtr(201)] = "Job";
        typeNameReader.Exceptions[new IntPtr(202)] = new InvalidOperationException("boom");

        jobApi.Setup(a => a.CloseHandle(new IntPtr(201)));
        jobApi.Setup(a => a.CloseHandle(new IntPtr(202)));
        jobApi.Setup(a => a.CloseHandle(new IntPtr(301)));

        var ex = Assert.Throws<InvalidOperationException>(() => sut.GetJobHandleCandidates(OwnerProcessHandle));

        Assert.Equal("boom", ex.Message);
        jobApi.Verify(a => a.CloseHandle(new IntPtr(201)), Times.Once);
        jobApi.Verify(a => a.CloseHandle(new IntPtr(202)), Times.Once);
        jobApi.Verify(a => a.CloseHandle(new IntPtr(301)), Times.Once);
    }

    private static FakeProcessHandleSnapshotNative CreateNativeWithHandles(IntPtr[] handles)
    {
        var native = new FakeProcessHandleSnapshotNative();
        native.QueryBehavior = (IntPtr buffer, int bufferSize, out int returnLength) =>
        {
            returnLength = bufferSize;
            FakeProcessHandleSnapshotNative.WriteSnapshot(buffer, handles);
            return SystemHandleNative.StatusSuccess;
        };
        return native;
    }

    private sealed class FakeProcessHandleSnapshotNative : IProcessHandleSnapshotNative
    {
        public QueryProcessHandleInformationCallback? QueryBehavior { get; set; }
        public Dictionary<IntPtr, IntPtr> SameAccessDuplicates { get; } = [];
        public Dictionary<IntPtr, IntPtr> AccessDuplicates { get; } = [];
        public List<int> QueryBufferSizes { get; } = [];
        public List<uint> DuplicateWithAccessDesiredAccesses { get; } = [];

        public int QueryProcessHandleInformation(
            SafeProcessHandle ownerProcessHandle,
            IntPtr buffer,
            int bufferSize,
            out int returnLength)
        {
            QueryBufferSizes.Add(bufferSize);
            Assert.NotNull(QueryBehavior);
            return QueryBehavior(buffer, bufferSize, out returnLength);
        }

        public bool DuplicateSameAccess(
            SafeProcessHandle ownerProcessHandle,
            IntPtr sourceHandle,
            out IntPtr duplicatedHandle) =>
            SameAccessDuplicates.TryGetValue(sourceHandle, out duplicatedHandle);

        public bool DuplicateWithAccess(
            SafeProcessHandle ownerProcessHandle,
            IntPtr sourceHandle,
            uint desiredAccess,
            out IntPtr duplicatedHandle)
        {
            DuplicateWithAccessDesiredAccesses.Add(desiredAccess);
            if (desiredAccess != ProcessJobManager.JobObjectReconnectAccess)
            {
                duplicatedHandle = IntPtr.Zero;
                return false;
            }

            return AccessDuplicates.TryGetValue(sourceHandle, out duplicatedHandle);
        }

        public static void WriteSnapshot(IntPtr buffer, IReadOnlyList<IntPtr> handles)
        {
            var headerSize = Marshal.SizeOf<ProcessHandleNative.PROCESS_HANDLE_SNAPSHOT_INFORMATION>();
            var entrySize = Marshal.SizeOf<ProcessHandleNative.PROCESS_HANDLE_TABLE_ENTRY_INFO>();
            var totalSize = headerSize + (entrySize * handles.Count);
            Marshal.Copy(new byte[totalSize], 0, buffer, totalSize);
            Marshal.WriteIntPtr(buffer, new IntPtr(handles.Count));
            for (var i = 0; i < handles.Count; i++)
            {
                var entryPtr = IntPtr.Add(buffer, headerSize + (i * entrySize));
                Marshal.WriteIntPtr(entryPtr, handles[i]);
            }
        }

        public delegate int QueryProcessHandleInformationCallback(IntPtr buffer, int bufferSize, out int returnLength);
    }

    private sealed class FakeObjectTypeNameReader : IObjectTypeNameReader
    {
        public Dictionary<IntPtr, string> TypeNames { get; } = [];
        public Dictionary<IntPtr, Exception> Exceptions { get; } = [];

        public bool TryGetObjectTypeName(IntPtr handle, out string typeName)
        {
            if (Exceptions.TryGetValue(handle, out var ex))
                throw ex;

            return TypeNames.TryGetValue(handle, out typeName!);
        }
    }
}
