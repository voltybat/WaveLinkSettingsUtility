using System.Text.Json;
using System.Text.Json.Nodes;

namespace WaveLinkHiddenInputCleaner;

public sealed class SettingsFormatException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record EffectChannel(string Key, string InputName, string DeviceName, bool IsHidden,
    IReadOnlyList<string> EffectNames)
{
    public int EffectCount => EffectNames.Count;
}

public sealed record EffectTransferResult(int SourceEffectCount, int ReplacedEffectCount);

public sealed class JsonCleaner
{
    public JsonNode Parse(ReadOnlySpan<byte> bytes)
    {
        try { return JsonNode.Parse(bytes) ?? throw new SettingsFormatException("Settings contain an empty JSON document."); }
        catch (JsonException ex) { throw new SettingsFormatException("Settings contain malformed JSON.", ex); }
    }

    public IReadOnlyList<string> Find(JsonNode root)
    {
        var inputs = GetInputs(root);
        var matches = new List<string>();
        foreach (var pair in inputs)
            if (IsHidden(pair.Value)) matches.Add(pair.Key);
        return matches;
    }

    public int Remove(JsonNode root)
    {
        var inputs = GetInputs(root);
        var keys = inputs.Where(p => IsHidden(p.Value)).Select(p => p.Key).ToArray();
        foreach (var key in keys) inputs.Remove(key);
        return keys.Length;
    }

    public int Unhide(JsonNode root)
    {
        var inputs = GetInputs(root);
        var entries = inputs.Where(p => IsHidden(p.Value)).Select(p => p.Value!.AsObject()).ToArray();
        foreach (var entry in entries) entry["IsHiddenFromMixes"] = false;
        return entries.Length;
    }

    public IReadOnlyList<EffectChannel> GetEffectChannels(JsonNode root)
    {
        var channels = new List<EffectChannel>();
        foreach (var pair in GetInputs(root))
        {
            if (pair.Value is not JsonObject entry)
                throw new SettingsFormatException($"InputSettings entry '{pair.Key}' must be a JSON object.");
            if (entry["AudioPluginConfigurations"] is not JsonArray effects)
                throw new SettingsFormatException($"InputSettings entry '{pair.Key}' must contain an AudioPluginConfigurations array.");

            channels.Add(new(pair.Key, ReadString(entry["InputName"], "Unnamed input"),
                ReadString(entry["DeviceSettings"]?["DeviceName"], "Unknown device"), IsHidden(entry),
                effects.Select((effect, index) => effect is JsonObject obj
                    ? ReadString(obj["Name"], $"Unnamed effect {index + 1}")
                    : $"Unnamed effect {index + 1}").ToArray()));
        }
        return channels;
    }

    public EffectTransferResult TransferEffects(JsonNode root, string sourceKey, string targetKey)
    {
        if (string.Equals(sourceKey, targetKey, StringComparison.Ordinal))
            throw new SettingsFormatException("Source and destination channels must be different.");

        var inputs = GetInputs(root);
        var source = GetEntry(inputs, sourceKey);
        var target = GetEntry(inputs, targetKey);
        var sourceEffects = GetEffects(source, sourceKey);
        var targetEffects = GetEffects(target, targetKey);
        if (sourceEffects.Count == 0)
            throw new SettingsFormatException("The selected source channel has no stored effects.");

        target["AudioPluginConfigurations"] = sourceEffects.DeepClone();
        return new(sourceEffects.Count, targetEffects.Count);
    }

    public byte[] Serialize(JsonNode root) => JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });

    public void Validate(JsonNode root) => _ = GetInputs(root);

    private static JsonObject GetInputs(JsonNode root)
    {
        if (root is not JsonObject obj || obj["MixerConfiguration"] is not JsonObject mixer ||
            mixer["InputSettings"] is not JsonObject inputs)
            throw new SettingsFormatException("Expected MixerConfiguration.InputSettings to be a JSON object.");
        return inputs;
    }

    private static JsonObject GetEntry(JsonObject inputs, string key) => inputs[key] as JsonObject ??
        throw new SettingsFormatException($"InputSettings entry '{key}' was not found or is not a JSON object.");

    private static JsonArray GetEffects(JsonObject entry, string key) => entry["AudioPluginConfigurations"] as JsonArray ??
        throw new SettingsFormatException($"InputSettings entry '{key}' must contain an AudioPluginConfigurations array.");

    private static string ReadString(JsonNode? node, string fallback) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : fallback;

    private static bool IsHidden(JsonNode? node) => node is JsonObject entry &&
        entry.TryGetPropertyValue("IsHiddenFromMixes", out var value) && value is JsonValue scalar &&
        scalar.TryGetValue<bool>(out var hidden) && hidden;
}
