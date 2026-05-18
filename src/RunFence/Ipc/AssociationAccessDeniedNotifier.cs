using RunFence.Infrastructure;

namespace RunFence.Ipc;

public class AssociationAccessDeniedNotifier(ITrayBalloonService trayBalloon, IClock clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private const int MaxNotificationsPerWindow = 5;
    private readonly SlidingWindowNotificationGate _notificationGate = new(clock, Window, MaxNotificationsPerWindow);

    public void Notify()
    {
        if (!_notificationGate.TryAcquire())
            return;

        trayBalloon.ShowWarning("RunFence blocked an association request because IPC access was denied.");
    }
}
