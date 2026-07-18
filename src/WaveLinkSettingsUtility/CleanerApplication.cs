using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WaveLinkSettingsUtility;

public sealed record CleanerOptions(string? SettingsPath, bool Yes, bool NoRestart,
    bool Unhide = false, bool Backup = false, string? RestorePath = null, bool InteractiveMenu = false,
    bool DetectUnavailable = false, bool RepairUnavailable = false);

internal enum CleanupAction { Remove, Unhide }

public sealed class CleanerApplication(
    ISettingsDiscovery discovery, IFileOperations files, IProcessControl processes,
    IAppActivator activator, TextReader input, TextWriter output, Func<DateTime> clock,
    IAudioEndpointInspector? endpointInspector = null)
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
            var failed = false;
            while (true)
            {
                PrintIntroduction();
                output.Write("Choose an operation [1-6]: ");
                var choice = input.ReadLine()?.Trim().ToLowerInvariant();
                if (choice is null or "6" or "exit" or "quit")
                {
                    output.WriteLine("Exited.");
                    return failed ? 1 : 0;
                }

                var result = choice switch
                {
                    "1" or "clean" or "cleanup" => Clean(location, options),
                    "2" or "transfer" => TransferEffects(location, options.NoRestart),
                    "3" or "backup" => Backup(location, options.NoRestart),
                    "4" or "restore" => ChooseAndRestore(location, options.NoRestart),
                    "5" or "detect" or "repair" => DetectUnavailable(location, options.NoRestart, repair: true),
                    _ => -1
                };
                if (result < 0) output.WriteLine("Invalid selection. Choose a number from 1 to 6.");
                else failed |= result != 0;
                output.WriteLine();
            }
        }
        if (options.Backup) return Backup(location, options.NoRestart);
        if (options.RestorePath is not null) return Restore(location, options.RestorePath, options.NoRestart);
        if (options.DetectUnavailable || options.RepairUnavailable)
            return DetectUnavailable(location, options.NoRestart, options.RepairUnavailable, options.Yes);
        return Clean(location, options);
    }

    private int DetectUnavailable(SettingsLocation location, bool noRestart, bool repair = false, bool yes = false)
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
                if (process.IsRunning) throw new InvalidOperationException("Wave Link is still running; endpoint detection could not read its settings.");
            }
            var root = ReadValid(location.SettingsPath);
            var channels = json.GetHardwareChannels(root);
            if (channels.Count == 0) { output.WriteLine("No hardware input channels were found."); return 0; }
            var inspector = endpointInspector ?? new WindowsAudioEndpointInspector();
            var activeEndpoints = inspector.GetActiveCaptureEndpoints();
            output.WriteLine("Hardware input endpoint status:");
            var unavailable = 0;
            var repairs = new List<(HardwareChannel Channel, AudioEndpoint Endpoint)>();
            foreach (var channel in channels)
            {
                var result = inspector.Inspect(channel.DeviceId);
                if (result.State != AudioEndpointState.Active) unavailable++;
                output.WriteLine($"  {ChannelLabel(channel)} - {StateLabel(result.State)}");
                output.WriteLine($"    ID: {channel.DeviceId}");
                if (!string.IsNullOrWhiteSpace(result.Detail)) output.WriteLine($"    {result.Detail}");
                if (result.State != AudioEndpointState.Active)
                {
                    var suggestions = EndpointReplacementMatcher.Find(channel, activeEndpoints);
                    if (suggestions.Count == 1)
                    {
                        var suggestion = suggestions[0];
                        output.WriteLine($"    Suggested replacement: {suggestion.Endpoint.FriendlyName}");
                        output.WriteLine($"    Replacement ID: {suggestion.Endpoint.Id}");
                        output.WriteLine($"    Match: {(suggestion.ExactNameMatch ? "exact friendly name" : "friendly name after removing Windows' numeric device prefix")}");
                        if (result.State is AudioEndpointState.Missing or AudioEndpointState.NotPresent)
                            repairs.Add((channel, suggestion.Endpoint));
                    }
                    else if (suggestions.Count > 1)
                    {
                        output.WriteLine("    Possible replacements (ambiguous; choose manually):");
                        foreach (var suggestion in suggestions)
                            output.WriteLine($"      {suggestion.Endpoint.FriendlyName} - {suggestion.Endpoint.Id}");
                    }
                    else output.WriteLine("    No high-confidence active replacement was found.");
                }
            }
            output.WriteLine(unavailable == 0
                ? "All configured hardware input endpoints are active."
                : $"Found {unavailable} hardware input endpoint{(unavailable == 1 ? "" : "s")} that {(unavailable == 1 ? "is" : "are")} not active.");
            if (repair && repairs.Count > 0)
            {
                foreach (var candidate in repairs)
                {
                    var question = $"Relink {ChannelLabel(candidate.Channel)} to {candidate.Endpoint.FriendlyName} ({candidate.Endpoint.Id})?";
                    if (!yes && !Confirm(question)) { output.WriteLine("Relink skipped."); continue; }
                    var result = json.RelinkHardwareChannel(root, candidate.Channel.Key, candidate.Endpoint.Id);
                    output.WriteLine($"Relink prepared; updated {result.UpdatedReferences} related reference{(result.UpdatedReferences == 1 ? "" : "s")}.");
                }
                ReplaceBytesSafely(location.SettingsPath, json.Serialize(root));
                output.WriteLine("Endpoint relink completed.");
            }
            else if (repair && repairs.Count == 0) output.WriteLine("No safely repairable endpoint was found.");
            return 0;
        }
        catch (Exception ex) { output.WriteLine($"Endpoint detection failed: {ex.Message}"); failed = true; }
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

    private static string ChannelLabel(HardwareChannel channel) => $"{channel.InputName} ({channel.DeviceName})";
    private static string StateLabel(AudioEndpointState state) => state switch
    {
        AudioEndpointState.Active => "active",
        AudioEndpointState.Disabled => "disabled",
        AudioEndpointState.NotPresent => "not present",
        AudioEndpointState.Unplugged => "unplugged",
        AudioEndpointState.Missing => "broken or stale ID",
        _ => "unknown state"
    };

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

    private int TransferEffects(SettingsLocation location, bool noRestart)
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
            failed = RunEffectTransfer(location) != 0;
        }
        catch (Exception ex) { output.WriteLine($"Effect transfer failed: {ex.Message}"); failed = true; }
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

    private int RunEffectTransfer(SettingsLocation location)
    {
        var root = ReadValid(location.SettingsPath);
        var channels = json.GetEffectChannels(root);
        var sources = channels.Where(channel => channel.EffectCount > 0).ToArray();
        if (sources.Length == 0) { output.WriteLine("No Wave Link channels with stored effects were found."); return 0; }

        output.WriteLine("Select the unavailable or old source channel:");
        PrintChannels(sources);
        var source = ChooseChannel(sources, "Select a source number, or press Enter to cancel: ");
        if (source is null) { output.WriteLine("Effect transfer cancelled."); return 0; }

        output.WriteLine($"Effects stored on {ChannelLabel(source)}:");
        foreach (var name in source.EffectNames) output.WriteLine($"  {name}");

        var targets = channels.Where(channel => !string.Equals(channel.Key, source.Key, StringComparison.Ordinal)).ToArray();
        if (targets.Length == 0) { output.WriteLine("No other channel is available as a destination."); return 0; }
        output.WriteLine("Select the new working destination channel:");
        PrintChannels(targets);
        var target = ChooseChannel(targets, "Select a destination number, or press Enter to cancel: ");
        if (target is null) { output.WriteLine("Effect transfer cancelled."); return 0; }

        var warning = $"Replace {target.EffectCount} destination effect{(target.EffectCount == 1 ? "" : "s")} on " +
            $"{ChannelLabel(target)} with {source.EffectCount} effect{(source.EffectCount == 1 ? "" : "s")} from {ChannelLabel(source)}?";
        if (!Confirm(warning)) { output.WriteLine("Effect transfer cancelled."); return 0; }

        var result = json.TransferEffects(root, source.Key, target.Key);
        ReplaceBytesSafely(location.SettingsPath, json.Serialize(root));
        output.WriteLine($"Transferred {result.SourceEffectCount} effect{(result.SourceEffectCount == 1 ? "" : "s")} to {ChannelLabel(target)}, replacing {result.ReplacedEffectCount} existing effect{(result.ReplacedEffectCount == 1 ? "" : "s")}.");
        return 0;
    }

    private void PrintChannels(IReadOnlyList<EffectChannel> channels)
    {
        for (var i = 0; i < channels.Count; i++)
        {
            var channel = channels[i];
            output.WriteLine($"  {i + 1}. {ChannelLabel(channel)}{(channel.IsHidden ? " [hidden]" : "")} - " +
                $"{channel.EffectCount} effect{(channel.EffectCount == 1 ? "" : "s")}");
        }
    }

    private EffectChannel? ChooseChannel(IReadOnlyList<EffectChannel> channels, string prompt)
    {
        output.Write(prompt);
        return int.TryParse(input.ReadLine()?.Trim(), out var choice) && choice >= 1 && choice <= channels.Count
            ? channels[choice - 1] : null;
    }

    private static string ChannelLabel(EffectChannel channel) => $"{channel.InputName} ({channel.DeviceName})";

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

    private void PrintIntroduction() => output.WriteLine($"""
        WaveLinkSettingsUtility {typeof(CleanerApplication).Assembly.GetName().Version?.ToString(3)}

        Wave Link supports up to eight input channels. Hidden entries still consume those slots.
        Removing a stale entry frees its slot; unhiding keeps it visible for inspection.

          1. Clean hidden inputs  - Remove stale entries or make them visible.
          2. Transfer effects    - Copy effects from an old channel to its replacement.
          3. Create backup       - Save an exact copy of the current settings.
          4. Restore backup      - Replace current settings with a previous backup.
          5. Detect and repair   - Check stale hardware IDs and offer safe relinking.
          6. Exit                - Close this tool without making changes.

        WARNING: Operations 1-5 will close Wave Link if it is running so the settings file
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
