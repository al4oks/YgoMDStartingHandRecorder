using Ygo.CardLib;
using Ygo.Recognition;

var root = args.Length > 0 ? args[0] : DataRoot.Resolve() ?? Directory.GetCurrentDirectory();
var dirs = args.Length > 1 ? args[1..] : new[]
{
    Path.Combine(root, "samples", "own"),
    Path.Combine(root, "samples", "web"),
};
var cfg = HandConfig.Load(Path.Combine(root, "data", "config.json"));
var db = CardDatabase.Load(Path.Combine(root, "data", "cards.json"));
var codec = new LabelCodec(cfg);
using var rec = new HandRecognizer(db, cfg);
using (var warm = new Bitmap(64, 64)) rec.Recognize(warm);

foreach (var dir in dirs)
{
    if (!Directory.Exists(dir)) { Console.WriteLine($"== {dir} (missing)"); continue; }
    Console.WriteLine($"== {dir}");
    int hands = 0, handsOk = 0, known = 0, knownOk = 0;
    var times = new List<double>();
    foreach (var path in Directory.EnumerateFiles(dir)
                 .Where(p => new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(p).ToLowerInvariant()))
                 .OrderBy(p => p, StringComparer.Ordinal))
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        if (!codec.TryParse(stem, out _, out var names)) continue;
        using var ms = new MemoryStream(File.ReadAllBytes(path));
        using var tmp = Image.FromStream(ms);
        using var bmp = new Bitmap(tmp);
        var hr = rec.Recognize(bmp);
        if (Environment.GetEnvironmentVariable("YGO_DUMP") != null)
        {
            Console.WriteLine($"    trace: {hr.Trace}");
            foreach (var l in hr.Lines)
                Console.WriteLine($"    line cx={l.Cx,6:0} cy={l.Cy,6:0} w={l.W,5:0} h={l.H,5:0} ang={l.Angle,6:0} score={l.Score:0.00} {l.Text}");
        }
        var cropDir = Environment.GetEnvironmentVariable("YGO_CROP");
        if (cropDir != null)
        {
            var r = cfg.HandRoi;
            var rect = new Rectangle((int)(r.X * bmp.Width), (int)(r.Y * bmp.Height), (int)(r.W * bmp.Width), (int)(r.H * bmp.Height));
            using var crop = bmp.Clone(rect, bmp.PixelFormat);
            using var up = new Bitmap(crop.Width * cfg.Upscale, crop.Height * cfg.Upscale);
            using (var g = Graphics.FromImage(up))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(crop, 0, 0, up.Width, up.Height);
            }
            up.Save(Path.Combine(cropDir, Path.GetFileNameWithoutExtension(path) + "_crop.png"));
        }
        times.Add(hr.Elapsed.TotalMilliseconds);
        hands++;
        var allOk = true;
        var marks = new string[cfg.Slots];
        for (var i = 0; i < cfg.Slots; i++)
        {
            var got = hr.Slots[i]?.Card?.DisplayName;
            var ok = names[i] == null
                ? got == null
                : got != null && CardDatabase.Normalize(got) == CardDatabase.Normalize(names[i]!);
            if (names[i] != null) { known++; if (ok) knownOk++; }
            if (!ok) allOk = false;
            marks[i] = ok ? "O" : "X";
            if (!ok)
                Console.WriteLine($"  slot{i + 1} expect={names[i] ?? "?"} got={got ?? "-"} raw={hr.Slots[i]?.RawText ?? "-"} sim={hr.Slots[i]?.MatchSim.ToString("0.00") ?? "-"}");
        }
        if (allOk) handsOk++;
        Console.WriteLine($"{string.Join("", marks)} {hr.Elapsed.TotalMilliseconds,6:0}ms {Path.GetFileName(path)}");
    }
    var avg = times.Count > 0 ? times.Average() : 0;
    var max = times.Count > 0 ? times.Max() : 0;
    Console.WriteLine($"-- whole-hand {handsOk}/{hands} = {(hands > 0 ? 100.0 * handsOk / hands : 0):0.0}%  per-card {knownOk}/{known} = {(known > 0 ? 100.0 * knownOk / known : 0):0.0}%  avg {avg:0}ms max {max:0}ms");
}