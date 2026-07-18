using System.Text.Json.Nodes;

namespace WaveLinkHiddenInputCleaner;

public sealed record CleanerOptions(string? SettingsPath, bool Yes, bool DryRun, bool NoRestart, bool Unhide = false);

internal enum CleanupAction { Remove, Unhide }

public sealed class CleanerApplication(
    ISettingsDiscovery discovery, IFileOperations files, IProcessControl processes,
    IAppActivator activator, TextReader input, TextWriter output, Func<DateTime> clock)
{
    private readonly JsonCleaner json = new();

    public int Run(CleanerOptions options)
    {
        SettingsLocation location;
        try { location = discovery.Discover(options.SettingsPath); }
        catch (Exception ex) { output.WriteLine($"Error: {ex.Message}"); return 1; }

        CleanupAction action;
        try
        {
            var initial = ReadValid(location.SettingsPath);
            var matches = json.Find(initial);
            if (matches.Count == 0) { output.WriteLine("No hidden Wave Link inputs require cleanup."); return 0; }
            output.WriteLine($"Found {matches.Count} hidden input entr{(matches.Count == 1 ? "y" : "ies")}:");
            foreach (var name in matches) output.WriteLine($"  {name}");
            if (options.DryRun) { output.WriteLine("Dry run: no changes were made."); return 0; }
            if (options.Unhide)
            {
                action = CleanupAction.Unhide;
                if (!options.Yes && !Confirm("Keep these entries and set IsHiddenFromMixes to false?"))
                { output.WriteLine("Cleanup cancelled."); return 0; }
            }
            else if (options.Yes) action = CleanupAction.Remove;
            else
            {
                var selected = ChooseAction();
                if (selected is null) { output.WriteLine("Cleanup cancelled."); return 0; }
                action = selected.Value;
            }
        }
        catch (Exception ex) { output.WriteLine($"Error: {ex.Message}"); return 1; }

        var process = processes.FindGuiProcess();
        var stoppedByUs = false;
        var failed = false;
        try
        {
            if (process?.IsRunning == true)
            {
                stoppedByUs = true;
                if (!process.CloseGracefully(TimeSpan.FromSeconds(10))) process.KillTree();
                if (process.IsRunning) throw new InvalidOperationException("Wave Link is still running; settings were not changed.");
            }

            var current = ReadValid(location.SettingsPath);
            var changed = action == CleanupAction.Unhide ? json.Unhide(current) : json.Remove(current);
            if (changed == 0) { output.WriteLine("Settings changed before cleanup; no entries now require changes."); return 0; }
            ReplaceSafely(location.SettingsPath, current);
            output.WriteLine(action == CleanupAction.Unhide
                ? $"Made {changed} hidden input entr{(changed == 1 ? "y" : "ies")} visible."
                : $"Removed {changed} hidden input entr{(changed == 1 ? "y" : "ies")}.");
        }
        catch (Exception ex) { output.WriteLine($"Cleanup failed: {ex.Message}"); failed = true; }
        finally
        {
            if (stoppedByUs && !options.NoRestart)
            {
                try { activator.Activate(location.PackageFamilyName); output.WriteLine("Wave Link restarted."); }
                catch (Exception ex) { output.WriteLine($"Restart failed: {ex.Message}"); failed = true; }
            }
        }
        return failed ? 1 : 0;
    }

    private JsonNode ReadValid(string path)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return json.Parse(files.ReadAllBytes(path)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SettingsFormatException) { last = ex; if (attempt < 2) Thread.Sleep(100); }
        }
        throw new InvalidOperationException("Could not read valid Wave Link settings after three attempts.", last);
    }

    private void ReplaceSafely(string path, JsonNode root)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var backup = path + ".backup-" + clock().ToString("yyyyMMdd-HHmmssfff");
        try
        {
            var bytes = json.Serialize(root);
            files.WriteAllBytes(temp, bytes);
            json.Parse(files.ReadAllBytes(temp));
            files.Replace(temp, path, backup);
        }
        finally { try { files.Delete(temp); } catch { } }
    }

    private bool Confirm(string question)
    {
        output.Write($"{question} [y/N] ");
        var answer = input.ReadLine()?.Trim();
        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private CleanupAction? ChooseAction()
    {
        output.Write("Choose action: [r]emove, [u]nhide, or [c]ancel: ");
        return input.ReadLine()?.Trim().ToLowerInvariant() switch
        {
            "r" or "remove" => CleanupAction.Remove,
            "u" or "unhide" => CleanupAction.Unhide,
            _ => null
        };
    }
}
