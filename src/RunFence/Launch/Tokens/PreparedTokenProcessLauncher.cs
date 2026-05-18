using System.ComponentModel;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launching.Environment;
using RunFence.Launching.Resolution;

namespace RunFence.Launch.Tokens;

public class PreparedTokenProcessLauncher(
    ILoggingService log,
    IExecutablePathResolver executablePathResolver)
    : IPreparedTokenProcessLauncher
{
    private const string CompatLayerVariableName = "__COMPAT_LAYER";
    private const string RunAsInvokerCompatLayerValue = "RunAsInvoker";

    private static readonly string[] PrivilegesToDisable =
    [
        TokenPrivilegeHelper.SeBackupPrivilege,
        TokenPrivilegeHelper.SeRestorePrivilege,
        TokenPrivilegeHelper.SeTakeOwnershipPrivilege,
        TokenPrivilegeHelper.SeDebugPrivilege,
        TokenPrivilegeHelper.SeIncreaseQuotaPrivilege,
        TokenPrivilegeHelper.SeRelabelPrivilege,
    ];

    public ProcessLaunchNative.PROCESS_INFORMATION LaunchWithPreparedToken(
        IntPtr token,
        ProcessLaunchTarget target,
        LaunchTokenSource tokenSource,
        string accountSid,
        bool allowUnsuspendedRetry = true)
    {
        AllowSetForegroundWindowAny();

        if (tokenSource == LaunchTokenSource.CurrentProcess)
            DisablePrivilegesOnToken(token, PrivilegesToDisable);

        SetRestrictiveDefaultDacl(token, accountSid);

        if (!TryCreateEnvironmentBlock(token, out var envBlock))
        {
            log.Warn("CreateEnvironmentBlock failed - process will inherit parent environment");
        }

        try
        {
            envBlock.MergeInPlace(target.EnvironmentVariables);

            var resolutionContext = envBlock.Pointer != IntPtr.Zero
                ? ExecutablePathResolutionContext.TargetEnvironment(
                    new NativeEnvironmentVariableReader(envBlock.Pointer),
                    accountSid)
                : ExecutablePathResolutionContext.DirectOnly();
            var resolvedExePath = executablePathResolver.TryResolvePath(target.ExePath, resolutionContext);
            if (resolvedExePath != null)
                target = target with { ExePath = resolvedExePath };

            try
            {
                return LaunchWithStandardRetries(token, target, envBlock.Pointer, allowUnsuspendedRetry);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorElevationRequired
                                           && TryBuildRunAsInvokerEnvironment(envBlock, target.EnvironmentVariables, out var retryEnvBlock))
            {
                using (retryEnvBlock)
                {
                    log.Warn("LaunchWithPreparedToken: elevation required, retrying with __COMPAT_LAYER=RunAsInvoker");
                    return LaunchWithStandardRetries(token, target, retryEnvBlock.Pointer, allowUnsuspendedRetry);
                }
            }
        }
        finally
        {
            envBlock.Dispose();
        }
    }

    protected virtual void AllowSetForegroundWindowAny()
        => ProcessLaunchNative.AllowSetForegroundWindow(ProcessLaunchNative.ASFW_ANY);

    protected virtual void DisablePrivilegesOnToken(IntPtr token, string[] privileges)
        => TokenPrivilegeHelper.DisablePrivilegesOnToken(token, privileges);

    protected virtual void SetRestrictiveDefaultDacl(IntPtr token, string accountSid)
        => NativeTokenAcquisition.SetRestrictiveDefaultDacl(token, accountSid);

    protected virtual bool TryCreateEnvironmentBlock(IntPtr token, out EnvironmentBlock envBlock)
    {
        if (!ProcessLaunchNative.CreateEnvironmentBlock(out var pointer, token, false))
        {
            envBlock = EnvironmentBlock.Empty();
            return false;
        }

        envBlock = EnvironmentBlock.Own(pointer, static environmentPointer => { ProcessLaunchNative.DestroyEnvironmentBlock(environmentPointer); });
        return true;
    }

    protected virtual ProcessLaunchNative.PROCESS_INFORMATION CreateProcessWithToken(
        IntPtr token,
        ProcessLaunchTarget target,
        IntPtr environmentPointer,
        bool suspended,
        bool breakawayFromJob)
        => ProcessLaunchNative.CreateProcessWithToken(
            token,
            target,
            environmentPointer,
            log,
            suspended: suspended,
            breakawayFromJob: breakawayFromJob);

    private ProcessLaunchNative.PROCESS_INFORMATION LaunchWithStandardRetries(
        IntPtr token,
        ProcessLaunchTarget target,
        IntPtr environmentPointer,
        bool allowUnsuspendedRetry)
    {
        try
        {
            return CreateProcessWithToken(token, target, environmentPointer, suspended: true, breakawayFromJob: true);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            log.Warn("LaunchWithPreparedToken: breakaway denied, retrying without CREATE_BREAKAWAY_FROM_JOB");
            try
            {
                return CreateProcessWithToken(token, target, environmentPointer, suspended: true, breakawayFromJob: false);
            }
            catch (Win32Exception ex2) when (ex2.NativeErrorCode == 5)
            {
                if (!allowUnsuspendedRetry)
                    throw;

                log.Warn("LaunchWithPreparedToken: suspended launch denied (restricted job active), retrying without CREATE_SUSPENDED");
                return CreateProcessWithToken(token, target, environmentPointer, suspended: false, breakawayFromJob: false);
            }
        }
    }

    private bool TryBuildRunAsInvokerEnvironment(
        EnvironmentBlock baseEnvironment,
        IReadOnlyDictionary<string, string>? targetEnvironmentVariables,
        out EnvironmentBlock retryEnvironment)
    {
        Dictionary<string, string> variables;
        if (baseEnvironment.Pointer != IntPtr.Zero)
        {
            variables = NativeEnvironmentBlockReader.Read(baseEnvironment.Pointer);
        }
        else
        {
            log.Warn("LaunchWithPreparedToken: environment block unavailable, building RunAsInvoker retry environment from current process variables");
            variables = ReadCurrentProcessEnvironment();
        }

        if (targetEnvironmentVariables != null)
        {
            foreach (var (key, value) in targetEnvironmentVariables)
                variables[key] = value;
        }

        if (variables.ContainsKey(CompatLayerVariableName))
        {
            retryEnvironment = EnvironmentBlock.Empty();
            log.Warn("LaunchWithPreparedToken: __COMPAT_LAYER already present, skipping RunAsInvoker retry");
            return false;
        }

        variables[CompatLayerVariableName] = RunAsInvokerCompatLayerValue;
        retryEnvironment = EnvironmentBlock.Build(variables);
        return true;
    }

    protected virtual Dictionary<string, string> ReadCurrentProcessEnvironment()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
                variables[key] = value;
        }

        return variables;
    }
}
