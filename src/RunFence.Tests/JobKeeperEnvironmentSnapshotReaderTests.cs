using System.Runtime.InteropServices;
using System.Text;
using RunFence.JobKeeper;
using Xunit;

namespace RunFence.Tests;

public sealed class JobKeeperEnvironmentSnapshotReaderTests
{
    [Fact]
    public void ReadAll_CreatesReadsDestroysFreshEnvironmentBlock()
    {
        var nativeApi = new TestEnvironmentNativeApi(new Dictionary<string, string>
        {
            ["PATH"] = @"C:\Fresh",
            ["LOCALAPPDATA"] = @"C:\Users\Target\AppData\Local",
        });
        var reader = new JobKeeperEnvironmentSnapshotReader(nativeApi);

        var environment = reader.ReadAll();

        Assert.Equal(@"C:\Fresh", environment["PATH"]);
        Assert.Equal(@"C:\Users\Target\AppData\Local", environment["LOCALAPPDATA"]);
        Assert.True(nativeApi.OpenCurrentProcessTokenCalled);
        Assert.True(nativeApi.CreateEnvironmentBlockCalled);
        Assert.True(nativeApi.DestroyEnvironmentBlockCalled);
        Assert.True(nativeApi.TokenClosed);
    }

    private sealed class TestEnvironmentNativeApi(IReadOnlyDictionary<string, string> environment)
        : IJobKeeperEnvironmentNativeApi
    {
        private readonly IntPtr _token = new(10);
        private IntPtr _environmentBlock;

        public bool OpenCurrentProcessTokenCalled { get; private set; }
        public bool CreateEnvironmentBlockCalled { get; private set; }
        public bool DestroyEnvironmentBlockCalled { get; private set; }
        public bool TokenClosed { get; private set; }

        public void CloseHandle(IntPtr handle)
        {
            if (handle == _token)
                TokenClosed = true;
        }

        public bool OpenCurrentProcessToken(out IntPtr tokenHandle)
        {
            OpenCurrentProcessTokenCalled = true;
            tokenHandle = _token;
            return true;
        }

        public bool CreateEnvironmentBlock(out IntPtr environmentBlock, IntPtr tokenHandle)
        {
            Assert.Equal(_token, tokenHandle);
            CreateEnvironmentBlockCalled = true;
            _environmentBlock = BuildEnvironmentBlock(environment);
            environmentBlock = _environmentBlock;
            return true;
        }

        public bool DestroyEnvironmentBlock(IntPtr environmentBlock)
        {
            Assert.Equal(_environmentBlock, environmentBlock);
            DestroyEnvironmentBlockCalled = true;
            Marshal.FreeHGlobal(environmentBlock);
            _environmentBlock = IntPtr.Zero;
            return true;
        }

        private static IntPtr BuildEnvironmentBlock(IReadOnlyDictionary<string, string> variables)
        {
            var builder = new StringBuilder();
            foreach (var (key, value) in variables)
                builder.Append(key).Append('=').Append(value).Append('\0');
            builder.Append('\0');

            var bytes = Encoding.Unicode.GetBytes(builder.ToString());
            var pointer = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            return pointer;
        }
    }
}
