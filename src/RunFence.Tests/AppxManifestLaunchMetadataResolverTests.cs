using RunFence.AppxLauncher;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public sealed class AppxManifestLaunchMetadataResolverTests
{
    [Fact]
    public void Resolve_MatchingExecutable_ReturnsAumidCommandAndPreferredProtocolFromArguments()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var packageDirectory = CreatePackage(
                tempDirectory.FullName,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                  <Applications>
                    <Application Id="Other" Executable="other/Other.exe" EntryPoint="Windows.FullTrustApplication">
                      <Extensions>
                        <uap:Extension xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" Category="windows.protocol">
                          <uap:Protocol Name="other" />
                        </uap:Extension>
                      </Extensions>
                    </Application>
                    <Application Id="App" Executable="app/Codex.exe" EntryPoint="Windows.FullTrustApplication">
                      <Extensions>
                        <uap:Extension xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" Category="windows.protocol">
                          <uap:Protocol Name="codex" />
                        </uap:Extension>
                        <uap:Extension xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" Category="windows.protocol">
                          <uap:Protocol Name="chatgpt" />
                        </uap:Extension>
                      </Extensions>
                    </Application>
                  </Applications>
                </Package>
                """);
            var exePath = Path.Combine(packageDirectory, "app", "Codex.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exePath)!);
            File.WriteAllText(exePath, string.Empty);
            var resolver = new AppxManifestLaunchMetadataResolver();

            var result = resolver.Resolve(exePath, "chatgpt:--prompt \"two words\"");

            Assert.True(result.Success);
            Assert.Equal("OpenAI.Codex_2p2nqsd0c76g0", result.Metadata.PackageFamilyName);
            Assert.Equal("OpenAI.Codex_2p2nqsd0c76g0!App", result.Metadata.AppUserModelId);
            Assert.Equal(exePath, result.Metadata.Command);
            Assert.Equal("chatgpt", result.Metadata.Protocol);
            Assert.True(result.Metadata.IsFullTrustApplication);
            Assert.False(result.Metadata.SupportsMultipleInstances);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void Resolve_NoLeadingProtocol_UsesFirstMatchingApplicationProtocol()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var packageDirectory = CreatePackage(
                tempDirectory.FullName,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                  <Applications>
                    <Application Id="App" Executable="app/Codex.exe" EntryPoint="Windows.FullTrustApplication">
                      <Extensions>
                        <uap:Extension xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" Category="windows.protocol">
                          <uap:Protocol Name="codex" />
                        </uap:Extension>
                      </Extensions>
                    </Application>
                  </Applications>
                </Package>
                """);
            var exePath = Path.Combine(packageDirectory, "app", "Codex.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exePath)!);
            File.WriteAllText(exePath, string.Empty);
            var resolver = new AppxManifestLaunchMetadataResolver();

            var result = resolver.Resolve(exePath, "--prompt \"two words\"");

            Assert.True(result.Success);
            Assert.Equal("codex", result.Metadata.Protocol);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void Resolve_NoMatchingExecutable_ReturnsManifestResolutionFailure()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var packageDirectory = CreatePackage(
                tempDirectory.FullName,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                  <Applications>
                    <Application Id="Other" Executable="other/Other.exe" EntryPoint="Windows.FullTrustApplication" />
                  </Applications>
                </Package>
                """);
            var exePath = Path.Combine(packageDirectory, "app", "Codex.exe");
            var resolver = new AppxManifestLaunchMetadataResolver();

            var result = resolver.Resolve(exePath, string.Empty);

            Assert.False(result.Success);
            Assert.Equal(AppxLaunchExitCode.ManifestResolutionFailed, result.Error.ExitCode);
            Assert.Equal("ResolveApplication", result.Error.Stage);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void Resolve_MatchingExecutableWithNonFullTrustEntryPoint_ReturnsNonFullTrustMetadata()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var packageDirectory = CreatePackage(
                tempDirectory.FullName,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
                  <Applications>
                    <Application Id="App" Executable="app/Codex.exe" EntryPoint="OpenAI.App">
                      <Extensions>
                        <uap:Extension xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" Category="windows.protocol">
                          <uap:Protocol Name="codex" />
                        </uap:Extension>
                      </Extensions>
                    </Application>
                  </Applications>
                </Package>
                """);
            var exePath = Path.Combine(packageDirectory, "app", "Codex.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exePath)!);
            File.WriteAllText(exePath, string.Empty);
            var resolver = new AppxManifestLaunchMetadataResolver();

            var result = resolver.Resolve(exePath, "codex:");

            Assert.True(result.Success);
            Assert.False(result.Metadata.IsFullTrustApplication);
            Assert.Equal("codex", result.Metadata.Protocol);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Theory]
    [InlineData("desktop4", "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4")]
    [InlineData("uap10", "http://schemas.microsoft.com/appx/manifest/uap/windows10/10")]
    public void Resolve_MatchingExecutableWithApplicationMultiInstanceAttribute_ReturnsMultiInstanceMetadata(
        string prefix,
        string namespaceUri)
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var packageDirectory = CreatePackage(
                tempDirectory.FullName,
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                         xmlns:{prefix}="{namespaceUri}">
                  <Applications>
                    <Application Id="App"
                                 Executable="app/Codex.exe"
                                 EntryPoint="Windows.FullTrustApplication"
                                 {prefix}:SupportsMultipleInstances="true" />
                  </Applications>
                </Package>
                """);
            var exePath = Path.Combine(packageDirectory, "app", "Codex.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exePath)!);
            File.WriteAllText(exePath, string.Empty);
            var resolver = new AppxManifestLaunchMetadataResolver();

            var result = resolver.Resolve(exePath, string.Empty);

            Assert.True(result.Success);
            Assert.True(result.Metadata.SupportsMultipleInstances);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void Resolve_MatchingExecutableWithFalseMultiInstanceAttribute_ReturnsSingleInstanceMetadata()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var packageDirectory = CreatePackage(
                tempDirectory.FullName,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                         xmlns:desktop4="http://schemas.microsoft.com/appx/manifest/desktop/windows10/4">
                  <Applications>
                    <Application Id="App"
                                 Executable="app/Codex.exe"
                                 EntryPoint="Windows.FullTrustApplication"
                                 desktop4:SupportsMultipleInstances="false" />
                  </Applications>
                </Package>
                """);
            var exePath = Path.Combine(packageDirectory, "app", "Codex.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exePath)!);
            File.WriteAllText(exePath, string.Empty);
            var resolver = new AppxManifestLaunchMetadataResolver();

            var result = resolver.Resolve(exePath, string.Empty);

            Assert.True(result.Success);
            Assert.False(result.Metadata.SupportsMultipleInstances);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    [Fact]
    public void Resolve_MatchingExecutableWithUnknownMultiInstanceAttribute_ReturnsSingleInstanceMetadata()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var packageDirectory = CreatePackage(
                tempDirectory.FullName,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                         xmlns:custom="urn:runfence:test">
                  <Applications>
                    <Application Id="App"
                                 Executable="app/Codex.exe"
                                 EntryPoint="Windows.FullTrustApplication"
                                 custom:SupportsMultipleInstances="true" />
                  </Applications>
                </Package>
                """);
            var exePath = Path.Combine(packageDirectory, "app", "Codex.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exePath)!);
            File.WriteAllText(exePath, string.Empty);
            var resolver = new AppxManifestLaunchMetadataResolver();

            var result = resolver.Resolve(exePath, string.Empty);

            Assert.True(result.Success);
            Assert.False(result.Metadata.SupportsMultipleInstances);
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }

    private static string CreatePackage(string root, string manifest)
    {
        var packageDirectory = Path.Combine(
            root,
            "Program Files",
            "WindowsApps",
            "OpenAI.Codex_26.527.3686.0_x64__2p2nqsd0c76g0");
        Directory.CreateDirectory(packageDirectory);
        File.WriteAllText(Path.Combine(packageDirectory, "AppxManifest.xml"), manifest);
        return packageDirectory;
    }
}
