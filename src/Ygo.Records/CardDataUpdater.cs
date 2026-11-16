using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ygo.Records;

public sealed record CardDataMeta(string UpdatedAt, string Md5);

// 卡表不再内嵌：从百鸽 API 下载 cards.zip（约 2.4MB）→ 解出 cards.json → 官方 MD5 校验 → 落盘并记录更新时间。
// 卡图缓存与卡表同目录（cardimg/），占用合计、改路径一起搬。
public static class CardDataUpdater
{
    public const string BaseUrl = "https://ygocdb.com/api/v0/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static string CardsPath(string dir) => Path.Combine(dir, "cards.json");
    public static string MetaPath(string dir) => Path.Combine(dir, "cards.meta.json");
    public static string ImagesDir(string dir) => Path.Combine(dir, "cardimg");

    public static CardDataMeta? ReadMeta(string dir)
    {
        try
        {
            var p = MetaPath(dir);
            return File.Exists(p) ? JsonSerializer.Deserialize<CardDataMeta>(File.ReadAllText(p)) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<CardDataMeta> DownloadAsync(string dir, IProgress<double>? percent = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(dir);
        var published = (await Http.GetStringAsync(BaseUrl + "cards.zip.md5", ct)).Trim().Trim('"').ToLowerInvariant();
        var zipTmp = Path.Combine(dir, "cards.zip.tmp");
        using (var resp = await Http.GetAsync(BaseUrl + "cards.zip", HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zipTmp);
            var buf = new byte[81920];
            long got = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                got += n;
                if (total > 0) percent?.Report(100.0 * got / total);
            }
        }
        var jsonTmp = Path.Combine(dir, "cards.json.tmp");
        using (var zip = ZipFile.OpenRead(zipTmp))
        {
            var entry = zip.GetEntry("cards.json") ?? throw new InvalidDataException("cards.zip 内无 cards.json");
            entry.ExtractToFile(jsonTmp, true);
        }
        var actual = Md5Of(jsonTmp);
        if (actual != published)
        {
            File.Delete(jsonTmp);
            File.Delete(zipTmp);
            throw new InvalidDataException($"MD5 校验不符：{actual} != {published}");
        }
        File.Move(jsonTmp, CardsPath(dir), true);
        File.Move(zipTmp, Path.Combine(dir, "cards.zip"), true);
        File.WriteAllText(Path.Combine(dir, "cards.zip.md5"), published + "\n");
        var meta = new CardDataMeta(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), published);
        File.WriteAllText(MetaPath(dir), JsonSerializer.Serialize(meta));
        percent?.Report(100);
        return meta;
    }

    // 跨盘符时 Directory.Move 会抛异常，回退递归拷贝+删源。
    public static void MoveDirectory(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(to))!);
        try
        {
            Directory.Move(from, to);
            return;
        }
        catch (IOException)
        {
        }
        CopyDirectory(from, to);
        Directory.Delete(from, true);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from))
            File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        foreach (var d in Directory.EnumerateDirectories(from))
            CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
    }

    public static long DirectoryBytes(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        long bytes = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            bytes += new FileInfo(f).Length;
        return bytes;
    }

    private static string Md5Of(string path)
    {
        using var md5 = MD5.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(md5.ComputeHash(fs)).ToLowerInvariant();
    }
}