using Ygo.Records;

namespace YgoMDStartingHandRecorder.Services;

public static class ShotCache
{
    public static string Dir(string root) => AppSettings.Load(root).Captures(root);

    public static (int Count, long Bytes) Stats(string dir)
    {
        if (!Directory.Exists(dir)) return (0, 0);
        var count = 0;
        long bytes = 0;
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            count++;
            bytes += new FileInfo(f).Length;
        }
        return (count, bytes);
    }

    public static string Format((int Count, long Bytes) s) =>
        $"截图缓存 {s.Count} 张 / {s.Bytes / 1024.0 / 1024.0:0.0} MB";
}