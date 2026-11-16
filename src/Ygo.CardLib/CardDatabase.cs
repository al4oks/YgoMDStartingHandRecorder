using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Ygo.CardLib;

public sealed class CardDatabase
{
    private sealed record Dto(
        [property: JsonPropertyName("cid")] int Cid,
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("md_name")] string? MdName,
        [property: JsonPropertyName("sc_name")] string? ScName,
        [property: JsonPropertyName("cn_name")] string? CnName,
        [property: JsonPropertyName("nwbbs_n")] string? NwbbsName,
        [property: JsonPropertyName("cnocg_n")] string? CnocgName,
        [property: JsonPropertyName("jp_name")] string? JpName,
        [property: JsonPropertyName("en_name")] string? EnName);

    private readonly List<Card> _cards = new();
    private readonly (string Norm, string Key, int Card, int Source)[] _aliases;
    private readonly Dictionary<string, List<int>> _exact = new();
    private readonly Dictionary<string, List<int>> _keyExact = new();
    private readonly Dictionary<int, int> _byCid = new();

    public IReadOnlyList<Card> Cards => _cards;

    private CardDatabase(List<Card> cards)
    {
        _cards = cards;
        for (var i = 0; i < cards.Count; i++) _byCid[cards[i].Cid] = i;
        var list = new List<(string, string, int, int)>();
        for (var i = 0; i < cards.Count; i++)
        {
            var source = 0;
            foreach (var alias in cards[i].Aliases())
            {
                var norm = Normalize(alias);
                if (norm.Length == 0) continue;
                var key = KeyOf(alias);
                list.Add((norm, key, i, source));
                if (!_exact.TryGetValue(norm, out var bucket))
                    _exact[norm] = bucket = new List<int>();
                if (!bucket.Contains(i)) bucket.Add(i);
                if (key.Length > 0)
                {
                    if (!_keyExact.TryGetValue(key, out var kb))
                        _keyExact[key] = kb = new List<int>();
                    if (!kb.Contains(i)) kb.Add(i);
                }
                source++;
            }
        }
        _aliases = list.ToArray();
    }

    public static CardDatabase Load(string cardsJsonPath) => Load(File.OpenRead(cardsJsonPath));

    public static CardDatabase Empty() => Load(new MemoryStream("{}"u8.ToArray()));

    public static CardDatabase Load(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var dto = JsonSerializer.Deserialize<Dictionary<string, Dto>>(
            reader.ReadToEnd()) ?? throw new InvalidDataException("cards.json empty");
        var cards = dto.Values
            .Select(d => new Card(d.Cid, d.Id, d.MdName, d.ScName, d.CnName, d.NwbbsName, d.CnocgName, d.JpName, d.EnName))
            .OrderBy(c => c.Cid)
            .ToList();
        return new CardDatabase(cards);
    }

    // Shared contract with tools/check_labels.py: NFKC, unify dashes/quotes, drop whitespace, lowercase.
    public static string Normalize(string s)
    {
        var t = s.Normalize(NormalizationForm.FormKC);
        t = t.Replace('—', '-').Replace('–', '-').Replace('−', '-');
        t = t.Replace('‘', '\'').Replace('’', '\'').Replace('“', '"').Replace('”', '"');
        t = t.Replace('・', '·').Replace('•', '·');
        t = Regex.Replace(t, @"\s+", "");
        return t.ToLowerInvariant();
    }

    public bool TryExact(string name, out Card card)
    {
        if (_exact.TryGetValue(Normalize(name), out var bucket) && bucket.Count > 0)
        {
            card = _cards[bucket[0]];
            return true;
        }
        card = null!;
        return false;
    }

    // Punctuation-insensitive key for OCR matching: letters/digits/CJK only.
    public static string KeyOf(string s) =>
        new string(Normalize(s).Where(char.IsLetterOrDigit).ToArray());

    public bool TryExactKey(string name, out Card card)
    {
        if (_keyExact.TryGetValue(KeyOf(name), out var bucket) && bucket.Count > 0)
        {
            card = _cards[bucket[0]];
            return true;
        }
        card = null!;
        return false;
    }

    public bool TryByCid(int cid, out Card card)
    {
        if (_byCid.TryGetValue(cid, out var idx))
        {
            card = _cards[idx];
            return true;
        }
        card = null!;
        return false;
    }

    public bool TryFuzzy(string raw, double minSimilarity, out Card card, out double similarity)
    {
        var top = FuzzyTop(raw, minSimilarity, 1);
        if (top.Count == 0)
        {
            card = null!;
            similarity = 0;
            return false;
        }
        card = top[0].Card;
        similarity = top[0].Sim;
        return true;
    }

    public IReadOnlyList<(Card Card, double Sim)> FuzzyTop(string raw, double minSimilarity, int limit)
    {
        var q = KeyOf(raw);
        if (q.Length == 0) return Array.Empty<(Card, double)>();
        var best = new Dictionary<int, double>();
        foreach (var (_, key, idx, _) in _aliases)
        {
            if (key.Length == 0 || Math.Abs(key.Length - q.Length) > 4) continue;
            var s = Similarity(q, key);
            if (s < minSimilarity) continue;
            if (!best.TryGetValue(idx, out var prev) || s > prev) best[idx] = s;
        }
        return best.OrderByDescending(kv => kv.Value)
            .Take(limit)
            .Select(kv => (_cards[kv.Key], kv.Value))
            .ToList();
    }

    private static double Similarity(string a, string b)
    {
        if (a == b) return 1;
        var max = Math.Max(a.Length, b.Length);
        if (max == 0) return 1;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return 1.0 - (double)prev[b.Length] / max;
    }

    public IReadOnlyList<Card> Search(string query, int limit = 20)
    {
        var q = Normalize(query);
        if (q.Length == 0) return Array.Empty<Card>();
        var best = new Dictionary<int, int>();
        foreach (var (norm, _, card, source) in _aliases)
        {
            int kind = norm == q ? 0 : norm.StartsWith(q, StringComparison.Ordinal) ? 1 : norm.Contains(q, StringComparison.Ordinal) ? 2 : -1;
            if (kind < 0) continue;
            var score = kind * 10 + source;
            if (!best.TryGetValue(card, out var prev) || score < prev) best[card] = score;
        }
        return best.OrderBy(kv => kv.Value)
            .ThenBy(kv => _cards[kv.Key].DisplayName.Length)
            .Take(limit)
            .Select(kv => _cards[kv.Key])
            .ToList();
    }
}