using System.Diagnostics;
using OpenCvSharp;
using Sdcb.PaddleOCR;
using Ygo.CardLib;

namespace Ygo.Recognition;

public sealed record OcrLine(string Text, double Score, double Cx, double Cy, double W, double H, double Angle);

public sealed record Candidate(Card Card, double Sim);

public sealed record SlotResult(Card? Card, string RawText, double OcrScore, double MatchSim)
{
    public IReadOnlyList<Candidate> Candidates { get; init; } = Array.Empty<Candidate>();
    public double BannerPurity { get; init; } = -1;
}

public interface IAliasLookup
{
    bool TryLookup(string raw, out int cid);
    bool TryPeek(string raw, out int cid);
}

public sealed record HandResult(SlotResult?[] Slots, IReadOnlyList<OcrLine> Lines, TimeSpan Elapsed, string Trace);

public sealed class HandRecognizer : IDisposable
{
    private static readonly int UpOverride =
        int.TryParse(Environment.GetEnvironmentVariable("YGO_UP"), out var u) && u > 0 ? u : 0;

    private static readonly int RetryUp =
        int.TryParse(Environment.GetEnvironmentVariable("YGO_RETRYUP"), out var ru) && ru > 0 ? ru : 5;

    private static readonly int DetUp =
        int.TryParse(Environment.GetEnvironmentVariable("YGO_DETUP"), out var du) && du > 0 ? du : 2;

    private static readonly double DeckSimMin =
        double.TryParse(Environment.GetEnvironmentVariable("YGO_DECKSIM"), out var ds) && ds > 0 ? ds : 0.25;

    private static readonly double SuperPurity =
        double.TryParse(Environment.GetEnvironmentVariable("YGO_SUPERPUR"), out var sp) && sp > 0 ? sp : 0.55;

    private readonly OcrEngine _ocr = new();
    private readonly CardDatabase _db;
    private readonly HandConfig _cfg;
    private readonly IAliasLookup? _aliases;
    private readonly Dictionary<int, int> _used = new();

    public IReadOnlyDictionary<int, int>? DeckCounts { get; set; }

    public HandRecognizer(CardDatabase db, HandConfig cfg, IAliasLookup? aliases = null)
    {
        _db = db;
        _cfg = cfg;
        _aliases = aliases;
    }

    public HandResult Recognize(Bitmap full)
    {
        var sw = Stopwatch.StartNew();
        _used.Clear();
        var r = _cfg.HandRoi;
        var rect = new Rectangle((int)(r.X * full.Width), (int)(r.Y * full.Height),
            (int)(r.W * full.Width), (int)(r.H * full.Height));
        rect.Intersect(new Rectangle(0, 0, full.Width, full.Height));
        // detector only needs the name band (top ~55% of the hand ROI)
        var band = new Rectangle(rect.X, rect.Y, rect.Width, (int)(rect.Height * 0.55));
        using var crop = full.Clone(band, full.PixelFormat);
        using var mat1 = OcrEngine.ToMat(crop);
        var detUp = DetUp;
        var recUp = UpOverride > 0 ? UpOverride : 4;
        using var matDet = new Mat();
        Cv2.Resize(mat1, matDet, new OpenCvSharp.Size(mat1.Width * detUp, mat1.Height * detUp), 0, 0, InterpolationFlags.Cubic);
        var boxes = _ocr.Detect(matDet);
        using var matRec = new Mat();
        Cv2.Resize(mat1, matRec, new OpenCvSharp.Size(mat1.Width * recUp, mat1.Height * recUp), 0, 0, InterpolationFlags.Cubic);
        var scale = (double)recUp / detUp;
        var crops = new List<Mat>();
        var kept = new List<RotatedRect>();
        foreach (var b in boxes)
        {
            if (Math.Max(b.Size.Width, b.Size.Height) * scale < 20) continue;
            var rr = new RotatedRect(new Point2f((float)(b.Center.X * scale), (float)(b.Center.Y * scale)),
                new Size2f((float)(b.Size.Width * scale), (float)(b.Size.Height * scale)), b.Angle);
            crops.Add(_ocr.CropRotated(matRec, rr));
            kept.Add(rr);
        }
        var recs = crops.Count > 0 ? _ocr.RecognizeBatch(crops.ToArray()) : Array.Empty<PaddleOcrRecognizerResult>();
        var lines = new List<OcrLine>();
        for (var i = 0; i < kept.Count; i++)
        {
            var c = kept[i].Center;
            var s = kept[i].Size;
            lines.Add(new OcrLine(recs[i].Text, recs[i].Score, c.X / recUp, c.Y / recUp, s.Width / recUp, s.Height / recUp, kept[i].Angle));
        }
        foreach (var m in crops) m.Dispose();
        var trace = $"det={matDet.Width}x{matDet.Height} boxes={boxes.Length} rec={recUp}x ";
        var slots = BuildSlots(lines, band, mat1, ref trace, out var pitch, out var center);
        RetryWeakSlots(full, band, slots, pitch, center, ref trace);
        sw.Stop();
        return new HandResult(slots, lines, sw.Elapsed, trace);
    }

