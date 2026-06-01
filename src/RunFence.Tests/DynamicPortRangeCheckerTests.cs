using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class DynamicPortRangeCheckerTests
{
    [Fact]
    public void ParseDynamicPortRange_StandardEnglishOutput_ReturnsCorrectValues()
    {
        const string output = """
            Protocol tcp Dynamic Port Range
            ---------------------------------
            Start Port      : 49152
            Number of Ports : 16384
            """;

        var (start, count) = DynamicPortRangeChecker.ParseDynamicPortRange(output);

        Assert.Equal(49152, start);
        Assert.Equal(16384, count);
    }

    [Fact]
    public void ParseDynamicPortRange_GermanLocale_ReturnsCorrectValues()
    {
        const string output = """
            Protokoll tcp Dynamischer Portbereich
            ---------------------------------
            Startport       : 49152
            Anzahl von Ports: 16384
            """;

        var (start, count) = DynamicPortRangeChecker.ParseDynamicPortRange(output);

        Assert.Equal(49152, start);
        Assert.Equal(16384, count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no numbers here at all")]
    public void ParseDynamicPortRange_UnparsableOutput_ReturnsFallbackDefaults(string output)
    {
        var (start, count) = DynamicPortRangeChecker.ParseDynamicPortRange(output);

        Assert.Equal(DynamicPortRangeChecker.StandardEphemeralStart, start);
        Assert.Equal(DynamicPortRangeChecker.StandardEphemeralCount, count);
    }

    [Fact]
    public async Task ReadDynamicPortRanges_UsesExactNetshArguments()
    {
        var netsh = new TestNetshCommandRunner();
        var checker = new DynamicPortRangeChecker(Mock.Of<ILoggingService>(), Confirm(true).Object, netsh);
        netsh.Enqueue("int ipv4 show dynamicport tcp", SuccessOutput(60000, 1000));
        netsh.Enqueue("int ipv6 show dynamicport tcp", SuccessOutput(61000, 1000));

        var ipv4 = await checker.ReadIPv4TcpDynamicPortRangeAsync();
        var ipv6 = await checker.ReadIPv6TcpDynamicPortRangeAsync();

        Assert.Equal((60000, 1000), ipv4);
        Assert.Equal((61000, 1000), ipv6);
        Assert.Equal(
            ["int ipv4 show dynamicport tcp", "int ipv6 show dynamicport tcp"],
            netsh.Arguments);
    }

    [Fact]
    public async Task ReadDynamicPortRange_CommandFailure_ReturnsFallbackDefaults()
    {
        var netsh = new TestNetshCommandRunner();
        var checker = new DynamicPortRangeChecker(Mock.Of<ILoggingService>(), Confirm(true).Object, netsh);
        netsh.Enqueue("int ipv4 show dynamicport tcp", new DynamicPortRangeCommandResult(1, string.Empty, false, "failed"));

        var result = await checker.ReadIPv4TcpDynamicPortRangeAsync();

        Assert.Equal((DynamicPortRangeChecker.StandardEphemeralStart, DynamicPortRangeChecker.StandardEphemeralCount), result);
    }

    [Fact]
    public async Task CheckIfNeededAsync_Confirmed_RunsResetCommands()
    {
        var log = new Mock<ILoggingService>();
        var netsh = new TestNetshCommandRunner();
        var checker = new DynamicPortRangeChecker(log.Object, Confirm(true).Object, netsh);
        netsh.Enqueue("int ipv4 show dynamicport tcp", SuccessOutput(1024, 64511));
        netsh.Enqueue("int ipv6 show dynamicport tcp", SuccessOutput(49152, 16384));
        netsh.Enqueue("int ipv4 set dynamicport tcp start=49152 num=16384", SuccessOutput(0, 0));
        netsh.Enqueue("int ipv6 set dynamicport tcp start=49152 num=16384", SuccessOutput(0, 0));

        await checker.CheckIfNeededAsync(SettingsRequiringCheck());

        Assert.Equal(
            [
                "int ipv4 show dynamicport tcp",
                "int ipv6 show dynamicport tcp",
                "int ipv4 set dynamicport tcp start=49152 num=16384",
                "int ipv6 set dynamicport tcp start=49152 num=16384"
            ],
            netsh.Arguments);
    }

    [Fact]
    public async Task CheckIfNeededAsync_Declined_DismissesFuturePromptsForSession()
    {
        var confirmation = Confirm(false);
        var netsh = new TestNetshCommandRunner();
        var checker = new DynamicPortRangeChecker(Mock.Of<ILoggingService>(), confirmation.Object, netsh);
        netsh.Enqueue("int ipv4 show dynamicport tcp", SuccessOutput(1024, 64511));
        netsh.Enqueue("int ipv6 show dynamicport tcp", SuccessOutput(1024, 64511));

        await checker.CheckIfNeededAsync(SettingsRequiringCheck());
        await checker.CheckIfNeededAsync(SettingsRequiringCheck());

        confirmation.Verify(c => c.Confirm(It.IsAny<string>(), "RunFence — Network Configuration"), Times.Once);
        Assert.Equal(
            ["int ipv4 show dynamicport tcp", "int ipv6 show dynamicport tcp"],
            netsh.Arguments);
    }

    private static DynamicPortRangeCommandResult SuccessOutput(int start, int count) =>
        new(0, $"""
                Start Port      : {start}
                Number of Ports : {count}
                """, false, null);

    private static Mock<IUserConfirmationService> Confirm(bool value)
    {
        var confirmation = new Mock<IUserConfirmationService>();
        confirmation.Setup(c => c.Confirm(It.IsAny<string>(), It.IsAny<string>())).Returns(value);
        return confirmation;
    }

    private static FirewallAccountSettings SettingsRequiringCheck() =>
        new() { AllowLocalhost = false, FilterEphemeralLoopback = true };

    private sealed class TestNetshCommandRunner : INetshCommandRunner
    {
        private readonly Dictionary<string, Queue<DynamicPortRangeCommandResult>> _results = new(StringComparer.Ordinal);

        public List<string> Arguments { get; } = [];

        public void Enqueue(string arguments, DynamicPortRangeCommandResult result)
        {
            if (!_results.TryGetValue(arguments, out var queue))
            {
                queue = new Queue<DynamicPortRangeCommandResult>();
                _results[arguments] = queue;
            }

            queue.Enqueue(result);
        }

        public Task<DynamicPortRangeCommandResult> RunAsync(string arguments)
        {
            Arguments.Add(arguments);
            Assert.True(_results.TryGetValue(arguments, out var queue) && queue.Count > 0, $"Unexpected netsh arguments: {arguments}");
            return Task.FromResult(queue.Dequeue());
        }
    }
}
