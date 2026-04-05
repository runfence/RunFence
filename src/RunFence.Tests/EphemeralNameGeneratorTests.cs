using RunFence.Account.Lifecycle;
using Xunit;

namespace RunFence.Tests;

public class EphemeralNameGeneratorTests
{
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

    [Fact]
    public void Generate_LargeSet_AllUnique()
    {
        const int count = 1000;
        var names = new HashSet<string>();
        for (int i = 0; i < count; i++)
            names.Add(EphemeralNameGenerator.Generate());

        Assert.Equal(count, names.Count);
    }
}