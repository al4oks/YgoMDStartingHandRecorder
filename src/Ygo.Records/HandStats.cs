namespace Ygo.Records;

public sealed record DeckStats(
    int Total, int First, int Second,
    IReadOnlyDictionary<int, int> TotalHits,
    IReadOnlyDictionary<int, int> FirstHits,
    IReadOnlyDictionary<int, int> SecondHits)
{
    public int Count(string bucket) => bucket switch { "先手" => First, "后手" => Second, _ => Total };

    public int Hits(int cid, string bucket) =>
        (bucket switch { "先手" => FirstHits, "后手" => SecondHits, _ => TotalHits }).GetValueOrDefault(cid);

    public double? Rate(int cid, string bucket)
    {
        var n = Count(bucket);
        return n == 0 ? null : 100.0 * Hits(cid, bucket) / n;
    }
}

public static class HandStats
{
    // P(at least one copy in a handSize-card opening) from a mainTotal-card main deck holding cardCount copies.
    public static double Theoretical(int mainTotal, int cardCount, int handSize)
    {
        if (mainTotal <= 0 || cardCount <= 0 || handSize <= 0) return 0;
        if (mainTotal - cardCount < handSize) return 100;
        double none = 1;
        for (var i = 0; i < handSize; i++) none *= (double)(mainTotal - cardCount - i) / (mainTotal - i);
        return (1 - none) * 100;
    }

    // Caller supplies the scope: one session's snapshots, or all sessions of a deck merged.
    public static DeckStats Compute(IReadOnlyList<SnapshotRow> snaps)
    {
        int t = 0, f = 0, s = 0;
        var th = new Dictionary<int, int>();
        var fh = new Dictionary<int, int>();
        var sh = new Dictionary<int, int>();
        foreach (var snap in snaps)
        {
            var cids = snap.Cards
                .Select(c => c.CorrectedCid ?? c.CardCid)
                .Where(x => x != null)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();
            t++;
            foreach (var cid in cids) th[cid] = th.GetValueOrDefault(cid) + 1;
            if (snap.TurnOrder == "先手")
            {
                f++;
                foreach (var cid in cids) fh[cid] = fh.GetValueOrDefault(cid) + 1;
            }
            else if (snap.TurnOrder == "后手")
            {
                s++;
                foreach (var cid in cids) sh[cid] = sh.GetValueOrDefault(cid) + 1;
            }
        }
        return new DeckStats(t, f, s, th, fh, sh);
    }

    public static string Mark(double observedPct, double theoreticalPct)
    {
        var d = observedPct - theoreticalPct;
        if (Math.Abs(d) <= 1) return "-";
        return d > 0 ? "↑" : "↓";
    }
}