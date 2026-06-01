using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

internal sealed class ProgramDataSecurityTestScope : IDisposable
{
    private readonly TempDirectory tempDirectory = new("RunFence_ProgramDataSecurity");

    public ProgramDataSecurityTestScope(Mock<ILoggingService>? log = null)
    {
        RootPath = Path.Combine(tempDirectory.Path, "ProgramData");
        Directory.CreateDirectory(RootPath);
        Log = log ?? new Mock<ILoggingService>();
        CurrentUserSid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current test account SID was not available.");
        State = new FakeProgramDataState(RootPath, CurrentUserSid);
        OperationLog = [];
        PathGuard = new FakeProgramDataPathGuard(State, OperationLog);
        Accessor = new FakeSecurityDescriptorAccessor(State);
        NativeFileSystem = new FakeBackupIntentNativeFileSystem(State, OperationLog);
        State.SetDirectorySecurity(RootPath, CreateRootSecurity(CurrentUserSid));

        var pathPolicyCatalog = new ProgramDataPathPolicyCatalog(PathGuard);
        var ownerPolicyService = new ProgramDataOwnerPolicyService(pathPolicyCatalog);
        var aclProfilePolicy = new ProgramDataAclProfilePolicy();
        var aclBuilder = new ProgramDataDirectoryAclBuilder(aclProfilePolicy);
        var ownerRepairService = new ProgramDataOwnerRepairService(Log.Object, Accessor, ownerPolicyService);
        var verifier = new ProgramDataSecurityVerifier(Accessor, ownerPolicyService, aclProfilePolicy);
        var applier = new ProgramDataSecurityApplier(Log.Object, Accessor, aclBuilder, pathPolicyCatalog);
        var directoryProvisioner = new ProgramDataDirectoryProvisioner(
            Log.Object,
            pathPolicyCatalog,
            PathGuard,
            aclBuilder,
            Accessor,
            applier,
            verifier,
            ownerRepairService,
            NativeFileSystem);
        var repairer = new ProgramDataManagedObjectRepairer(
            PathGuard,
            Accessor,
            ownerPolicyService,
            ownerRepairService,
            applier,
            verifier);
        var explicitAclApplier = new ProgramDataExplicitAclApplier(Log.Object, Accessor);
        var objectProvisioner = new ProgramDataObjectProvisioner(
            Log.Object,
            directoryProvisioner,
            pathPolicyCatalog,
            PathGuard,
            aclBuilder,
            Accessor,
            ownerRepairService,
            explicitAclApplier,
            NativeFileSystem);

        Service = new ProgramDataSecurityTestFacade(
            directoryProvisioner,
            objectProvisioner,
            repairer,
            PathGuard);
        ManagedObjectRepairer = repairer;
        PathPolicyCatalog = pathPolicyCatalog;
        OwnerPolicyService = ownerPolicyService;
        Verifier = verifier;
    }

    public string RootPath { get; }
    public Mock<ILoggingService> Log { get; }
    public SecurityIdentifier CurrentUserSid { get; }
    public FakeProgramDataState State { get; }
    public FakeProgramDataPathGuard PathGuard { get; }
    public FakeSecurityDescriptorAccessor Accessor { get; }
    public FakeBackupIntentNativeFileSystem NativeFileSystem { get; }
    public ProgramDataSecurityTestFacade Service { get; }
    public ProgramDataManagedObjectRepairer ManagedObjectRepairer { get; }
    public ProgramDataPathPolicyCatalog PathPolicyCatalog { get; }
    public ProgramDataOwnerPolicyService OwnerPolicyService { get; }
    public ProgramDataSecurityVerifier Verifier { get; }
    public List<string> OperationLog { get; }

