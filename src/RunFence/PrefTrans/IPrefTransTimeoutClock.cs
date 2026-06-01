namespace RunFence.PrefTrans;

internal interface IPrefTransTimeoutClock
{
    long ElapsedMilliseconds { get; }
}
