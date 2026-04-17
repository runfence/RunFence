using System.Runtime.InteropServices;
using RunFence.TokenTest.Native;

namespace RunFence.TokenTest;

internal class ApproachRunner
{
    private readonly TokenHelper _tokenHelper;
    private readonly WinStaHelper _winStaHelper;
    private readonly SystemTokenHelper _systemTokenHelper;
    private readonly S4UHelper _s4uHelper;
    private readonly NtCreateTokenHelper _ntCreateTokenHelper;
    private readonly SaferTokenHelper _saferTokenHelper;

    private readonly List<(int approach, string failDesc)> _failures = new();
    private readonly List<int> _successes = new();
    private readonly List<int> _skipped = new();
    private int _stepNum;
    private string _lastStepDesc = "";

    public ApproachRunner(
        TokenHelper tokenHelper,
        WinStaHelper winStaHelper,
        SystemTokenHelper systemTokenHelper,
        S4UHelper s4uHelper,
        NtCreateTokenHelper ntCreateTokenHelper,
        SaferTokenHelper saferTokenHelper)
    {
        _tokenHelper = tokenHelper;
        _winStaHelper = winStaHelper;
        _systemTokenHelper = systemTokenHelper;
        _s4uHelper = s4uHelper;
        _ntCreateTokenHelper = ntCreateTokenHelper;
        _saferTokenHelper = saferTokenHelper;
    }

    public void RunAll()
    {
        EnableRequiredPrivileges();
        PrintStartupDacls();

        RunApproach21();
        RunApproach1();
        RunApproach2();
        RunApproach3();
        RunApproach4();
        RunApproach5();
        RunApproach6();
        RunApproach7();
        RunApproach8();
        RunApproach9();
        RunApproach10();
        RunApproach11();
        RunApproach12();
        RunApproach13();
        RunApproach14();
        RunApproach15();
        RunApproach16();
        RunApproach17();
        RunApproach18();

        PrintSummary();
        Console.WriteLine("\nPress Enter to exit...");
        Console.ReadLine();
    }

    private void EnableRequiredPrivileges()
    {
        Console.WriteLine("=== Enabling privileges ===");
        foreach (string priv in new[]
        {
            "SeDebugPrivilege",
            "SeImpersonatePrivilege",
            "SeIncreaseQuotaPrivilege",
            "SeAssignPrimaryTokenPrivilege",
            "SeSecurityPrivilege",
            "SeRelabelPrivilege"
        })
        {
            bool ok = _systemTokenHelper.EnablePrivilege(priv);
            Console.WriteLine($"  {priv}: {(ok ? "[OK]" : "[not available]")}");
        }
        Console.WriteLine();
    }

    private void PrintStartupDacls()
    {
        IntPtr hWinSta = WinStaNative.GetProcessWindowStation();
        _winStaHelper.PrintDacl("WinSta0", hWinSta, SecurityNative.SE_OBJECT_TYPE.SE_WINDOW_OBJECT);

        IntPtr hDesktop = WinStaNative.OpenDesktop("Default", 0, false,
            SecurityNative.READ_CONTROL | SecurityNative.GENERIC_ALL);
        if (hDesktop != IntPtr.Zero)
        {
            _winStaHelper.PrintDacl("Default Desktop", hDesktop, SecurityNative.SE_OBJECT_TYPE.SE_WINDOW_OBJECT);
            WinStaNative.CloseDesktop(hDesktop);
        }
        Console.WriteLine();
    }

