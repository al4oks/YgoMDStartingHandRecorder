namespace Ygo.Records;

// P13/P14 聚合统计：类型（卡集合）与组合（条目=req 全齐 + opt 任一;类型=任一条目成立）。
// 分布按「手牌中该类型卡的张数 0~5」计（含 copies）;Mean=实际平均每局上手张数。
// 理论：精确多元超几何（枚举计数向量）+ 条目间容斥;组合/条目规模超限返回 null。
public static class DeckAgg
{
    public sealed record Bucket(int Hands, int[] Dist, int Hits)
    {
        public double? Rate => Hands == 0 ? null : 100.0 * Hits / Hands;
        public double? Mean => Hands == 0 ? null : Enumerable.Range(0, 6).Sum(k => k * Dist[k]) / (double)Hands;
    }

    public sealed record AggResult(Bucket Total, Bucket First, Bucket Second);

    // 条目：req 每卡各≥1 全齐;opt 非空时还需其中任意≥1。
    public sealed record EntrySpec(IReadOnlyList<int> Req, IReadOnlyList<int> Opt);

    public static AggResult TagStats(IReadOnlyList<SnapshotRow> snaps, IReadOnlyCollection<int> cids)
    {
        var set = cids as IReadOnlySet<int> ?? new HashSet<int>(cids);
        return Compute(snaps, hand =>
        {
            var k = hand.Count(c => c != null && set.Contains(c.Value));
            return (k, k >= 1 ? 1 : 0);
        });
    }

    public static AggResult EntryStats(IReadOnlyList<SnapshotRow> snaps, EntrySpec spec)
    {
        var req = new HashSet<int>(spec.Req);
        var opt = new HashSet<int>(spec.Opt);
        return Compute(snaps, hand =>
        {
            var ok = req.Count > 0 && req.All(c => hand.Contains(c)) && (opt.Count == 0 || opt.Any(c => hand.Contains(c)));
            return (ok ? 1 : 0, ok ? 1 : 0);
        });
    }

    public static AggResult ComboStats(IReadOnlyList<SnapshotRow> snaps, IReadOnlyList<EntrySpec> entries)
    {
        var specs = entries.Where(e => e.Req.Count > 0).ToList();
        return Compute(snaps, hand =>
        {
            var ok = specs.Any(sp =>
            {
                var req = new HashSet<int>(sp.Req);
                var opt = new HashSet<int>(sp.Opt);
                return req.All(c => hand.Contains(c)) && (opt.Count == 0 || opt.Any(c => hand.Contains(c)));
            });
            return (ok ? 1 : 0, ok ? 1 : 0);
        });
    }

    private static AggResult Compute(IReadOnlyList<SnapshotRow> snaps, Func<List<int?>, (int K, int Hit)> f)
    {
        var t = new Mut();
        var fi = new Mut();
        var se = new Mut();
        foreach (var s in snaps)
        {
            var hand = s.Cards.Select(c => c.CorrectedCid ?? c.CardCid).ToList();
            var (k, hit) = f(hand);
            t.Add(k, hit);
            if (s.TurnOrder == "先手") fi.Add(k, hit);
            else if (s.TurnOrder == "后手") se.Add(k, hit);
        }
        return new AggResult(t.Done(), fi.Done(), se.Done());
    }

    private sealed class Mut
    {
        private int _hands;
        private int _hits;
        private readonly int[] _dist = new int[6];

        public void Add(int k, int hit)
        {
            _hands++;
            _hits += hit;
            if (k >= 0 && k < 6) _dist[k]++;
        }

        public Bucket Done() => new(_hands, _dist, _hits);
    }

    public static double Expected(int mainTotal, int copies, int handSize) =>
        mainTotal <= 0 ? 0 : (double)handSize * copies / mainTotal;

    // ===== P14 精确理论 =====

    public static double? EntryTheory(int mainTotal, IReadOnlyDictionary<int, int> copies, EntrySpec spec, int handSize) =>
        PExactly(mainTotal, copies, new[] { spec }, handSize);

