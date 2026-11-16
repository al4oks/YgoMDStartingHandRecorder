using Ygo.CardLib;

var root = DataRoot.Resolve() ?? throw new Exception("repo root with data/cards.json not found");
var cfg = HandConfig.Load(Path.Combine(root, "data", "config.json"));
var db = CardDatabase.Load(Path.Combine(root, "data", "cards.json"));
Check(db.Cards.Count > 14000, "card count > 14000");
Check(db.TryExact("青眼白龙", out var blue), "exact md_name lookup");
Check(db.TryExact("蓝眼白龙", out var blue2) && blue2.Cid == blue.Cid, "cnocg alias maps to same card");
Check(db.Search("青眼").Any(c => c.Cid == blue.Cid), "prefix search hits");
Check(db.Search("被封印的右足").Count > 0, "exodia piece search hits");

var codec = new LabelCodec(cfg);
string?[] raw = { "A/B:C*D?E\"F<G>H|I", "青眼白龙", null, "被封印的右足", "次元的裂缝" };
var encoded = codec.Encode("f_000001", raw);
Check(!encoded.Any(c => "\\/:*?\"<>|".Contains(c)), "encoded filename has no illegal chars");
Check(codec.TryParse(encoded, out var stem, out var parsed), "encode/parse roundtrip");
Check(stem == "f_000001", "stem preserved");
Check(parsed[2] == null, "unknown slot roundtrip");
Check(CardDatabase.Normalize(parsed[0]!) == CardDatabase.Normalize(raw[0]!), "illegal chars recover via NFKC");
Check(codec.StripLabel(encoded) == "f_000001", "strip label");
Check(codec.IsSkipped("SKIP_f_000002"), "skip prefix detect");
Check(codec.StripLabel("SKIP_f_000002__a+b+c+d+e") == "f_000002", "strip skip + label");

var tmp = Path.Combine(Path.GetTempPath(), "ygohand-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tmp);
try
{
    var store = new LabelStore(codec);
    File.WriteAllBytes(Path.Combine(tmp, "f_000001.png"), new byte[] { 1 });
    File.WriteAllBytes(Path.Combine(tmp, "f_000002.png"), new byte[] { 1 });
    string?[] names = { "青眼白龙", "被封印的右足", null, "次元的裂缝", "被封印的艾克佐迪亚" };
    var n1 = store.ApplyLabels(tmp, "f_000001.png", names);
    Check(File.Exists(Path.Combine(tmp, n1)) && !File.Exists(Path.Combine(tmp, "f_000001.png")), "rename applied");
    Check(store.LoadIndex(tmp)[n1].Names![0] == "青眼白龙", "labels.json entry written");
    var n2 = store.ApplyLabels(tmp, n1, names);
    Check(n2 != n1 && !File.Exists(Path.Combine(tmp, n1)), "relabel strips old label, unique suffix");
    var s1 = store.Skip(tmp, "f_000002.png");
    Check(s1.StartsWith("SKIP_"), "skip rename");
    var s2 = store.ApplyLabels(tmp, s1, names);
    Check(!s2.StartsWith("SKIP_"), "relabel unskips");
    Check(store.LoadIndex(tmp).Count == 2, "index tracks moves");
}
finally
{
    Directory.Delete(tmp, true);
}

var fixture = Path.Combine(root, "tests", "fixtures", "contract");
if (Directory.Exists(fixture)) Directory.Delete(fixture, true);
Directory.CreateDirectory(fixture);
var fstore = new LabelStore(codec);
File.WriteAllBytes(Path.Combine(fixture, "f_000001.png"), new byte[] { 1 });
File.WriteAllBytes(Path.Combine(fixture, "f_000002.png"), new byte[] { 1 });
File.WriteAllBytes(Path.Combine(fixture, "f_000003.png"), new byte[] { 1 });
fstore.ApplyLabels(fixture, "f_000001.png", new string?[] { "青眼白龙", "被封印的右足", null, "次元的裂缝", "被封印的艾克佐迪亚" });
fstore.Skip(fixture, "f_000002.png");
Console.WriteLine("fixture written: " + fixture);
Console.WriteLine("SMOKE PASS");
return 0;

static void Check(bool ok, string what)
{
    if (!ok)
    {
        Console.WriteLine("FAIL: " + what);
        Environment.Exit(1);
    }
    Console.WriteLine("ok: " + what);
}