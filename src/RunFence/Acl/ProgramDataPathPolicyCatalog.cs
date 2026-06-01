using System.Collections.Concurrent;
using RunFence.Core;

namespace RunFence.Acl;

public class ProgramDataPathPolicyCatalog(IProgramDataPathGuard pathGuard) : IProgramDataKnownPathResolver
{
    private readonly ConcurrentDictionary<string, ProgramDataDirectoryAclProfile> dynamicProfiles =
        new(StringComparer.OrdinalIgnoreCase);

    public string RootPath { get; } = pathGuard.NormalizeRoot();

    public string GetDirectoryPath(ProgramDataDirectoryPolicy policy)
        => pathGuard.NormalizeRelativePath(policy.RelativePath);

    public string GetFilePath(ProgramDataFilePolicy policy)
        => pathGuard.NormalizeRelativePath(policy.RelativePath);

    public ProgramDataDirectoryAclProfile RegisterDirectoryProfile(string path, ProgramDataDirectoryAclProfile profile)
    {
        var normalizedPath = pathGuard.NormalizeAbsolutePathUnderRoot(path);
        if (string.Equals(normalizedPath, RootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ProgramData root does not use a managed ACL profile.");
        }

        var relativePath = GetRootRelativePath(normalizedPath);
        if (TryResolveExactKnownProfile(relativePath, out var knownProfile))
        {
            if (knownProfile != profile)
            {
                throw new InvalidOperationException(
                    $"Managed ProgramData directory '{normalizedPath}' has fixed ACL profile '{knownProfile}' but caller requested '{profile}'.");
            }

            return knownProfile;
        }

        var allowsDynamicRegistration = ProgramDataPolicies.Directories.Any(policy =>
            policy.AllowsDynamicChildren &&
            IsRelativePathUnderDirectory(relativePath, policy.RelativePath));
        if (!allowsDynamicRegistration)
        {
            throw new InvalidOperationException(
                $"Managed ProgramData directory '{normalizedPath}' is not under a known caller-owned ProgramData subtree.");
        }

        var existingProfile = dynamicProfiles.GetOrAdd(normalizedPath, profile);
        if (existingProfile != profile)
        {
            throw new InvalidOperationException(
                $"Managed ProgramData directory '{normalizedPath}' was already registered with ACL profile '{existingProfile}' but caller requested '{profile}'.");
        }

        return existingProfile;
    }

    public ProgramDataDirectoryAclProfile? ResolveKnownDirectoryProfile(string path)
    {
        var normalizedPath = pathGuard.NormalizeAbsolutePathUnderRoot(path);
        if (string.Equals(normalizedPath, RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relativePath = GetRootRelativePath(normalizedPath);
        if (TryResolveExactKnownProfile(relativePath, out var knownProfile))
        {
            return knownProfile;
        }

        if (dynamicProfiles.TryGetValue(normalizedPath, out var dynamicProfile))
        {
            return dynamicProfile;
        }

        return null;
    }

    public bool IsUnderDynamicDirectoryPolicyRoot(string path)
    {
        var normalizedPath = pathGuard.NormalizeAbsolutePathUnderRoot(path);
        if (string.Equals(normalizedPath, RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relativePath = GetRootRelativePath(normalizedPath);
        return ProgramDataPolicies.Directories.Any(policy =>
            policy.AllowsDynamicChildren &&
            IsRelativePathUnderDirectory(relativePath, policy.RelativePath));
    }

    public bool IsKnownManagedDirectory(string path)
    {
        var normalizedPath = pathGuard.NormalizeAbsolutePathUnderRoot(path);
        if (string.Equals(normalizedPath, RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryResolveExactKnownProfile(GetRootRelativePath(normalizedPath), out _);
    }

    public ProgramDataAllowedOwnerPolicy ResolveOwnerPolicy(string path)
    {
        var absolutePath = pathGuard.NormalizeAbsolutePathUnderRoot(path);
        var relativePath = string.Equals(absolutePath, RootPath, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : Path.GetRelativePath(RootPath, absolutePath);
        if (relativePath.Equals(ProgramDataPolicies.Ac.RelativePath, StringComparison.OrdinalIgnoreCase) ||
            relativePath.StartsWith($"{ProgramDataPolicies.Ac.RelativePath}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return ProgramDataAllowedOwnerPolicy.AdministratorsCurrentAccountWithExpectedOwners;
        }

        return ProgramDataAllowedOwnerPolicy.AdministratorsOrCurrentAccount;
    }

    private static bool TryResolveExactKnownProfile(string relativePath, out ProgramDataDirectoryAclProfile profile)
    {
        var policy = ProgramDataPolicies.Directories.FirstOrDefault(
            candidate => string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (policy != null)
        {
            profile = policy.Profile;
            return true;
        }

        profile = default;
        return false;
    }

    private static bool IsRelativePathUnderDirectory(string relativePath, string directoryRelativePath)
    {
        var directoryWithSeparator = directoryRelativePath + Path.DirectorySeparatorChar;
        return relativePath.StartsWith(directoryWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private string GetRootRelativePath(string normalizedPath)
        => Path.GetRelativePath(RootPath, normalizedPath);

}
