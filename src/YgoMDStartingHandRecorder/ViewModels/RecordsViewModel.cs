using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Ygo.CardLib;
using Ygo.Recognition;
using Ygo.Records;
using YgoMDStartingHandRecorder.Services;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;
using Sd = System.Drawing;

namespace YgoMDStartingHandRecorder.ViewModels;

public sealed record SessionItem(long Id, string Note)
{
    public override string ToString() => Note;
}

public sealed class RecordsViewModel : ViewModelBase
{
    private readonly RecorderContext _ctx;
    private readonly CardImageCache _images;
    private readonly LabelCodec _codec;

    private bool _loading;
    private bool _cleanArmed;
    private long? _detailId;
    private List<long> _selectedIds = new();
    private string _listMeta = "";
    private bool _hasRows;
    private string _detailMeta = "";
    private bool _hasSelection;
    private AvBitmap? _evidence;
    private string _evidenceTitle = "";
    private string _evidenceDesc = "";
    private bool _showEvidence;
    private string _cleanShotsLabel = "清理截图";
    private SessionItem? _selectedSession;
    private DeckItem? _selectedDeckFilter;

    public ObservableCollection<SessionItem> Sessions { get; } = new();
    public ObservableCollection<DeckItem> DeckFilters { get; } = new();
    public ObservableCollection<RecordRowViewModel> Rows { get; } = new();
    public ObservableCollection<SlotViewModel> DetailSlots { get; } = new();

    public event Action<string>? Status;
    public event Action? CacheChanged;
    public event Action<CardPickViewModel?>? PickChanged;
    public event Action<AliasViewModel?>? AliasChanged;

    public Func<string, string, string, Task<bool>>? ConfirmAsync { get; set; }
    public Func<string, Task<string?>>? SaveFileAsync { get; set; }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand CleanCommand { get; }
    public RelayCommand CleanShotsCommand { get; }
    public RelayCommand ExportCsvCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand AliasesCommand { get; }

    public RecordsViewModel(RecorderContext ctx, CardImageCache images)
    {
        _ctx = ctx;
        _images = images;
        _codec = new LabelCodec(ctx.Cfg);
        RefreshCommand = new RelayCommand(RefreshAll);
        CleanCommand = new RelayCommand(() => _ = CleanAsync());
        CleanShotsCommand = new RelayCommand(CleanShots);
        ExportCsvCommand = new RelayCommand(() => _ = ExportCsvAsync());
        ImportCommand = new RelayCommand(() => _ = ImportAsync());
        AliasesCommand = new RelayCommand(Aliases);
        RefreshAll();
    }

    public string ListMeta { get => _listMeta; private set => Set(ref _listMeta, value); }
    public bool HasRows { get => _hasRows; private set => Set(ref _hasRows, value); }
    public string DetailMeta { get => _detailMeta; private set => Set(ref _detailMeta, value); }
    public bool HasSelection { get => _hasSelection; private set => Set(ref _hasSelection, value); }
    public AvBitmap? Evidence { get => _evidence; private set => Set(ref _evidence, value); }
    public string EvidenceTitle { get => _evidenceTitle; private set => Set(ref _evidenceTitle, value); }
    public string EvidenceDesc { get => _evidenceDesc; private set => Set(ref _evidenceDesc, value); }
    public bool ShowEvidence { get => _showEvidence; private set => Set(ref _showEvidence, value); }
    public string CleanShotsLabel { get => _cleanShotsLabel; private set => Set(ref _cleanShotsLabel, value); }

