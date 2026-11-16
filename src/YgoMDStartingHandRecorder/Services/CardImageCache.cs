using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace YgoMDStartingHandRecorder.Services;

// 百鸽 CDN → 磁盘缓存 → 异步解码；并发上限 6、同请求去重；失败回退 null（占位）。
public sealed class CardImageCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim _gate = new(6);
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _inflight = new();

    public string Dir { get; private set; }

    public CardImageCache(string dir)
    {
        Dir = dir;
        Directory.CreateDirectory(dir);
    }

    public void Relocate(string dir)
    {
        Dir = dir;
        Directory.CreateDirectory(dir);
        _inflight.Clear();
    }

    public async Task<Bitmap?> GetAsync(long id, string spec = "!thumb")
    {
        var key = $"{id}{spec}";
        var task = _inflight.GetOrAdd(key, _ => LoadAsync(id, spec));
        var bmp = await task;
        if (bmp == null) _inflight.TryRemove(key, out _);
        return bmp;
    }

    private async Task<Bitmap?> LoadAsync(long id, string spec)
    {
        var file = Path.Combine(Dir, $"{id}{spec.Replace("!", "_")}.webp");
        if (File.Exists(file))
        {
            var cached = Decode(file);
            if (cached != null) return cached;
        }
        await _gate.WaitAsync();
        try
        {
            var bytes = await Http.GetByteArrayAsync($"https://cdn.233.momobako.com/ygoimg/sc/{id}.webp{spec}");
            await File.WriteAllBytesAsync(file, bytes);
            return Decode(file);
        }
        catch
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Bitmap? Decode(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            return new Bitmap(fs);
        }
        catch
        {
            return null;
        }
    }
}