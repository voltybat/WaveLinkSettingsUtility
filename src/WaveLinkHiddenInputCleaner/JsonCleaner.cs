using System.Text.Json;
using System.Text.Json.Nodes;

namespace WaveLinkHiddenInputCleaner;

public sealed class SettingsFormatException(string message, Exception? inner = null) : Exception(message, inner);

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

    public byte[] Serialize(JsonNode root) => JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });

    private static JsonObject GetInputs(JsonNode root)
    {
        if (root is not JsonObject obj || obj["MixerConfiguration"] is not JsonObject mixer ||
            mixer["InputSettings"] is not JsonObject inputs)
            throw new SettingsFormatException("Expected MixerConfiguration.InputSettings to be a JSON object.");
        return inputs;
    }

    private static bool IsHidden(JsonNode? node) => node is JsonObject entry &&
        entry.TryGetPropertyValue("IsHiddenFromMixes", out var value) && value is JsonValue scalar &&
        scalar.TryGetValue<bool>(out var hidden) && hidden;
}
