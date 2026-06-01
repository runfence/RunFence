using System.ComponentModel;
using Moq;
using RunFence.Core;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.Tests;

public sealed class TokenIntegrityLevelServiceTests
{
    [Fact]
    public void SetMediumIntegrity_DirectSuccess_DoesNotUseSystemFallback()
    {
        var runner = new RecordingSystemPrivilegeRunner();
        var service = new TestTokenIntegrityLevelService(runner);

        service.SetMediumIntegrity(new IntPtr(10), out var pSid, out var tmlBuffer);

        Assert.Equal(new IntPtr(101), pSid);
        Assert.Equal(new IntPtr(201), tmlBuffer);
        Assert.Equal(["medium-direct"], service.Events);
        Assert.Empty(runner.PrivilegeRuns);
    }

    [Fact]
    public void SetMediumIntegrity_PrivilegeNotHeld_RetriesUnderSystemImpersonation()
    {
        var runner = new RecordingSystemPrivilegeRunner();
        var service = new TestTokenIntegrityLevelService(runner)
        {
            ThrowPrivilegeNotHeldOnFirstMediumCall = true,
        };

        service.SetMediumIntegrity(new IntPtr(10), out var pSid, out var tmlBuffer);

        Assert.Equal(new IntPtr(102), pSid);
        Assert.Equal(new IntPtr(202), tmlBuffer);
        Assert.Equal(["medium-direct", "medium-direct"], service.Events);
        Assert.Single(runner.PrivilegeRuns);
        Assert.Equal([TokenPrivilegeHelper.SeRelabelPrivilege, TokenPrivilegeHelper.SeTcbPrivilege], runner.PrivilegeRuns[0]);
    }

    [Fact]
    public void SetLowIntegrity_NonPrivilegeFailure_DoesNotUseSystemFallback()
    {
        var runner = new RecordingSystemPrivilegeRunner();
        var service = new TestTokenIntegrityLevelService(runner)
        {
            LowIntegrityException = new Win32Exception(5, "Access denied"),
        };

        var ex = Assert.Throws<Win32Exception>(() =>
            service.SetLowIntegrity(new IntPtr(10), out _, out _));

        Assert.Equal(5, ex.NativeErrorCode);
        Assert.Equal(["low-direct"], service.Events);
        Assert.Empty(runner.PrivilegeRuns);
    }

    [Fact]
    public void SetHighIntegrity_PrivilegeNotHeld_RetriesUnderSystemImpersonation()
    {
        var runner = new RecordingSystemPrivilegeRunner();
        var service = new TestTokenIntegrityLevelService(runner)
        {
            ThrowPrivilegeNotHeldOnFirstHighCall = true,
        };

        service.SetHighIntegrity(new IntPtr(10), out var pSid, out var tmlBuffer);

        Assert.Equal(new IntPtr(102), pSid);
        Assert.Equal(new IntPtr(202), tmlBuffer);
        Assert.Equal(["high-direct", "high-direct"], service.Events);
        Assert.Single(runner.PrivilegeRuns);
        Assert.Equal([TokenPrivilegeHelper.SeRelabelPrivilege, TokenPrivilegeHelper.SeTcbPrivilege], runner.PrivilegeRuns[0]);
    }

    private sealed class TestTokenIntegrityLevelService
        : TokenIntegrityLevelService
    {
        private int _mediumCalls;
        private int _highCalls;

        public TestTokenIntegrityLevelService(ISystemPrivilegeRunner systemPrivilegeRunner)
            : base(new Mock<ILoggingService>().Object, systemPrivilegeRunner)
        {
        }

        public bool ThrowPrivilegeNotHeldOnFirstMediumCall { get; init; }

        public bool ThrowPrivilegeNotHeldOnFirstHighCall { get; init; }

        public Exception? LowIntegrityException { get; init; }

        public List<string> Events { get; } = [];

        protected override void ApplyLowIntegrityDirect(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer)
        {
            Events.Add("low-direct");
            if (LowIntegrityException != null)
                throw LowIntegrityException;

            pSid = new IntPtr(100);
            tmlBuffer = new IntPtr(200);
        }

        protected override void ApplyMediumIntegrityDirect(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer)
        {
            Events.Add("medium-direct");
            _mediumCalls++;
            if (ThrowPrivilegeNotHeldOnFirstMediumCall && _mediumCalls == 1)
                throw new Win32Exception(1314, "Privilege not held");

            pSid = new IntPtr(100 + _mediumCalls);
            tmlBuffer = new IntPtr(200 + _mediumCalls);
        }

        protected override void ApplyHighIntegrityDirect(IntPtr hToken, out IntPtr pSid, out IntPtr tmlBuffer)
        {
            Events.Add("high-direct");
            _highCalls++;
            if (ThrowPrivilegeNotHeldOnFirstHighCall && _highCalls == 1)
                throw new Win32Exception(1314, "Privilege not held");

            pSid = new IntPtr(100 + _highCalls);
            tmlBuffer = new IntPtr(200 + _highCalls);
        }
    }

    private sealed class RecordingSystemPrivilegeRunner : ISystemPrivilegeRunner
    {
        public List<IReadOnlyList<string>> PrivilegeRuns { get; } = [];

        public void RunWithPrivileges(IEnumerable<string> privilegeNames, Action action)
        {
            PrivilegeRuns.Add(privilegeNames.ToArray());
            action();
        }

        public T RunWithPrivileges<T>(IEnumerable<string> privilegeNames, Func<T> action)
        {
            PrivilegeRuns.Add(privilegeNames.ToArray());
            return action();
        }
    }
}
