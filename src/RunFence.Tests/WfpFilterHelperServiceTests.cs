using Moq;
using RunFence.Core;
using RunFence.Firewall;
using RunFence.Firewall.Wfp;
using Xunit;

namespace RunFence.Tests;

public class WfpFilterHelperServiceTests
{
    private readonly WfpFilterHelperService _service = new(new Mock<ILoggingService>().Object);

    [Fact]
    public void AddFilterWithSddl_InvalidSddl_ThrowsHelperFailure()
    {
        var filterKey = Guid.NewGuid();
        var layerKey = WfpNative.LayerAleAuthConnectV4;

        var ex = Assert.Throws<WfpFilterHelperException>(() =>
            _service.AddFilterWithSddl(
                IntPtr.Zero,
                "not-an-sddl",
                conditionCount: 0,
                ref filterKey,
                ref layerKey,
                "Test Filter",
                WfpNative.FWPM_FILTER_FLAG_PERSISTENT,
                "Test",
                (_, _, _) => { }));

        Assert.Contains("Failed to convert SDDL", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddFilterWithSddl_InvalidHandle_ThrowsHelperFailure()
    {
        var filterKey = Guid.NewGuid();
        var layerKey = WfpNative.LayerAleAuthConnectV4;

        var ex = Assert.Throws<WfpFilterHelperException>(() =>
            _service.AddFilterWithSddl(
                IntPtr.Zero,
                FirewallSddlHelper.BuildSddl("S-1-5-18"),
                conditionCount: 1,
                ref filterKey,
                ref layerKey,
                "Test Filter",
                WfpNative.FWPM_FILTER_FLAG_PERSISTENT,
                "Test",
                (condArrayPtr, sdBlobPtr, _) =>
                    WfpFilterStructHelper.WriteCondition(
                        condArrayPtr,
                        0,
                        WfpNative.ConditionAleUserId,
                        WfpNative.FWP_MATCH_EQUAL,
                        WfpNative.FWP_SECURITY_DESCRIPTOR_TYPE,
                        sdBlobPtr)));

        Assert.Contains("FwpmFilterAdd0 failed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteFilter_InvalidHandle_ThrowsHelperFailure()
    {
        var filterKey = Guid.NewGuid();

        var ex = Assert.Throws<WfpFilterHelperException>(() =>
            _service.DeleteFilter(IntPtr.Zero, ref filterKey, "Test"));

        Assert.Contains("FwpmFilterDeleteByKey0 failed", ex.Message, StringComparison.Ordinal);
    }
}
