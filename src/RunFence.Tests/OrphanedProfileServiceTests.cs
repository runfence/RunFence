using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Moq;
using RunFence.Account;
using RunFence.Account.OrphanedProfiles;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class OrphanedProfileServiceTests : IDisposable
{
    private readonly Mock<ILoggingService> _log = new();
    private readonly TempDirectory _usersDir = new("OrphanedProfiles_Test");

    public void Dispose() => _usersDir.Dispose();

    private const string DeadSid = "S-1-5-21-9999999999-9999999999-9999999999-1001";
    private const string AliveSid = "S-1-5-21-9999999999-9999999999-9999999999-1002";

    // Creates a service with injectable registry entries and account existence set
    private TestOrphanedProfileService CreateService(
        IEnumerable<(string Sid, string ProfilePath)>? registryEntries = null,
        IEnumerable<string>? aliveAccounts = null) =>
        new(_log.Object, _usersDir.Path,
            registryEntries ?? [],
            new HashSet<string>(aliveAccounts ?? [], StringComparer.OrdinalIgnoreCase));

    // --- GetOrphanedProfiles: Case A (no registry entry) ---

    [Fact]
    public void GetOrphanedProfiles_EmptyUsersDir_ReturnsEmpty()
    {
        var service = CreateService();

        var result = service.GetOrphanedProfiles();

        Assert.Empty(result);
    }

    [Fact]
    public void GetOrphanedProfiles_MissingUsersDir_ReturnsEmpty()
    {
        var service = new TestOrphanedProfileService(
            _log.Object,
            @"C:\NonExistent_RunFenceTest_" + Guid.NewGuid().ToString("N"),
            [], []);

        var result = service.GetOrphanedProfiles();

        Assert.Empty(result);
    }

    [Fact]
    public void GetOrphanedProfiles_DirWithNoRegistryEntry_ReturnsIt()
    {
        var dir = Path.Combine(_usersDir.Path, "OrphanedUser");
        Directory.CreateDirectory(dir);
        var service = CreateService(); // no registry entries

        var result = service.GetOrphanedProfiles();

        Assert.Single(result);
        Assert.Equal(dir, result[0].ProfilePath, StringComparer.OrdinalIgnoreCase);
        Assert.Null(result[0].Sid); // no SID — Case A
    }

    [Fact]
    public void GetOrphanedProfiles_ExcludesWellKnownDirsWithNoRegistryEntry()
    {
        foreach (var name in new[] { "Default", "All Users", "Default User", "Public", "DefaultAppPool" })
            Directory.CreateDirectory(Path.Combine(_usersDir.Path, name));

        var service = CreateService();

        var result = service.GetOrphanedProfiles();

        Assert.Empty(result);
    }

    [Fact]
    public void GetOrphanedProfiles_TempDirWithNoRegistryEntry_ReturnsIt()
    {
        // TEMP is NOT excluded — it represents a Windows corrupted profile that should be detectable
        Directory.CreateDirectory(Path.Combine(_usersDir.Path, "TEMP"));

        var service = CreateService();

        var result = service.GetOrphanedProfiles();

        Assert.Single(result);
        Assert.Null(result[0].Sid);
    }

    [Fact]
    public void GetOrphanedProfiles_ExclusionIsCaseInsensitive()
    {
        Directory.CreateDirectory(Path.Combine(_usersDir.Path, "default"));
        Directory.CreateDirectory(Path.Combine(_usersDir.Path, "PUBLIC"));

        var service = CreateService();

        var result = service.GetOrphanedProfiles();

        Assert.Empty(result);
    }

    // --- GetOrphanedProfiles: Case B (registry entry, dead account) ---

    [Fact]
    public void GetOrphanedProfiles_RegistryEntryWithDeadAccount_ReturnsIt()
    {
        var dir = Path.Combine(_usersDir.Path, "DeadUser");
        Directory.CreateDirectory(dir);
        var service = CreateService(
            registryEntries: [(DeadSid, dir)],
            aliveAccounts: []); // DeadSid has no account

        var result = service.GetOrphanedProfiles();

        Assert.Single(result);
        Assert.Equal(dir, result[0].ProfilePath, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(DeadSid, result[0].Sid); // SID present — Case B
    }

    [Fact]
    public void GetOrphanedProfiles_RegistryEntryWithLiveAccount_ExcludesIt()
    {
        var dir = Path.Combine(_usersDir.Path, "LiveUser");
        Directory.CreateDirectory(dir);
        var service = CreateService(
            registryEntries: [(AliveSid, dir)],
            aliveAccounts: [AliveSid]);

        var result = service.GetOrphanedProfiles();

        Assert.Empty(result);
    }

    [Fact]
    public void GetOrphanedProfiles_RegistryEntryDeadAccountButNoDirectory_ExcludesIt()
    {
        // Dead account, but directory doesn't exist on disk — nothing to delete
        var missingDir = Path.Combine(_usersDir.Path, "GhostUser");
        var service = CreateService(
            registryEntries: [(DeadSid, missingDir)],
            aliveAccounts: []);

        var result = service.GetOrphanedProfiles();

        Assert.Empty(result);
    }

    // --- Mixed cases ---

    [Fact]
    public void GetOrphanedProfiles_MixedCases_ReturnsBothTypes()
    {
        // Case A: dir with no registry entry
        var caseADir = Path.Combine(_usersDir.Path, "LeftoverDir");
        Directory.CreateDirectory(caseADir);

        // Case B: registry entry with dead account
        var caseBDir = Path.Combine(_usersDir.Path, "DeadAccountUser");
        Directory.CreateDirectory(caseBDir);

        // Live account dir — excluded
        var liveDir = Path.Combine(_usersDir.Path, "LiveUser");
        Directory.CreateDirectory(liveDir);

        var service = CreateService(
            registryEntries: [(DeadSid, caseBDir), (AliveSid, liveDir)],
            aliveAccounts: [AliveSid]);

        var result = service.GetOrphanedProfiles();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.ProfilePath.EndsWith("LeftoverDir", StringComparison.OrdinalIgnoreCase) && p.Sid == null);
        Assert.Contains(result, p => p.ProfilePath.EndsWith("DeadAccountUser", StringComparison.OrdinalIgnoreCase) && p.Sid == DeadSid);
    }

    [Fact]
    public void GetOrphanedProfiles_ResultIsSortedByPath()
    {
        Directory.CreateDirectory(Path.Combine(_usersDir.Path, "Zebra"));
        Directory.CreateDirectory(Path.Combine(_usersDir.Path, "Alpha"));
        Directory.CreateDirectory(Path.Combine(_usersDir.Path, "Mango"));

        var service = CreateService();

        var result = service.GetOrphanedProfiles();

        Assert.Equal(3, result.Count);
        Assert.Equal(
            result.OrderBy(p => p.ProfilePath, StringComparer.OrdinalIgnoreCase).Select(p => p.ProfilePath).ToList(),
            result.Select(p => p.ProfilePath).ToList());
    }

    // --- DeleteProfiles tests ---

    [Fact]
    public void DeleteProfiles_DoesNotFollowJunctions()
    {
        // Arrange: profile dir contains a junction pointing outside the users dir
        using var externalDir = new TempDirectory("JunctionTarget");
        File.WriteAllText(Path.Combine(externalDir.Path, "sentinel.txt"), "do not delete");

        var profileDir = Path.Combine(_usersDir.Path, "UserWithJunction");
        Directory.CreateDirectory(profileDir);

        // Directory junctions do not require elevation — create via P/Invoke directly.
        var junctionPath = Path.Combine(profileDir, "Junction");
        CreateJunction(junctionPath, externalDir.Path);

        var profile = new OrphanedProfile(null, profileDir);
        var service = CreateService();

        var (deleted, failed) = service.DeleteProfiles([profile]);

        Assert.Single(deleted);
        Assert.Empty(failed);
        Assert.False(Directory.Exists(profileDir));

        // The external directory (junction target) must still exist with its contents
        Assert.True(Directory.Exists(externalDir.Path));
        Assert.True(File.Exists(Path.Combine(externalDir.Path, "sentinel.txt")));
    }

    // Creates a directory junction (mount point) using native Win32 APIs.
    // Directory junctions require no special privileges — only write access to the target directory.
    private static void CreateJunction(string junctionPath, string targetPath)
    {
        // Native path format required for mount-point substitute name
        var nativeTarget = @"\??\" + Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar);
        var subNameBytes = Encoding.Unicode.GetBytes(nativeTarget);

        ushort subLen = (ushort)subNameBytes.Length;
        ushort printOffset = (ushort)(subLen + 2); // substitute name + UTF-16 NUL terminator
        // PathBuffer: substitute name + NUL + empty print name + NUL (all UTF-16)
        var pathBuf = new byte[subLen + 2 + 0 + 2];
        Buffer.BlockCopy(subNameBytes, 0, pathBuf, 0, subLen);

        // ReparseDataLength covers the four ushort header fields plus PathBuffer
        ushort reparseDataLen = (ushort)(8 + pathBuf.Length);

        // Full REPARSE_DATA_BUFFER: tag(4) + reparseDataLen(2) + reserved(2) + header(8) + pathBuf
        var buf = new byte[4 + 2 + 2 + reparseDataLen];
        int i = 0;
        BitConverter.GetBytes(0xA0000003u).CopyTo(buf, i);
        i += 4; // IO_REPARSE_TAG_MOUNT_POINT
        BitConverter.GetBytes(reparseDataLen).CopyTo(buf, i);
        i += 2;
        i += 2; // Reserved
        BitConverter.GetBytes((ushort)0).CopyTo(buf, i);
        i += 2; // SubstituteNameOffset
        BitConverter.GetBytes(subLen).CopyTo(buf, i);
        i += 2; // SubstituteNameLength
        BitConverter.GetBytes(printOffset).CopyTo(buf, i);
        i += 2; // PrintNameOffset
        BitConverter.GetBytes((ushort)0).CopyTo(buf, i);
        i += 2; // PrintNameLength
        pathBuf.CopyTo(buf, i);

        Directory.CreateDirectory(junctionPath);

        using var handle = CreateFileW(junctionPath,
            0x40000000u, // GENERIC_WRITE
            0x1u | 0x2u | 0x4u, // FILE_SHARE_READ|WRITE|DELETE
            IntPtr.Zero, 3u, // OPEN_EXISTING
            0x02000000u, // FILE_FLAG_BACKUP_SEMANTICS
            IntPtr.Zero);

        if (handle.IsInvalid)
            throw new IOException($"CreateFile failed: {Marshal.GetLastWin32Error()}");

        if (!DeviceIoControl(handle, 0x000900A4u, buf, buf.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new IOException($"FSCTL_SET_REPARSE_POINT failed: {Marshal.GetLastWin32Error()}");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        byte[] lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    [Fact]
    public void DeleteProfiles_CaseA_SuccessfulDelete_ReturnsInDeleted()
    {
        var dir = Path.Combine(_usersDir.Path, "UserToDelete");
        Directory.CreateDirectory(dir);
        var profile = new OrphanedProfile(null, dir); // Case A — no SID
        var service = CreateService();

        var (deleted, failed) = service.DeleteProfiles([profile]);

        Assert.Single(deleted);
        Assert.Empty(failed);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void DeleteProfiles_CaseB_SuccessfulDelete_ReturnsInDeleted()
    {
        var dir = Path.Combine(_usersDir.Path, "DeadUser");
        Directory.CreateDirectory(dir);
        var profile = new OrphanedProfile(DeadSid, dir); // Case B — has SID
        var service = CreateService();

        var (deleted, failed) = service.DeleteProfiles([profile]);

        Assert.Single(deleted);
        Assert.Empty(failed);
        Assert.False(Directory.Exists(dir));
        // Registry deletion is a best-effort no-op in tests (registry not writable in test environment)
    }

    [Fact]
    public void DeleteProfiles_NestedPath_RejectsForSafety()
    {
        var dir = Path.Combine(_usersDir.Path, "user1", "subdir");
        Directory.CreateDirectory(dir);
        var profile = new OrphanedProfile(null, dir);
        var service = CreateService();

        var (deleted, failed) = service.DeleteProfiles([profile]);

        Assert.Empty(deleted);
        Assert.Single(failed);
        Assert.Contains("safety", failed[0].Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void DeleteProfiles_PathOutsideUsersDir_RejectsForSafety()
    {
        using var outsideDir = new TempDirectory("outside");
        var profile = new OrphanedProfile(null, outsideDir.Path);
        var service = CreateService();

        var (deleted, failed) = service.DeleteProfiles([profile]);

        Assert.Empty(deleted);
        Assert.Single(failed);
        Assert.Contains("safety", failed[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeleteProfiles_NonexistentPath_ReturnsFailure()
    {
        var nonexistent = Path.Combine(_usersDir.Path, "ghost");
        var profile = new OrphanedProfile(null, nonexistent);
        var service = CreateService();

        var (deleted, failed) = service.DeleteProfiles([profile]);

        Assert.Empty(deleted);
        Assert.Single(failed);
    }

    [Fact]
    public void DeleteProfiles_EmptyList_ReturnsEmpty()
    {
        var service = CreateService();

        var (deleted, failed) = service.DeleteProfiles([]);

        Assert.Empty(deleted);
        Assert.Empty(failed);
    }

    [Fact]
    public void DeleteProfiles_ReadOnlyFiles_StillDeletes()
    {
        var dir = Path.Combine(_usersDir.Path, "ReadOnlyUser");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "readonly.txt");
        File.WriteAllText(filePath, "content");
        File.SetAttributes(filePath, FileAttributes.ReadOnly | FileAttributes.System);

        var profile = new OrphanedProfile(null, dir);
        var service = CreateService();

        var (deleted, failed) = service.DeleteProfiles([profile]);

        Assert.Single(deleted);
        Assert.Empty(failed);
        Assert.False(Directory.Exists(dir));
    }

    // --- Test subclass ---

    private sealed class TestOrphanedProfileService(
        ILoggingService log,
        string usersDir,
        IEnumerable<(string Sid, string ProfilePath)> entries,
        HashSet<string> aliveAccounts)
        : OrphanedProfileService(log, new NTTranslateApi(log), new GroupPolicyScriptHelper(new LogonScriptIniManager(), log), usersDir)
    {
        private readonly List<(string Sid, string ProfilePath)> _entries = entries.ToList();

        protected override IEnumerable<(string Sid, string ProfilePath)> GetProfileRegistryEntries() => _entries;

        protected override bool AccountExists(string sidString) => aliveAccounts.Contains(sidString);
    }
}