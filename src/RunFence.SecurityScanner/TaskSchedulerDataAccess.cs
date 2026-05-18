using System.Runtime.InteropServices;
using System.Security.Principal;

namespace RunFence.SecurityScanner;

public class TaskSchedulerDataAccess
{
    private const int TaskActionExecute = 0;
    private const int TaskSecurityInformationOwnerGroupDacl = 0x07;

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
                try
                {
                    scheduler.Connect();
                    dynamic rootFolder = scheduler.GetFolder("\\");
                    try
                    {
                        CollectTaskFolderData(rootFolder, tasks);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(rootFolder);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(scheduler);
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(30_000))
        {
            thread.Interrupt();
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
            foreach (dynamic task in folder.GetTasks(1))
            {
                try
                {
                    tasks.Add(ReadTask(task));
                }
                catch
                {
                    /* skip individual task */
                }
                finally
                {
                    Marshal.ReleaseComObject(task);
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
                finally
                {
                    Marshal.ReleaseComObject(subFolder);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to collect task folder data: {ex.Message}");
        }
    }

    private static ScheduledTaskInfo ReadTask(dynamic task)
    {
        var actions = new List<ScheduledTaskActionInfo>();
        bool isPerUser = false;
        string? userSid = null;
        string? principalSid = null;
        string? taskSecurityDescriptor = null;
        string taskPath = "";
        dynamic? definition = null;

        try
        {
            taskPath = task.Path as string ?? "";
        }
        catch
        {
            /* skip path */
        }

        try
        {
            definition = task.Definition;

            try
            {
                foreach (dynamic action in definition.Actions)
                {
                    try
                    {
                        int actionType = (int)action.Type;
                        actions.Add(new ScheduledTaskActionInfo(
                            actionType,
                            actionType == TaskActionExecute ? action.Path as string : null,
                            actionType == TaskActionExecute ? action.Arguments as string : null,
                            actionType == TaskActionExecute ? action.WorkingDirectory as string : null));
                    }
                    catch
                    {
                        /* skip */
                    }
                    finally
                    {
                        ReleaseComObjectIfNeeded(action);
                    }
                }
            }
            catch
            {
                /* definition actions not accessible */
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
                        principalSid = userId;
                    }
                    else
                    {
                        try
                        {
                            var account = new NTAccount(userId);
                            var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
                            isPerUser = true;
                            userSid = sid.Value;
                            principalSid = sid.Value;
                        }
                        catch
                        {
                            principalSid = userId;
                        }
                    }
                }
            }
            catch
            {
                /* no principal info - treat as system task */
            }
            finally
            {
                ReleaseComObjectIfNeeded(definition);
            }
        }
        catch
        {
            /* definition not accessible */
        }

        try
        {
            taskSecurityDescriptor = task.GetSecurityDescriptor(TaskSecurityInformationOwnerGroupDacl) as string;
        }
        catch
        {
            /* best effort */
        }

        return new ScheduledTaskInfo(taskPath, actions, principalSid, taskSecurityDescriptor, isPerUser, userSid);
    }

    private static void ReleaseComObjectIfNeeded(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }
}
