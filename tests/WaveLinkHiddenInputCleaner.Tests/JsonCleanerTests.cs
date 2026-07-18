using System.Text;
using WaveLinkHiddenInputCleaner;

namespace WaveLinkHiddenInputCleaner.Tests;

public class JsonCleanerTests
{
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
}
