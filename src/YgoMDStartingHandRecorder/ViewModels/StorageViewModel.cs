using Avalonia.Media.Imaging;
using Ygo.Records;
using YgoMDStartingHandRecorder.Services;

namespace YgoMDStartingHandRecorder.ViewModels;

// 存储页：卡表+卡图数据 / 起手截图 两个可改路径、占用、卡表更新时间与 MD5、下载/更新进度。
public sealed class StorageViewModel : ViewModelBase
{
    private readonly RecorderContext _ctx;
    private readonly CardImageCache _images;
    private string _cardDir = "";
    private string _cardSize = "";
    private string _cardUpdated = "";
    private string _cardMd5 = "";
    private string _cardCount = "";
    private string _capturesDir = "";
    private string _capturesSize = "";
    private double _progress;
    private string _progressText = "";
    private bool _busy;
    private bool _cleanShotsArmed;
    private bool _cleanCardArmed;
    private string? _pendingCardTarget;
    private string? _pendingCapturesTarget;
    private string _cleanShotsLabel = "清理截图";
    private string _cleanCardLabel = "清理卡图缓存";

    public StorageViewModel(RecorderContext ctx, CardImageCache images)
    {
        _ctx = ctx;
        _images = images;
        UpdateCommand = new RelayCommand(() => _ = UpdateAsync(), () => !Busy);
        ChangeCardDirCommand = new RelayCommand(() => _ = ChangeCardDirAsync(), () => !Busy);
        ChangeCapturesDirCommand = new RelayCommand(() => _ = ChangeCapturesDirAsync(), () => !Busy);
        CleanShotsCommand = new RelayCommand(CleanShots, () => !Busy);
        CleanCardImgCommand = new RelayCommand(CleanCardImg, () => !Busy);
        RefreshCommand = new RelayCommand(Refresh);
        _ctx.CardsChanged += Refresh;
        Refresh();
    }

    public RelayCommand UpdateCommand { get; }
    public RelayCommand ChangeCardDirCommand { get; }
    public RelayCommand ChangeCapturesDirCommand { get; }
    public RelayCommand CleanShotsCommand { get; }
    public RelayCommand CleanCardImgCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public event Action<string>? Status;
    public event Action? CacheChanged;
    public Func<string, Task<string?>>? PickFolderAsync { get; set; }

