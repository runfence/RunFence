using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsJobObjectApiTests
{
    private const int ErrorMoreData = 234;

    [Fact]
    public void IsProcessInJob_WhenNativeFails_ReturnsNull()
    {
        var native = new FakeWindowsJobObjectNative
        {
            IsProcessInJobResult = false,
        };
        var api = new WindowsJobObjectApi(native);

        var result = api.IsProcessInJob(new IntPtr(1), new IntPtr(2));

        Assert.Null(result);
    }

    [Fact]
    public void QueryProcessIds_WhenNativeNeedsLargerBuffer_RetriesAndReturnsIds()
    {
        var native = new FakeWindowsJobObjectNative();
        var processIds = Enumerable.Range(1000, 96).ToArray();
        var expectedGrownBufferLength = 8 + processIds.Length * IntPtr.Size;
        native.QueryInformationJobObjectBehavior = (IntPtr jobHandle, int infoClass, IntPtr buffer, uint bufferLength, out uint returnLength) =>
        {
            if (native.QueryCallCount == 0)
            {
                Marshal.WriteInt32(buffer, processIds.Length);
                Marshal.WriteInt32(buffer, 4, 0);
                native.LastWin32Error = ErrorMoreData;
                native.QueryCallCount++;
                returnLength = 0;
                return false;
            }

            Assert.Equal((uint)expectedGrownBufferLength, bufferLength);
            native.QueryCallCount++;
            returnLength = 0;
            FakeWindowsJobObjectNative.WriteProcessIdList(buffer, processIds);
            return true;
        };

        var api = new WindowsJobObjectApi(native);

        var result = api.QueryProcessIds(new IntPtr(1));

        Assert.NotNull(result);
        Assert.Equal(processIds, result!.OrderBy(x => x).ToArray());
        Assert.Equal(2, native.QueryCallCount);
        Assert.Equal(
            [unchecked((uint)(8 + 64 * IntPtr.Size)), unchecked((uint)expectedGrownBufferLength)],
            native.QueryBufferLengths);
    }

    [Fact]
    public void QueryProcessIds_WhenNativeFailsWithoutMoreData_ReturnsNull()
    {
        var native = new FakeWindowsJobObjectNative
        {
            LastWin32Error = 5,
            QueryInformationJobObjectBehavior = (IntPtr jobHandle, int infoClass, IntPtr buffer, uint bufferLength, out uint returnLength) =>
            {
                returnLength = 0;
                return false;
            },
        };
        var api = new WindowsJobObjectApi(native);

        var result = api.QueryProcessIds(new IntPtr(1));

        Assert.Null(result);
    }

    [Fact]
    public void QueryUiRestrictions_ReadsFourByteFlags()
    {
        var native = new FakeWindowsJobObjectNative
        {
            QueryInformationJobObjectBehavior = (IntPtr jobHandle, int infoClass, IntPtr buffer, uint bufferLength, out uint returnLength) =>
            {
                Marshal.WriteInt32(buffer, unchecked((int)0x12345678u));
                returnLength = 4;
                return true;
            },
        };
        var api = new WindowsJobObjectApi(native);

        var result = api.QueryUiRestrictions(new IntPtr(1));

        Assert.Equal(0x12345678u, result);
    }

    [Fact]
    public void QueryUiRestrictions_WhenNativeFails_ReturnsNull()
    {
        var native = new FakeWindowsJobObjectNative
        {
            QueryInformationJobObjectBehavior = (IntPtr jobHandle, int infoClass, IntPtr buffer, uint bufferLength, out uint returnLength) =>
            {
                returnLength = 0;
                return false;
            },
        };
        var api = new WindowsJobObjectApi(native);

        Assert.Null(api.QueryUiRestrictions(new IntPtr(1)));
    }

    [Fact]
    public void SetUiRestrictions_WritesFourByteFlags()
    {
        uint capturedFlags = 0;
        var native = new FakeWindowsJobObjectNative
        {
            SetInformationJobObjectBehavior = (IntPtr jobHandle, int infoClass, IntPtr buffer, uint bufferLength) =>
            {
                capturedFlags = unchecked((uint)Marshal.ReadInt32(buffer));
                return true;
            },
        };
        var api = new WindowsJobObjectApi(native);

        var result = api.SetUiRestrictions(new IntPtr(1), 0xAABBCCDD);

        Assert.True(result);
        Assert.Equal(0xAABBCCDDu, capturedFlags);
    }

    [Fact]
    public void QueryBasicLimitFlags_ExtractsLimitFlags()
    {
        var native = new FakeWindowsJobObjectNative
        {
            QueryInformationJobObjectBehavior = (IntPtr jobHandle, int infoClass, IntPtr buffer, uint bufferLength, out uint returnLength) =>
            {
                FakeWindowsJobObjectNative.WriteBasicLimitFlags(buffer, 0x1234u);
                returnLength = (uint)Marshal.SizeOf<FakeWindowsJobObjectNative.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                return true;
            },
        };
        var api = new WindowsJobObjectApi(native);

        var result = api.QueryBasicLimitFlags(new IntPtr(1));

        Assert.Equal(0x1234u, result);
    }

    [Fact]
    public void GetSecuritySnapshot_ParsesOwnerAndAccessEntriesAndFreesBuffers()
    {
        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        const string sddl = "O:BAD:(A;;GA;;;SY)(A;;GR;;;BA)";
        var securityDescriptor = new IntPtr(300);
        var sddlPointer = Marshal.StringToHGlobalUni(sddl);
        var native = new FakeWindowsJobObjectNative
        {
            GetSecurityInfoBehavior = (IntPtr handle, FileSecurityNative.SE_OBJECT_TYPE objectType, FileSecurityNative.SECURITY_INFORMATION securityInformation, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor) =>
            {
                owner = IntPtr.Zero;
                group = IntPtr.Zero;
                dacl = IntPtr.Zero;
                sacl = IntPtr.Zero;
                descriptor = securityDescriptor;
                return 0;
            },
            ConvertSecurityDescriptorBehavior = (IntPtr securityDescriptorValue, uint revision, FileSecurityNative.SECURITY_INFORMATION securityInformation, out IntPtr stringDescriptor, out uint length) =>
            {
                stringDescriptor = sddlPointer;
                length = (uint)sddl.Length;
                return true;
            },
        };
        var api = new WindowsJobObjectApi(native);

        var snapshot = api.GetSecuritySnapshot(new IntPtr(1));

        Assert.NotNull(snapshot);
        Assert.Equal(adminSid, snapshot!.Owner);
        Assert.True(snapshot.HasDiscretionaryAcl);
        Assert.Collection(
            snapshot.AccessEntries,
            entry =>
            {
                Assert.Equal(systemSid, entry.Identity);
                Assert.True(entry.IsAllow);
            },
            entry =>
            {
                Assert.Equal(adminSid, entry.Identity);
                Assert.True(entry.IsAllow);
            });
        Assert.Contains(securityDescriptor, native.LocalFreedPointers);
        Assert.Contains(sddlPointer, native.LocalFreedPointers);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void GetSecuritySnapshot_WhenNativeSecurityConversionFails_ReturnsNull(bool failGetSecurityInfo, bool failSddlConversion)
    {
        var securityDescriptor = new IntPtr(301);
        var native = new FakeWindowsJobObjectNative
        {
            GetSecurityInfoBehavior = (IntPtr handle, FileSecurityNative.SE_OBJECT_TYPE objectType, FileSecurityNative.SECURITY_INFORMATION securityInformation, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor) =>
            {
                owner = IntPtr.Zero;
                group = IntPtr.Zero;
                dacl = IntPtr.Zero;
                sacl = IntPtr.Zero;
                descriptor = failGetSecurityInfo ? IntPtr.Zero : securityDescriptor;
                return failGetSecurityInfo ? 5 : 0;
            },
            ConvertSecurityDescriptorBehavior = (IntPtr securityDescriptorValue, uint revision, FileSecurityNative.SECURITY_INFORMATION securityInformation, out IntPtr stringDescriptor, out uint length) =>
            {
                stringDescriptor = IntPtr.Zero;
                length = 0;
                return !failSddlConversion;
            },
        };
        var api = new WindowsJobObjectApi(native);

        var snapshot = api.GetSecuritySnapshot(new IntPtr(1));

        Assert.Null(snapshot);
        if (failGetSecurityInfo)
            Assert.DoesNotContain(securityDescriptor, native.LocalFreedPointers);
        else
            Assert.Contains(securityDescriptor, native.LocalFreedPointers);
    }

    [Fact]
    public void DuplicateHandleToProcess_DefaultInterfaceOverload_DelegatesToOutOverload()
    {
        var api = new JobObjectApiWithDuplicateOnly(
            sourceHandle: new IntPtr(10),
            targetHandle: new IntPtr(20),
            duplicatedHandle: new IntPtr(30));

        Assert.True(((IJobObjectApi)api).DuplicateHandleToProcess(
            api.SourceHandle,
            api.SourceHandle,
            api.TargetHandle,
            1234));

        Assert.True(api.OutOverloadCalled);
        Assert.Equal(api.DuplicatedHandle, api.CapturedHandle);
    }

    private sealed class FakeWindowsJobObjectNative : IWindowsJobObjectNative
    {
        public int LastWin32Error { get; set; }
        public int QueryCallCount { get; set; }
        public List<uint> QueryBufferLengths { get; } = [];
        public QueryInformationJobObjectDelegate? QueryInformationJobObjectBehavior { get; set; }
        public SetInformationJobObjectDelegate? SetInformationJobObjectBehavior { get; set; }
        public GetSecurityInfoDelegate? GetSecurityInfoBehavior { get; set; }
        public ConvertSecurityDescriptorDelegate? ConvertSecurityDescriptorBehavior { get; set; }
        public bool IsProcessInJobResult { get; set; } = true;
        public bool IsProcessInJobValue { get; set; }
        public List<IntPtr> LocalFreedPointers { get; } = [];

        public IntPtr CreateJobObject(string? name) => IntPtr.Zero;
        public IntPtr CreateJobObjectWithSecurityDescriptor(string? name, string securityDescriptorSddl) => IntPtr.Zero;
        public IntPtr OpenJobObject(uint desiredAccess, bool inheritHandle, string name) => IntPtr.Zero;
        public bool AssignProcessToJobObject(IntPtr jobHandle, IntPtr processHandle) => false;

        public bool IsProcessInJob(IntPtr processHandle, IntPtr jobHandle, out bool result)
        {
            result = IsProcessInJobValue;
            return IsProcessInJobResult;
        }

        public bool CompareObjectHandles(IntPtr firstHandle, IntPtr secondHandle) => firstHandle == secondHandle;

        public bool QueryInformationJobObject(
            IntPtr jobHandle,
            int jobObjectInfoClass,
            IntPtr buffer,
            uint bufferLength,
            out uint returnLength)
        {
            QueryBufferLengths.Add(bufferLength);
            if (QueryInformationJobObjectBehavior != null)
                return QueryInformationJobObjectBehavior(jobHandle, jobObjectInfoClass, buffer, bufferLength, out returnLength);

            returnLength = 0;
            return false;
        }

        public bool SetInformationJobObject(
            IntPtr jobHandle,
            int jobObjectInfoClass,
            IntPtr buffer,
            uint bufferLength) =>
            SetInformationJobObjectBehavior?.Invoke(jobHandle, jobObjectInfoClass, buffer, bufferLength) ?? false;

        public bool DuplicateHandleToProcess(
            IntPtr sourceProcessHandle,
            IntPtr sourceHandle,
            IntPtr targetProcessHandle,
            uint desiredAccess,
            out IntPtr duplicatedTargetHandle)
        {
            duplicatedTargetHandle = IntPtr.Zero;
            return false;
        }

        public int GetSecurityInfo(
            IntPtr handle,
            FileSecurityNative.SE_OBJECT_TYPE objectType,
            FileSecurityNative.SECURITY_INFORMATION securityInformation,
            out IntPtr ownerSid,
            out IntPtr groupSid,
            out IntPtr dacl,
            out IntPtr sacl,
            out IntPtr securityDescriptor)
        {
            if (GetSecurityInfoBehavior != null)
                return GetSecurityInfoBehavior(handle, objectType, securityInformation, out ownerSid, out groupSid, out dacl, out sacl, out securityDescriptor);

            ownerSid = IntPtr.Zero;
            groupSid = IntPtr.Zero;
            dacl = IntPtr.Zero;
            sacl = IntPtr.Zero;
            securityDescriptor = IntPtr.Zero;
            return 5;
        }

        public bool ConvertSecurityDescriptorToStringSecurityDescriptor(
            IntPtr securityDescriptor,
            uint revision,
            FileSecurityNative.SECURITY_INFORMATION securityInformation,
            out IntPtr stringSecurityDescriptor,
            out uint stringSecurityDescriptorLength)
        {
            if (ConvertSecurityDescriptorBehavior != null)
            {
                return ConvertSecurityDescriptorBehavior(
                    securityDescriptor,
                    revision,
                    securityInformation,
                    out stringSecurityDescriptor,
                    out stringSecurityDescriptorLength);
            }

            stringSecurityDescriptor = IntPtr.Zero;
            stringSecurityDescriptorLength = 0;
            return false;
        }

        public IntPtr LocalFree(IntPtr memory)
        {
            LocalFreedPointers.Add(memory);
            if (memory != IntPtr.Zero && memory != new IntPtr(300) && memory != new IntPtr(301))
                Marshal.FreeHGlobal(memory);
            return IntPtr.Zero;
        }

        public void CloseHandle(IntPtr handle)
        {
        }

        public int GetLastWin32Error() => LastWin32Error;

        public int GetProcessId(IntPtr processHandle) => processHandle.ToInt32();

        public static void WriteProcessIdList(IntPtr buffer, IReadOnlyList<int> processIds)
        {
            Marshal.WriteInt32(buffer, processIds.Count);
            Marshal.WriteInt32(buffer, 4, processIds.Count);
            for (var i = 0; i < processIds.Count; i++)
            {
                Marshal.WriteIntPtr(buffer, 8 + (i * IntPtr.Size), new IntPtr(processIds[i]));
            }
        }

        public static void WriteBasicLimitFlags(IntPtr buffer, uint flags)
        {
            Marshal.Copy(new byte[Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()], 0, buffer, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());
            Marshal.WriteInt32(buffer, 16, unchecked((int)flags));
        }

        public delegate int GetSecurityInfoDelegate(
            IntPtr handle,
            FileSecurityNative.SE_OBJECT_TYPE objectType,
            FileSecurityNative.SECURITY_INFORMATION securityInformation,
            out IntPtr ownerSid,
            out IntPtr groupSid,
            out IntPtr dacl,
            out IntPtr sacl,
            out IntPtr securityDescriptor);

        public delegate bool ConvertSecurityDescriptorDelegate(
            IntPtr securityDescriptor,
            uint revision,
            FileSecurityNative.SECURITY_INFORMATION securityInformation,
            out IntPtr stringSecurityDescriptor,
            out uint stringSecurityDescriptorLength);

        public delegate bool QueryInformationJobObjectDelegate(
            IntPtr jobHandle,
            int jobObjectInfoClass,
            IntPtr buffer,
            uint bufferLength,
            out uint returnLength);

        public delegate bool SetInformationJobObjectDelegate(
            IntPtr jobHandle,
            int jobObjectInfoClass,
            IntPtr buffer,
            uint bufferLength);

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public IntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }

    private sealed class JobObjectApiWithDuplicateOnly : IJobObjectApi
    {
        public JobObjectApiWithDuplicateOnly(IntPtr sourceHandle, IntPtr targetHandle, IntPtr duplicatedHandle)
        {
            SourceHandle = sourceHandle;
            TargetHandle = targetHandle;
            DuplicatedHandle = duplicatedHandle;
        }

        public IntPtr SourceHandle { get; }
        public IntPtr TargetHandle { get; }
        public IntPtr DuplicatedHandle { get; }

        public bool OutOverloadCalled { get; private set; }
        public IntPtr CapturedHandle { get; private set; }

        public bool DuplicateHandleToProcess(
            IntPtr sourceProcessHandle,
            IntPtr sourceHandle,
            IntPtr targetProcessHandle,
            uint desiredAccess,
            out IntPtr duplicatedTargetHandle)
        {
            OutOverloadCalled = true;
            duplicatedTargetHandle = DuplicatedHandle;
            CapturedHandle = duplicatedTargetHandle;
            return true;
        }

        public IntPtr CreateJobObject(string? name, string? securityDescriptorSddl) => IntPtr.Zero;
        public IntPtr OpenJobObject(uint desiredAccess, bool inheritHandle, string name) => IntPtr.Zero;
        public bool AssignProcessToJobObject(IntPtr jobHandle, IntPtr processHandle) => false;
        public bool? IsProcessInJob(IntPtr processHandle, IntPtr jobHandle) => null;
        public bool AreSameJobObject(IntPtr firstJobHandle, IntPtr secondJobHandle) => false;
        public int GetProcessId(IntPtr processHandle) => 0;
        public bool SetUiRestrictions(IntPtr jobHandle, uint flags) => false;
        public uint? QueryUiRestrictions(IntPtr jobHandle) => null;
        public uint? QueryBasicLimitFlags(IntPtr jobHandle) => null;
        public HashSet<int>? QueryProcessIds(IntPtr jobHandle) => null;
        public JobObjectSecuritySnapshot? GetSecuritySnapshot(IntPtr jobHandle) => null;
        public void CloseHandle(IntPtr handle) { }
        public int GetLastWin32Error() => 0;
    }
}
