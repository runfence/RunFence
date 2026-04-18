using PrefTrans.Services;
using Xunit;

namespace RunFence.Tests;

public class UserProfileFilterTests
{
    [Theory]
    [InlineData(@"C:\Users\john/Documents", true)]
    [InlineData(@"C:\Users\john\Documents", true)]
    [InlineData(@"C:\Users\johnsmith\Documents", false)]
    [InlineData("\"C:\\Users\\john\"", true)]
    [InlineData(@"C:\Users\john", true)]
    [InlineData(@"path=C:\Users\john/AppData/Local", true)]
    [InlineData(@"C:\Users\john something", true)]
    public void ContainsUserProfilePath_BoundaryDetection(string value, bool expected)
    {
        var filter = new UserProfileFilter();
        var paths = new[] { @"C:\Users\john" };

        Assert.Equal(expected, filter.ContainsUserProfilePath(value, paths));
    }
}
