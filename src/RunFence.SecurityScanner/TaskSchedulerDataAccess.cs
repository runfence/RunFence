using System.Runtime.ExceptionServices;
using System.Security.Principal;

namespace RunFence.SecurityScanner;

public class TaskSchedulerDataAccess
{
    public List<ScheduledTaskInfo> GetTaskSchedulerData()
    {
        var tasks = new List<ScheduledTaskInfo>();

        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var schedulerType = Type.GetTypeFromProgID("Schedule.Service");
                if (schedulerType == null)
                    return;

                dynamic scheduler = Activator.CreateInstance(schedulerType)!;
                scheduler.Connect();

                CollectTaskFolderData(scheduler.GetFolder("\\"), tasks);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(30_000))
        {
            Console.Error.WriteLine("Task Scheduler enumeration timed out");
            return [];
        }

        if (error != null)
            Console.Error.WriteLine($"Task Scheduler COM error: {error.Message}");

        return tasks;
    }

    private void CollectTaskFolderData(dynamic folder, List<ScheduledTaskInfo> tasks)
    {
        try
        {
            string folderPath = folder.Path;

            foreach (dynamic task in folder.GetTasks(1))
            {
                try
                {
                    string taskPath = task.Path;
                    string taskName = task.Name;

                    var exePaths = new List<string>();
                    bool isPerUser = false;
                    string? userSid = null;

                    try
                    {
                        dynamic definition = task.Definition;
                        foreach (dynamic action in definition.Actions)
                        {
                            try
                            {
                                if (action.Type == 0)
                                {
                                    string? exePath = action.Path;
                                    if (!string.IsNullOrEmpty(exePath))
                                        exePaths.Add(SecurityScanner.ExpandEnvVars(exePath));
                                }
                            }
                            catch
                            {
                                /* skip */
                            }
                        }

                        try
                        {
                            string? userId = (string?)definition.Principal.UserId;
                            if (!string.IsNullOrEmpty(userId))
                            {
                                if (userId.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
                                {
                                    isPerUser = true;
                                    userSid = userId;
                                }
                                else
                                {
                                    try
                                    {
                                        var account = new NTAccount(userId);
                                        var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
                                        isPerUser = true;
                                        userSid = sid.Value;
                                    }
                                    catch
                                    {
                                        /* leave as non-per-user */
                                    }
                                }
                            }
                        }
                        catch
                        {
                            /* no principal info — treat as system task */
                        }
                    }
                    catch
                    {
                        /* definition not accessible */
                    }

                    tasks.Add(new ScheduledTaskInfo(taskPath, folderPath, taskName, exePaths, isPerUser, userSid));
                }
                catch
                {
                    /* skip individual task */
                }
            }

            foreach (dynamic subFolder in folder.GetFolders(0))
            {
                try
                {
                    CollectTaskFolderData(subFolder, tasks);
                }
                catch
                {
                    /* skip inaccessible subfolder */
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to collect task folder data: {ex.Message}");
        }
    }

    public string? ResolveShortcutTarget(string lnkPath)
    {
        string? result = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    return;
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(lnkPath);
                result = shortcut.TargetPath as string;
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(10_000))
            return null;
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }
}