namespace RunFence.TokenTest;

internal class Program
{
    static void Main()
    {
        var tokenHelper = new TokenHelper();
        var winStaHelper = new WinStaHelper();
        var systemTokenHelper = new SystemTokenHelper();
        var s4uHelper = new S4UHelper(systemTokenHelper);
        var ntCreateTokenHelper = new NtCreateTokenHelper(systemTokenHelper, tokenHelper);
        var saferTokenHelper = new SaferTokenHelper(ntCreateTokenHelper);

        var runner = new ApproachRunner(tokenHelper, winStaHelper, systemTokenHelper, s4uHelper, ntCreateTokenHelper, saferTokenHelper);
        runner.RunAll();
    }
}
