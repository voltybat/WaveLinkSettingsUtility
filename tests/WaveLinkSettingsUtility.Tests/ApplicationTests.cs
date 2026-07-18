using System.Text;
using System.Text.Json.Nodes;
using WaveLinkSettingsUtility;

namespace WaveLinkSettingsUtility.Tests;

public class ApplicationTests
{
    [Fact]
    public void NoMatchInspectsProcessButDoesNotWrite()
    {
        var files = new FakeFiles(Json(false)); var processes = new FakeProcesses();
        var result = App(files, processes).Run(new(null, true, false, false));
        Assert.Equal(0, result); Assert.Equal(1, processes.FindCalls); Assert.Empty(files.Writes);
    }

    [Fact]
    public void SecondReadGovernsRemoval()
    {
        var files = new FakeFiles(Json(true), Json(false)); var processes = new FakeProcesses();
        var result = App(files, processes).Run(new(null, true, false, false));
        Assert.Equal(0, result); Assert.Empty(files.Writes);
    }

    [Fact]
    public void RunningProcessClosesAndRestarts()
    {
        var files = new FakeFiles(Json(true), Json(true));
        var process = new FakeProcess { Running = true, Graceful = true };
        var activator = new FakeActivator();
        var result = App(files, new FakeProcesses(process), activator).Run(new(null, true, false, false));
        Assert.Equal(0, result); Assert.Equal(1, process.CloseCalls); Assert.Equal(0, process.KillCalls); Assert.Equal(1, activator.Calls); Assert.Single(files.Replacements);
    }

    [Fact]
    public void RunningProcessIsStoppedBeforeFirstSettingsRead()
    {
        var process = new FakeProcess { Running = true, Graceful = true };
        var files = new FakeFiles(Json(true), Json(true)) { ReadBlockedWhile = () => process.Running };

        var result = App(files, new FakeProcesses(process)).Run(new(null, true, false, false));

        Assert.Equal(0, result);
        Assert.Single(files.Replacements);
    }

    [Fact]
    public void TimeoutForcesTreeAndNoRestartSuppressesActivation()
    {
        var files = new FakeFiles(Json(true), Json(true));
        var process = new FakeProcess { Running = true, Graceful = false };
        var activator = new FakeActivator();
        var result = App(files, new FakeProcesses(process), activator).Run(new(null, true, true));
        Assert.Equal(0, result); Assert.Equal(1, process.KillCalls); Assert.Equal(0, activator.Calls);
    }

    [Fact]
    public void FailureAfterShutdownStillRestarts()
    {
        var files = new FakeFiles(Json(true), Json(true)) { ThrowOnWrite = true };
        var process = new FakeProcess { Running = true, Graceful = true }; var activator = new FakeActivator();
        var result = App(files, new FakeProcesses(process), activator).Run(new(null, true, false, false));
        Assert.Equal(1, result); Assert.Equal(1, activator.Calls);
    }

