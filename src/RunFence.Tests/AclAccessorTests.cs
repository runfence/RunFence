using System.Text.RegularExpressions;
using Xunit;

namespace RunFence.Tests;

public class AclAccessorTests
{
    [Fact]
    public void PathExists_FallbackHandleOpen_DoesNotRequestReadControl()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "RunFence", "Acl", "AclAccessor.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Matches(
            new Regex(@"public bool PathExists\(string path, out bool isFolder\).*?CreateFile\(path,\s*0,\s*FileSecurityNative\.FILE_SHARE_READ \| FileSecurityNative\.FILE_SHARE_WRITE,",
                RegexOptions.Singleline),
            source);
    }
}