    public string CardDir { get => _cardDir; private set => Set(ref _cardDir, value); }
    public string CardSize { get => _cardSize; private set => Set(ref _cardSize, value); }
    public string CardUpdated { get => _cardUpdated; private set => Set(ref _cardUpdated, value); }
    public string CardMd5 { get => _cardMd5; private set => Set(ref _cardMd5, value); }
    public string CardCount { get => _cardCount; private set => Set(ref _cardCount, value); }
    public string CapturesDir { get => _capturesDir; private set => Set(ref _capturesDir, value); }
    public string CapturesSize { get => _capturesSize; private set => Set(ref _capturesSize, value); }
    public string CleanShotsLabel { get => _cleanShotsLabel; private set => Set(ref _cleanShotsLabel, value); }
    public string CleanCardLabel { get => _cleanCardLabel; private set => Set(ref _cleanCardLabel, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    public bool MissingCards => !_ctx.HasCards;

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                UpdateCommand.RaiseCanExecuteChanged();
                ChangeCardDirCommand.RaiseCanExecuteChanged();
                ChangeCapturesDirCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void Refresh()
    {
        _cleanShotsArmed = false;
        _cleanCardArmed = false;
        _pendingCardTarget = null;
        _pendingCapturesTarget = null;
        CleanShotsLabel = "清理截图";
        CleanCardLabel = "清理卡图缓存";
        CardDir = _ctx.CardDataDir;
        CardSize = Mb(CardDataUpdater.DirectoryBytes(_ctx.CardDataDir));
        var meta = CardDataUpdater.ReadMeta(_ctx.CardDataDir);
        CardUpdated = meta?.UpdatedAt ?? "未记录（点「更新卡表」后记录）";
        CardMd5 = meta?.Md5 ?? ReadMd5File() ?? "—";
        CardCount = _ctx.HasCards ? $"{_ctx.Db.Cards.Count} 张" : "缺失";
        CapturesDir = _ctx.CapturesDir;
        var shots = ShotCache.Stats(_ctx.CapturesDir);
        CapturesSize = $"{shots.Count} 张 / {Mb(shots.Bytes)}";
        Raise(nameof(MissingCards));
    }

    private string? ReadMd5File()
    {
        var p = Path.Combine(_ctx.CardDataDir, "cards.zip.md5");
        return File.Exists(p) ? File.ReadAllText(p).Trim() : null;
    }

    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:0.0} MB";

    private void SetStatus(string text) => Status?.Invoke(text);

    public async Task UpdateAsync()
    {
        if (Busy) return;
        Busy = true;
        Progress = 0;
        ProgressText = "连接数据源…";
        SetStatus("卡表下载中…");
        try
        {
            var progress = new Progress<double>(p =>
            {
                Progress = p;
                ProgressText = $"下载 cards.zip {p:0}%";
            });
            var meta = await CardDataUpdater.DownloadAsync(_ctx.CardDataDir, progress);
            ProgressText = "校验通过，加载卡表…";
            _ctx.ReloadCards();
            Refresh();
            SetStatus($"卡表已更新 {meta.UpdatedAt} · MD5 {meta.Md5[..8]} · {_ctx.Db.Cards.Count} 张");
        }
        catch (Exception ex)
        {
            ProgressText = "下载失败";
            SetStatus("卡表下载失败：" + ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    // 与记录页清理截图同语义：清空目录 + 记录标记「证据截图已被清理」;两段确认防误触。
    private void CleanShots()
    {
        var store = _ctx.Store;
        var dir = _ctx.CapturesDir;
        var (count, bytes) = ShotCache.Stats(dir);
        if (!_cleanShotsArmed)
        {
            if (count == 0) { SetStatus("截图目录为空，无需清理"); return; }
            _cleanShotsArmed = true;
            CleanShotsLabel = "确认清理截图?";
            SetStatus($"将删除截图目录全部 {count} 张（{bytes / 1048576.0:0.0} MB）;记录保留并标记「证据截图已被清理」。再点一次确认，点「刷新」取消");
            return;
        }
        var deleted = 0;
        if (Directory.Exists(dir))
            foreach (var f in Directory.EnumerateFiles(dir).ToList())
                try { File.Delete(f); deleted++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
        var marked = store?.MarkImagesCleanedUnder(dir) ?? 0;
        Refresh();
        CacheChanged?.Invoke();
        SetStatus($"已清理 {deleted} 张截图（释放 {bytes / 1048576.0:0.0} MB），{marked} 条记录标记为「证据截图已被清理」");
    }

    // 只清卡图缓存 cardimg（卡表保留，卡图按需重新下载）;两段确认。
    private void CleanCardImg()
    {
        var dir = _ctx.CardImagesDir;
        var count = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count() : 0;
        var bytes = CardDataUpdater.DirectoryBytes(dir);
        if (!_cleanCardArmed)
        {
            if (count == 0) { SetStatus("卡图缓存为空，无需清理"); return; }
            _cleanCardArmed = true;
            CleanCardLabel = "确认清理卡图?";
            SetStatus($"将删除卡图缓存 {count} 张（{bytes / 1048576.0:0.0} MB）;卡表保留，卡图之后按需重新下载。再点一次确认，点「刷新」取消");
            return;
        }
        if (Directory.Exists(dir))
            try { Directory.Delete(dir, true); } catch (IOException) { }
        Directory.CreateDirectory(dir);
        Refresh();
        CacheChanged?.Invoke();
        SetStatus($"已清理卡图缓存 {count} 张（释放 {bytes / 1048576.0:0.0} MB），卡表保留");
    }

    private async Task ChangeCardDirAsync()
    {
        if (PickFolderAsync == null || Busy) return;
        var picked = await PickFolderAsync("选择卡片数据目录（卡表+卡图缓存）");
        if (picked == null) return;
        try
        {
            var target = Path.GetFullPath(picked);
            if (target == Path.GetFullPath(_ctx.CardDataDir)) { _pendingCardTarget = null; return; }
            var entries = Directory.Exists(target) ? Directory.EnumerateFileSystemEntries(target).Count() : 0;
            if (entries > 0 && _pendingCardTarget != target)
            {
                _pendingCardTarget = target;
                SetStatus($"目标目录非空（{entries} 项）：将把现有卡表+卡图合并搬入（同名覆盖）。再点「更改路径」并重选该目录确认，点「刷新」取消");
                return;
            }
            _pendingCardTarget = null;
            if (Directory.Exists(_ctx.CardDataDir)) CardDataUpdater.MoveDirectory(_ctx.CardDataDir, target);
            else Directory.CreateDirectory(target);
            var settings = AppSettings.Load(_ctx.Root);
            settings.CardDataDir = target;
            settings.Save(_ctx.Root);
            _ctx.SetCardDataDir(target);
            _images.Relocate(_ctx.CardImagesDir);
            Refresh();
            SetStatus("卡片数据目录已改为 " + target);
        }
        catch (Exception ex)
        {
            SetStatus("搬迁卡片数据失败：" + ex.Message);
        }
    }

    private async Task ChangeCapturesDirAsync()
    {
        if (PickFolderAsync == null || Busy) return;
        var picked = await PickFolderAsync("选择起手截图目录");
        if (picked == null) return;
        try
        {
            var target = Path.GetFullPath(picked);
            if (target == Path.GetFullPath(_ctx.CapturesDir)) { _pendingCapturesTarget = null; return; }
            var entries = Directory.Exists(target) ? Directory.EnumerateFileSystemEntries(target).Count() : 0;
            if (entries > 0 && _pendingCapturesTarget != target)
            {
                _pendingCapturesTarget = target;
                SetStatus($"目标目录非空（{entries} 项）：将把现有截图合并搬入（同名覆盖）。再点「更改路径」并重选该目录确认，点「刷新」取消");
                return;
            }
            _pendingCapturesTarget = null;
            if (Directory.Exists(_ctx.CapturesDir)) CardDataUpdater.MoveDirectory(_ctx.CapturesDir, target);
            else Directory.CreateDirectory(target);
            var settings = AppSettings.Load(_ctx.Root);
            settings.CapturesDir = target;
            settings.Save(_ctx.Root);
            Refresh();
            SetStatus("起手截图目录已改为 " + target);
        }
        catch (Exception ex)
        {
            SetStatus("搬迁截图目录失败：" + ex.Message);
        }
    }
}