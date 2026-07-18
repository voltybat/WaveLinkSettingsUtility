using System.Text;
using WaveLinkHiddenInputCleaner;

namespace WaveLinkHiddenInputCleaner.Tests;

public class ApplicationTests
{
    [Fact]
    public void NoMatchDoesNotInspectProcessOrWrite()
    {
        var files = new FakeFiles(Json(false)); var processes = new FakeProcesses();
        var result = App(files, processes).Run(new(null, true, false, false));
        Assert.Equal(0, result); Assert.Equal(0, processes.FindCalls); Assert.Empty(files.Writes);
    }

    [Fact]
    public void DryRunDoesNotInspectProcessOrWrite()
    {
        var files = new FakeFiles(Json(true)); var processes = new FakeProcesses();
        var result = App(files, processes).Run(new(null, true, true, false));
        Assert.Equal(0, result); Assert.Equal(0, processes.FindCalls); Assert.Empty(files.Writes);
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
    public void TimeoutForcesTreeAndNoRestartSuppressesActivation()
    {
        var files = new FakeFiles(Json(true), Json(true));
        var process = new FakeProcess { Running = true, Graceful = false };
        var activator = new FakeActivator();
        var result = App(files, new FakeProcesses(process), activator).Run(new(null, true, false, true));
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
        var root = Path.Combine(Path.GetTempPath(), "wlhic-app-" + Guid.NewGuid().ToString("N"));
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
        Assert.Equal(0, app.Run(new(null, false, false, false, true)));
        Assert.Single(files.Writes);
    }

    private static CleanerApplication App(FakeFiles files, FakeProcesses processes, FakeActivator? activator = null) =>
        new(new FakeDiscovery(), files, processes, activator ?? new(), new StringReader("y\n"), new StringWriter(), () => new(2026, 1, 2, 3, 4, 5, 6));
    private static byte[] Json(bool hidden) => Encoding.UTF8.GetBytes(
        "{\"MixerConfiguration\":{\"InputSettings\":{\"one\":{\"IsHiddenFromMixes\":VALUE}}}}"
            .Replace("VALUE", hidden ? "true" : "false"));

    private sealed class FakeDiscovery : ISettingsDiscovery { public SettingsLocation Discover(string? _) => new("C:\\pkg\\LocalState\\Settings.json", "Elgato.WaveLink_test"); }
    private sealed class FakeFiles(params byte[][] reads) : IFileOperations
    {
        private readonly Queue<byte[]> reads = new(reads); public List<string> Writes { get; } = []; public List<string> Replacements { get; } = []; public bool ThrowOnWrite; public byte[]? LastWritten;
        public byte[] ReadAllBytes(string path) => reads.Count > 1 ? reads.Dequeue() : reads.Peek();
        public void WriteAllBytes(string path, byte[] bytes) { if (ThrowOnWrite) throw new IOException("test"); Writes.Add(path); LastWritten = bytes; }
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
}
