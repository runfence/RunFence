using RunFence.AppxLauncher;
using Xunit;

namespace RunFence.Tests;

public sealed class WinRtPackageRegistrationTests
{
    [Fact]
    public void RegisterPackageByFamilyName_PackageFamilyName_PassesToContextAndDisposesOnSuccess()
    {
        const string packageFamilyName = "Microsoft.TestApp_8wekyb3d8bbwe";

        var context = new FakeContext();
        var registration = new WinRtPackageRegistration(new FakeFactory(context));

        registration.RegisterPackageByFamilyName(packageFamilyName);

        Assert.Equal(packageFamilyName, context.RegisteredPackageFamilyName);
        Assert.True(context.DisposeCalled);
    }

    [Fact]
    public void RegisterPackageByFamilyName_WhenRegisterThrows_DisposesContext()
    {
        const string packageFamilyName = "Microsoft.TestApp_8wekyb3d8bbwe";
        var context = new FakeContext
        {
            RegisterThrows = true
        };
        var registration = new WinRtPackageRegistration(new FakeFactory(context));

        Assert.Throws<InvalidOperationException>(() => registration.RegisterPackageByFamilyName(packageFamilyName));

        Assert.Equal(packageFamilyName, context.RegisteredPackageFamilyName);
        Assert.True(context.DisposeCalled);
    }

    private sealed class FakeFactory(FakeContext context) : IWinRtPackageManagerContextFactory
    {
        private readonly FakeContext _context = context;

        public IWinRtPackageManagerContext Create()
            => _context;
    }

    private sealed class FakeContext : IWinRtPackageManagerContext
    {
        private bool _disposed;
        public string? RegisteredPackageFamilyName { get; private set; }
        public bool DisposeCalled => _disposed;
        public bool RegisterThrows { get; init; }

        public void RegisterPackageByFamilyName(string packageFamilyName)
        {
            RegisteredPackageFamilyName = packageFamilyName;
            if (RegisterThrows)
                throw new InvalidOperationException("Registration failed.");
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
