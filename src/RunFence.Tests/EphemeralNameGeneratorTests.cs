using RunFence.Account.Lifecycle;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class EphemeralNameGeneratorTests
{
    private sealed class SequenceRandomSource(params int[] values) : IRandomSource
    {
        private readonly Queue<int> _values = new(values);

        public int NextInt32(int exclusiveUpperBound)
        {
            var next = _values.Dequeue();
            Assert.InRange(next, 0, exclusiveUpperBound - 1);
            return next;
        }
    }

    [Fact]
    public void Generate_StartsWithE()
    {
        var name = EphemeralNameGenerator.Generate();

        Assert.StartsWith("e", name);
    }

    [Fact]
    public void Generate_IsSevenCharacters()
    {
        var name = EphemeralNameGenerator.Generate();

        Assert.Equal(7, name.Length);
    }

    [Fact]
    public void Generate_ContainsOnlyAlphanumericCharacters()
    {
        for (int i = 0; i < 20; i++)
        {
            var name = EphemeralNameGenerator.Generate();
            Assert.Matches("^[A-Za-z0-9]+$", name);
        }
    }

    [Theory]
    [InlineData(new[] { 0, 1, 2, 3, 4, 5 }, "eABCDEF")]
    [InlineData(new[] { 51, 52, 53, 54, 55, 61 }, "ez01239")]
    public void Generate_UsesRandomSourceSequence(int[] indexes, string expectedName)
    {
        var randomSource = new SequenceRandomSource(indexes);

        var name = EphemeralNameGenerator.Generate(randomSource);

        Assert.Equal(expectedName, name);
    }
}
