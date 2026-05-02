namespace RunFence.Account.UI;

public class ProcessCommandLineFormatter
{
    public string? StripExecutable(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
            return null;

        commandLine = commandLine.TrimStart();

        string remainder;
        if (commandLine.StartsWith('"'))
        {
            int close = commandLine.IndexOf('"', 1);
            remainder = close >= 0 ? commandLine[(close + 1)..] : "";
        }
        else
        {
            int space = commandLine.IndexOf(' ');
            remainder = space >= 0 ? commandLine[space..] : "";
        }

        remainder = remainder.TrimStart();
        return remainder.Length > 0 ? remainder : null;
    }
}