    public string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(path);
        State.EnsureDirectoryEntry(path);
        return path;
    }

    public string CreateFile(string relativePath)
    {
        var path = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            using var _ = File.Create(path);
        }

        State.EnsureDirectoryEntry(Path.GetDirectoryName(path)!);
        State.EnsureFileEntry(path);
        return path;
    }

    public void Dispose()
    {
        tempDirectory.Dispose();
    }

    public static DirectorySecurity CreateOwnedDirectorySecurity(SecurityIdentifier owner)
    {
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadData,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    public static FileSecurity CreateOwnedFileSecurity(SecurityIdentifier owner)
    {
        var security = new FileSecurity();
        security.SetOwner(owner);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadData,
            AccessControlType.Allow));
        return security;
    }

    public static IEnumerable<FileSystemAccessRule> GetExplicitRules(FileSystemSecurity security)
        => security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>();

    public static SecurityIdentifier GetOwnerSid(FileSystemSecurity security)
        => (SecurityIdentifier?)security.GetOwner(typeof(SecurityIdentifier))
           ?? throw new InvalidOperationException("Security owner SID was not available.");

    public static bool IsRuleForSid(FileSystemAccessRule rule, WellKnownSidType sidType)
        => rule.IdentityReference is SecurityIdentifier sid && sid.IsWellKnown(sidType);

    public static bool IsRuleForSid(FileSystemAccessRule rule, string sidValue)
        => rule.IdentityReference is SecurityIdentifier sid && sid.Value == sidValue;

    public static bool IsExactTraverseRule(FileSystemAccessRule rule, string sidValue)
        => rule.AccessControlType == AccessControlType.Allow &&
           IsRuleForSid(rule, sidValue) &&
           rule.FileSystemRights == RunFence.Acl.Traverse.TraverseRightsHelper.TraverseRights &&
           rule.InheritanceFlags == InheritanceFlags.None &&
           rule.PropagationFlags == PropagationFlags.None;

    public static string GetSecuritySignature(FileSystemSecurity security)
    {
        var rules = GetExplicitRules(security)
            .Select(rule =>
            {
                var sid = (SecurityIdentifier)rule.IdentityReference;
                return $"{sid.Value}|{rule.AccessControlType}|{(int)rule.FileSystemRights}|{(int)rule.InheritanceFlags}|{(int)rule.PropagationFlags}";
            })
            .OrderBy(x => x, StringComparer.Ordinal);
        return $"{security.AreAccessRulesProtected}:{GetOwnerSid(security).Value}:{string.Join(";", rules)}";
    }

    public static string GetDaclSignature(FileSystemSecurity security)
    {
        var rules = GetExplicitRules(security)
            .Select(rule =>
            {
                var sid = (SecurityIdentifier)rule.IdentityReference;
                return $"{sid.Value}|{rule.AccessControlType}|{(int)rule.FileSystemRights}|{(int)rule.InheritanceFlags}|{(int)rule.PropagationFlags}";
            })
            .OrderBy(x => x, StringComparer.Ordinal);
        return $"{security.AreAccessRulesProtected}:{string.Join(";", rules)}";
    }

    private static DirectorySecurity CreateRootSecurity(SecurityIdentifier owner)
    {
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    internal sealed class FakeProgramDataPathGuard : IProgramDataPathGuard, IProgramDataPathPolicyService
    {
        private readonly FakeProgramDataState state;
        private readonly List<string>? operationLog;
        private readonly HashSet<string> rejectedPaths = new(StringComparer.OrdinalIgnoreCase);
        public List<OpenCall> OpenCalls { get; } = [];

        public FakeProgramDataPathGuard(FakeProgramDataState state, List<string>? operationLog = null)
        {
            this.state = state;
            this.operationLog = operationLog;
        }

        public FakeProgramDataPathGuard(FakeProgramDataState state) : this(state, null)
        {
        }

        public string NormalizeRoot() => state.RootPath;

        public string NormalizeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new InvalidOperationException("ProgramData relative path must not be empty.");
            }

            if (Path.IsPathFullyQualified(relativePath) || Path.IsPathRooted(relativePath))
            {
                throw new InvalidOperationException("ProgramData relative path must not be absolute.");
            }

            ValidateRelativeSegments(relativePath);
            return Normalize(Path.Combine(state.RootPath, relativePath));
        }

        public string NormalizeAbsolutePathUnderRoot(string path)
        {
            var normalized = Normalize(path);
            EnsureUnderRoot(normalized);
            return normalized;
        }

        public string NormalizeExistingPathUnderRoot(string path, ProgramDataObjectKind kind)
        {
            var normalized = NormalizeAbsolutePathUnderRoot(path);
            RejectIfNeeded(normalized);
            return normalized;
        }

        public SafeFileHandle OpenExistingManagedObject(
            string path,
            ProgramDataObjectKind kind,
            ProgramDataManagedObjectAccess access)
        {
            var normalized = NormalizeExistingPathUnderRoot(path, kind);
            OpenCalls.Add(new OpenCall(normalized, kind, access));
            operationLog?.Add($"Open:{access}:{normalized}");
            return state.CreateSyntheticHandle(normalized);
        }

        public bool IsUnderRoot(string path)
        {
            var normalized = Normalize(path);
            return string.Equals(normalized, state.RootPath, StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(state.RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public void RejectAsReparse(string path) => rejectedPaths.Add(Normalize(path));

        private void EnsureUnderRoot(string path)
        {
            if (!IsUnderRoot(path))
            {
                throw new InvalidOperationException($"Managed ProgramData path '{path}' is outside '{state.RootPath}'.");
            }
        }

        private void RejectIfNeeded(string path)
        {
            if (rejectedPaths.Contains(path))
            {
                throw new InvalidOperationException($"Managed ProgramData path '{path}' must not be a reparse point.");
            }
        }

        private static void ValidateRelativeSegments(string relativePath)
        {
            foreach (var rawSegment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (string.IsNullOrWhiteSpace(rawSegment))
                {
                    throw new InvalidOperationException("ProgramData relative path must not contain empty segments.");
                }

                if (rawSegment is "." or "..")
                {
                    throw new InvalidOperationException("ProgramData relative path must not contain '.' or '..' segments.");
                }
            }
        }

        private static string Normalize(string path)
            => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal sealed class FakeSecurityDescriptorAccessor(FakeProgramDataState state)
        : IPathSecurityDescriptorAccessor, IHandleSecurityDescriptorAccessor
    {
        public int HandleModifyCallCount { get; private set; }

        public FileSystemSecurity GetSecurity(string path) => state.CloneSecurity(path);

        public FileSystemSecurity GetSecurity(SafeFileHandle handle, bool isDirectory)
            => state.CloneSecurity(state.GetPath(handle));

        public string? GetOwnerSid(string path) => ProgramDataSecurityTestScope.GetOwnerSid(state.CloneSecurity(path)).Value;

        public bool PathExists(string path, out bool isFolder)
        {
            var normalized = Path.GetFullPath(path);
            isFolder = Directory.Exists(normalized);
            return isFolder || File.Exists(normalized);
        }

        public bool ModifyAclWithFallback(string path, Func<FileSystemSecurity, bool> modify)
        {
            var security = state.CloneSecurity(path);
            bool changed = modify(security);
            if (changed)
            {
                state.SetSecurity(path, security);
            }

            return changed;
        }

        public bool ModifyAclWithFallback(SafeFileHandle handle, bool isFolder, Func<FileSystemSecurity, bool> modify)
        {
            HandleModifyCallCount++;
            var path = state.GetPath(handle);
            var security = state.CloneSecurity(path);
            bool changed = modify(security);
            if (changed)
            {
                state.SetSecurity(path, security);
            }

            return changed;
        }

        public bool ModifyOwnerAndAclWithFallback(string path, Func<FileSystemSecurity, bool> modify)
            => ModifyAclWithFallback(path, modify);

        public void SetOwnerAndAclWithFallback(string path, FileSystemSecurity security)
            => state.SetSecurity(path, security);

        public void SetOwnerWithFallback(string path, SecurityIdentifier ownerSid)
        {
            var security = state.CloneSecurity(path);
            security.SetOwner(ownerSid);
            state.SetSecurity(path, security);
        }

        public void SetOwnerWithFallback(SafeFileHandle handle, SecurityIdentifier ownerSid)
        {
            var path = state.GetPath(handle);
            var security = state.CloneSecurity(path);
            security.SetOwner(ownerSid);
            state.SetSecurity(path, security);
        }

        public void ApplyNonPropagatingAcl(string path, FileSystemSecurity security)
            => state.SetSecurity(path, security);
    }

    internal sealed class FakeBackupIntentNativeFileSystem : IBackupIntentNativeFileSystem
    {
        private readonly FakeProgramDataState state;
        private readonly List<string> operationLog;
        public readonly record struct CreateRelativeFileCall(string Path, bool Overwrite);
        public List<string> CreatedDirectories { get; } = [];
        public List<CreateRelativeFileCall> CreatedFiles { get; } = [];

        public FakeBackupIntentNativeFileSystem(FakeProgramDataState state, List<string> operationLog)
        {
            this.state = state;
            this.operationLog = operationLog;
        }

        public BackupIntentNativeOpenResult TryOpen(string path, bool directory) => new(null, 0);

        public SafeFileHandle CreateRelativeDirectory(
            SafeFileHandle parentHandle,
            string childName,
            uint desiredAccess,
            uint shareAccess,
            byte[]? securityDescriptor = null)
        {
            var parentPath = state.GetPath(parentHandle);
            var childPath = Path.Combine(parentPath, childName);
            Directory.CreateDirectory(childPath);
            state.SetSecurity(childPath, CreateSecurity(true, securityDescriptor, state.CurrentUserSid));
            operationLog.Add($"CreateRelativeDirectory:{childPath}");
            CreatedDirectories.Add(childPath);
            return state.CreateSyntheticHandle(childPath);
        }

        public SafeFileHandle CreateRelativeFile(
            SafeFileHandle parentHandle,
            string childName,
            uint desiredAccess,
            uint shareAccess,
            bool overwrite,
            byte[]? securityDescriptor = null)
        {
            var parentPath = state.GetPath(parentHandle);
            var filePath = Path.Combine(parentPath, childName);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
            var handle = File.OpenHandle(filePath, mode, FileAccess.ReadWrite, FileShare.Read);
            state.SetSecurity(filePath, CreateSecurity(false, securityDescriptor, state.CurrentUserSid));
            state.RegisterHandle(filePath, handle);
            CreatedFiles.Add(new CreateRelativeFileCall(filePath, overwrite));
            operationLog.Add($"CreateRelativeFile:{filePath}");
            return handle;
        }

        public bool TryEnumerateDirectories(SafeFileHandle handle, string rootPath, out IReadOnlyList<string> directories)
            => throw new NotSupportedException();

        public bool TryGetLastWriteTimeUtc(SafeFileHandle handle, out DateTime lastWriteTimeUtc)
            => throw new NotSupportedException();

        private static FileSystemSecurity CreateSecurity(bool isDirectory, byte[]? descriptor, SecurityIdentifier ownerSid)
        {
            FileSystemSecurity security = isDirectory ? new DirectorySecurity() : new FileSecurity();
            if (descriptor != null)
            {
                security.SetSecurityDescriptorBinaryForm(descriptor);
            }

            security.SetOwner(ownerSid);
            return security;
        }
    }

    internal sealed class FakeProgramDataState(string rootPath, SecurityIdentifier currentUserSid)
    {
        private readonly Dictionary<string, FileSystemSecurity> entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<nint, string> handlePaths = new();
        private int nextSyntheticHandle = 1000;

        public string RootPath { get; } = Normalize(rootPath);
        public SecurityIdentifier CurrentUserSid { get; } = currentUserSid;

        public void EnsureDirectoryEntry(string path)
        {
            var normalized = Normalize(path);
            if (!entries.ContainsKey(normalized))
            {
                SetSecurity(normalized, DefaultDirectorySecurity());
            }
        }

        public void EnsureFileEntry(string path)
        {
            var normalized = Normalize(path);
            if (!entries.ContainsKey(normalized))
            {
                SetSecurity(normalized, DefaultFileSecurity());
            }
        }

        public void SetDirectorySecurity(string path, DirectorySecurity security) => SetSecurity(path, security);

        public DirectorySecurity GetDirectorySecurity(string path)
            => Assert.IsType<DirectorySecurity>(CloneSecurity(path));

        public void SetFileSecurity(string path, FileSecurity security) => SetSecurity(path, security);

        public FileSecurity GetFileSecurity(string path)
            => Assert.IsType<FileSecurity>(CloneSecurity(path));

        public void SetSecurity(string path, FileSystemSecurity security)
            => entries[Normalize(path)] = Clone(security);

        public FileSystemSecurity CloneSecurity(string path)
        {
            var normalized = Normalize(path);
            if (!entries.TryGetValue(normalized, out var security))
            {
                throw new InvalidOperationException($"Missing fake ProgramData security entry for '{normalized}'.");
            }

            return Clone(security);
        }

        public SafeFileHandle CreateSyntheticHandle(string path)
        {
            var handleValue = Interlocked.Increment(ref nextSyntheticHandle);
            var handle = new SafeFileHandle(new IntPtr(handleValue), ownsHandle: false);
            RegisterHandle(path, handle);
            return handle;
        }

        public void RegisterHandle(string path, SafeFileHandle handle)
            => handlePaths[handle.DangerousGetHandle()] = Normalize(path);

        public string GetPath(SafeFileHandle handle)
        {
            if (!handlePaths.TryGetValue(handle.DangerousGetHandle(), out var path))
            {
                throw new InvalidOperationException($"Unknown fake handle '{handle.DangerousGetHandle()}'.");
            }

            return path;
        }

        private DirectorySecurity DefaultDirectorySecurity()
        {
            var security = new DirectorySecurity();
            security.SetOwner(CurrentUserSid);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            return security;
        }

        private FileSecurity DefaultFileSecurity()
        {
            var security = new FileSecurity();
            security.SetOwner(CurrentUserSid);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            return security;
        }

        private static FileSystemSecurity Clone(FileSystemSecurity security)
        {
            FileSystemSecurity clone = security switch
            {
                DirectorySecurity => new DirectorySecurity(),
                FileSecurity => new FileSecurity(),
                _ => throw new InvalidOperationException($"Unsupported security type '{security.GetType().Name}'.")
            };
            clone.SetSecurityDescriptorBinaryForm(security.GetSecurityDescriptorBinaryForm());
            return clone;
        }

        private static string Normalize(string path)
            => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal readonly record struct OpenCall(
        string Path,
        ProgramDataObjectKind Kind,
        ProgramDataManagedObjectAccess Access);
}
