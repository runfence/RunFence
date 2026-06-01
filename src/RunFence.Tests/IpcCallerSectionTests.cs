using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Tests.Helpers;
using RunFence.UI;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class IpcCallerSectionTests
{
    [Fact]
    public void AddCaller_AcceptedSelection_AddsEntryAndRaisesChanged()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var modalService = new Mock<IIpcCallerModalService>();
            modalService
                .Setup(service => service.PromptForCallerIdentity(It.IsAny<IWin32Window?>(), It.IsAny<IReadOnlyList<LocalUserAccount>>(), It.IsAny<ISidEntryHelper>()))
                .Returns(new CallerIdentitySelectionResult(true, "S-1-5-21-test", "TestUser"));
            var sidResolver = new Mock<ISidResolver>();
            sidResolver.Setup(service => service.TryResolveName(It.IsAny<string>())).Returns<string>(sid => sid);
            var accountQueryService = Mock.Of<IWindowsAccountQueryService>(service => service.GetLocalUsers() == Array.Empty<LocalUserAccount>());
            var receivedCallCount = 0;
            string? receivedSid = null;
            string? receivedName = null;

            using var host = new Form();
            using var section = new IpcCallerSection(
                accountQueryService,
                Mock.Of<ISidEntryHelper>(),
                new SidDisplayNameResolver(sidResolver.Object, Mock.Of<IProfilePathResolver>()),
                modalService.Object);
            host.Controls.Add(section);
            StaTestHelper.CreateControlTree(host);
            section.SetSidNames(null, (sid, name) =>
            {
                receivedCallCount++;
                receivedSid = sid;
                receivedName = name;
            });

            var changedCount = 0;
            section.Changed += () => changedCount++;

            FindToolStripButton(section, "Add...").PerformClick();
            Application.DoEvents();

            Assert.Equal(["S-1-5-21-test"], section.GetCallers());
            Assert.Equal(1, changedCount);
            Assert.Equal(1, receivedCallCount);
            Assert.Equal("S-1-5-21-test", receivedSid);
            Assert.Equal("TestUser", receivedName);
            modalService.Verify(
                service => service.PromptForCallerIdentity(host, It.IsAny<IReadOnlyList<LocalUserAccount>>(), It.IsAny<ISidEntryHelper>()),
                Times.Once);
        });
    }

    [Fact]
    public void AddCaller_DuplicateSelection_ShowsWarningWithoutAddingDuplicate()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var modalService = new Mock<IIpcCallerModalService>();
            modalService
                .Setup(service => service.PromptForCallerIdentity(It.IsAny<IWin32Window?>(), It.IsAny<IReadOnlyList<LocalUserAccount>>(), It.IsAny<ISidEntryHelper>()))
                .Returns(new CallerIdentitySelectionResult(true, "S-1-5-21-test", "TestUser"));
            var sidResolver = new Mock<ISidResolver>();
            sidResolver.Setup(service => service.TryResolveName(It.IsAny<string>())).Returns<string>(sid => sid);
            var accountQueryService = Mock.Of<IWindowsAccountQueryService>(service => service.GetLocalUsers() == Array.Empty<LocalUserAccount>());

            using var host = new Form();
            using var section = new IpcCallerSection(
                accountQueryService,
                Mock.Of<ISidEntryHelper>(),
                new SidDisplayNameResolver(sidResolver.Object, Mock.Of<IProfilePathResolver>()),
                modalService.Object);
            host.Controls.Add(section);
            section.SetCallers(["S-1-5-21-test"]);
            StaTestHelper.CreateControlTree(host);

            FindToolStripButton(section, "Add...").PerformClick();
            Application.DoEvents();

            Assert.Equal(["S-1-5-21-test"], section.GetCallers());
            modalService.Verify(
                service => service.PromptForCallerIdentity(host, It.IsAny<IReadOnlyList<LocalUserAccount>>(), It.IsAny<ISidEntryHelper>()),
                Times.Once);
            modalService.Verify(service => service.ShowDuplicateCallerWarning(host), Times.Once);
        });
    }

    [Fact]
    public void AddCaller_WithoutParentForm_UsesSectionHandleAsOwner()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var modalService = new Mock<IIpcCallerModalService>();
            modalService
                .Setup(service => service.PromptForCallerIdentity(It.IsAny<IWin32Window?>(), It.IsAny<IReadOnlyList<LocalUserAccount>>(), It.IsAny<ISidEntryHelper>()))
                .Returns(new CallerIdentitySelectionResult(true, "S-1-5-21-test", "TestUser"));
            var sidResolver = new Mock<ISidResolver>();
            sidResolver.Setup(service => service.TryResolveName(It.IsAny<string>())).Returns<string>(sid => sid);
            var accountQueryService = Mock.Of<IWindowsAccountQueryService>(service => service.GetLocalUsers() == Array.Empty<LocalUserAccount>());

            using var section = new IpcCallerSection(
                accountQueryService,
                Mock.Of<ISidEntryHelper>(),
                new SidDisplayNameResolver(sidResolver.Object, Mock.Of<IProfilePathResolver>()),
                modalService.Object);
            StaTestHelper.CreateControlTree(section);

            FindToolStripButton(section, "Add...").PerformClick();
            Application.DoEvents();

            modalService.Verify(
                service => service.PromptForCallerIdentity(section, It.IsAny<IReadOnlyList<LocalUserAccount>>(), It.IsAny<ISidEntryHelper>()),
                Times.Once);
        });
    }

    [Fact]
    public void AddCaller_DuplicateWithoutParentForm_UsesSectionHandleForWarningOwner()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var modalService = new Mock<IIpcCallerModalService>();
            modalService
                .Setup(service => service.PromptForCallerIdentity(It.IsAny<IWin32Window?>(), It.IsAny<IReadOnlyList<LocalUserAccount>>(), It.IsAny<ISidEntryHelper>()))
                .Returns(new CallerIdentitySelectionResult(true, "S-1-5-21-test", "TestUser"));
            var sidResolver = new Mock<ISidResolver>();
            sidResolver.Setup(service => service.TryResolveName(It.IsAny<string>())).Returns<string>(sid => sid);
            var accountQueryService = Mock.Of<IWindowsAccountQueryService>(service => service.GetLocalUsers() == Array.Empty<LocalUserAccount>());

            using var section = new IpcCallerSection(
                accountQueryService,
                Mock.Of<ISidEntryHelper>(),
                new SidDisplayNameResolver(sidResolver.Object, Mock.Of<IProfilePathResolver>()),
                modalService.Object);
            section.SetCallers(["S-1-5-21-test"]);
            StaTestHelper.CreateControlTree(section);

            FindToolStripButton(section, "Add...").PerformClick();
            Application.DoEvents();

            modalService.Verify(
                service => service.PromptForCallerIdentity(section, It.IsAny<IReadOnlyList<LocalUserAccount>>(), It.IsAny<ISidEntryHelper>()),
                Times.Once);
            modalService.Verify(service => service.ShowDuplicateCallerWarning(section), Times.Once);
        });
    }

    private static ToolStripButton FindToolStripButton(Control root, string toolTipText)
        => root.Controls.OfType<ToolStrip>()
            .SelectMany(toolStrip => toolStrip.Items.OfType<ToolStripButton>())
            .Single(button => button.ToolTipText == toolTipText);
}
