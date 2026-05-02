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
            CredentialStore = new CredentialStore()
        };
        if (addCredentialForSid)
            session.CredentialStore.Credentials.Add(new CredentialEntry { Sid = Sid });
        return session;
    }

    private WizardCredentialCollector CreateCollector() =>
        new(_secureDesktopRunner.Object, _credentialDialogRunner.Object);

    [Fact]
    public void CollectIfNeeded_NoExistingCredential_InvokesRunAndDialogRunner()
    {
        var session = CreateSession();
        var runnerError = new InvalidOperationException("Dialog unavailable in test");

        _secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Callback<Action>(action => action());

        _credentialDialogRunner
            .Setup(r => r.ShowCredentialDialog(
                It.Is<CredentialEntry>(c => c.Sid == Sid),
                session.Database.SidNames))
            .Throws(runnerError);

        var collector = CreateCollector();

        var ex = Assert.Throws<OperationCanceledException>(() =>
            collector.CollectIfNeeded(Sid, session, _progress.Object));

        _secureDesktopRunner.Verify(r => r.Run(It.IsAny<Action>()), Times.Once);
        _credentialDialogRunner.Verify(r => r.ShowCredentialDialog(
            It.Is<CredentialEntry>(c => c.Sid == Sid),
            session.Database.SidNames), Times.Once);
        _progress.Verify(p => p.ReportError(
            It.Is<string>(s => s.Contains("Dialog unavailable in test"))), Times.Once);
        Assert.Same(runnerError, ex.InnerException);
    }

    [Fact]
    public void CollectIfNeeded_DialogOk_ReturnsPasswordWithoutError()
    {
        var session = CreateSession();
        var password = ProtectedString.FromChars("WizardPass1!".AsSpan());

        _secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Callback<Action>(action => action());

        _credentialDialogRunner
            .Setup(r => r.ShowCredentialDialog(
                It.Is<CredentialEntry>(c => c.Sid == Sid),
                session.Database.SidNames))
            .Returns(new CredentialDialogResult(true, password));

        var collector = CreateCollector();

        var result = collector.CollectIfNeeded(Sid, session, _progress.Object);

        Assert.Same(password, result);
        _secureDesktopRunner.Verify(r => r.Run(It.IsAny<Action>()), Times.Once);
        _progress.Verify(p => p.ReportError(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CollectIfNeeded_AlreadyHasCredential_ReturnsNull()
    {
        var session = CreateSession(addCredentialForSid: true);
        var collector = CreateCollector();

        var result = collector.CollectIfNeeded(Sid, session, _progress.Object);

        Assert.Null(result);
        _secureDesktopRunner.Verify(r => r.Run(It.IsAny<Action>()), Times.Never);
        _credentialDialogRunner.Verify(r => r.ShowCredentialDialog(
            It.IsAny<CredentialEntry>(),
            It.IsAny<IReadOnlyDictionary<string, string>>()), Times.Never);
        _progress.Verify(p => p.ReportError(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CollectIfNeeded_CredentialExistsForDifferentSid_ProceedsToCollection()
    {
        var session = CreateSession();
        session.CredentialStore.Credentials.Add(new CredentialEntry { Sid = OtherSid });

        _secureDesktopRunner.Setup(r => r.Run(It.IsAny<Action>()));

        var collector = CreateCollector();

        Assert.Throws<OperationCanceledException>(() =>
            collector.CollectIfNeeded(Sid, session, _progress.Object));

        _secureDesktopRunner.Verify(r => r.Run(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void CollectIfNeeded_DialogException_ReportsErrorAndThrowsOperationCanceled()
    {
        var session = CreateSession();
        var innerException = new InvalidOperationException("Secure desktop unavailable");
        _secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Throws(innerException);

        var collector = CreateCollector();

        var ex = Assert.Throws<OperationCanceledException>(() =>
            collector.CollectIfNeeded(Sid, session, _progress.Object));

        _progress.Verify(p => p.ReportError(
            It.Is<string>(s => s.Contains("Secure desktop unavailable"))), Times.Once);
        Assert.Same(innerException, ex.InnerException);
    }

    [Fact]
    public void CollectIfNeeded_NullPassword_ReportsErrorAndThrowsOperationCanceled()
    {
        var session = CreateSession();
        _secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()));

        var collector = CreateCollector();

        var ex = Assert.Throws<OperationCanceledException>(() =>
            collector.CollectIfNeeded(Sid, session, _progress.Object));

        _progress.Verify(p => p.ReportError(
            It.Is<string>(s => s.Contains("Password is required"))), Times.Once);
        Assert.Null(ex.InnerException);

        _secureDesktopRunner.Verify(r => r.Run(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void CollectIfNeeded_DialogCancel_ReportsPasswordRequired()
    {
        var session = CreateSession();
        _secureDesktopRunner
            .Setup(r => r.Run(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        _credentialDialogRunner
            .Setup(r => r.ShowCredentialDialog(It.IsAny<CredentialEntry>(), It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(new CredentialDialogResult(false, null));

        var collector = CreateCollector();

        Assert.Throws<OperationCanceledException>(() =>
            collector.CollectIfNeeded(Sid, session, _progress.Object));

        _progress.Verify(p => p.ReportError(
            It.Is<string>(s => s.Contains("Password is required"))), Times.Once);
    }

    [Fact]
    public void CollectIfNeeded_AlreadyHasCredential_SidCaseInsensitive_ReturnsNull()
    {
        var session = CreateSession();
        session.CredentialStore.Credentials.Add(
            new CredentialEntry { Sid = Sid.ToLowerInvariant() });

        var collector = CreateCollector();

        var result = collector.CollectIfNeeded(Sid.ToUpperInvariant(), session, _progress.Object);

        Assert.Null(result);
    }
}
