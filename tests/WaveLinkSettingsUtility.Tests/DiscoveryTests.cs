using WaveLinkSettingsUtility;

namespace WaveLinkSettingsUtility.Tests;

public class DiscoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "wlsu-tests-" + Guid.NewGuid().ToString("N"));
    [Fact] public void MissingFails() => Assert.Throws<InvalidOperationException>(() => new SettingsDiscovery(root).Discover(null));
    [Fact]
    public void FindsPackageAndValidatesOverride()
    {
        var path = Make("Elgato.WaveLink_abc"); var discovery = new SettingsDiscovery(root);
        Assert.Equal(path, discovery.Discover(null).SettingsPath);
        Assert.Throws<InvalidOperationException>(() => discovery.Discover(Path.Combine(root, "Settings.json")));
    }
    [Fact]
    public void AmbiguousFailsAndCanBeSelected()
    {
        var one = Make("Elgato.WaveLink_one"); Make("Elgato.WaveLink_two"); var discovery = new SettingsDiscovery(root);
        Assert.Throws<InvalidOperationException>(() => discovery.Discover(null)); Assert.Equal(one, discovery.Discover(one).SettingsPath);
    }
    private string Make(string package) { var dir = Path.Combine(root, "Packages", package, "LocalState"); Directory.CreateDirectory(dir); var path = Path.Combine(dir, "Settings.json"); File.WriteAllText(path, "{}"); return path; }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); GC.SuppressFinalize(this); }
}
