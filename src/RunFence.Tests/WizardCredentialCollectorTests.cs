using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Wizard;
using Xunit;

namespace RunFence.Tests;

public class WizardCredentialCollectorTests
{
    private const string Sid = "S-1-5-21-9999999999-9999999999-9999999999-2001";
    private const string OtherSid = "S-1-5-21-9999999999-9999999999-9999999999-2002";

    private readonly Mock<ISecureDesktopRunner> _secureDesktopRunner = new();
    private readonly Mock<ICredentialDialogRunner> _credentialDialogRunner = new();
    private readonly Mock<IWizardProgressReporter> _progress = new();

    private static SessionContext CreateSession(bool addCredentialForSid = false)
    {
        var session = new SessionContext
{
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore(),
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        if (addCredentialForSid)
            session.CredentialStore.Credentials.Add(new CredentialEntry { Sid = Sid });
        return session;
    }

    private WizardCredentialCollector CreateCollector(SessionContext session) =>
        new(_secureDesktopRunner.Object, _credentialDialogRunner.Object, new LambdaSessionProvider(() => session));

    [Fact]
    public void CollectCredentialForStep_NoExistingCredential_InvokesRunAndDialogRunner()
    {
        using var session = CreateSession();
        var runnerError = new InvalidOperationException("Dialog unavailable in test");

        _secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Callback<Action>(action => action());

        _credentialDialogRunner
            .Setup(r => r.ShowCredentialDialog(
                It.Is<CredentialEntry>(c => c.Sid == Sid),
                session.Database.SidNames))
            .Throws(runnerError);

        var collector = CreateCollector(session);

        var ex = Assert.Throws<WizardReportedException>(() =>
            collector.CollectCredentialForStep(Sid, _progress.Object));

        _secureDesktopRunner.Verify(r => r.Run(It.IsAny<Action>()), Times.Once);
        _credentialDialogRunner.Verify(r => r.ShowCredentialDialog(
            It.Is<CredentialEntry>(c => c.Sid == Sid),
            session.Database.SidNames), Times.Once);
        _progress.Verify(p => p.ReportError(
            It.Is<string>(s => s.Contains("Dialog unavailable in test"))), Times.Once);
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Same(runnerError, inner.InnerException);
    }

    [Fact]
    public void CollectCredentialForStep_DialogOk_ReturnsPasswordWithoutError()
    {
        using var session = CreateSession();
        var password = ProtectedString.FromChars("WizardPass1!".AsSpan());

        _secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Callback<Action>(action => action());

        _credentialDialogRunner
            .Setup(r => r.ShowCredentialDialog(
                It.Is<CredentialEntry>(c => c.Sid == Sid),
                session.Database.SidNames))
            .Returns(new CredentialDialogResult(true, password));

        var collector = CreateCollector(session);

        var result = collector.CollectCredentialForStep(Sid, _progress.Object);

        Assert.Same(password, result);
        _secureDesktopRunner.Verify(r => r.Run(It.IsAny<Action>()), Times.Once);
        _progress.Verify(p => p.ReportError(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CollectCredentialForStep_AlreadyHasCredential_ReturnsNull()
    {
        using var session = CreateSession(addCredentialForSid: true);
        var collector = CreateCollector(session);

        var result = collector.CollectCredentialForStep(Sid, _progress.Object);

        Assert.Null(result);
        _secureDesktopRunner.Verify(r => r.Run(It.IsAny<Action>()), Times.Never);
        _credentialDialogRunner.Verify(r => r.ShowCredentialDialog(
            It.IsAny<CredentialEntry>(),
            It.IsAny<IReadOnlyDictionary<string, string>>()), Times.Never);
        _progress.Verify(p => p.ReportError(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CollectCredentialForStep_CredentialExistsForDifferentSid_ProceedsToCollection()
    {
        using var session = CreateSession();
        session.CredentialStore.Credentials.Add(new CredentialEntry { Sid = OtherSid });

        _secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        _credentialDialogRunner
            .Setup(r => r.ShowCredentialDialog(It.IsAny<CredentialEntry>(), It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(new CredentialDialogResult(false, null));

        var collector = CreateCollector(session);

        Assert.Throws<OperationCanceledException>(() =>
            collector.CollectCredentialForStep(Sid, _progress.Object));

        _secureDesktopRunner.Verify(r => r.Run(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void CollectCredentialForStep_DialogException_ReportsErrorAndThrowsWizardReportedException()
    {
        using var session = CreateSession();
        var innerException = new InvalidOperationException("Secure desktop unavailable");
        _secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Throws(innerException);

        var collector = CreateCollector(session);

        var ex = Assert.Throws<WizardReportedException>(() =>
            collector.CollectCredentialForStep(Sid, _progress.Object));

        _progress.Verify(p => p.ReportError(
            It.Is<string>(s => s.Contains("Secure desktop unavailable"))), Times.Once);
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Same(innerException, inner.InnerException);
    }

    [Fact]
    public void CollectCredentialForStep_AcceptedDialogWithoutPassword_ReportsErrorAndThrowsWizardReportedException()
    {
        using var session = CreateSession();
        _secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        _credentialDialogRunner
            .Setup(r => r.ShowCredentialDialog(It.IsAny<CredentialEntry>(), It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(new CredentialDialogResult(true, null));

        var collector = CreateCollector(session);

        var ex = Assert.Throws<WizardReportedException>(() =>
            collector.CollectCredentialForStep(Sid, _progress.Object));

        _progress.Verify(p => p.ReportError(
            It.Is<string>(s => s.Contains("did not return a password"))), Times.Once);
        Assert.IsType<InvalidOperationException>(ex.InnerException);

        _secureDesktopRunner.Verify(r => r.Run(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void CollectCredentialForStep_SecureDesktopReturnsWithoutRunningDialog_ReportsErrorAndThrowsWizardReportedException()
    {
        using var session = CreateSession();
        _secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Callback<Action>(_ => { });

        var collector = CreateCollector(session);

        var ex = Assert.Throws<WizardReportedException>(() =>
            collector.CollectCredentialForStep(Sid, _progress.Object));

        _progress.Verify(p => p.ReportError(
            It.Is<string>(s => s.Contains("did not open"))), Times.Once);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        _credentialDialogRunner.Verify(r => r.ShowCredentialDialog(
            It.IsAny<CredentialEntry>(),
            It.IsAny<IReadOnlyDictionary<string, string>>()), Times.Never);
    }

    [Fact]
    public void CollectCredentialForStep_DialogCancel_ReportsPasswordRequired()
    {
        using var session = CreateSession();
        _secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        _credentialDialogRunner
            .Setup(r => r.ShowCredentialDialog(It.IsAny<CredentialEntry>(), It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(new CredentialDialogResult(false, null));

        var collector = CreateCollector(session);

        Assert.Throws<OperationCanceledException>(() =>
            collector.CollectCredentialForStep(Sid, _progress.Object));

        _progress.Verify(p => p.ReportError(
            It.Is<string>(s => s.Contains("Password is required"))), Times.Once);
    }

    [Fact]
    public void CollectCredentialForStep_SecureDesktopCancellation_RethrowsOperationCanceled()
    {
        using var session = CreateSession();
        _secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Throws(new OperationCanceledException("Canceled on secure desktop."));

        var collector = CreateCollector(session);

        var ex = Assert.Throws<OperationCanceledException>(() =>
            collector.CollectCredentialForStep(Sid, _progress.Object));

        Assert.Equal("Canceled on secure desktop.", ex.Message);
        _progress.Verify(p => p.ReportError(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CollectCredentialForStep_AlreadyHasCredential_SidCaseInsensitive_ReturnsNull()
    {
        using var session = CreateSession();
        session.CredentialStore.Credentials.Add(
            new CredentialEntry { Sid = Sid.ToLowerInvariant() });

        var collector = CreateCollector(session);

        var result = collector.CollectCredentialForStep(Sid.ToUpperInvariant(), _progress.Object);

        Assert.Null(result);
    }
}
