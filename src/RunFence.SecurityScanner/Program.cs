namespace RunFence.SecurityScanner;

public static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var scanner = new SecurityScanner();
            var findings = scanner.RunChecks();
            foreach (var f in findings)
            {
                Console.WriteLine(string.Join('\t',
                    f.Category,
                    Sanitize(f.TargetDescription),
                    Sanitize(f.VulnerableSid),
                    Sanitize(f.VulnerablePrincipal),
                    Sanitize(f.AccessDescription),
                    Sanitize(f.NavigationTarget ?? "")));
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Security scan failed: {ex.Message}");
            return 1;
        }
    }

    private static string Sanitize(string value) =>
        value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
}