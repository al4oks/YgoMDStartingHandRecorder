using System.Text.RegularExpressions;

namespace Ygo.CardLib;

public sealed record DeckEntry(string Name, int Count);

public sealed class DeckParseResult
{
    public string Name { get; set; } = "";
    public List<DeckEntry> Main { get; } = new();
    public List<DeckEntry> Extra { get; } = new();
    public List<DeckEntry> Side { get; } = new();
}

// Port of build/edgeprobe/parse_deck.py: Konami db member_deck page clipboard text,
// Gallery (repeated name rows) and List (index/name/count triples) views.
public static class DeckTextParser
{
    private static readonly string[] GallerySections = { "Main Deck", "Extra Deck", "Side Deck" };
    private static readonly string[] ListHeads = { "Monster Cards", "Spell Cards", "Trap Cards", "Extra Deck", "Side Deck" };

    public static DeckParseResult Parse(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).ToArray();
        var result = new DeckParseResult();
        foreach (var l in lines)
        {
            if (!l.StartsWith("【") || !l.Contains("】")) continue;
            result.Name = l[(l.IndexOf("】") + 1)..].Trim();
            break;
        }
        if (lines.Contains("Monster Cards")) ParseList(lines, result);
        else ParseGallery(lines, result);
        return result;
    }

    private static List<DeckEntry> Target(DeckParseResult r, string section) => section switch
    {
        "Main Deck" => r.Main,
        "Extra Deck" => r.Extra,
        _ => r.Side,
    };

    private static void AddCounts(List<DeckEntry> list, string name, int n)
    {
        var idx = list.FindIndex(e => e.Name == name);
        if (idx >= 0) list[idx] = list[idx] with { Count = list[idx].Count + n };
        else list.Add(new DeckEntry(name, n));
    }

    private static void ParseGallery(string[] lines, DeckParseResult r)
    {
        foreach (var sec in GallerySections)
        {
            var i = Array.LastIndexOf(lines, sec);
            if (i < 0 || i + 1 >= lines.Length) throw new FormatException($"缺少分区 {sec}");
            var n = int.Parse(lines[i + 1]);
            var names = lines.Skip(i + 2).Take(n).Where(l => l.Length > 0).ToList();
            if (names.Count != n) throw new FormatException($"{sec}: 期望 {n} 行，实际 {names.Count}");
            foreach (var name in names) AddCounts(Target(r, sec), name, 1);
        }
    }

    private static void ParseList(string[] lines, DeckParseResult r)
    {
        var groups = new Dictionary<string, List<DeckEntry>>();
        var i = 0;
        while (i < lines.Length)
        {
            var head = ListHeads.FirstOrDefault(h => lines[i] == h);
            if (head == null) { i++; continue; }
            if (i + 1 >= lines.Length) throw new FormatException($"{head}: 缺少数量行");
            var total = int.Parse(lines[i + 1]);
            i += 2;
            var cnt = new List<DeckEntry>();
            while (i < lines.Length && !ListHeads.Contains(lines[i]) && lines[i] != "Delete")
            {
                if (Regex.IsMatch(lines[i], @"^\d+$"))
                {
                    var nxt = i + 1 < lines.Length ? lines[i + 1] : "";
                    if (nxt.Length > 0 && !Regex.IsMatch(nxt, @"^\d+$") && !ListHeads.Contains(nxt) && nxt != "Delete" && i + 2 < lines.Length)
                    {
                        AddCounts(cnt, nxt, int.Parse(lines[i + 2]));
                        i += 3;
                        continue;
                    }
                }
                i++;
            }
            if (cnt.Sum(e => e.Count) != total) throw new FormatException($"{head}: 期望 {total}，实际 {cnt.Sum(e => e.Count)}");
            groups[head] = cnt;
        }
        foreach (var h in ListHeads)
            if (!groups.ContainsKey(h)) throw new FormatException($"缺少分区 {h}");
        foreach (var h in new[] { "Monster Cards", "Spell Cards", "Trap Cards" })
            foreach (var e in groups[h]) AddCounts(r.Main, e.Name, e.Count);
        foreach (var e in groups["Extra Deck"]) AddCounts(r.Extra, e.Name, e.Count);
        foreach (var e in groups["Side Deck"]) AddCounts(r.Side, e.Name, e.Count);
    }
}