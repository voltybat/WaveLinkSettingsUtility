using System.Text;
using System.Text.Json.Nodes;
using WaveLinkSettingsUtility;

namespace WaveLinkSettingsUtility.Tests;

public class JsonCleanerTests
{
    [Fact]
    public void RelinkChangesIdentityAndStructuredReferencesButPreservesChannelSettings()
    {
        var cleaner = new JsonCleaner();
        var root = cleaner.Parse("""{"Order":["old"],"Icons":{"old":"icon"},"MixerConfiguration":{"InputSettings":{"old":{"InputName":"Mic","DeviceSettings":{"DeviceId":"old","DeviceType":"HardwareInputDevice"},"AudioPluginConfigurations":[{"Id":"fx"}]}},"AudioPluginHostSettings":{"AutoScalePluginWindowState":{"old|fx":"Default"}}}}"""u8);

        var result = cleaner.RelinkHardwareChannel(root, "old", "new");

        Assert.True(result.UpdatedReferences >= 3);
        Assert.Null(root["MixerConfiguration"]!["InputSettings"]!["old"]);
        Assert.Equal("new", root["MixerConfiguration"]!["InputSettings"]!["new"]!["DeviceSettings"]!["DeviceId"]!.GetValue<string>());
        Assert.Equal("fx", root["MixerConfiguration"]!["InputSettings"]!["new"]!["AudioPluginConfigurations"]![0]!["Id"]!.GetValue<string>());
        Assert.Equal("new", root["Order"]![0]!.GetValue<string>());
        Assert.Equal("Default", root["MixerConfiguration"]!["AudioPluginHostSettings"]!["AutoScalePluginWindowState"]!["new|fx"]!.GetValue<string>());
    }

    [Fact]
    public void RelinkRejectsExistingReplacementChannel()
    {
        var cleaner = new JsonCleaner();
        var root = cleaner.Parse("""{"MixerConfiguration":{"InputSettings":{"old":{"DeviceSettings":{"DeviceId":"old","DeviceType":"HardwareInputDevice"}},"new":{}}}}"""u8);
        Assert.Throws<SettingsFormatException>(() => cleaner.RelinkHardwareChannel(root, "old", "new"));
    }

    [Fact]
    public void RelinkCollapsesIdenticalHistoricalReferenceButRejectsConflict()
    {
        var cleaner = new JsonCleaner();
        var identical = cleaner.Parse("""{"Icons":{"old":"icon","new":"icon"},"MixerConfiguration":{"InputSettings":{"channel":{"DeviceSettings":{"DeviceId":"old","DeviceType":"HardwareInputDevice"}}}}}"""u8);
        cleaner.RelinkHardwareChannel(identical, "channel", "new");
        Assert.Null(identical["Icons"]!["old"]);

        var conflict = cleaner.Parse("""{"Icons":{"old":"old-icon","new":"new-icon"},"MixerConfiguration":{"InputSettings":{"channel":{"DeviceSettings":{"DeviceId":"old","DeviceType":"HardwareInputDevice"}}}}}"""u8);
        Assert.Throws<SettingsFormatException>(() => cleaner.RelinkHardwareChannel(conflict, "channel", "new"));
    }
    private readonly JsonCleaner cleaner = new();