    private void RetryWeakSlots(Bitmap full, Rectangle band, SlotResult?[] slots, double pitch, double center, ref string trace)
    {
        var mid = (_cfg.Slots - 1) / 2;
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
        for (var i = 0; i < _cfg.Slots; i++)
        {
            var old = slots[i];
            if (old != null && old.Card != null) continue;
            var cx = center + (i - mid) * pitch;
            var x0 = (int)Math.Max(0, cx - pitch * 0.55);
            var x1 = (int)Math.Min(band.Width, cx + pitch * 0.55);
            if (x1 - x0 < 8) continue;
            var region = new Rectangle(band.X + x0, band.Y, x1 - x0, (int)(band.Height * 0.65));
            using var crop = full.Clone(region, full.PixelFormat);
            using var src = OcrEngine.ToMat(crop);
            var preEnv = Environment.GetEnvironmentVariable("YGO_RETRYPRE");
            var variants = preEnv != null ? new[] { preEnv } : new[] { "sharpen" };
            foreach (var variant in variants)
            {
                Mat morph = src;
                if (variant == "dilate") { morph = new Mat(); Cv2.Dilate(src, morph, kernel); }
                else if (variant == "erode") { morph = new Mat(); Cv2.Erode(src, morph, kernel); }
                else if (variant == "median") { morph = new Mat(); Cv2.MedianBlur(src, morph, 3); }
                else if (variant == "clahe")
                {
                    morph = new Mat();
                    using var lab = new Mat();
                    Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);
                    var ch = Cv2.Split(lab);
                    using var clahe = Cv2.CreateCLAHE(2.0, new OpenCvSharp.Size(8, 8));
                    clahe.Apply(ch[0], ch[0]);
                    using var merged = new Mat();
                    Cv2.Merge(ch, merged);
                    Cv2.CvtColor(merged, morph, ColorConversionCodes.Lab2BGR);
                    foreach (var c in ch) c.Dispose();
                }
                using var up = new Mat();
                Cv2.Resize(morph, up, new OpenCvSharp.Size(crop.Width * RetryUp, crop.Height * RetryUp), 0, 0, InterpolationFlags.Cubic);
                if (!ReferenceEquals(morph, src)) morph.Dispose();
                if (variant == "sharpen")
                {
                    using var blur = new Mat();
                    Cv2.GaussianBlur(up, blur, new OpenCvSharp.Size(0, 0), 3);
                    Cv2.AddWeighted(up, 1.5, blur, -0.5, 0, up);
                }
                var res = _ocr.Run(up);
                var cand = res.Regions
                    .Select(rg => new OcrLine(rg.Text, rg.Score, rg.Rect.Center.X / (double)RetryUp, rg.Rect.Center.Y / (double)RetryUp,
                        rg.Rect.Size.Width / (double)RetryUp, rg.Rect.Size.Height / (double)RetryUp, rg.Rect.Angle))
                    .Where(l => l.Score >= 0.4 && l.Text.Trim().Length >= 2)
                    .Where(l => l.Text.Any(c => c >= 0x4E00 && c <= 0x9FFF) || l.Text.Trim().Length >= 4)
                    .Where(l => !l.Text.Contains('【') && !l.Text.Contains("法卡") && !l.Text.Contains("陷阱"))
                    .OrderBy(l => l.Cy).ThenBy(l => l.Cx)
                    .ToList();
                if (cand.Count == 0)
                {
                    trace += $"retry{i}/{variant}:- ";
                    continue;
                }
                var nameLine = cand[0];
                var text = nameLine.Text;
                var right = nameLine.Cx + nameLine.W / 2;
                foreach (var l in cand.Skip(1).Where(l => l.W <= pitch * 0.25 && Math.Abs(l.Cy - nameLine.Cy) <= 10).OrderBy(l => l.Cx))
                {
                    if (l.Cx - l.W / 2 < right - 4 || l.Cx - l.W / 2 - right > pitch * 0.6) continue;
                    text += l.Text;
                    right = l.Cx + l.W / 2;
                }
                var (card, sim) = MatchName(text, BannerPurity(src));
                trace += $"retry{i}/{variant}:{text}={(card == null ? "-" : card.DisplayName)} ";
                var retryDir = Environment.GetEnvironmentVariable("YGO_RETRYCROP");
                if (retryDir != null)
                    Cv2.ImWrite(Path.Combine(retryDir, $"slot{i}_{variant}_{string.Join("_", text.Split(Path.GetInvalidFileNameChars()))}.png"), up);
                if (card == null) continue;
                var deckHit = sim < 0.6; // global match never returns <0.6; only deck-constrained fallback does
                if (!deckHit && sim < 0.99 && !(sim >= 0.8 && nameLine.Score >= 0.75)) continue;
                slots[i] = new SlotResult(card, text, nameLine.Score, sim)
                {
                    Candidates = sim < 0.90 ? CandidatesFor(text) : Array.Empty<Candidate>(),
                    BannerPurity = BannerPurity(src),
                };
                break;
            }
        }
    }

    private SlotResult?[] BuildSlots(List<OcrLine> lines, Rectangle rect, Mat bandMat, ref string trace, out double pitchOut, out double centerOut)
    {
        var slots = new SlotResult?[_cfg.Slots];
        var cand = lines
            .Where(l => l.Score >= 0.5 && l.Text.Trim().Length >= 2)
            .Where(l => l.Text.Any(c => c >= 0x4E00 && c <= 0x9FFF) || l.Text.Trim().Length >= 4)
            .Where(l => !l.Text.Contains('【') && !l.Text.Contains("法卡") && !l.Text.Contains("陷阱"))
            .Where(l => Math.Max(l.W, l.H) >= 35 && Math.Min(l.W, l.H) >= 10)
            .Where(l => l.Cy >= rect.Height * 0.04 && l.Cy <= rect.Height * 0.6)
            .OrderBy(l => l.Cx)
            .ToList();
        if (cand.Count == 0)
        {
            pitchOut = rect.Width / 7.0;
            centerOut = rect.Width / 2.0;
            return slots;
        }

        var gaps = cand.Zip(cand.Skip(1), (a, b) => b.Cx - a.Cx).Where(g => g > 1).ToList();
        var pitch = gaps.Count > 0 ? Median(gaps) : rect.Width / 7.0;
        if (pitch < rect.Width * 0.05) pitch = rect.Width / 7.0;
        var groups = Assign(cand, pitch, rect, ref trace, out var center);
        pitchOut = pitch;
        centerOut = center;
        for (var i = 0; i < _cfg.Slots; i++)
        {
            if (groups[i] == null) continue;
            var ordered = groups[i]!.OrderBy(l => l.Cy).ThenBy(l => l.Cx).ToList();
            var nameLine = ordered[0];
            // name suffixes (－“剑” etc.) often arrive as separate small regions right of the name
            var text = nameLine.Text;
            var right = nameLine.Cx + nameLine.W / 2;
            foreach (var l in lines.Where(l => l.Score >= 0.5 && !ReferenceEquals(l, nameLine))
                         .Where(l => l.W <= pitch * 0.25)
                         .Where(l => Math.Abs(l.Cy - nameLine.Cy) <= 10)
                         .Where(l => !l.Text.Contains('【') && !l.Text.Contains("法卡"))
                         .OrderBy(l => l.Cx))
            {
                if (l.Cx - l.W / 2 < right - 4 || l.Cx - l.W / 2 - right > pitch * 0.6) continue;
                text += l.Text;
                right = l.Cx + l.W / 2;
            }
            trace += $"[{text}->{i}] ";
            var purity = SlotPurity(bandMat, center + (i - (_cfg.Slots - 1) / 2) * pitch, pitch);
            var (card, sim) = MatchName(text, purity);
            slots[i] = new SlotResult(card, text, nameLine.Score, sim)
            {
                Candidates = card == null || sim < 0.90 ? CandidatesFor(text) : Array.Empty<Candidate>(),
                BannerPurity = purity,
            };
        }
        return slots;
    }

    private List<OcrLine>?[] Assign(List<OcrLine> cand, double pitch, Rectangle rect, ref string trace, out double bestCenter)
    {
        var mid = (_cfg.Slots - 1) / 2;
        var centers = new List<double> { (cand[0].Cx + cand[^1].Cx) / 2 };
        if (cand.Count < _cfg.Slots)
            for (var k = 0; k <= _cfg.Slots - cand.Count; k++)
                centers.Add(cand[0].Cx - (k - mid) * pitch);
        List<OcrLine>?[] best = new List<OcrLine>?[_cfg.Slots];
        var bestCost = double.MaxValue;
        bestCenter = centers[0];
        foreach (var center in centers)
        {
            var groups = new List<OcrLine>?[_cfg.Slots];
            var cost = 0.0;
            foreach (var l in cand)
            {
                var i = (int)Math.Round((l.Cx - center) / pitch, MidpointRounding.AwayFromZero) + mid;
                if (i < 0 || i >= _cfg.Slots) { cost += pitch; continue; }
                cost += Math.Abs(l.Cx - (center + (i - mid) * pitch));
                (groups[i] ??= new List<OcrLine>()).Add(l);
            }
            if (cost < bestCost)
            {
                bestCost = cost;
                best = groups;
                bestCenter = center;
            }
        }
        trace += $"pitch={pitch:0} center={bestCenter:0} rect={rect.Width}x{rect.Height} ";
        return best;
    }

    private (Card? Card, double Sim) MatchName(string raw, double purity)
    {
        var (card, sim) = GlobalMatch(raw);
        var deck = DeckCounts;
        if (deck != null && sim < 1)
        {
            var super = purity >= 0 && purity < SuperPurity;
            if (card == null || (super && !deck.ContainsKey(card.Cid)))
            {
                var top = _db.FuzzyTop(raw, 0.2, 300).FirstOrDefault(t => deck.ContainsKey(t.Card.Cid));
                if (top.Card != null && top.Sim >= DeckSimMin && top.Sim > sim &&
                    _used.GetValueOrDefault(top.Card.Cid) < deck[top.Card.Cid])
                    (card, sim) = (top.Card, top.Sim);
            }
        }
        if (card != null) _used[card.Cid] = _used.GetValueOrDefault(card.Cid) + 1;
        return (card, sim);
    }

    private (Card? Card, double Sim) GlobalMatch(string raw)
    {
        var q = CardDatabase.Normalize(raw);
        if (q.Length == 0) return (null, 0);
        if (_db.TryExactKey(raw, out var exact)) return (exact, 1);
        if (_db.TryExact(raw, out var exact2)) return (exact2, 1);
        if (_aliases != null && _aliases.TryLookup(raw, out var acid) && _db.TryByCid(acid, out var acard))
            return (acard, 1);
        var hits = _db.Search(raw, 5);
        if (hits.Count > 0)
        {
            var norm = CardDatabase.Normalize(hits[0].DisplayName);
            if (norm.StartsWith(q, StringComparison.Ordinal) && q.Length >= 4 && q.Length * 2 >= norm.Length)
                return (hits[0], 0.9);
        }
        if (_db.TryFuzzy(raw, 0.6, out var fuzzy, out var sim)) return (fuzzy, sim);
        return (null, 0);
    }

    private static double SlotPurity(Mat m, double cx, double pitch)
    {
        var w = (int)(pitch * 0.8);
        var x0 = Math.Clamp((int)(cx - w / 2.0), 0, Math.Max(0, m.Width - w));
        var h = (int)(m.Height * 0.6);
        if (w < 4 || h < 4) return -1;
        using var region = new Mat(m, new Rect(x0, 0, w, h));
        return BannerPurity(region);
    }

    private static double BannerPurity(Mat banner)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(banner, hsv, ColorConversionCodes.BGR2HSV);
        var bins = new int[18];
        var total = 0;
        for (var y = 0; y < hsv.Height; y++)
            for (var x = 0; x < hsv.Width; x++)
            {
                var p = hsv.At<Vec3b>(y, x);
                if (p.Item1 < 60 || p.Item2 < 60) continue;
                bins[p.Item0 / 10]++;
                total++;
            }
        if (total == 0) return -1;
        var d = Array.IndexOf(bins, bins.Max());
        var near = bins[d] + bins[(d + 1) % 18] + bins[(d + 17) % 18];
        return (double)near / total;
    }

    public IReadOnlyList<Candidate> CandidatesFor(string raw)
    {
        var list = new List<Candidate>();
        void Add(Card c, double s)
        {
            if (!list.Any(x => x.Card.Cid == c.Cid)) list.Add(new Candidate(c, s));
        }
        if (_aliases != null && _aliases.TryPeek(raw, out var acid) && _db.TryByCid(acid, out var acard)) Add(acard, 1);
        if (_db.TryExactKey(raw, out var e1)) Add(e1, 1);
        if (_db.TryExact(raw, out var e2)) Add(e2, 1);
        var q = CardDatabase.Normalize(raw);
        foreach (var c in _db.Search(raw, 5))
        {
            var norm = CardDatabase.Normalize(c.DisplayName);
            if (norm.StartsWith(q, StringComparison.Ordinal) && q.Length >= 4 && q.Length * 2 >= norm.Length) Add(c, 0.9);
        }
        foreach (var (c, s) in _db.FuzzyTop(raw, 0.2, 5)) Add(c, s);
        return list.OrderByDescending(x => x.Sim).Take(5).ToList();
    }

    private static double Median(List<double> xs)
    {
        var s = xs.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    public void Dispose() => _ocr.Dispose();
}