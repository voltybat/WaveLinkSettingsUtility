using WaveLinkSettingsUtility;

namespace WaveLinkSettingsUtility.Tests;

public class EndpointReplacementMatcherTests
{
    private static readonly HardwareChannel Channel = new("old", "Microphone (Shure MV7+)", "Microphone", "old", false);

    [Fact]
    public void FindsExactNameAndExcludesCurrentId()
    {
        var matches = EndpointReplacementMatcher.Find(Channel,
            [new("old", "Microphone (Shure MV7+)"), new("new", "Microphone (Shure MV7+)")]);

        var match = Assert.Single(matches);
        Assert.Equal("new", match.Endpoint.Id);
        Assert.True(match.ExactNameMatch);
    }

    [Fact]
    public void MatchesWindowsNumericDevicePrefixConservatively()
    {
        var matches = EndpointReplacementMatcher.Find(Channel, [new("new", "Microphone (2- Shure MV7+)")]);

        var match = Assert.Single(matches);
        Assert.False(match.ExactNameMatch);
    }

    [Fact]
    public void RejectsMerelySimilarNames()
    {
        Assert.Empty(EndpointReplacementMatcher.Find(Channel,
            [new("camera", "Microphone (Camera)"), new("headphones", "Headphones (Shure MV7+)")]));
    }
}
