using System.Text.Json;
using System.Text.Json.Serialization;
using PrefTrans.Services;
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

            var command = args[0].ToLowerInvariant();
            try
            {
                return command switch
                {
                    "store" => Store(args.Length > 1 ? args[1] : "user-settings.json"),
                    "load" => Load(args.Length > 1 ? args[1] : "user-settings.json"),
                    "show" => Show(),
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

    /// <summary>
    /// Strips --logfile &lt;path&gt; from args and redirects Console.Error to that file.
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
                Console.SetError(writer);
                return (filteredArgs, writer);
            }
            catch
            {
                return (filteredArgs, null);
            }
        }

        return (args, null);
    }

    private static int Store(string filename)
    {
        var settings = SettingsReader.ReadAll();
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

    private static int Load(string filename)
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

        SettingsWriter.WriteAll(settings);
        Console.Error.WriteLine($"Settings applied from {filename}");
        return 0;
    }

    private static int Show()
    {
        var settings = SettingsReader.ReadAll();
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
