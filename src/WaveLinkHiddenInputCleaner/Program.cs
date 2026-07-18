using System.Reflection;
using WaveLinkHiddenInputCleaner;

var parsed = Parse(args);
if (parsed.ExitCode is { } code) return code;
var app = new CleanerApplication(
    new SettingsDiscovery(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
    new FileOperations(), new ProcessControl(), new AppActivator(), Console.In, Console.Out, () => DateTime.Now);
return app.Run(parsed.Options!);

static (CleanerOptions? Options, int? ExitCode) Parse(string[] args)
{
    string? path = null; var yes = false; var dry = false; var noRestart = false; var unhide = false;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--help": PrintHelp(); return (null, 0);
            case "--version": Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)); return (null, 0);
            case "--yes": yes = true; break;
            case "--dry-run": dry = true; break;
            case "--unhide": unhide = true; break;
            case "--no-restart": noRestart = true; break;
            case "--settings-path" when i + 1 < args.Length: path = args[++i]; break;
            default: Console.Error.WriteLine($"Invalid argument: {args[i]}"); Console.Error.WriteLine("Use --help for usage."); return (null, 2);
        }
    }
    return (new CleanerOptions(path, yes, dry, noRestart, unhide), null);
}

static void PrintHelp() => Console.WriteLine("""
WaveLinkHiddenInputCleaner 1.1.0
Safely removes or unhides stale Wave Link inputs whose IsHiddenFromMixes value is true.

Usage: WaveLinkHiddenInputCleaner.exe [options]
  --yes                  Skip confirmation
  --dry-run              Report matches without changing anything
  --unhide               Keep matched entries and set the flag to false
  --settings-path <path> Select Settings.json when discovery is ambiguous
  --no-restart           Do not restart Wave Link if this tool stopped it
  --help                  Show this help
  --version               Show the build version
""");
