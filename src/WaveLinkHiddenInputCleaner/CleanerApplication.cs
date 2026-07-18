using System.Text.Json.Nodes;

namespace WaveLinkHiddenInputCleaner;

public sealed record CleanerOptions(string? SettingsPath, bool Yes, bool DryRun, bool NoRestart);

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

        try
        {
            var initial = ReadValid(location.SettingsPath);
            var matches = json.Find(initial);
            if (matches.Count == 0) { output.WriteLine("No hidden Wave Link inputs require cleanup."); return 0; }
            output.WriteLine($"Found {matches.Count} hidden input entr{(matches.Count == 1 ? "y" : "ies")}:");
            foreach (var name in matches) output.WriteLine($"  {name}");
            if (options.DryRun) { output.WriteLine("Dry run: no changes were made."); return 0; }
            if (!options.Yes && !Confirm()) { output.WriteLine("Cleanup cancelled."); return 0; }
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
            var removed = json.Remove(current);
            if (removed == 0) { output.WriteLine("Settings changed before cleanup; no entries now require removal."); return 0; }
            ReplaceSafely(location.SettingsPath, current);
            output.WriteLine($"Removed {removed} hidden input entr{(removed == 1 ? "y" : "ies")}.");
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

    private bool Confirm()
    {
        output.Write("Continue? [y/N] ");
        var answer = input.ReadLine()?.Trim();
        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
