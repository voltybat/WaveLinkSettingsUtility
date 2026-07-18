using System.Diagnostics;

namespace WaveLinkHiddenInputCleaner;

public sealed record SettingsLocation(string SettingsPath, string PackageFamilyName);

public interface ISettingsDiscovery { SettingsLocation Discover(string? overridePath); }
public interface IFileOperations
{
    byte[] ReadAllBytes(string path);
    void WriteAllBytes(string path, byte[] bytes);
    IEnumerable<string> EnumerateFiles(string directory, string pattern);
    void Replace(string source, string destination, string backup);
    void Delete(string path);
}
public interface IWaveLinkProcess
{
    bool IsRunning { get; }
    bool CloseGracefully(TimeSpan timeout);
    void KillTree();
}
public interface IProcessControl { IWaveLinkProcess? FindGuiProcess(); }
public interface IAppActivator { void Activate(string packageFamilyName); }

public sealed class SettingsDiscovery : ISettingsDiscovery
{
    private readonly string localAppData;
    public SettingsDiscovery(string localAppData) => this.localAppData = localAppData;

    public SettingsLocation Discover(string? overridePath)
    {
        var packages = Path.Combine(localAppData, "Packages");
        var candidates = Directory.Exists(packages)
            ? Directory.EnumerateDirectories(packages, "Elgato.WaveLink_*", SearchOption.TopDirectoryOnly)
                .Select(d => new SettingsLocation(Path.Combine(d, "LocalState", "Settings.json"), Path.GetFileName(d)))
                .Where(x => File.Exists(x.SettingsPath)).ToArray()
            : [];

        if (overridePath is not null)
        {
            var full = Path.GetFullPath(overridePath);
            var match = candidates.FirstOrDefault(x => string.Equals(Path.GetFullPath(x.SettingsPath), full, StringComparison.OrdinalIgnoreCase));
            if (match is null) throw new InvalidOperationException("--settings-path must name Settings.json in an Elgato.WaveLink_* package LocalState directory.");
            return match;
        }
        return candidates.Length switch
        {
            0 => throw new InvalidOperationException("Wave Link was not found."),
            1 => candidates[0],
            _ => throw new InvalidOperationException("Multiple Wave Link settings files were found. Use --settings-path:\n" + string.Join("\n", candidates.Select(x => "  " + x.SettingsPath)))
        };
    }
}

public sealed class FileOperations : IFileOperations
{
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);
    public IEnumerable<string> EnumerateFiles(string directory, string pattern) => Directory.EnumerateFiles(directory, pattern);
    public void Replace(string source, string destination, string backup) => File.Replace(source, destination, backup, true);
    public void Delete(string path) { if (File.Exists(path)) File.Delete(path); }
}

public sealed class ProcessControl : IProcessControl
{
    public IWaveLinkProcess? FindGuiProcess() => Process.GetProcessesByName("Elgato.WaveLink").FirstOrDefault() is { } p ? new WaveLinkProcess(p) : null;
    private sealed class WaveLinkProcess(Process process) : IWaveLinkProcess
    {
        public bool IsRunning { get { try { return !process.HasExited; } catch { return false; } } }
        public bool CloseGracefully(TimeSpan timeout)
        {
            if (!IsRunning) return true;
            try { process.CloseMainWindow(); return process.WaitForExit((int)timeout.TotalMilliseconds); }
            catch (InvalidOperationException) { return true; }
        }
        public void KillTree() { if (IsRunning) { process.Kill(true); if (!process.WaitForExit(5000)) throw new InvalidOperationException("Wave Link did not terminate."); } }
    }
}

public sealed class AppActivator : IAppActivator
{
    public void Activate(string packageFamilyName)
    {
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"shell:AppsFolder\\{packageFamilyName}!App",
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Windows did not start Wave Link.");
    }
}
