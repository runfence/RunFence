using System.Text.Json;
using System.Text.Json.Serialization;
using PrefTrans.Services;
using PrefTrans.Services.IO;
using PrefTrans.Settings;

namespace PrefTrans;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [STAThread]
    private static int Main(string[] args)
    {
        (args, var logFileWriter) = ExtractLogFile(args);

        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            var rawCommand = args[0];
            var command = CommandLineParser.ResolveCommand(rawCommand, ["store", "load", "show"]);
            try
            {
                var (reader, writer) = CreateServices();
                return command switch
                {
                    "store" => Store(reader, args.Length > 1 ? args[1] : "user-settings.json"),
                    "load" => Load(writer, args.Length > 1 ? args[1] : "user-settings.json"),
                    "show" => Show(reader),
                    _ => PrintUsage(),
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                return 1;
            }
        }
        finally
        {
            logFileWriter?.Dispose();
        }
    }

    private static (ISettingsReader reader, ISettingsWriter writer) CreateServices()
    {
        var safe = new SafeExecutor();
        var broadcast = new BroadcastHelper();
        var userProfileFilter = new UserProfileFilter();
        var settingsFilter = new SettingsFilter(userProfileFilter);
        var allIo = SettingsIoCatalog.CreateAll(safe, broadcast, userProfileFilter);
        var reader = new SettingsReader(allIo, settingsFilter);
        var writer = new SettingsWriter(allIo);

        return (reader, writer);
    }

    /// <summary>
    /// Strips --logfile &lt;path&gt; from args and redirects console output to that file.
    /// Console.SetError always redirects (warnings/errors go to log for all commands).
    /// Console.SetOut only redirects for non-'show' commands — 'show' pipes JSON to stdout,
    /// so redirecting stdout would break callers that consume the JSON output.
    /// Returns the filtered args and the opened writer (null if not specified or on open failure).
    /// </summary>
    private static (string[] args, TextWriter? writer) ExtractLogFile(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--logfile")
                continue;

            var path = args[i + 1];
            var filteredArgs = args.Take(i).Concat(args.Skip(i + 2)).ToArray();
            try
            {
                var writer = new StreamWriter(path, append: false) { AutoFlush = true };
                var command = filteredArgs.Length > 0 ? filteredArgs[0].ToLowerInvariant() : "";
                if (command != "show")
                    Console.SetOut(writer);
                Console.SetError(writer);
                return (filteredArgs, writer);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Unable to open log file: {ex.Message}");
                return (filteredArgs, null);
            }
        }

        return (args, null);
    }

    private static int Store(ISettingsReader reader, string filename)
    {
        var settings = reader.ReadAll();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tempFile = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, filename, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }

        Console.Error.WriteLine($"Settings saved to {filename}");
        return 0;
    }

    private static int Load(ISettingsWriter writer, string filename)
    {
        if (!File.Exists(filename))
        {
            Console.Error.WriteLine($"File not found: {filename}");
            return 1;
        }

        var json = File.ReadAllText(filename);
        var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
        if (settings == null)
        {
            Console.Error.WriteLine("Failed to parse settings file.");
            return 1;
        }

        writer.WriteAll(settings);
        Console.Error.WriteLine($"Settings applied from {filename}");
        return 0;
    }

    private static int Show(ISettingsReader reader)
    {
        var settings = reader.ReadAll();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        Console.WriteLine(json);
        return 0;
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine("Usage: preftrans <command> [filename]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Commands:");
        Console.Error.WriteLine("  store [filename.json]  Capture current settings (default: user-settings.json)");
        Console.Error.WriteLine("  load  [filename.json]  Apply settings from file (default: user-settings.json)");
        Console.Error.WriteLine("  show                   Print current settings as JSON to stdout");
        return 1;
    }
}
