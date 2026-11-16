namespace Ygo.CardLib;

public sealed record Card(
    int Cid,
    long Id,
    string? MdName,
    string? ScName,
    string? CnName,
    string? NwbbsName,
    string? CnocgName,
    string? JpName,
    string? EnName)
{
    public string DisplayName => MdName ?? CnName ?? ScName ?? EnName ?? $"cid{Cid}";

    public IEnumerable<string> Aliases()
    {
        foreach (var n in new[] { MdName, ScName, CnName, NwbbsName, CnocgName })
            if (!string.IsNullOrWhiteSpace(n))
                yield return n!;
    }
}