    [Fact]
    public void RemovesOnlyDirectEntriesWithBooleanTrue()
    {
        var root = cleaner.Parse(Encoding.UTF8.GetBytes("""
        {"MixerConfiguration":{"InputSettings":{
          "remove1":{"IsHiddenFromMixes":true,"keep":"x"},
          "remove2":{"IsHiddenFromMixes":true},
          "false":{"IsHiddenFromMixes":false},
          "string":{"IsHiddenFromMixes":"true"},
          "number":{"IsHiddenFromMixes":1},
          "null":{"IsHiddenFromMixes":null},
          "nested":{"child":{"IsHiddenFromMixes":true}},
          "similar":{"IsHiddenFromMixesExtra":true}
        }},"unrelated":{"value":42}}
        """));

        Assert.Equal(["remove1", "remove2"], cleaner.Find(root));
        Assert.Equal(2, cleaner.Remove(root));
        Assert.Equal(["false", "string", "number", "null", "nested", "similar"],
            root["MixerConfiguration"]!["InputSettings"]!.AsObject().Select(x => x.Key));
        Assert.Equal(42, root["unrelated"]!["value"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"MixerConfiguration\":{}}")]
    public void MissingStructureFails(string value) => Assert.Throws<SettingsFormatException>(() => cleaner.Find(cleaner.Parse(Encoding.UTF8.GetBytes(value))));

    [Fact]
    public void MalformedJsonFails() => Assert.Throws<SettingsFormatException>(() => cleaner.Parse("{"u8));

    [Fact]
    public void UnhideRetainsEntriesAndChangesOnlyExactMatches()
    {
        var root = cleaner.Parse("""{"MixerConfiguration":{"InputSettings":{"yes":{"IsHiddenFromMixes":true,"name":"kept"},"no":{"IsHiddenFromMixes":false},"text":{"IsHiddenFromMixes":"true"}}}}"""u8);
        Assert.Equal(1, cleaner.Unhide(root));
        var inputs = root["MixerConfiguration"]!["InputSettings"]!.AsObject();
        Assert.Equal(["yes", "no", "text"], inputs.Select(x => x.Key));
        Assert.False(inputs["yes"]!["IsHiddenFromMixes"]!.GetValue<bool>());
        Assert.Equal("kept", inputs["yes"]!["name"]!.GetValue<string>());
        Assert.Equal("true", inputs["text"]!["IsHiddenFromMixes"]!.GetValue<string>());
    }

    [Fact]
    public void TransferEffectsReplacesOnlyTargetEffectChain()
    {
        var root = cleaner.Parse("""
        {"MixerConfiguration":{"InputSettings":{
          "old":{"InputName":"Main Mic","DeviceSettings":{"DeviceName":"Old Device","DeviceId":"old-id"},"IsHiddenFromMixes":true,
            "AudioPluginConfigurations":[{"Name":"EQ","ParameterState":{"gain":7}},{"Name":"Gate","ParameterState":[1,2]}],
            "AudioAppConfigurations":[{"Id":"old-app"}],"MasterVolume":0.25},
          "new":{"InputName":"Main Mic 2","DeviceSettings":{"DeviceName":"New Device","DeviceId":"new-id"},"IsHiddenFromMixes":false,
            "AudioPluginConfigurations":[{"Name":"Marker"}],
            "AudioAppConfigurations":[{"Id":"new-app"}],"MasterVolume":0.75}
        }}}
        """u8);
        var inputs = root["MixerConfiguration"]!["InputSettings"]!.AsObject();
        var sourceBefore = inputs["old"]!.DeepClone();
        var targetAppsBefore = inputs["new"]!["AudioAppConfigurations"]!.DeepClone();
        var targetDeviceBefore = inputs["new"]!["DeviceSettings"]!.DeepClone();

        var channels = cleaner.GetEffectChannels(root);
        Assert.Equal(2, channels.Count);
        Assert.Equal(["EQ", "Gate"], channels[0].EffectNames);
        Assert.True(channels[0].IsHidden);

        var result = cleaner.TransferEffects(root, "old", "new");

        Assert.Equal(new EffectTransferResult(2, 1), result);
        Assert.True(JsonNode.DeepEquals(sourceBefore, inputs["old"]));
        Assert.True(JsonNode.DeepEquals(inputs["old"]!["AudioPluginConfigurations"], inputs["new"]!["AudioPluginConfigurations"]));
        Assert.False(ReferenceEquals(inputs["old"]!["AudioPluginConfigurations"], inputs["new"]!["AudioPluginConfigurations"]));
        Assert.True(JsonNode.DeepEquals(targetAppsBefore, inputs["new"]!["AudioAppConfigurations"]));
        Assert.True(JsonNode.DeepEquals(targetDeviceBefore, inputs["new"]!["DeviceSettings"]));
        Assert.Equal(0.75, inputs["new"]!["MasterVolume"]!.GetValue<double>());
    }

    [Theory]
    [InlineData("{\"MixerConfiguration\":{\"InputSettings\":{\"one\":{}}}}")]
    [InlineData("{\"MixerConfiguration\":{\"InputSettings\":{\"one\":{\"AudioPluginConfigurations\":{}}}}}")]
    public void EffectChannelDiscoveryRejectsMissingOrMalformedEffectArrays(string value) =>
        Assert.Throws<SettingsFormatException>(() => cleaner.GetEffectChannels(cleaner.Parse(Encoding.UTF8.GetBytes(value))));

    [Fact]
    public void TransferRejectsSameChannelAndEmptySource()
    {
        var root = cleaner.Parse("""{"MixerConfiguration":{"InputSettings":{"empty":{"AudioPluginConfigurations":[]},"target":{"AudioPluginConfigurations":[]}}}}"""u8);
        Assert.Throws<SettingsFormatException>(() => cleaner.TransferEffects(root, "empty", "empty"));
        Assert.Throws<SettingsFormatException>(() => cleaner.TransferEffects(root, "empty", "target"));
    }
}
