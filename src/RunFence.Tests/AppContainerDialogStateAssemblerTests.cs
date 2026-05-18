using RunFence.Account.UI.AppContainer;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public class AppContainerDialogStateAssemblerTests
{
    [Fact]
    public void BuildRequest_CreateNonEphemeral_GeneratesSanitizedProfileName()
    {
        var assembler = new AppContainerDialogStateAssembler();

        var result = assembler.BuildRequest(
            existing: null,
            displayName: " Browser / Main ",
            isEphemeral: false,
            selectedCapabilities: ["S-1-15-3-1", "S-1-15-3-2"],
            loopbackChecked: true,
            comClsids: ["{CLSID-1}"]);

        Assert.Equal("Browser / Main", result.DisplayName);
        Assert.Equal("rfn_browser___main", result.ProfileName);
        Assert.Equal(["S-1-15-3-1", "S-1-15-3-2"], result.Capabilities);
        Assert.True(result.LoopbackChecked);
        Assert.Equal(["{CLSID-1}"], result.ComClsids);
    }

    [Fact]
    public void BuildRequest_CreateEphemeral_UsesGeneratedEphemeralProfileName()
    {
        var assembler = new AppContainerDialogStateAssembler();

        var result = assembler.BuildRequest(
            existing: null,
            displayName: "Ephemeral",
            isEphemeral: true,
            selectedCapabilities: [],
            loopbackChecked: false,
            comClsids: []);

        Assert.StartsWith("rfn_e", result.ProfileName);
        Assert.Equal(11, result.ProfileName.Length);
        Assert.True(result.IsEphemeral);
    }

    [Fact]
    public void BuildRequest_Edit_UsesExistingProfileName()
    {
        var assembler = new AppContainerDialogStateAssembler();
        var existing = new AppContainerEntry { Name = "rfn_existing", DisplayName = "Existing" };

        var result = assembler.BuildRequest(
            existing,
            displayName: "Updated",
            isEphemeral: true,
            selectedCapabilities: ["S-1-15-3-1"],
            loopbackChecked: false,
            comClsids: []);

        Assert.Same(existing, result.Existing);
        Assert.Equal("rfn_existing", result.ProfileName);
        Assert.Equal("Updated", result.DisplayName);
    }
}
