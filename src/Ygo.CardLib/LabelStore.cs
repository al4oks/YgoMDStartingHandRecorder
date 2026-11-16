using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ygo.CardLib;

public sealed record LabelEntry(
    [property: JsonPropertyName("names")] string?[]? Names,
    [property: JsonPropertyName("skipped")] bool Skipped);

public sealed class LabelStore
{
    private const string IndexFile = "labels.json";
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly LabelCodec _codec;

    public LabelStore(LabelCodec codec) => _codec = codec;

    public Dictionary<string, LabelEntry> LoadIndex(string dir)
    {
        var path = Path.Combine(dir, IndexFile);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, LabelEntry>>(File.ReadAllText(path)) ?? new()
            : new();
    }

    public string ApplyLabels(string dir, string fileName, IReadOnlyList<string?> names)
    {
        var stem = _codec.StripLabel(Path.GetFileNameWithoutExtension(fileName));
        var newName = Unique(dir, _codec.Encode(stem, names) + Path.GetExtension(fileName));
        File.Move(Path.Combine(dir, fileName), Path.Combine(dir, newName));
        UpdateIndex(dir, fileName, newName, new LabelEntry(names.ToArray(), false));
        return newName;
    }

    public string Skip(string dir, string fileName)
    {
        var stem = _codec.StripLabel(Path.GetFileNameWithoutExtension(fileName));
        var newName = Unique(dir, _codec.SkipPrefix + stem + Path.GetExtension(fileName));
        File.Move(Path.Combine(dir, fileName), Path.Combine(dir, newName));
        UpdateIndex(dir, fileName, newName, new LabelEntry(null, true));
        return newName;
    }

    private string Unique(string dir, string candidate)
    {
        if (!File.Exists(Path.Combine(dir, candidate))) return candidate;
        var stem = Path.GetFileNameWithoutExtension(candidate);
        var ext = Path.GetExtension(candidate);
        for (var i = 2; ; i++)
        {
            var next = $"{stem}_{i}{ext}";
            if (!File.Exists(Path.Combine(dir, next))) return next;
        }
    }

    private void UpdateIndex(string dir, string oldName, string newName, LabelEntry entry)
    {
        var index = LoadIndex(dir);
        index.Remove(oldName);
        index[newName] = entry;
        File.WriteAllText(Path.Combine(dir, IndexFile), JsonSerializer.Serialize(index, JsonOpts));
    }
}