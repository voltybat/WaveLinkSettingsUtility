using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WaveLinkHiddenInputCleaner;

public sealed record CleanerOptions(string? SettingsPath, bool Yes, bool NoRestart,
    bool Unhide = false, bool Backup = false, string? RestorePath = null, bool InteractiveMenu = false);

internal enum CleanupAction { Remove, Unhide }

public sealed class CleanerApplication(
    ISettingsDiscovery discovery, IFileOperations files, IProcessControl processes,
    IAppActivator activator, TextReader input, TextWriter output, Func<DateTime> clock)
{
    private readonly JsonCleaner json = new();
    private static readonly Regex BackupName = new("^Settings\\.json\\.backup-[0-9]{8}-[0-9]{9}$", RegexOptions.IgnoreCase);

    public int Run(CleanerOptions options)
    {
        SettingsLocation location;
        try { location = discovery.Discover(options.SettingsPath); }
        catch (Exception ex) { output.WriteLine($"Error: {ex.Message}"); return 1; }

        if (options.InteractiveMenu)
        {
            PrintIntroduction();
            output.Write("Choose an operation [1-4]: ");
            switch (input.ReadLine()?.Trim().ToLowerInvariant())
            {
                case "1" or "clean" or "cleanup": return Clean(location, options);
                case "2" or "backup": return Backup(location, options.NoRestart);
                case "3" or "restore": return ChooseAndRestore(location, options.NoRestart);
                default: output.WriteLine("Exited without changes."); return 0;
            }
        }
        if (options.Backup) return Backup(location, options.NoRestart);
        if (options.RestorePath is not null) return Restore(location, options.RestorePath, options.NoRestart);
        return Clean(location, options);
    }

    private int Backup(SettingsLocation location, bool noRestart)
    {
        var process = processes.FindGuiProcess();
        var stoppedByUs = false;
        var failed = false;
        try
        {
            if (process?.IsRunning == true)
            {
                stoppedByUs = true;
                if (!process.CloseGracefully(TimeSpan.FromSeconds(10))) process.KillTree();
                if (process.IsRunning) throw new InvalidOperationException("Wave Link is still running; no backup was created.");
            }
            var bytes = ReadValidBytes(location.SettingsPath);
            var backup = NewBackupPath(location.SettingsPath);
            files.WriteAllBytes(backup, bytes);
            output.WriteLine($"Settings backup created: {backup}");
        }
        catch (Exception ex) { output.WriteLine($"Backup failed: {ex.Message}"); failed = true; }
        finally
        {
            if (stoppedByUs && !noRestart)
            {
                try { activator.Activate(location.PackageFamilyName); output.WriteLine("Wave Link restarted."); }
                catch (Exception ex) { output.WriteLine($"Restart failed: {ex.Message}"); failed = true; }
            }
        }
        return failed ? 1 : 0;
    }

    private int ChooseAndRestore(SettingsLocation location, bool noRestart)
    {
        try
        {
            var backups = ManagedBackups(location.SettingsPath).ToArray();
            if (backups.Length == 0) { output.WriteLine("No managed settings backups were found."); return 0; }
            output.WriteLine("Available settings backups (newest first):");
            for (var i = 0; i < backups.Length; i++) output.WriteLine($"  {i + 1}. {Path.GetFileName(backups[i])}");
            output.Write("Select a backup number, or press Enter to cancel: ");
            if (!int.TryParse(input.ReadLine()?.Trim(), out var choice) || choice < 1 || choice > backups.Length)
            { output.WriteLine("Restore cancelled."); return 0; }
            return Restore(location, backups[choice - 1], noRestart);
        }
        catch (Exception ex) { output.WriteLine($"Restore failed: {ex.Message}"); return 1; }
    }

    private int Restore(SettingsLocation location, string backupPath, bool noRestart)
    {
        var failed = false;
        try
        {
            var managedPath = ValidateManagedPath(location.SettingsPath, backupPath);
            var replacement = ReadValidBytes(managedPath);
            output.WriteLine("Warning: backups from another Wave Link version may fail, be ignored, or cause Wave Link to reset its settings.");

            var process = processes.FindGuiProcess();
            if (process?.IsRunning == true)
            {
                if (!process.CloseGracefully(TimeSpan.FromSeconds(10))) process.KillTree();
                if (process.IsRunning) throw new InvalidOperationException("Wave Link is still running; settings were not changed.");
            }
            ReplaceBytesSafely(location.SettingsPath, replacement);
            output.WriteLine($"Restored settings from: {managedPath}");
        }
        catch (Exception ex) { output.WriteLine($"Restore failed: {ex.Message}"); failed = true; }
        finally
        {
            if (!noRestart)
            {
                try { activator.Activate(location.PackageFamilyName); output.WriteLine("Wave Link restarted."); }
                catch (Exception ex) { output.WriteLine($"Restart failed: {ex.Message}"); failed = true; }
            }
        }
        return failed ? 1 : 0;
    }

    private int Clean(SettingsLocation location, CleanerOptions options)
    {
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

            var initial = ReadValid(location.SettingsPath);
            var matches = json.Find(initial);
            if (matches.Count == 0) { output.WriteLine("No hidden Wave Link inputs require cleanup."); return 0; }
            output.WriteLine($"Found {matches.Count} hidden input entr{(matches.Count == 1 ? "y" : "ies")}:");
            foreach (var name in matches) output.WriteLine($"  {name}");
            if (options.Unhide)
            {
                if (!options.Yes && !Confirm("Keep these entries and set IsHiddenFromMixes to false?"))
                { output.WriteLine("Cleanup cancelled."); return 0; }
            }
            else if (!options.Yes)
            {
                var selected = ChooseAction();
                if (selected is null) { output.WriteLine("Cleanup cancelled."); return 0; }
                return ApplyCleanup(location, selected.Value);
            }

            return ApplyCleanup(location, options.Unhide ? CleanupAction.Unhide : CleanupAction.Remove);
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

    private int ApplyCleanup(SettingsLocation location, CleanupAction action)
    {
        var current = ReadValid(location.SettingsPath);
        var changed = action == CleanupAction.Unhide ? json.Unhide(current) : json.Remove(current);
        if (changed == 0) { output.WriteLine("Settings changed before cleanup; no entries now require changes."); return 0; }
        ReplaceBytesSafely(location.SettingsPath, json.Serialize(current));
        output.WriteLine(action == CleanupAction.Unhide
            ? $"Made {changed} hidden input entr{(changed == 1 ? "y" : "ies")} visible. The occupied channel slot is now available for inspection."
            : $"Removed {changed} hidden input entr{(changed == 1 ? "y" : "ies")}, freeing the channel slot{(changed == 1 ? "" : "s")}.");
        return 0;
    }

    private byte[] ReadValidBytes(string path)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { var bytes = files.ReadAllBytes(path); json.Validate(json.Parse(bytes)); return bytes; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SettingsFormatException)
            { last = ex; if (attempt < 2) Thread.Sleep(100); }
        }
        throw new InvalidOperationException($"Could not read valid Wave Link settings after three attempts: {last?.Message}", last);
    }

    private JsonNode ReadValid(string path) => json.Parse(ReadValidBytes(path));

    private void ReplaceBytesSafely(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            files.WriteAllBytes(temp, bytes);
            json.Validate(json.Parse(files.ReadAllBytes(temp)));
            files.Replace(temp, path, NewBackupPath(path));
        }
        finally { try { files.Delete(temp); } catch { } }
    }

    private string NewBackupPath(string settingsPath) => settingsPath + ".backup-" + clock().ToString("yyyyMMdd-HHmmssfff");

    private IEnumerable<string> ManagedBackups(string settingsPath) =>
        files.EnumerateFiles(Path.GetDirectoryName(settingsPath)!, "Settings.json.backup-*")
            .Where(path => BackupName.IsMatch(Path.GetFileName(path)))
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

    private string ValidateManagedPath(string settingsPath, string candidate)
    {
        var full = Path.GetFullPath(candidate);
        var directory = Path.GetFullPath(Path.GetDirectoryName(settingsPath)!);
        if (!string.Equals(Path.GetDirectoryName(full), directory, StringComparison.OrdinalIgnoreCase) ||
            !BackupName.IsMatch(Path.GetFileName(full)))
            throw new InvalidOperationException("The restore path must be a managed Settings.json.backup-* file beside the selected package's Settings.json.");
        return full;
    }

    private void PrintIntroduction() => output.WriteLine("""
        Wave Link supports up to eight input channels. Hidden entries still consume those slots.
        Removing a stale entry frees its slot; unhiding keeps it visible for inspection.

          1. Clean hidden inputs  - Remove stale entries or make them visible.
          2. Create backup       - Save an exact copy of the current settings.
          3. Restore backup      - Replace current settings with a previous backup.
          4. Exit                - Close this tool without making changes.

        WARNING: Operations 1-3 will close Wave Link if it is running so the settings file
        can be accessed safely. Wave Link will restart afterward unless --no-restart is used.
        """);

    private bool Confirm(string question)
    {
        output.Write($"{question} [y/N] ");
        var answer = input.ReadLine()?.Trim();
        return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private CleanupAction? ChooseAction()
    {
        output.Write("Choose action: [r]emove, [u]nhide, or [c]ancel: ");
        return input.ReadLine()?.Trim().ToLowerInvariant() switch
        { "r" or "remove" => CleanupAction.Remove, "u" or "unhide" => CleanupAction.Unhide, _ => null };
    }
}
