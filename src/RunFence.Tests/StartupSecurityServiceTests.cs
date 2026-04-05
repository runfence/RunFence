using RunFence.Core.Models;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public class StartupSecurityServiceTests
{
    // ===== ParseFindings tests =====

    [Fact]
    public void ParseFindings_ValidLines_ReturnsFindings()
    {
        var input = "StartupFolder\tC:\\ProgramData\\Startup\tS-1-5-21-123\tPC\\User1\tWriteData, AppendData\n" +
                    "RegistryRunKey\tHKLM\\...\\Run\tS-1-5-21-456\tPC\\User2\tSetValue\n";
        using var reader = new StringReader(input);

        var findings = StartupSecurityService.ParseFindings(reader);

        Assert.Equal(2, findings.Count);
        Assert.Equal(StartupSecurityCategory.StartupFolder, findings[0].Category);
        Assert.Equal(@"C:\ProgramData\Startup", findings[0].TargetDescription);
        Assert.Equal("S-1-5-21-123", findings[0].VulnerableSid);
        Assert.Equal(@"PC\User1", findings[0].VulnerablePrincipal);
        Assert.Equal("WriteData, AppendData", findings[0].AccessDescription);

        Assert.Equal(StartupSecurityCategory.RegistryRunKey, findings[1].Category);
    }

    [Fact]
    public void ParseFindings_MalformedLine_Skipped()
    {
        var input = "StartupFolder\tC:\\Path\tS-1\n" + // too few fields
                    "StartupFolder\tC:\\Path\tS-1-5-21-123\tUser\tWriteData\n";
        using var reader = new StringReader(input);

        var findings = StartupSecurityService.ParseFindings(reader);

        Assert.Single(findings);
    }

    [Fact]
    public void ParseFindings_UnknownCategory_Skipped()
    {
        var input = "UnknownCategory\tPath\tSid\tUser\tAccess\n" +
                    "StartupFolder\tC:\\Path\tS-1-5\tUser\tWriteData\n";
        using var reader = new StringReader(input);

        var findings = StartupSecurityService.ParseFindings(reader);

        Assert.Single(findings);
        Assert.Equal(StartupSecurityCategory.StartupFolder, findings[0].Category);
    }

    [Fact]
    public void ParseFindings_EmptyInput_ReturnsEmpty()
    {
        using var reader = new StringReader("");

        var findings = StartupSecurityService.ParseFindings(reader);

        Assert.Empty(findings);
    }

    [Fact]
    public void ParseFindings_ExtraTabFields_StillParses()
    {
        var input = "StartupFolder\tC:\\Path\tS-1-5\tUser\tWriteData\tExtraField\n";
        using var reader = new StringReader(input);

        var findings = StartupSecurityService.ParseFindings(reader);

        Assert.Single(findings);
        Assert.Equal("WriteData", findings[0].AccessDescription);
    }

    [Fact]
    public void ParseFindings_WithNavigationTarget_Parsed()
    {
        var input = "StartupFolder\tC:\\ProgramData\\Startup\tS-1-5-21-1\tUser\tWriteData\tC:\\ProgramData\\Startup\n";
        using var reader = new StringReader(input);

        var findings = StartupSecurityService.ParseFindings(reader);

        Assert.Single(findings);
        Assert.Equal(@"C:\ProgramData\Startup", findings[0].NavigationTarget);
    }

    [Fact]
    public void ParseFindings_WithEmptyNavigationTarget_NullNavigationTarget()
    {
        var input = "StartupFolder\tC:\\Path\tS-1-5-21-1\tUser\tWriteData\t\n";
        using var reader = new StringReader(input);

        var findings = StartupSecurityService.ParseFindings(reader);

        Assert.Single(findings);
        Assert.Null(findings[0].NavigationTarget);
    }

    [Fact]
    public void ParseFindings_FiveFields_BackwardCompat_NullNavigationTarget()
    {
        var input = "StartupFolder\tC:\\Path\tS-1-5-21-1\tUser\tWriteData\n";
        using var reader = new StringReader(input);

        var findings = StartupSecurityService.ParseFindings(reader);

        Assert.Single(findings);
        Assert.Null(findings[0].NavigationTarget);
    }

    [Fact]
    public void ParseFindings_RegistryNavigationTarget_Parsed()
    {
        var input = "RegistryRunKey\tHKLM\\...\\Run\tS-1-5-21-1\tUser\tSetValue\tHKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run\n";
        using var reader = new StringReader(input);

        var findings = StartupSecurityService.ParseFindings(reader);

        Assert.Single(findings);
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", findings[0].NavigationTarget);
    }

    [Fact]
    public void ParseFindings_BlankLines_Skipped()
    {
        var input = "\n\n  \nStartupFolder\tC:\\Path\tS-1-5\tUser\tWriteData\n\n";
        using var reader = new StringReader(input);

        var findings = StartupSecurityService.ParseFindings(reader);

        Assert.Single(findings);
    }

    [Fact]
    public void ParseFindings_AllCategories_Parsed()
    {
        var input = "StartupFolder\tp\ts\tu\ta\n" +
                    "RegistryRunKey\tp\ts\tu\ta\n" +
                    "AutorunExecutable\tp\ts\tu\ta\n" +
                    "TaskScheduler\tp\ts\tu\ta\n" +
                    "LogonScript\tp\ts\tu\ta\n" +
                    "AutoStartService\tp\ts\tu\ta\n" +
                    "DiskRootAcl\tp\ts\tu\ta\n" +
                    "AccountPolicy\tp\ts\tu\ta\n" +
                    "FirewallPolicy\tp\ts\tu\ta\n";
        using var reader = new StringReader(input);

        var findings = StartupSecurityService.ParseFindings(reader);

        Assert.Equal(9, findings.Count);
        Assert.Equal(StartupSecurityCategory.StartupFolder, findings[0].Category);
        Assert.Equal(StartupSecurityCategory.RegistryRunKey, findings[1].Category);
        Assert.Equal(StartupSecurityCategory.AutorunExecutable, findings[2].Category);
        Assert.Equal(StartupSecurityCategory.TaskScheduler, findings[3].Category);
        Assert.Equal(StartupSecurityCategory.LogonScript, findings[4].Category);
        Assert.Equal(StartupSecurityCategory.AutoStartService, findings[5].Category);
        Assert.Equal(StartupSecurityCategory.DiskRootAcl, findings[6].Category);
        Assert.Equal(StartupSecurityCategory.AccountPolicy, findings[7].Category);
        Assert.Equal(StartupSecurityCategory.FirewallPolicy, findings[8].Category);
    }

    // ===== ComputeHash tests =====

    [Fact]
    public void ComputeFindingsHash_EmptyList_ReturnsEmpty()
    {
        var hash = StartupSecurityFinding.ComputeHash([]);

        Assert.Equal("", hash);
    }

    [Fact]
    public void ComputeFindingsHash_SameFindings_DifferentOrder_SameHash()
    {
        var findings1 = new List<StartupSecurityFinding>
        {
            new(StartupSecurityCategory.StartupFolder, "C:\\Path1", "S-1-5-21-1", "User1", "WriteData"),
            new(StartupSecurityCategory.RegistryRunKey, "HKLM\\Run", "S-1-5-21-2", "User2", "SetValue")
        };
        var findings2 = new List<StartupSecurityFinding>
        {
            new(StartupSecurityCategory.RegistryRunKey, "HKLM\\Run", "S-1-5-21-2", "User2", "SetValue"),
            new(StartupSecurityCategory.StartupFolder, "C:\\Path1", "S-1-5-21-1", "User1", "WriteData")
        };

        var hash1 = StartupSecurityFinding.ComputeHash(findings1);
        var hash2 = StartupSecurityFinding.ComputeHash(findings2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeFindingsHash_DifferentFindings_DifferentHash()
    {
        var findings1 = new List<StartupSecurityFinding>
        {
            new(StartupSecurityCategory.StartupFolder, "C:\\Path1", "S-1-5-21-1", "User1", "WriteData")
        };
        var findings2 = new List<StartupSecurityFinding>
        {
            new(StartupSecurityCategory.StartupFolder, "C:\\Path2", "S-1-5-21-1", "User1", "WriteData")
        };

        var hash1 = StartupSecurityFinding.ComputeHash(findings1);
        var hash2 = StartupSecurityFinding.ComputeHash(findings2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeFindingsHash_UsesSidNotPrincipal()
    {
        // Same SID, different display name → same hash
        var findings1 = new List<StartupSecurityFinding>
        {
            new(StartupSecurityCategory.StartupFolder, "C:\\Path", "S-1-5-21-1", "DOMAIN\\User1", "WriteData")
        };
        var findings2 = new List<StartupSecurityFinding>
        {
            new(StartupSecurityCategory.StartupFolder, "C:\\Path", "S-1-5-21-1", "User1 (renamed)", "WriteData")
        };

        var hash1 = StartupSecurityFinding.ComputeHash(findings1);
        var hash2 = StartupSecurityFinding.ComputeHash(findings2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeFindingsHash_NonEmpty_ReturnsHexString()
    {
        var findings = new List<StartupSecurityFinding>
        {
            new(StartupSecurityCategory.StartupFolder, "C:\\Path", "S-1-5-21-1", "User", "WriteData")
        };

        var hash = StartupSecurityFinding.ComputeHash(findings);

        Assert.NotEmpty(hash);
        Assert.Equal(64, hash.Length); // SHA-256 = 32 bytes = 64 hex chars
        Assert.Matches("^[0-9a-f]+$", hash); // lowercase hex
    }

    // ===== ComputeKey tests =====

    [Fact]
    public void ComputeKey_SameFields_SameKey()
    {
        var f1 = new StartupSecurityFinding(StartupSecurityCategory.DiskRootAcl, "C:\\", "S-1-5-21-1", "User", "WriteData");
        var f2 = new StartupSecurityFinding(StartupSecurityCategory.DiskRootAcl, "C:\\", "S-1-5-21-1", "User (renamed)", "WriteData");

        Assert.Equal(f1.ComputeKey(), f2.ComputeKey());
    }

    [Fact]
    public void ComputeKey_DifferentTarget_DifferentKey()
    {
        var f1 = new StartupSecurityFinding(StartupSecurityCategory.DiskRootAcl, "C:\\", "S-1-5-21-1", "User", "WriteData");
        var f2 = new StartupSecurityFinding(StartupSecurityCategory.DiskRootAcl, "D:\\", "S-1-5-21-1", "User", "WriteData");

        Assert.NotEqual(f1.ComputeKey(), f2.ComputeKey());
    }

    [Fact]
    public void ComputeKey_DifferentSid_DifferentKey()
    {
        var f1 = new StartupSecurityFinding(StartupSecurityCategory.DiskRootAcl, "C:\\", "S-1-5-21-1", "User", "WriteData");
        var f2 = new StartupSecurityFinding(StartupSecurityCategory.DiskRootAcl, "C:\\", "S-1-5-21-2", "User", "WriteData");

        Assert.NotEqual(f1.ComputeKey(), f2.ComputeKey());
    }

    [Fact]
    public void ComputeKey_DifferentAccess_DifferentKey()
    {
        var f1 = new StartupSecurityFinding(StartupSecurityCategory.DiskRootAcl, "C:\\", "S-1-5-21-1", "User", "WriteData");
        var f2 = new StartupSecurityFinding(StartupSecurityCategory.DiskRootAcl, "C:\\", "S-1-5-21-1", "User", "FullControl");

        Assert.NotEqual(f1.ComputeKey(), f2.ComputeKey());
    }
}