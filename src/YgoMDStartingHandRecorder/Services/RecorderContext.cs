using Avalonia.Threading;
using Ygo.CardLib;
using Ygo.Recognition;
using Ygo.Records;

namespace YgoMDStartingHandRecorder.Services;

// 四页共用的运行时上下文：卡表（可缺失/可热重载）、配置、记录库、识别器（异步预热）、存储路径。
public sealed class RecorderContext : IDisposable
{
    public string Root { get; }
    public HandConfig Cfg { get; }
    public CardDatabase Db { get; private set; }
    public RecordStore? Store { get; }
    public string? StoreError { get; }
    public HandRecognizer? Rec { get; private set; }
    public string CardDataDir { get; private set; }
    public string CapturesDir => ShotCache.Dir(Root);
    public string CardImagesDir => CardDataUpdater.ImagesDir(CardDataDir);
    public bool HasCards => Db.Cards.Count > 0;

    public event Action? Ready;
    public event Action? CardsChanged;

    public RecorderContext(string root, bool embedded = false)
    {
        Root = root;
        var cfgPath = Path.Combine(root, "data", "config.json");
        Cfg = File.Exists(cfgPath) ? HandConfig.Load(cfgPath) : HandConfig.Load(EmbeddedData.Config());
        CardDataDir = AppSettings.Load(root).CardData(root);
        MigrateLegacyImages(root);
        Db = LoadCards();
        try
        {
            Store = new RecordStore(Environment.GetEnvironmentVariable("YGO_DB") ?? RecordStore.DefaultDbPath(root));
        }
        catch (Exception ex)
        {
            StoreError = "记录库打开失败：" + ex.Message;
        }
    }

    private CardDatabase LoadCards()
    {
        var path = CardDataUpdater.CardsPath(CardDataDir);
        return File.Exists(path) ? CardDatabase.Load(path) : CardDatabase.Empty();
    }

    // P12 前卡图缓存在 root/cache/cardimg;现随卡表同目录，首次启动搬一次。
    private void MigrateLegacyImages(string root)
    {
        var old = Path.Combine(root, "cache", "cardimg");
        var now = CardImagesDir;
        if (Directory.Exists(old) && !Directory.Exists(now))
            try { CardDataUpdater.MoveDirectory(old, now); } catch (IOException) { }
    }

    public void SetCardDataDir(string dir)
    {
        CardDataDir = dir;
        ReloadCards();
    }

    public void ReloadCards()
    {
        Db = LoadCards();
        Rec?.Dispose();
        Rec = null;
        CardsChanged?.Invoke();
        if (HasCards) WarmAsync();
        else Dispatcher.UIThread.Post(() => Ready?.Invoke());
    }

    public void WarmAsync()
    {
        if (!HasCards) return;
        Task.Run(() =>
        {
            var rec = new HandRecognizer(Db, Cfg, Store);
            using (var warm = new System.Drawing.Bitmap(64, 64)) rec.Recognize(warm);
            Rec = rec;
            Dispatcher.UIThread.Post(() => Ready?.Invoke());
        });
    }

    public void Dispose()
    {
        Rec?.Dispose();
        Store?.Dispose();
    }
}