    private void RunApproach1()
    {
        const int approachNum = 1;
        Console.WriteLine("[Approach 1] DISABLED — CreateRestrictedToken always produces 0xC0000142 regardless of WinSta grants or RestrictingSids.");
        Console.WriteLine("  Root cause: kernel rejects CreateRestrictedToken tokens during child-process DLL init. Use NtCreateToken (see Approach 9/14).");
        _skipped.Add(approachNum);
        return;
#pragma warning disable CS0162
        ResetSteps();

        IntPtr hToken = IntPtr.Zero, logonSid = IntPtr.Zero, userSid = IntPtr.Zero, adminsSid = IntPtr.Zero;
        IntPtr hDup = IntPtr.Zero, hRestricted = IntPtr.Zero;

        try
        {
            if (!Step("OpenProcessToken", () => { hToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("GetLogonSid", () => { logonSid = _tokenHelper.GetLogonSid(hToken); PrintSid(logonSid); return true; })) goto Fail;
            if (!Step("GetUserSid", () => { userSid = _tokenHelper.GetUserSid(hToken); return true; })) goto Fail;
            if (!Step("GetAdminsSid", () => { adminsSid = _tokenHelper.GetAdminsSid(); return true; })) goto Fail;
            if (!Step("AddLogonSidToWindowStation", () => _winStaHelper.AddSidToWindowStation(logonSid))) goto Fail;
            if (!Step("AddLogonSidToDesktop", () => _winStaHelper.AddSidToDesktop(logonSid))) goto Fail;
            if (!Step("DuplicateAsPrimary", () => { hDup = _tokenHelper.DuplicateAsPrimary(hToken); return true; })) goto Fail;
            if (!Step("CreateRestrictedToken (Admins deny + RestrictingSids)", () => { hRestricted = _tokenHelper.CreateRestrictedWithSids(hDup, adminsSid, userSid); return true; })) goto Fail;

            IntPtr mediumSid = _tokenHelper.GetMediumIntegritySid();
            if (!Step("SetIntegrityLevel = Medium", () => { _tokenHelper.SetMediumIntegrity(hRestricted, mediumSid); Marshal.FreeHGlobal(mediumSid); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hRestricted);
            if (!LaunchCmd(hRestricted)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hToken); CloseHandle(hDup); CloseHandle(hRestricted); FreeHGlobal(logonSid); FreeHGlobal(userSid); FreeHGlobal(adminsSid); }
#pragma warning restore CS0162
    }

    private void RunApproach2()
    {
        const int approachNum = 2;
        Console.WriteLine("[Approach 2] DISABLED — same root cause as Approach 1 (CreateRestrictedToken, user SID WinSta variant).");
        _skipped.Add(approachNum);
        return;
#pragma warning disable CS0162
        ResetSteps();

        IntPtr hToken = IntPtr.Zero, userSid = IntPtr.Zero, adminsSid = IntPtr.Zero;
        IntPtr hDup = IntPtr.Zero, hRestricted = IntPtr.Zero;

        try
        {
            if (!Step("OpenProcessToken", () => { hToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("GetUserSid", () => { userSid = _tokenHelper.GetUserSid(hToken); PrintSid(userSid); return true; })) goto Fail;
            if (!Step("GetAdminsSid", () => { adminsSid = _tokenHelper.GetAdminsSid(); return true; })) goto Fail;
            if (!Step("AddUserSidToWindowStation", () => _winStaHelper.AddSidToWindowStation(userSid))) goto Fail;
            if (!Step("AddUserSidToDesktop", () => _winStaHelper.AddSidToDesktop(userSid))) goto Fail;
            if (!Step("DuplicateAsPrimary", () => { hDup = _tokenHelper.DuplicateAsPrimary(hToken); return true; })) goto Fail;
            if (!Step("CreateRestrictedToken (Admins deny + RestrictingSids)", () => { hRestricted = _tokenHelper.CreateRestrictedWithSids(hDup, adminsSid, userSid); return true; })) goto Fail;

            IntPtr mediumSid = _tokenHelper.GetMediumIntegritySid();
            if (!Step("SetIntegrityLevel = Medium", () => { _tokenHelper.SetMediumIntegrity(hRestricted, mediumSid); Marshal.FreeHGlobal(mediumSid); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hRestricted);
            if (!LaunchCmd(hRestricted)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hToken); CloseHandle(hDup); CloseHandle(hRestricted); FreeHGlobal(userSid); FreeHGlobal(adminsSid); }
#pragma warning restore CS0162
    }

    private void RunApproach3()
    {
        const int approachNum = 3;
        Console.WriteLine("[Approach 3] DISABLED — same root cause as Approach 1 (CreateRestrictedToken, both SIDs WinSta variant).");
        _skipped.Add(approachNum);
        return;
#pragma warning disable CS0162
        ResetSteps();

        IntPtr hToken = IntPtr.Zero, logonSid = IntPtr.Zero, userSid = IntPtr.Zero, adminsSid = IntPtr.Zero;
        IntPtr hDup = IntPtr.Zero, hRestricted = IntPtr.Zero;

        try
        {
            if (!Step("OpenProcessToken", () => { hToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("GetLogonSid", () => { logonSid = _tokenHelper.GetLogonSid(hToken); PrintSid(logonSid); return true; })) goto Fail;
            if (!Step("GetUserSid", () => { userSid = _tokenHelper.GetUserSid(hToken); PrintSid(userSid); return true; })) goto Fail;
            if (!Step("GetAdminsSid", () => { adminsSid = _tokenHelper.GetAdminsSid(); return true; })) goto Fail;
            if (!Step("AddLogonSidToWindowStation", () => _winStaHelper.AddSidToWindowStation(logonSid))) goto Fail;
            if (!Step("AddLogonSidToDesktop", () => _winStaHelper.AddSidToDesktop(logonSid))) goto Fail;
            if (!Step("AddUserSidToWindowStation", () => _winStaHelper.AddSidToWindowStation(userSid))) goto Fail;
            if (!Step("AddUserSidToDesktop", () => _winStaHelper.AddSidToDesktop(userSid))) goto Fail;
            if (!Step("DuplicateAsPrimary", () => { hDup = _tokenHelper.DuplicateAsPrimary(hToken); return true; })) goto Fail;
            if (!Step("CreateRestrictedToken (Admins deny + RestrictingSids)", () => { hRestricted = _tokenHelper.CreateRestrictedWithSids(hDup, adminsSid, userSid); return true; })) goto Fail;

            IntPtr mediumSid = _tokenHelper.GetMediumIntegritySid();
            if (!Step("SetIntegrityLevel = Medium", () => { _tokenHelper.SetMediumIntegrity(hRestricted, mediumSid); Marshal.FreeHGlobal(mediumSid); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hRestricted);
            if (!LaunchCmd(hRestricted)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hToken); CloseHandle(hDup); CloseHandle(hRestricted); FreeHGlobal(logonSid); FreeHGlobal(userSid); FreeHGlobal(adminsSid); }
#pragma warning restore CS0162
    }

    private void RunApproach4()
    {
        const int approachNum = 4;
        Console.WriteLine("[Approach 4] S4U from SYSTEM (Network logon type)");
        Console.WriteLine("  Note: S4U creates NEW logon session — DPAPI/EFS will NOT work in launched process");
        ResetSteps();

        IntPtr hToken = IntPtr.Zero;
        try
        {
            if (!Step("GetS4UToken(Network)", () => { hToken = _s4uHelper.GetS4UToken(Environment.UserName); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hToken);
            if (!LaunchCmd(hToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hToken); }

        WaitForEnter();
    }

    private void RunApproach5()
    {
        const int approachNum = 5;
        Console.WriteLine("[Approach 5] NtCreateToken with same logon session LUID");
        Console.WriteLine("  Note: Same logon session LUID reused → DPAPI/EFS SHOULD work");
        ResetSteps();

        IntPtr hCurrentToken = IntPtr.Zero, hCustomToken = IntPtr.Zero;
        try
        {
            if (!Step("OpenCurrentProcessToken", () => { hCurrentToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("NtCreateToken (custom stripped token)", () => { hCustomToken = _ntCreateTokenHelper.GetCustomToken(hCurrentToken); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hCustomToken);
            if (!LaunchCmd(hCustomToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hCurrentToken); CloseHandle(hCustomToken); }

        WaitForEnter();
    }

    private void RunApproach6()
    {
        const int approachNum = 6;
        Console.WriteLine("[Approach 6] DISABLED — same root cause as Approach 1 (CreateRestrictedToken, High IL variant).");
        _skipped.Add(approachNum);
        return;
#pragma warning disable CS0162
        Console.WriteLine("  Note: Tests if High vs Medium IL matters once RestrictingSids are present");
        ResetSteps();

        IntPtr hToken = IntPtr.Zero, userSid = IntPtr.Zero, adminsSid = IntPtr.Zero;
        IntPtr hDup = IntPtr.Zero, hRestricted = IntPtr.Zero;

        try
        {
            if (!Step("OpenProcessToken", () => { hToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("GetUserSid", () => { userSid = _tokenHelper.GetUserSid(hToken); return true; })) goto Fail;
            if (!Step("GetAdminsSid", () => { adminsSid = _tokenHelper.GetAdminsSid(); return true; })) goto Fail;
            if (!Step("DuplicateAsPrimary", () => { hDup = _tokenHelper.DuplicateAsPrimary(hToken); return true; })) goto Fail;
            if (!Step("CreateRestrictedToken (Admins deny + RestrictingSids, no IL change)", () => { hRestricted = _tokenHelper.CreateRestrictedWithSids(hDup, adminsSid, userSid); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hRestricted);
            if (!LaunchCmd(hRestricted)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hToken); CloseHandle(hDup); CloseHandle(hRestricted); FreeHGlobal(userSid); FreeHGlobal(adminsSid); }
#pragma warning restore CS0162
    }

    private void RunApproach7()
    {
        const int approachNum = 7;
        Console.WriteLine("[Approach 7] DISABLED — same root cause as Approach 1 (CreateRestrictedToken, quick-exit variant).");
        _skipped.Add(approachNum);
        return;
#pragma warning disable CS0162
        Console.WriteLine("  Note: Same as Approach 1 but launches cmd /C exit 0 — tests quick-exit process with fixed token");
        ResetSteps();

        IntPtr hToken = IntPtr.Zero, logonSid = IntPtr.Zero, userSid = IntPtr.Zero, adminsSid = IntPtr.Zero;
        IntPtr hDup = IntPtr.Zero, hRestricted = IntPtr.Zero;

        try
        {
            if (!Step("OpenProcessToken", () => { hToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("GetLogonSid", () => { logonSid = _tokenHelper.GetLogonSid(hToken); PrintSid(logonSid); return true; })) goto Fail;
            if (!Step("GetUserSid", () => { userSid = _tokenHelper.GetUserSid(hToken); return true; })) goto Fail;
            if (!Step("GetAdminsSid", () => { adminsSid = _tokenHelper.GetAdminsSid(); return true; })) goto Fail;
            if (!Step("AddLogonSidToWindowStation", () => _winStaHelper.AddSidToWindowStation(logonSid))) goto Fail;
            if (!Step("AddLogonSidToDesktop", () => _winStaHelper.AddSidToDesktop(logonSid))) goto Fail;
            if (!Step("DuplicateAsPrimary", () => { hDup = _tokenHelper.DuplicateAsPrimary(hToken); return true; })) goto Fail;
            if (!Step("CreateRestrictedToken (Admins deny + RestrictingSids)", () => { hRestricted = _tokenHelper.CreateRestrictedWithSids(hDup, adminsSid, userSid); return true; })) goto Fail;

            IntPtr mediumSid = _tokenHelper.GetMediumIntegritySid();
            if (!Step("SetIntegrityLevel = Medium", () => { _tokenHelper.SetMediumIntegrity(hRestricted, mediumSid); Marshal.FreeHGlobal(mediumSid); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hRestricted);
            if (!LaunchCmdDetached(hRestricted)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hToken); CloseHandle(hDup); CloseHandle(hRestricted); FreeHGlobal(logonSid); FreeHGlobal(userSid); FreeHGlobal(adminsSid); }
#pragma warning restore CS0162
    }

    private void RunApproach8()
    {
        const int approachNum = 8;
        Console.WriteLine("[Approach 8] DISABLED — same root cause as Approach 1 (CreateRestrictedToken, WinSta label lowering variant).");
        Console.WriteLine("  Note: WinSta/Desktop labels were already Medium (S-1-16-4096), so this was irrelevant anyway.");
        _skipped.Add(approachNum);
        return;
#pragma warning disable CS0162
        Console.WriteLine("  Note: Tests if High mandatory label on WinSta0/Desktop blocks medium-IL process write during DLL init");
        ResetSteps();

        IntPtr hToken = IntPtr.Zero, logonSid = IntPtr.Zero, userSid = IntPtr.Zero, adminsSid = IntPtr.Zero;
        IntPtr hDup = IntPtr.Zero, hRestricted = IntPtr.Zero;
        IntPtr hWinStaOwn = IntPtr.Zero, hDesktopOwn = IntPtr.Zero;

        uint winStaAccess = SecurityNative.READ_CONTROL | SecurityNative.WRITE_OWNER | SecurityNative.WRITE_DAC;

        try
        {
            if (!Step("OpenWindowStation(WinSta0) with WRITE_OWNER", () =>
            {
                hWinStaOwn = WinStaNative.OpenWindowStation("WinSta0", false, winStaAccess);
                return hWinStaOwn != IntPtr.Zero;
            })) goto Fail;

            if (!Step("ReadAndLowerMandatoryLabel(WinSta0)",
                () => _winStaHelper.ReadAndLowerMandatoryLabel(hWinStaOwn, SecurityNative.SE_OBJECT_TYPE.SE_WINDOW_OBJECT, "WinSta0"))) goto Fail;

            if (!Step("OpenDesktop(Default) with WRITE_OWNER", () =>
            {
                hDesktopOwn = WinStaNative.OpenDesktop("Default", 0, false, winStaAccess);
                return hDesktopOwn != IntPtr.Zero;
            })) goto Fail;

            if (!Step("ReadAndLowerMandatoryLabel(Desktop)",
                () => _winStaHelper.ReadAndLowerMandatoryLabel(hDesktopOwn, SecurityNative.SE_OBJECT_TYPE.SE_WINDOW_OBJECT, "Default Desktop"))) goto Fail;

            if (!Step("OpenProcessToken", () => { hToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("GetLogonSid", () => { logonSid = _tokenHelper.GetLogonSid(hToken); PrintSid(logonSid); return true; })) goto Fail;
            if (!Step("GetUserSid", () => { userSid = _tokenHelper.GetUserSid(hToken); return true; })) goto Fail;
            if (!Step("GetAdminsSid", () => { adminsSid = _tokenHelper.GetAdminsSid(); return true; })) goto Fail;
            if (!Step("AddLogonSidToWindowStation", () => _winStaHelper.AddSidToWindowStation(logonSid))) goto Fail;
            if (!Step("AddLogonSidToDesktop", () => _winStaHelper.AddSidToDesktop(logonSid))) goto Fail;
            if (!Step("DuplicateAsPrimary", () => { hDup = _tokenHelper.DuplicateAsPrimary(hToken); return true; })) goto Fail;
            if (!Step("CreateRestrictedToken (Admins deny + RestrictingSids)", () => { hRestricted = _tokenHelper.CreateRestrictedWithSids(hDup, adminsSid, userSid); return true; })) goto Fail;

            IntPtr mediumSid = _tokenHelper.GetMediumIntegritySid();
            if (!Step("SetIntegrityLevel = Medium", () => { _tokenHelper.SetMediumIntegrity(hRestricted, mediumSid); Marshal.FreeHGlobal(mediumSid); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hRestricted);
            if (!LaunchCmd(hRestricted)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally
        {
            CloseHandle(hToken); CloseHandle(hDup); CloseHandle(hRestricted);
            FreeHGlobal(logonSid); FreeHGlobal(userSid); FreeHGlobal(adminsSid);
            if (hWinStaOwn != IntPtr.Zero) WinStaNative.CloseWindowStation(hWinStaOwn);
            if (hDesktopOwn != IntPtr.Zero) WinStaNative.CloseDesktop(hDesktopOwn);
        }
#pragma warning restore CS0162
    }

    private void RunApproach9()
    {
        const int approachNum = 9;
        Console.WriteLine("[Approach 9] NtCreateToken — deny-only Admins + standard-user privileges");
        Console.WriteLine("  Note: Tests if deny-only Admins (still in token) vs completely omitted (Approach 10/14) matters when stdPrivs are correct.");
        ResetSteps();

        IntPtr hCurrentToken = IntPtr.Zero, hCustomToken = IntPtr.Zero;
        try
        {
            if (!Step("OpenCurrentProcessToken", () => { hCurrentToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("NtCreateToken (deny-only Admins, stdPrivs)", () => { hCustomToken = _ntCreateTokenHelper.GetCustomToken(hCurrentToken, standardUserPrivileges: true); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hCustomToken);
            if (!LaunchCmd(hCustomToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hCurrentToken); CloseHandle(hCustomToken); }

        WaitForEnter();
    }

    private void RunApproach10()
    {
        const int approachNum = 10;
        Console.WriteLine("[Approach 10] NtCreateToken — omit Admins + standard-user privileges (direct, no SaferComputeTokenFromLevel)");
        Console.WriteLine("  Note: Confirms that SaferComputeTokenFromLevel in Approach 14 is not essential — NtCreateToken from hCurrentToken directly works.");
        ResetSteps();

        IntPtr hCurrentToken = IntPtr.Zero, hCustomToken = IntPtr.Zero;
        try
        {
            if (!Step("OpenCurrentProcessToken", () => { hCurrentToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("NtCreateToken (omit Admins, stdPrivs)", () => { hCustomToken = _ntCreateTokenHelper.GetCustomToken(hCurrentToken, omitAdmins: true, standardUserPrivileges: true); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hCustomToken);
            if (!LaunchCmd(hCustomToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hCurrentToken); CloseHandle(hCustomToken); }

        WaitForEnter();
    }

    private void RunApproach11()
    {
        const int approachNum = 11;
        Console.WriteLine("[Approach 11] S4U with Batch logon type");
        Console.WriteLine("  Note: S4U Batch — new logon session (no DPAPI/EFS). Tests if SeBatchLogonRight allows this.");
        ResetSteps();

        IntPtr hToken = IntPtr.Zero;
        try
        {
            if (!Step("GetS4UToken(Batch)", () => { hToken = _s4uHelper.GetS4UToken(Environment.UserName, TokenNative.SECURITY_LOGON_TYPE.Batch); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hToken);
            if (!LaunchCmd(hToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hToken); }

        WaitForEnter();
    }

    private void RunApproach12()
    {
        const int approachNum = 12;
        Console.WriteLine("[Approach 12] S4U with Service logon type");
        Console.WriteLine("  Note: S4U Service — new logon session (no DPAPI/EFS). Tests if SeServiceLogonRight allows this.");
        ResetSteps();

        IntPtr hToken = IntPtr.Zero;
        try
        {
            if (!Step("GetS4UToken(Service)", () => { hToken = _s4uHelper.GetS4UToken(Environment.UserName, TokenNative.SECURITY_LOGON_TYPE.Service); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hToken);
            if (!LaunchCmd(hToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hToken); }

        WaitForEnter();
    }

    private void RunApproach13()
    {
        const int approachNum = 13;
        Console.WriteLine("[Approach 13] Find same-user medium-integrity process, steal token");
        Console.WriteLine("  Note: Steals a genuine medium-IL token for this user from an existing process — no token manipulation.");
        ResetSteps();

        IntPtr hCurrentToken = IntPtr.Zero, hMediumToken = IntPtr.Zero;
        try
        {
            if (!Step("OpenCurrentProcessToken", () => { hCurrentToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;

            _stepNum++;
            _lastStepDesc = "FindSameUserMediumToken";
            Console.Write($"  Step {_stepNum}: {_lastStepDesc}... ");
            try { hMediumToken = _tokenHelper.FindSameUserMediumToken(hCurrentToken); }
            catch (Exception ex) { Console.WriteLine($"[FAIL] exception={ex.Message}"); goto Fail; }

            if (hMediumToken == IntPtr.Zero)
            {
                Console.WriteLine("[SKIPPED] No same-user medium-integrity process found");
                _skipped.Add(approachNum);
                goto Done;
            }
            Console.WriteLine("[OK]");

            _tokenHelper.PrintTokenInfo(hMediumToken);
            if (!LaunchCmd(hMediumToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hCurrentToken); CloseHandle(hMediumToken); }

        WaitForEnter();
    }

    private void RunApproach14()
    {
        const int approachNum = 14;
        Console.WriteLine("[Approach 14] SaferComputeTokenFromLevel(SAFER_LEVELID_NORMALUSER)");
        Console.WriteLine("  Note: Uses documented Safer API — different restriction mechanism than CreateRestrictedToken.");
        ResetSteps();

        IntPtr hCurrentToken = IntPtr.Zero, hSaferToken = IntPtr.Zero;
        try
        {
            if (!Step("OpenCurrentProcessToken", () => { hCurrentToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("SaferComputeTokenFromLevel(NormalUser)", () => { hSaferToken = _saferTokenHelper.GetSaferToken(hCurrentToken); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hSaferToken);
            if (!LaunchCmd(hSaferToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hCurrentToken); CloseHandle(hSaferToken); }

        WaitForEnter();
    }

    private void RunApproach15()
    {
        const int approachNum = 15;
        Console.WriteLine("[Approach 15] NtCreateToken (omit Admins, stdPrivs) + CreateProcessWithTokenW from SYSTEM-impersonated thread");
        Console.WriteLine("  Note: Token created first (before impersonation), then SYSTEM context during CreateProcessWithTokenW.");
        Console.WriteLine("  Tests if SYSTEM impersonation during process creation affects outcome vs Approach 10/14 (no impersonation).");
        ResetSteps();

        IntPtr hToken = IntPtr.Zero, hCustomToken = IntPtr.Zero;
        IntPtr hSystemToken = IntPtr.Zero;
        bool impersonating = false;

        try
        {
            if (!Step("OpenProcessToken", () => { hToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("NtCreateToken (omit Admins, stdPrivs)", () => { hCustomToken = _ntCreateTokenHelper.GetCustomToken(hToken, omitAdmins: true, standardUserPrivileges: true); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hCustomToken);

            if (!Step("GetSystemToken", () =>
            {
                hSystemToken = _systemTokenHelper.GetSystemToken();
                return hSystemToken != IntPtr.Zero;
            })) goto Fail;

            if (!Step("ImpersonateToken(SYSTEM)", () =>
            {
                bool ok = _systemTokenHelper.ImpersonateToken(hSystemToken);
                if (ok) impersonating = true;
                return ok;
            })) goto Fail;

            if (!LaunchCmd(hCustomToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally
        {
            if (impersonating) _systemTokenHelper.RevertImpersonation();
            CloseHandle(hToken); CloseHandle(hCustomToken); CloseHandle(hSystemToken);
        }

        WaitForEnter();
    }

    private void RunApproach16()
    {
        const int approachNum = 16;
        Console.WriteLine("[Approach 16] SaferBase LUID + NtCreateToken — deny-only Admins + stdPrivs");
        Console.WriteLine("  Note: Approach 14 = SaferBase → NtCreateToken(omitAdmins=true, stdPrivs). This uses omitAdmins=false (deny-only).");
        Console.WriteLine("  Approach 9 = elevated LUID + deny-only + stdPrivs → FAILED. Tests if deny-only works when LUID is SaferBase's.");
        ResetSteps();

        IntPtr hCurrentToken = IntPtr.Zero, hSaferToken = IntPtr.Zero;
        try
        {
            if (!Step("OpenCurrentProcessToken", () => { hCurrentToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("SaferBase → NtCreateToken (deny-only Admins, stdPrivs)", () => { hSaferToken = _saferTokenHelper.GetSaferToken(hCurrentToken, omitAdmins: false); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hSaferToken);
            if (!LaunchCmd(hSaferToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hCurrentToken); CloseHandle(hSaferToken); }

        WaitForEnter();
    }

    private void RunApproach18()
    {
        const int approachNum = 18;
        Console.WriteLine("[Approach 18] Raw SaferComputeTokenFromLevel output — no NtCreateToken rebuild (moved from Approach 16)");
        Console.WriteLine("  Note: Proved that SaferBase LUID is the key differentiator. Kept as reference — lacks correct privilege set.");
        Console.WriteLine("  Production solution is Approach 14 (SaferBase → NtCreateToken + stdPrivs).");
        ResetSteps();

        IntPtr hCurrentToken = IntPtr.Zero, hSaferToken = IntPtr.Zero;
        try
        {
            if (!Step("OpenCurrentProcessToken", () => { hCurrentToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("SaferComputeTokenFromLevel(NormalUser) — raw, no NtCreateToken", () => { hSaferToken = _saferTokenHelper.GetRawSaferToken(hCurrentToken); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hSaferToken);
            if (!LaunchCmd(hSaferToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hCurrentToken); CloseHandle(hSaferToken); }

        WaitForEnter();
    }

    private void RunApproach17()
    {
        const int approachNum = 17;
        Console.WriteLine("[Approach 17] NtCreateToken (omit Admins) — 2 privileges: SeChangeNotify + SeIncreaseWorkingSet");
        Console.WriteLine("  Note: 1-priv (Approach 5) fails, 6-priv (Approach 10/14) works.");
        Console.WriteLine("  Tests whether any second privilege suffices or SeIncreaseWorkingSetPrivilege specifically is required.");
        ResetSteps();

        IntPtr hCurrentToken = IntPtr.Zero, hCustomToken = IntPtr.Zero;
        try
        {
            if (!Step("OpenCurrentProcessToken", () => { hCurrentToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("NtCreateToken (omit Admins, 2 privs)", () => { hCustomToken = _ntCreateTokenHelper.GetCustomToken(hCurrentToken, omitAdmins: true, twoPrivileges: true); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hCustomToken);
            if (!LaunchCmd(hCustomToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hCurrentToken); CloseHandle(hCustomToken); }

        WaitForEnter();
    }

    private void RunApproach21()
    {
        const int approachNum = 21;
        Console.WriteLine("[Approach 21] Raw SaferComputeTokenFromLevel + CreateRestrictedToken(S-1-5-114 deny-only) + Medium IL + LOGON_WITH_PROFILE");
        ResetSteps();

        IntPtr hCurrentToken = IntPtr.Zero, hSaferToken = IntPtr.Zero, hFinalToken = IntPtr.Zero;
        IntPtr pSid114 = IntPtr.Zero;
        try
        {
            if (!Step("OpenCurrentProcessToken", () => { hCurrentToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("SaferComputeTokenFromLevel(NormalUser) — raw", () => { hSaferToken = _saferTokenHelper.GetRawSaferToken(hCurrentToken); return true; })) goto Fail;

            if (!Step("CreateRestrictedToken(S-1-5-114 → deny-only)", () =>
            {
                SecurityNative.ConvertStringSidToSid("S-1-5-114", out pSid114);
                if (pSid114 == IntPtr.Zero) return false;

                var sidToDisable = new TokenNative.SID_AND_ATTRIBUTES { Sid = pSid114, Attributes = 0 };
                return TokenNative.CreateRestrictedToken(hSaferToken, 0,
                    1, [sidToDisable],
                    0, IntPtr.Zero,
                    0, null,
                    out hFinalToken);
            })) goto Fail;

            if (!Step("SetTokenInformation(TokenIntegrityLevel) — Medium IL", () =>
            {
                return _tokenHelper.SetMediumIntegrityLevel(hFinalToken);
            })) goto Fail;

            _tokenHelper.PrintTokenInfo(hFinalToken);
            if (!LaunchCmdWithProfile(hFinalToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hCurrentToken); CloseHandle(hSaferToken); CloseHandle(hFinalToken); if (pSid114 != IntPtr.Zero) SecurityNative.LocalFree(pSid114); }

        WaitForEnter();
    }

    private bool TryAddStandardUserPrivileges(IntPtr hToken)
    {
        (string name, bool enabled)[] standardPrivs =
        [
            ("SeLockMemoryPrivilege", false),
            ("SeShutdownPrivilege", false),
            ("SeChangeNotifyPrivilege", true),
            ("SeUndockPrivilege", false),
            ("SeIncreaseWorkingSetPrivilege", false),
            ("SeTimeZonePrivilege", false),
        ];

        var privs = new List<(TokenNative.LUID luid, uint attrs)>();
        foreach (var (name, enabled) in standardPrivs)
        {
            if (TokenNative.LookupPrivilegeValue(null, name, out var luid))
                privs.Add((luid, enabled ? TokenNative.SE_PRIVILEGE_ENABLED : 0u));
        }

        const int luidAttrSize = 12;
        int size = 4 + privs.Count * luidAttrSize;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(buf, privs.Count);
            int offset = 4;
            foreach (var (luid, attrs) in privs)
            {
                Marshal.WriteInt32(buf, offset, (int)luid.LowPart);
                Marshal.WriteInt32(buf, offset + 4, luid.HighPart);
                Marshal.WriteInt32(buf, offset + 8, (int)attrs);
                offset += luidAttrSize;
            }

            int ntStatus = TokenNative.NtSetInformationToken(hToken,
                TokenNative.TOKEN_INFORMATION_CLASS.TokenPrivileges, buf, (uint)size);

            if (ntStatus != 0)
            {
                Console.Write($"NTSTATUS=0x{ntStatus:X8} ");
                return false;
            }
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private void RunApproach19()
    {
        const int approachNum = 19;
        Console.WriteLine("[Approach 19] Same as #14 (SaferComputeTokenFromLevel + NtCreateToken) but with LOGON_WITH_PROFILE");
        ResetSteps();

        IntPtr hCurrentToken = IntPtr.Zero, hSaferToken = IntPtr.Zero;
        try
        {
            if (!Step("OpenCurrentProcessToken", () => { hCurrentToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;
            if (!Step("SaferComputeTokenFromLevel(NormalUser)", () => { hSaferToken = _saferTokenHelper.GetSaferToken(hCurrentToken); return true; })) goto Fail;
            _tokenHelper.PrintTokenInfo(hSaferToken);
            if (!LaunchCmdWithProfile(hSaferToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hCurrentToken); CloseHandle(hSaferToken); }

        WaitForEnter();
    }

    private void RunApproach20()
    {
        const int approachNum = 20;
        Console.WriteLine("[Approach 20] Same as #19 but NtCreateToken uses ORIGINAL token's authId (not Safer's)");
        Console.WriteLine("  Tests whether using the real logon session LUID fixes app activation / MSIX access.");
        ResetSteps();

        IntPtr hCurrentToken = IntPtr.Zero, hSaferToken = IntPtr.Zero;
        try
        {
            if (!Step("OpenCurrentProcessToken", () => { hCurrentToken = _tokenHelper.OpenCurrentProcessToken(); return true; })) goto Fail;

            // Get the original token's authId BEFORE Safer
            var originalAuthId = _tokenHelper.GetTokenStatistics(hCurrentToken).AuthenticationId;
            Console.WriteLine($"  [diag] Original authId: 0x{originalAuthId.HighPart:X8}:{originalAuthId.LowPart:X8}");

            if (!Step("SaferComputeTokenFromLevel(NormalUser) + NtCreateToken(originalAuthId)", () =>
            {
                hSaferToken = _saferTokenHelper.GetSaferTokenWithAuthId(hCurrentToken, originalAuthId);
                return true;
            })) goto Fail;

            _tokenHelper.PrintTokenInfo(hSaferToken);
            if (!LaunchCmdWithProfile(hSaferToken)) goto Fail;

            _successes.Add(approachNum);
            Console.WriteLine("→ SUCCESS.");
            goto Done;
            Fail: _failures.Add((approachNum, FailDesc()));
            Console.WriteLine("→ FAILED. Continuing to next approach.");
            Done:;
        }
        catch (Exception ex) { HandleException(approachNum, ex); }
        finally { CloseHandle(hCurrentToken); CloseHandle(hSaferToken); }

        WaitForEnter();
    }

    private bool LaunchCmdWithProfile(IntPtr hToken)
    {
        _stepNum++;
        _lastStepDesc = "CreateProcessWithTokenW(cmd.exe, LOGON_WITH_PROFILE)";
        Console.Write($"  Step {_stepNum}: {_lastStepDesc}... ");

        string cmdPath = Environment.ExpandEnvironmentVariables(@"%ComSpec%");
        string cmdLine = $"\"{cmdPath}\" /K echo Launched with LOGON_WITH_PROFILE";

        var si = new TokenNative.STARTUPINFO { cb = Marshal.SizeOf<TokenNative.STARTUPINFO>() };
        si.lpDesktop = "WinSta0\\Default";

        bool ok = TokenNative.CreateProcessWithTokenW(
            hToken, TokenNative.LOGON_WITH_PROFILE, null, cmdLine,
            ProcessNative.CREATE_NEW_CONSOLE,
            IntPtr.Zero, null,
            ref si, out var pi);

        if (!ok)
        {
            Console.WriteLine($"[FAIL] error={TokenHelper.GetLastError()}");
            return false;
        }

        ProcessNative.WaitForSingleObject(pi.hProcess, 3000);
        ProcessNative.GetExitCodeProcess(pi.hProcess, out uint exitCode);

        const uint STILL_ACTIVE = 0x103;
        if (exitCode == STILL_ACTIVE)
            Console.WriteLine($"[OK] PID={pi.dwProcessId} (running)");
        else
            Console.WriteLine($"[FAIL] PID={pi.dwProcessId} exited 0x{exitCode:X8}");

        ProcessNative.CloseHandle(pi.hProcess);
        ProcessNative.CloseHandle(pi.hThread);
        return exitCode == STILL_ACTIVE;
    }

    private bool Step(string description, Func<bool> action)
    {
        _stepNum++;
        _lastStepDesc = description;
        Console.Write($"  Step {_stepNum}: {description}... ");
        try
        {
            bool result = action();
            Console.WriteLine(result ? "[OK]" : $"[FAIL] error={TokenHelper.GetLastError()}");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] exception={ex.Message}");
            return false;
        }
    }

    private bool LaunchCmd(IntPtr hToken)
    {
        _stepNum++;
        _lastStepDesc = "CreateProcessWithTokenW(cmd.exe)";
        Console.Write($"  Step {_stepNum}: {_lastStepDesc}... ");

        string cmdPath = Environment.ExpandEnvironmentVariables(@"%ComSpec%");
        string cmdLine = $"\"{cmdPath}\" /K echo Launched as non-elevated";

        var si = new TokenNative.STARTUPINFO { cb = Marshal.SizeOf<TokenNative.STARTUPINFO>() };
        si.lpDesktop = "WinSta0\\Default";

        bool ok = TokenNative.CreateProcessWithTokenW(
            hToken, 0, null, cmdLine,
            ProcessNative.CREATE_NEW_CONSOLE,
            IntPtr.Zero, null,
            ref si, out var pi);

        if (!ok)
        {
            Console.WriteLine($"[FAIL] error={TokenHelper.GetLastError()}");
            return false;
        }

        ProcessNative.WaitForSingleObject(pi.hProcess, 3000);
        ProcessNative.GetExitCodeProcess(pi.hProcess, out uint exitCode);

        const uint STILL_ACTIVE = 0x103;
        if (exitCode == STILL_ACTIVE)
            Console.WriteLine($"[OK] PID={pi.dwProcessId} (running)");
        else
            Console.WriteLine($"[FAIL] PID={pi.dwProcessId} exited 0x{exitCode:X8}");

        ProcessNative.CloseHandle(pi.hProcess);
        ProcessNative.CloseHandle(pi.hThread);
        return exitCode == STILL_ACTIVE;
    }

    private bool LaunchCmdDetached(IntPtr hToken)
    {
        _stepNum++;
        _lastStepDesc = "CreateProcessWithTokenW(cmd.exe /C exit 0, CREATE_NEW_CONSOLE)";
        Console.Write($"  Step {_stepNum}: {_lastStepDesc}... ");

        string cmdPath = Environment.ExpandEnvironmentVariables(@"%ComSpec%");
        string cmdLine = $"\"{cmdPath}\" /C exit 0";

        var si = new TokenNative.STARTUPINFO { cb = Marshal.SizeOf<TokenNative.STARTUPINFO>() };
        si.lpDesktop = "WinSta0\\Default";

        bool ok = TokenNative.CreateProcessWithTokenW(
            hToken, 0, null, cmdLine,
            ProcessNative.CREATE_NEW_CONSOLE,
            IntPtr.Zero, null,
            ref si, out var pi);

        if (!ok)
        {
            Console.WriteLine($"[FAIL] error={TokenHelper.GetLastError()}");
            return false;
        }

        ProcessNative.WaitForSingleObject(pi.hProcess, 5000);
        ProcessNative.GetExitCodeProcess(pi.hProcess, out uint exitCode);

        if (exitCode == 0)
            Console.WriteLine($"[OK] PID={pi.dwProcessId} exited 0");
        else
            Console.WriteLine($"[FAIL] PID={pi.dwProcessId} exited 0x{exitCode:X8}");

        ProcessNative.CloseHandle(pi.hProcess);
        ProcessNative.CloseHandle(pi.hThread);
        return exitCode == 0;
    }

    private bool LaunchCmdAsUser(IntPtr hToken)
    {
        _stepNum++;
        _lastStepDesc = "CreateProcessAsUser(cmd.exe)";
        Console.Write($"  Step {_stepNum}: {_lastStepDesc}... ");

        string cmdPath = Environment.ExpandEnvironmentVariables(@"%ComSpec%");
        string cmdLine = $"\"{cmdPath}\" /K echo Launched as non-elevated";

        var si = new TokenNative.STARTUPINFO { cb = Marshal.SizeOf<TokenNative.STARTUPINFO>() };
        si.lpDesktop = "WinSta0\\Default";

        bool ok = TokenNative.CreateProcessAsUser(
            hToken, null, cmdLine,
            IntPtr.Zero, IntPtr.Zero, false,
            ProcessNative.CREATE_NEW_CONSOLE,
            IntPtr.Zero, null,
            ref si, out var pi);

        if (!ok)
        {
            Console.WriteLine($"[FAIL] error={TokenHelper.GetLastError()}");
            return false;
        }

        ProcessNative.WaitForSingleObject(pi.hProcess, 3000);
        ProcessNative.GetExitCodeProcess(pi.hProcess, out uint exitCode);

        const uint STILL_ACTIVE = 0x103;
        if (exitCode == STILL_ACTIVE)
            Console.WriteLine($"[OK] PID={pi.dwProcessId} (running)");
        else
            Console.WriteLine($"[FAIL] PID={pi.dwProcessId} exited 0x{exitCode:X8}");

        ProcessNative.CloseHandle(pi.hProcess);
        ProcessNative.CloseHandle(pi.hThread);
        return exitCode == STILL_ACTIVE;
    }

    private void ResetSteps() => _stepNum = 0;

    private string FailDesc() => $"step {_stepNum} ({_lastStepDesc})";

    private void HandleException(int approachNum, Exception ex)
    {
        Console.WriteLine($"  EXCEPTION: {ex.Message}");
        _failures.Add((approachNum, $"step {_stepNum} ({ex.Message})"));
    }

    private static void PrintSid(IntPtr sid)
    {
        SecurityNative.ConvertSidToStringSid(sid, out var s);
        Console.Write($" = {s ?? "?"}");
    }

    private static void WaitForEnter()
    {
        Console.WriteLine("\nPress Enter to continue to next approach...");
        Console.ReadLine();
    }

    private static void CloseHandle(IntPtr h) { if (h != IntPtr.Zero) ProcessNative.CloseHandle(h); }
    private static void FreeHGlobal(IntPtr p) { if (p != IntPtr.Zero) Marshal.FreeHGlobal(p); }

    private void PrintSummary()
    {
        Console.WriteLine("\n=== Summary ===");
        for (int i = 1; i <= 18; i++)
        {
            if (_successes.Contains(i))
                Console.WriteLine($"Approach {i,2}: SUCCESS");
            else if (_skipped.Contains(i))
                Console.WriteLine($"Approach {i,2}: SKIPPED");
            else
            {
                var f = _failures.FirstOrDefault(x => x.approach == i);
                Console.WriteLine(f != default
                    ? $"Approach {i,2}: FAILED at {f.failDesc}"
                    : $"Approach {i,2}: NOT RUN");
            }
        }
    }
}