    public SessionItem? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (_loading || !Set(ref _selectedSession, value)) return;
            FillGrid();
        }
    }

    public DeckItem? SelectedDeckFilter
    {
        get => _selectedDeckFilter;
        set
        {
            if (_loading || !Set(ref _selectedDeckFilter, value)) return;
            RebuildSessions();
            FillGrid();
        }
    }

    public void RefreshOnTab() => RefreshAll();

    public void SetSelection(IReadOnlyList<long> ids, long? current)
    {
        _selectedIds = ids.ToList();
        LoadDetail(current);
    }

    private void SetStatus(string text) => Status?.Invoke(text);

    private string CapturesDir => ShotCache.Dir(_ctx.Root);

    private long? SessionFilter() => _selectedSession is { Id: > 0 } s ? s.Id : null;

    private long? DeckFilter() => _selectedDeckFilter is { Id: >= 0 } d ? d.Id : null;

    public void RefreshAll()
    {
        _cleanArmed = false;
        CleanShotsLabel = "清理截图";
        if (_ctx.Store == null)
        {
            SetStatus(_ctx.StoreError ?? "记录库不可用");
            return;
        }
        _loading = true;
        var keepDeck = DeckFilter() ?? -1;
        DeckFilters.Clear();
        DeckFilters.Add(new DeckItem(-1, "全部卡组"));
        DeckFilters.Add(new DeckItem(0, "未分类"));
        foreach (var d in _ctx.Store.ListDecks()) DeckFilters.Add(new DeckItem(d.Id, d.Name));
        _selectedDeckFilter = DeckFilters.FirstOrDefault(d => d.Id == keepDeck) ?? DeckFilters[0];
        RebuildSessions();
        _loading = false;
        FillGrid();
    }

    // 会话下拉只列当前卡组过滤下含记录的会话（全部卡组=全部会话）
    private void RebuildSessions()
    {
        var keepSession = SessionFilter();
        Sessions.Clear();
        Sessions.Add(new SessionItem(0, "全部会话"));
        foreach (var s in _ctx.Store!.ListSessions(DeckFilter())) Sessions.Add(new SessionItem(s.Id, s.Note));
        _selectedSession = Sessions.FirstOrDefault(s => s.Id == (keepSession ?? 0)) ?? Sessions[0];
    }

    private void FillGrid()
    {
        var store = _ctx.Store;
        if (store == null) return;
        var sessions = store.ListSessions().ToDictionary(s => s.Id, s => s.Note);
        Rows.Clear();
        foreach (var s in store.ListSnapshots(SessionFilter(), DeckFilter()))
        {
            var names = new string[5];
            var fixedAny = false;
            var thumbs = new List<ThumbViewModel>();
            for (var i = 0; i < 5; i++)
            {
                var c = i < s.Cards.Count ? s.Cards[i] : null;
                var cid = c?.CorrectedCid ?? c?.CardCid;
                names[i] = cid != null && _ctx.Db.TryByCid(cid.Value, out var card) ? card.DisplayName : "？";
                if (c?.CorrectedCid != null) { names[i] += "*"; fixedAny = true; }
                var thumb = new ThumbViewModel { Miss = cid == null };
                thumbs.Add(thumb);
                if (cid != null) _ = LoadThumbAsync(thumb, cid.Value);
            }
            var row = new RecordRowViewModel
            {
                Id = s.Id,
                Time = s.CapturedAt.Length >= 19 ? s.CapturedAt[..19] : s.CapturedAt,
                SessionNote = sessions.GetValueOrDefault(s.SessionId, ""),
                Fixed = fixedAny,
                Ms = s.ElapsedMs,
                Turn = s.TurnOrder,
                HandTip = string.Join(" / ", names),
            };
            foreach (var t in thumbs) row.Thumbs.Add(t);
            Rows.Add(row);
        }
        ListMeta = $"共 {Rows.Count} 条" + (_selectedSession is { Id: > 0 } ss ? " · " + ss.Note : "");
        HasRows = Rows.Count > 0;
        SetStatus($"共 {Rows.Count} 条记录");
        CacheChanged?.Invoke();
        LoadDetail(null);
    }

    private async Task LoadThumbAsync(ThumbViewModel thumb, long cid)
    {
        if (!_ctx.Db.TryByCid((int)cid, out var card)) return;
        var bmp = await _images.GetAsync(card.Id);
        if (bmp != null) thumb.Thumb = bmp;
    }

    private async Task LoadSlotThumbAsync(SlotViewModel slot, long cid)
    {
        if (!_ctx.Db.TryByCid((int)cid, out var card)) return;
        var bmp = await _images.GetAsync(card.Id);
        if (bmp != null) slot.Thumb = bmp;
    }

    private void LoadDetail(long? id)
    {
        _loading = true;
        _detailId = null;
        Evidence = null;
        ShowEvidence = false;
        DetailSlots.Clear();
        DetailMeta = "";
        HasSelection = false;
        if (id is not long snapId || _ctx.Store == null)
        {
            EvidenceTitle = Rows.Count == 0 ? "暂无对局记录" : "未选中记录";
            EvidenceDesc = Rows.Count == 0 ? "捕获页五张齐全后自动入库" : "点击左侧列表查看详情";
            _loading = false;
            return;
        }
        var snap = _ctx.Store.GetSnapshot(snapId);
        if (snap == null)
        {
            _loading = false;
            return;
        }
        _detailId = snap.Id;
        HasSelection = true;
        DetailMeta = $"#{snap.Id} · {snap.CapturedAt}";
        if (snap.ImagePath.Length == 0)
        {
            EvidenceTitle = "证据截图已被清理";
            EvidenceDesc = "清理截图后仅保留卡名与统计";
        }
        else if (File.Exists(snap.ImagePath))
        {
            Evidence = CropRoi(snap.ImagePath);
            ShowEvidence = true;
        }
        else
        {
            EvidenceTitle = "证据截图缺失";
            EvidenceDesc = snap.ImagePath;
        }
        for (var i = 0; i < 5; i++)
        {
            var c = i < snap.Cards.Count ? snap.Cards[i] : null;
            var cid = c?.CorrectedCid ?? c?.CardCid;
            var name = cid != null && _ctx.Db.TryByCid(cid.Value, out var card) ? card.DisplayName : "未识别";
            var sim = c?.MatchSim ?? 0;
            var (bg, border, fg) = SlotViewModel.SimColors(sim);
            var idx = i;
            var slot = new SlotViewModel
            {
                Index = i + 1,
                CardId = cid,
                Name = name,
                Miss = cid == null,
                Raw = c?.RawText ?? "",
                SimText = sim.ToString("0.00"),
                ShowSim = c != null,
                SimBg = bg,
                SimBorder = border,
                SimFg = fg,
                Warn = c == null || sim < 0.90,
                Corrected = c?.CorrectedCid != null,
                FixCommand = new RelayCommand(() => PickSlot(idx)),
            };
            DetailSlots.Add(slot);
            if (cid != null) _ = LoadSlotThumbAsync(slot, cid.Value);
        }
        _loading = false;
    }

    private AvBitmap CropRoi(string path)
    {
        using var full = new Sd.Bitmap(path);
        var r = _ctx.Cfg.HandRoi;
        var crop = new Sd.Rectangle((int)(full.Width * r.X), (int)(full.Height * r.Y),
            (int)(full.Width * r.W), (int)(full.Height * r.H));
        using var view = new Sd.Bitmap(crop.Width, crop.Height);
        using (var g = Sd.Graphics.FromImage(view))
            g.DrawImage(full, new Sd.Rectangle(0, 0, crop.Width, crop.Height), crop, Sd.GraphicsUnit.Pixel);
        using var ms = new MemoryStream();
        view.Save(ms, Sd.Imaging.ImageFormat.Png);
        ms.Position = 0;
        return new AvBitmap(ms);
    }

    private void PickSlot(int idx)
    {
        var store = _ctx.Store;
        if (store == null || _detailId is not long id) return;
        var snap = store.GetSnapshot(id);
        if (snap == null) return;
        var card = idx < snap.Cards.Count ? snap.Cards[idx] : null;
        IReadOnlyList<Candidate> candidates = Array.Empty<Candidate>();
        var rec = _ctx.Rec;
        if (rec != null && card != null && card.RawText.Length > 0 && (card.CardCid == null || card.MatchSim < 0.90))
            candidates = rec.CandidatesFor(card.RawText);
        var pick = new CardPickViewModel(_ctx.Db, _images, candidates, idx + 1, card?.RawText ?? "");
        pick.Done += chosen =>
        {
            PickChanged?.Invoke(null);
            if (chosen == null) return;
            store.Correct(id, idx + 1, chosen.Cid, card?.RawText ?? "");
            RefreshAll();
            SetStatus($"槽{idx + 1} 已纠正为 {chosen.DisplayName}，并写入 OcrAlias");
        };
        PickChanged?.Invoke(pick);
    }

    private async Task CleanAsync()
    {
        var store = _ctx.Store;
        if (store == null || ConfirmAsync == null) return;
        var ids = Rows.Where(r => r.IsChecked).Select(r => r.Id).ToList();
        if (ids.Count == 0) ids = _selectedIds.ToList();
        if (ids.Count == 0)
        {
            SetStatus("先勾选或选中要清理的记录");
            return;
        }
        if (!await ConfirmAsync("清理", $"删除 {ids.Count} 条记录及其证据截图？", "删除")) return;
        store.DeleteSnapshots(ids, true);
        RefreshAll();
        SetStatus($"已清理 {ids.Count} 条记录（含证据图）");
    }

    private void CleanShots()
    {
        var store = _ctx.Store;
        if (store == null) return;
        var (count, bytes) = ShotCache.Stats(CapturesDir);
        if (!_cleanArmed)
        {
            if (count == 0)
            {
                SetStatus("截图缓存为空，无需清理");
                return;
            }
            _cleanArmed = true;
            CleanShotsLabel = "确认清理截图?";
            SetStatus($"将删除截图缓存全部 {count} 张（{bytes / 1048576.0:0.0} MB）;记录保留并标记「证据截图已被清理」。再点一次确认，点「刷新」取消");
            return;
        }
        var dir = CapturesDir;
        var deleted = 0;
        if (Directory.Exists(dir))
            foreach (var f in Directory.EnumerateFiles(dir).ToList())
                try { File.Delete(f); deleted++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
        var marked = store.MarkImagesCleanedUnder(dir);
        RefreshAll();
        var (leftCount, leftBytes) = ShotCache.Stats(dir);
        SetStatus($"已清理 {deleted} 张截图（释放 {bytes / 1048576.0:0.0} MB），{marked} 条记录标记为「证据截图已被清理」 · 截图缓存 {leftCount} 张 / {leftBytes / 1048576.0:0.0} MB");
    }

    private async Task ExportCsvAsync()
    {
        var store = _ctx.Store;
        if (store == null || SaveFileAsync == null) return;
        var path = await SaveFileAsync("ygo-hands-" + DateTime.Now.ToString("yyyyMMdd") + ".csv");
        if (path == null) return;
        var rows = store.ListSnapshots(SessionFilter(), DeckFilter());
        store.ExportCsv(path, _ctx.Db, rows);
        SetStatus($"已导出 {rows.Count} 条记录 → {path}");
    }

    private async Task ImportAsync()
    {
        var store = _ctx.Store;
        if (store == null || ConfirmAsync == null) return;
        var dir = Path.Combine(_ctx.Root, "samples", "own");
        if (!await ConfirmAsync("导入记录集", $"导入 {dir} 的已标注样本（标签作为人工确认）？", "导入")) return;
        var n = store.ImportLabeledDir(dir, _ctx.Db, _codec, "导入记录集 own");
        RefreshAll();
        SetStatus($"已导入 {n} 条记录");
    }

    private void Aliases()
    {
        if (_ctx.Store == null) return;
        AliasChanged?.Invoke(new AliasViewModel(_ctx.Store, _ctx.Db));
    }
}