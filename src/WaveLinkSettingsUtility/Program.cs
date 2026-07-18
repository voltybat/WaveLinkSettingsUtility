using System.Reflection;
using WaveLinkSettingsUtility;

var parsed = Parse(args);
if (parsed.ExitCode is { } code) return code;
var app = new CleanerApplication(
    new SettingsDiscovery(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
    new FileOperations(), new ProcessControl(), new AppActivator(), Console.In, Console.Out, () => DateTime.Now);
var options = parsed.Options!;
var exitCode = app.Run(options);
if (options.InteractiveMenu)
{
    Console.Write("Press Enter to close...");
    Console.ReadLine();
}
return exitCode;

static (CleanerOptions? Options, int? ExitCode) Parse(string[] args)
{
    string? path = null; string? restore = null; var yes = false; var noRestart = false; var unhide = false; var backup = false;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--help": PrintHelp(); return (null, 0);
            case "--version": Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)); return (null, 0);
            case "--yes": yes = true; break;
            case "--unhide": unhide = true; break;
            case "--backup": backup = true; break;
            case "--restore" when i + 1 < args.Length: restore = args[++i]; break;
            case "--restore": Console.Error.WriteLine("Invalid argument: --restore requires a backup path."); return (null, 2);
            case "--no-restart": noRestart = true; break;
            case "--settings-path" when i + 1 < args.Length: path = args[++i]; break;
            default: Console.Error.WriteLine($"Invalid argument: {args[i]}"); Console.Error.WriteLine("Use --help for usage."); return (null, 2);
        }
    }
    if ((backup && restore is not null) || ((backup || restore is not null) && unhide))
    {
        Console.Error.WriteLine("Invalid combination: backup/restore cannot be combined with --unhide, and --backup cannot be combined with --restore.");
        return (null, 2);
    }
    var cleanupAction = yes || unhide;
    return (new CleanerOptions(path, yes, noRestart, unhide, backup, restore,
        InteractiveMenu: args.Length == 0 || (!cleanupAction && !backup && restore is null)), null);
}

static void PrintHelp()
{
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
    Console.WriteLine($"""
    WaveLinkSettingsUtility {version}
    Wave Link supports eight input channels, and hidden entries still consume those slots.
    Remove stale entries to free slots, or unhide them to inspect the occupied slots.

    Usage: WaveLinkSettingsUtility.exe [options]
      --yes                  Skip confirmation
      --unhide               Keep matched entries and set the flag to false
      --backup               Create an exact timestamped settings backup
      --restore <path>       Restore a managed backup beside the selected Settings.json
      --settings-path <path> Select Settings.json when discovery is ambiguous
      --no-restart           Do not restart Wave Link if this tool stopped it
      --help                  Show this help
      --version               Show the build version

    Restore warning: backups from another Wave Link version may fail, be ignored, or
    cause Wave Link to reset its settings.
    Cleanup, effect transfer, backup, and restore close Wave Link if it is running so the settings file
    can be accessed safely. They restart it afterward unless --no-restart is used.
    """);
}
