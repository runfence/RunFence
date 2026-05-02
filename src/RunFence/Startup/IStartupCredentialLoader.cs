using RunFence.Core.Models;

namespace RunFence.Startup;

/// <summary>
/// Loads and verifies credentials during the startup sequence.
/// Implemented by <see cref="StartupCredentialLoader"/>; this interface exists as a
/// test seam so <see cref="StartupOrchestrator"/> scenarios can be verified without
/// running the full credential store and PIN dialog pipeline.
/// </summary>
public interface IStartupCredentialLoader
{
    CredentialLoadResult? LoadAndVerifyCredentials();
}

/// <summary>Result of the credential load and verify step.</summary>
public record CredentialLoadResult(
    CredentialStore Store,
    byte[] PinDerivedKey,
    byte[]? MismatchKey,
    bool PinBypassed = false);
