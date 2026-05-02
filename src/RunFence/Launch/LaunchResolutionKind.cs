namespace RunFence.Launch;

public enum LaunchResolutionKind
{
    /// <summary>Resolved to a native executable, ready to launch directly.</summary>
    Direct,
    
    /// <summary>Batch script.</summary>
    Script,

    /// <summary>Association resolved to a concrete executable via registry lookup.</summary>
    Handler,

    /// <summary>No association found — using ShellExec_RunDLL fallback. Process PID should not be tracked.</summary>
    ShellWrapped
}