    // 类型=任一条目成立 → 条目事件容斥;交集=合并 req 全齐 + 各 opt 任一。
    public static double? ComboTheory(int mainTotal, IReadOnlyDictionary<int, int> copies, IReadOnlyList<EntrySpec> entries, int handSize)
    {
        var list = entries.Where(e => e.Req.Count > 0).ToList();
        if (list.Count == 0 || list.Count > 10) return null;
        double sum = 0;
        for (var mask = 1; mask < (1 << list.Count); mask++)
        {
            var subset = new List<EntrySpec>();
            for (var i = 0; i < list.Count; i++)
                if ((mask & (1 << i)) != 0) subset.Add(list[i]);
            var p = PExactly(mainTotal, copies, subset, handSize);
            if (p == null) return null;
            sum += (subset.Count % 2 == 1 ? 1 : -1) * p.Value;
        }
        return Math.Clamp(sum, 0, 100);
    }

    // 枚举「各组上手张数」计数向量（sum=handSize），权重=Π C(c_i,k_i)/C(total,hand);
    // 约束：合并 req 每组≥1;每个 opt 集合合计≥1。
    private static double? PExactly(int mainTotal, IReadOnlyDictionary<int, int> copies, IReadOnlyList<EntrySpec> events, int handSize)
    {
        var req = new HashSet<int>();
        var opts = new List<HashSet<int>>();
        foreach (var e in events)
        {
            foreach (var c in e.Req) req.Add(c);
            if (e.Opt.Count > 0) opts.Add(new HashSet<int>(e.Opt));
        }
        var groups = req.Union(opts.SelectMany(o => o)).OrderBy(c => c).ToList();
        if (groups.Count == 0 || groups.Count > 12 || mainTotal <= 0) return null;
        var caps = groups.Select(c => copies.GetValueOrDefault(c)).ToList();
        if (caps.Any(c => c <= 0)) return null;
        // 组外其余卡作为无约束组参与计数向量（否则 sum=hand 无解）
        var restCap = mainTotal - caps.Sum();
        if (restCap < 0) return null;
        caps.Add(restCap);
        groups.Add(-1);
        var reqIdx = groups.Select((c, i) => (c, i)).Where(t => t.c >= 0 && req.Contains(t.c)).Select(t => t.i).ToHashSet();
        var optIdx = opts.Select(o => groups.Select((c, i) => (c, i)).Where(t => t.c >= 0 && o.Contains(t.c)).Select(t => t.i).ToList()).ToList();
        var k = new int[groups.Count];
        double acc = 0;
        void Rec(int idx, int left, double weight)
        {
            if (left < 0) return;
            if (idx == groups.Count)
            {
                if (left != 0) return;
                foreach (var ri in reqIdx) if (k[ri] < 1) return;
                foreach (var oi in optIdx) if (oi.Sum(i => k[i]) < 1) return;
                acc += weight;
                return;
            }
            var cap = Math.Min(caps[idx], left);
            var lo = reqIdx.Contains(idx) ? 1 : 0;
            // 剪枝：后续组容量之和须够剩余张数
            var restCap = 0;
            for (var j = idx + 1; j < groups.Count; j++) restCap += caps[j];
            for (var v = lo; v <= cap; v++)
            {
                if (left - v > restCap) continue;
                k[idx] = v;
                Rec(idx + 1, left - v, weight * Comb(caps[idx], v));
            }
            k[idx] = 0;
        }
        Rec(0, handSize, 1);
        var denom = Comb(mainTotal, handSize);
        return denom <= 0 ? null : acc / denom * 100;
    }

    private static double Comb(int n, int k)
    {
        if (k < 0 || k > n) return 0;
        k = Math.Min(k, n - k);
        double r = 1;
        for (var i = 1; i <= k; i++) r = r * (n - k + i) / i;
        return r;
    }
}