    [Fact]
    public void RealReplacementCreatesIdenticalBackupAndParseableOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "wlsu-app-" + Guid.NewGuid().ToString("N"));
        var state = Path.Combine(root, "Packages", "Elgato.WaveLink_test", "LocalState");
        Directory.CreateDirectory(state);
        var path = Path.Combine(state, "Settings.json");
        var original = Json(true);
        File.WriteAllBytes(path, original);
        try
        {
            var app = new CleanerApplication(new SettingsDiscovery(root), new FileOperations(), new FakeProcesses(),
                new FakeActivator(), new StringReader("y\n"), new StringWriter(), () => new(2026, 1, 2, 3, 4, 5, 6));
            Assert.Equal(0, app.Run(new(null, true, false, false)));
            Assert.Equal(original, File.ReadAllBytes(path + ".backup-20260102-030405006"));
            var cleaner = new JsonCleaner();
            Assert.Empty(cleaner.Find(cleaner.Parse(File.ReadAllBytes(path))));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void InteractiveUnhideRetainsEntryAndSetsFlagFalse()
    {
        var files = new FakeFiles(Json(true), Json(true));
        var writer = new StringWriter();
        var app = new CleanerApplication(new FakeDiscovery(), files, new FakeProcesses(), new FakeActivator(),
            new StringReader("u\n"), writer, () => new(2026, 1, 2));
        Assert.Equal(0, app.Run(new(null, false, false, false)));
        Assert.Single(files.Writes);
        var cleaner = new JsonCleaner();
        var changed = cleaner.Parse(files.LastWritten!);
        Assert.Empty(cleaner.Find(changed));
        Assert.Contains("Made 1 hidden input entry visible", writer.ToString());
    }

    [Fact]
    public void UnhideOptionCanBeConfirmedInteractively()
    {
        var files = new FakeFiles(Json(true), Json(true));
        var app = new CleanerApplication(new FakeDiscovery(), files, new FakeProcesses(), new FakeActivator(),
            new StringReader("yes\n"), new StringWriter(), () => new(2026, 1, 2));
        Assert.Equal(0, app.Run(new(null, false, false, true)));
        Assert.Single(files.Writes);
    }

    [Fact]
    public void ManualBackupIsExactAndInspectsProcess()
    {
        using var temp = new TempSettings(Json(true));
        var processes = new FakeProcesses();
        var app = temp.App(processes, new FakeActivator(), new StringReader(""),
            () => new(2026, 7, 18, 12, 34, 56, 789));

        Assert.Equal(0, app.Run(new(null, false, false, false, Backup: true)));

        Assert.Equal(1, processes.FindCalls);
        Assert.Equal(temp.Original, File.ReadAllBytes(temp.Path + ".backup-20260718-123456789"));
    }

    [Fact]
    public void ManualBackupStopsRunningProcessBeforeReadingAndRestartsIt()
    {
        var process = new FakeProcess { Running = true, Graceful = true };
        var files = new FakeFiles(Json(true)) { ReadBlockedWhile = () => process.Running };
        var activator = new FakeActivator();

        var result = App(files, new FakeProcesses(process), activator)
            .Run(new(null, false, false, false, Backup: true));

        Assert.Equal(0, result);
        Assert.Equal(1, process.CloseCalls);
        Assert.Equal(1, activator.Calls);
        Assert.Single(files.Writes);
    }

    [Fact]
    public void RestoreReplacesMalformedCurrentAndPreservesItExactly()
    {
        using var temp = new TempSettings("{malformed"u8.ToArray());
        var valid = Json(false);
        var restore = temp.Path + ".backup-20260101-010101001";
        File.WriteAllBytes(restore, valid);
        var activator = new FakeActivator();
        var app = temp.App(new FakeProcesses(), activator, new StringReader(""),
            () => new(2026, 7, 18, 12, 34, 56, 789));

        Assert.Equal(0, app.Run(new(null, false, false, false, RestorePath: restore)));

        Assert.Equal(valid, File.ReadAllBytes(temp.Path));
        Assert.Equal(temp.Original, File.ReadAllBytes(temp.Path + ".backup-20260718-123456789"));
        Assert.Equal(1, activator.Calls);
    }

    [Fact]
    public void RestoreRejectsArbitraryFileButStillAttemptsSingleRestart()
    {
        using var temp = new TempSettings(Json(true));
        var arbitrary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(temp.Path)!, "other.json");
        File.WriteAllBytes(arbitrary, Json(false));
        var activator = new FakeActivator();

        Assert.Equal(1, temp.App(new FakeProcesses(), activator, new StringReader(""), () => DateTime.Now)
            .Run(new(null, false, false, false, RestorePath: arbitrary)));
        Assert.Equal(1, activator.Calls);
        Assert.Equal(temp.Original, File.ReadAllBytes(temp.Path));
    }

    [Fact]
    public void InteractiveMenuCanBackupEvenWhenThereAreNoHiddenEntries()
    {
        using var temp = new TempSettings(Json(false));
        var output = new StringWriter();
        var result = temp.App(new FakeProcesses(), new FakeActivator(), new StringReader("3\n"),
            () => new(2026, 7, 18, 1, 2, 3, 4), output)
            .Run(new(null, false, false, false, InteractiveMenu: true));

        Assert.Equal(0, result);
        Assert.True(File.Exists(temp.Path + ".backup-20260718-010203004"));
        Assert.StartsWith("WaveLinkSettingsUtility 2.0.0", output.ToString());
        Assert.Contains("eight input channels", output.ToString());
        Assert.Contains("1. Clean hidden inputs", output.ToString());
        Assert.Contains("2. Transfer effects", output.ToString());
        Assert.Contains("WARNING: Operations 1-4 will close Wave Link", output.ToString());
        Assert.Equal(2, output.ToString().Split("Choose an operation [1-5]:").Length - 1);
    }

    [Fact]
    public void InteractiveMenuRunsMultipleOperationsUntilExit()
    {
        using var temp = new TempSettings(Json(false));
        var output = new StringWriter();
        var result = temp.App(new FakeProcesses(), new FakeActivator(), new StringReader("3\n3\n5\n"),
            () => new(2026, 7, 18, 1, 2, 3, 4), output)
            .Run(new(null, false, false, InteractiveMenu: true));

        Assert.Equal(0, result);
        Assert.Equal(3, output.ToString().Split("Choose an operation [1-5]:").Length - 1);
        Assert.Contains("Exited.", output.ToString());
    }

    [Fact]
    public void InteractiveEffectTransferCopiesOnlyEffectsAndReportsOverwrite()
    {
        var files = new FakeFiles(EffectJson());
        var output = new StringWriter();
        var result = new CleanerApplication(new FakeDiscovery(), files, new FakeProcesses(), new FakeActivator(),
            new StringReader("2\n1\n1\ny\n"), output, () => new(2026, 1, 2, 3, 4, 5, 6))
            .Run(new(null, false, false, InteractiveMenu: true));

        Assert.Equal(0, result);
        Assert.Single(files.Replacements);
        var changed = new JsonCleaner().Parse(files.LastWritten!);
        var inputs = changed["MixerConfiguration"]!["InputSettings"]!;
        Assert.True(JsonNode.DeepEquals(inputs["old"]!["AudioPluginConfigurations"], inputs["new"]!["AudioPluginConfigurations"]));
        Assert.Equal("new-device", inputs["new"]!["DeviceSettings"]!["DeviceId"]!.GetValue<string>());
        Assert.Equal("new-app", inputs["new"]!["AudioAppConfigurations"]![0]!["Id"]!.GetValue<string>());
        Assert.Equal(0.8, inputs["new"]!["MasterVolume"]!.GetValue<double>());
        Assert.Contains("EQ", output.ToString());
        Assert.Contains("replacing 1 existing effect", output.ToString());
    }

    [Fact]
    public void EffectTransferStopsBeforeReadingAndRestartsAfterCancellation()
    {
        var process = new FakeProcess { Running = true, Graceful = true };
        var files = new FakeFiles(EffectJson()) { ReadBlockedWhile = () => process.Running };
        var activator = new FakeActivator();

        var result = new CleanerApplication(new FakeDiscovery(), files, new FakeProcesses(process), activator,
            new StringReader("2\n\n"), new StringWriter(), () => new(2026, 1, 2))
            .Run(new(null, false, false, InteractiveMenu: true));

        Assert.Equal(0, result);
        Assert.Equal(1, process.CloseCalls);
        Assert.Equal(1, activator.Calls);
        Assert.Empty(files.Writes);
    }

    [Fact]
    public void EffectTransferWithNoStoredEffectsDoesNotWrite()
    {
        var files = new FakeFiles(Encoding.UTF8.GetBytes("""
            {"MixerConfiguration":{"InputSettings":{"one":{"InputName":"Mic","DeviceSettings":{"DeviceName":"Device"},"AudioPluginConfigurations":[]}}}}
            """));
        var output = new StringWriter();

        var result = new CleanerApplication(new FakeDiscovery(), files, new FakeProcesses(), new FakeActivator(),
            new StringReader("2\n"), output, () => new(2026, 1, 2))
            .Run(new(null, false, false, InteractiveMenu: true));

        Assert.Equal(0, result);
        Assert.Empty(files.Writes);
        Assert.Contains("No Wave Link channels with stored effects", output.ToString());
    }

    [Fact]
    public void RealEffectTransferCreatesExactBackupAndParseableOutput()
    {
        var original = EffectJson();
        using var temp = new TempSettings(original);
        var app = temp.App(new FakeProcesses(), new FakeActivator(), new StringReader("2\n1\n1\ny\n"),
            () => new(2026, 7, 18, 3, 4, 5, 6));

        Assert.Equal(0, app.Run(new(null, false, false, InteractiveMenu: true)));

        Assert.Equal(original, File.ReadAllBytes(temp.Path + ".backup-20260718-030405006"));
        var changed = new JsonCleaner().Parse(File.ReadAllBytes(temp.Path));
        var inputs = changed["MixerConfiguration"]!["InputSettings"]!;
        Assert.True(JsonNode.DeepEquals(inputs["old"]!["AudioPluginConfigurations"], inputs["new"]!["AudioPluginConfigurations"]));
    }

    private static CleanerApplication App(FakeFiles files, FakeProcesses processes, FakeActivator? activator = null) =>
        new(new FakeDiscovery(), files, processes, activator ?? new(), new StringReader("y\n"), new StringWriter(), () => new(2026, 1, 2, 3, 4, 5, 6));
    private static byte[] Json(bool hidden) => Encoding.UTF8.GetBytes(
        "{\"MixerConfiguration\":{\"InputSettings\":{\"one\":{\"IsHiddenFromMixes\":VALUE}}}}"
            .Replace("VALUE", hidden ? "true" : "false"));
    private static byte[] EffectJson() => Encoding.UTF8.GetBytes("""
        {"MixerConfiguration":{"InputSettings":{
          "old":{"InputName":"Main Mic","DeviceSettings":{"DeviceName":"Unavailable Mic","DeviceId":"old-device"},"IsHiddenFromMixes":false,
            "AudioPluginConfigurations":[{"Name":"EQ","ParameterState":{"gain":7}},{"Name":"Gate","ParameterState":[1,2]}],
            "AudioAppConfigurations":[{"Id":"old-app"}],"MasterVolume":0.2},
          "new":{"InputName":"Main Mic New","DeviceSettings":{"DeviceName":"Working Mic","DeviceId":"new-device"},"IsHiddenFromMixes":false,
            "AudioPluginConfigurations":[{"Name":"Marker"}],
            "AudioAppConfigurations":[{"Id":"new-app"}],"MasterVolume":0.8}
        }}}
        """);

    private sealed class FakeDiscovery : ISettingsDiscovery { public SettingsLocation Discover(string? _) => new("C:\\pkg\\LocalState\\Settings.json", "Elgato.WaveLink_test"); }
    private sealed class FakeFiles(params byte[][] reads) : IFileOperations
    {
        private readonly Queue<byte[]> reads = new(reads); public List<string> Writes { get; } = []; public List<string> Replacements { get; } = []; public bool ThrowOnWrite; public byte[]? LastWritten;
        public Func<bool>? ReadBlockedWhile;
        public byte[] ReadAllBytes(string path)
        {
            if (ReadBlockedWhile?.Invoke() == true) throw new IOException("settings file is locked");
            return reads.Count > 1 ? reads.Dequeue() : reads.Peek();
        }
        public void WriteAllBytes(string path, byte[] bytes) { if (ThrowOnWrite) throw new IOException("test"); Writes.Add(path); LastWritten = bytes; }
        public IEnumerable<string> EnumerateFiles(string directory, string pattern) => [];
        public void Replace(string source, string destination, string backup) => Replacements.Add(backup);
        public void Delete(string path) { }
    }
    private sealed class FakeProcesses(FakeProcess? process = null) : IProcessControl { public int FindCalls; public IWaveLinkProcess? FindGuiProcess() { FindCalls++; return process; } }
    private sealed class FakeProcess : IWaveLinkProcess
    {
        public bool Running; public bool Graceful; public int CloseCalls; public int KillCalls; public bool IsRunning => Running;
        public bool CloseGracefully(TimeSpan _) { CloseCalls++; if (Graceful) Running = false; return Graceful; }
        public void KillTree() { KillCalls++; Running = false; }
    }
    private sealed class FakeActivator : IAppActivator { public int Calls; public void Activate(string _) => Calls++; }

    private sealed class TempSettings : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wlsu-test-" + Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public byte[] Original { get; }

        public TempSettings(byte[] original)
        {
            Original = original;
            var state = System.IO.Path.Combine(root, "LocalState");
            Directory.CreateDirectory(state);
            Path = System.IO.Path.Combine(state, "Settings.json");
            File.WriteAllBytes(Path, original);
        }

        public CleanerApplication App(FakeProcesses processes, FakeActivator activator, TextReader input,
            Func<DateTime> clock, TextWriter? output = null) =>
            new(new LocationDiscovery(Path), new FileOperations(), processes, activator, input,
                output ?? new StringWriter(), clock);

        public void Dispose() => Directory.Delete(root, true);
    }

    private sealed class LocationDiscovery(string path) : ISettingsDiscovery
    {
        public SettingsLocation Discover(string? _) => new(path, "Elgato.WaveLink_test");
    }
}
