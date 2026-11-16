namespace Ygo.CardLib;

public sealed class LabelCodec
{
    private static readonly Dictionary<char, char> IllegalToFull = new()
    {
        ['\\'] = '＼', ['/'] = '／', [':'] = '：', ['*'] = '＊', ['?'] = '？',
        ['"'] = '＂', ['<'] = '＜', ['>'] = '＞', ['|'] = '｜',
    };

    private readonly HandConfig _cfg;

    public LabelCodec(HandConfig cfg) => _cfg = cfg;

    public static string Sanitize(string name)
    {
        var chars = name.Trim().Select(c => IllegalToFull.TryGetValue(c, out var f) ? f : c).ToArray();
        return new string(chars);
    }

    public bool IsUnknown(string? name) =>
        string.IsNullOrWhiteSpace(name) || name == "?" || name == "？";

    public string Encode(string stem, IReadOnlyList<string?> names)
    {
        if (names.Count != _cfg.Slots)
            throw new ArgumentException($"expected {_cfg.Slots} slots, got {names.Count}");
        var parts = names.Select(n => IsUnknown(n) ? Sanitize(_cfg.UnknownSlot) : Sanitize(n!));
        return stem + _cfg.LabelSeparator + string.Join(_cfg.NameSeparator, parts);
    }

    public bool IsSkipped(string fileNameWithoutExt) =>
        fileNameWithoutExt.StartsWith(_cfg.SkipPrefix, StringComparison.Ordinal);

    public string SkipPrefix => _cfg.SkipPrefix;

    public string StripLabel(string fileNameWithoutExt)
    {
        var s = fileNameWithoutExt;
        if (IsSkipped(s)) s = s[_cfg.SkipPrefix.Length..];
        var idx = s.LastIndexOf(_cfg.LabelSeparator, StringComparison.Ordinal);
        return idx > 0 ? s[..idx] : s;
    }

    public bool TryParse(string fileNameWithoutExt, out string stem, out string?[] names)
    {
        stem = "";
        names = Array.Empty<string?>();
        if (IsSkipped(fileNameWithoutExt)) return false;
        var idx = fileNameWithoutExt.LastIndexOf(_cfg.LabelSeparator, StringComparison.Ordinal);
        if (idx <= 0) return false;
        var parts = fileNameWithoutExt[(idx + _cfg.LabelSeparator.Length)..]
            .Split(_cfg.NameSeparator);
        if (parts.Length != _cfg.Slots) return false;
        stem = fileNameWithoutExt[..idx];
        names = parts.Select(p => IsUnknown(p) ? null : p).ToArray();
        return true;
    }
}