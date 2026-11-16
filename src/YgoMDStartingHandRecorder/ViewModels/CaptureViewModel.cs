using System.Collections.ObjectModel;
using System.Drawing.Imaging;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ygo.CardLib;
using Ygo.Recognition;
using Ygo.Records;
using YgoMDStartingHandRecorder.Services;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;
using Sd = System.Drawing;

namespace YgoMDStartingHandRecorder.ViewModels;

public sealed record WindowItem(IntPtr Hwnd, string Title)
{
    public override string ToString() => Title;
}

public sealed class CaptureViewModel : ViewModelBase
{
    private static readonly IBrush CommitDim = new SolidColorBrush(Color.Parse("#8D97AD"));
    private static readonly IBrush CommitAmber = new SolidColorBrush(Color.Parse("#E6C15C"));
    private static readonly IBrush CommitGreen = new SolidColorBrush(Color.Parse("#46C08A"));

    private readonly RecorderContext _ctx;
    private readonly CardImageCache _images;
    private HandResult? _lastResult;
    private string _lastCapturePath = "";
    private string _lastTurn = "";
    private long? _lastSnapshotId;
    private long? _sessionId;
    private string _sessionName = "";
    private string _pendingImage = "";
    private DateTime _sessionStarted;
    private int _runSaved;
    private bool _sessionPending = true;
    private bool _saved;
    private bool _hasTurn;
    private bool _deckSwitching;
    private bool _ready;
    private bool _busy;
    private bool _fixEnabled;
    private int _pendingCount;
    private string _commitText = "待截图";
    private IBrush _commitBrush = CommitDim;
    private IBrush _turnWash = Wash("#B6C1D3");
    private readonly int?[] _corrected = new int?[5];

    private WindowItem? _selectedWindow;
    private DeckItem? _selectedDeck;
    private AvBitmap? _preview;
    private string _previewMeta = "—";
    private string _resultMeta = "0 张";
    private bool _hasResult;
    private string _turnZh = "未定";
    private string _turnEn = "TURN";
    private bool _first;
    private string _deckName = "未分类";
    private string _sessionLine = "会话待入库 · 第 0 局";
    private string _currentSessionName = "—";
    private CardPickViewModel? _pick;

    public ObservableCollection<WindowItem> Windows { get; } = new();
    public ObservableCollection<DeckItem> Decks { get; } = new();
    public ObservableCollection<SlotViewModel> Slots { get; } = new();

    public event Action<string, string?>? Status;
    public event Action<string, bool>? Session;
    public event Action? CacheChanged;
    public event Action<CardPickViewModel?>? PickChanged;

    public CaptureViewModel(RecorderContext ctx, CardImageCache images)
    {
        _ctx = ctx;
        _images = images;
        RefreshWindowsCommand = new RelayCommand(RefreshWindows);
        CaptureFirstCommand = new RelayCommand(async () => await CaptureAsync("先手"), () => CaptureEnabled);
        CaptureSecondCommand = new RelayCommand(async () => await CaptureAsync("后手"), () => CaptureEnabled);
        _ctx.Ready += () =>
        {
            _ready = true;
            Raise(nameof(CaptureEnabled));
            CaptureFirstCommand.RaiseCanExecuteChanged();
            CaptureSecondCommand.RaiseCanExecuteChanged();
            SetStatus(_ctx.StoreError ?? "就绪：选择大师决斗窗口后点击「先手截图 / 后手截图」");
        };
        if (_ctx.StoreError != null) SetStatus(_ctx.StoreError);
        _ctx.CardsChanged += () =>
        {
            Raise(nameof(CaptureEnabled));
            CaptureFirstCommand.RaiseCanExecuteChanged();
            CaptureSecondCommand.RaiseCanExecuteChanged();
            if (!_ctx.HasCards) SetStatus("卡表缺失：请到「存储」页下载卡片数据");
        };
        if (!_ctx.HasCards) SetStatus("卡表缺失：请到「存储」页下载卡片数据");
        RefreshWindows();
        RefreshDecks();
        PrepareSession();
        _ctx.WarmAsync();
    }

    public RelayCommand RefreshWindowsCommand { get; }
    public RelayCommand CaptureFirstCommand { get; }
    public RelayCommand CaptureSecondCommand { get; }

    public Roi HandRoi => _ctx.Cfg.HandRoi;
    public int SlotCount => _ctx.Cfg.Slots;

    public void RefreshDecksOnTab() => RefreshDecks();

    public bool CaptureEnabled => _ready && !_busy && _ctx.HasCards;

    public bool FixEnabled
    {
        get => _fixEnabled;
        private set => Set(ref _fixEnabled, value);
    }

    public WindowItem? SelectedWindow
    {
        get => _selectedWindow;
        set => Set(ref _selectedWindow, value);
    }

    public DeckItem? SelectedDeck
    {
        get => _selectedDeck;
        set
        {
            if (!Set(ref _selectedDeck, value)) return;
            DeckName = value?.Name ?? "未分类";
            if (_deckSwitching) return;
            PrepareSession();
            if (_lastResult != null) RenderNames(_lastResult);
        }
    }

    public AvBitmap? Preview
    {
        get => _preview;
        set => Set(ref _preview, value);
    }

    public string PreviewMeta { get => _previewMeta; set => Set(ref _previewMeta, value); }
    public string ResultMeta { get => _resultMeta; set => Set(ref _resultMeta, value); }
    public bool HasResult { get => _hasResult; set => Set(ref _hasResult, value); }
    public string TurnZh { get => _turnZh; set => Set(ref _turnZh, value); }
    public string TurnEn { get => _turnEn; set => Set(ref _turnEn, value); }
    public bool First { get => _first; set => Set(ref _first, value); }
    public string DeckName { get => _deckName; set => Set(ref _deckName, value); }
    public string SessionLine { get => _sessionLine; set => Set(ref _sessionLine, value); }
    public string CurrentSessionName { get => _currentSessionName; private set => Set(ref _currentSessionName, value); }

    public bool HasTurn
    {
        get => _hasTurn;
        private set
        {
            if (Set(ref _hasTurn, value)) Raise(nameof(TurnBlank));
        }
    }

    public bool TurnBlank => !_hasTurn;
    public bool SessionPending { get => _sessionPending; private set => Set(ref _sessionPending, value); }

    public int PendingCount
    {
        get => _pendingCount;
        private set
        {
            if (Set(ref _pendingCount, value))
            {
                Raise(nameof(HasPending));
                Raise(nameof(PendingTitle));
            }
        }
    }

    public bool HasPending => _pendingCount > 0;
    public string PendingTitle => $"未入库 · {_pendingCount} 张待纠正";
    public string CommitText { get => _commitText; private set => Set(ref _commitText, value); }
    public IBrush CommitBrush { get => _commitBrush; private set => Set(ref _commitBrush, value); }
    public IBrush TurnWash { get => _turnWash; private set => Set(ref _turnWash, value); }

    public CardPickViewModel? Pick
    {
        get => _pick;
        private set
        {
            if (Set(ref _pick, value)) PickChanged?.Invoke(value);
        }
    }

    private void SetStatus(string text, string? path = null) => Status?.Invoke(text, path);

    private void RefreshWindows()
    {
        var wins = NativeMethods.VisibleWindows().OrderBy(w => w.Title, StringComparer.Ordinal).ToList();
        Windows.Clear();
        foreach (var w in wins) Windows.Add(new WindowItem(w.Hwnd, w.Title));
        var pick = wins.FindIndex(w => w.Title.Contains("Master Duel", StringComparison.OrdinalIgnoreCase)
                                       || w.Title.Contains("masterduel", StringComparison.OrdinalIgnoreCase)
                                       || w.Title.Contains("Yu-Gi-Oh", StringComparison.OrdinalIgnoreCase)
                                       || w.Title.Contains("游戏王"));
        SelectedWindow = pick >= 0 ? Windows[pick] : (Windows.Count > 0 ? Windows[0] : null);
        SetStatus($"共 {wins.Count} 个可见窗口");
    }

    private void RefreshDecks()
    {
        _deckSwitching = true;
        var keep = SelectedDeck?.Id ?? 0;
        Decks.Clear();
        Decks.Add(new DeckItem(0, "未分类"));
        if (_ctx.Store != null)
            foreach (var d in _ctx.Store.ListDecks()) Decks.Add(new DeckItem(d.Id, d.Name));
        var idx = Math.Max(0, Decks.ToList().FindIndex(d => d.Id == keep));
        var envDeck = Environment.GetEnvironmentVariable("YGO_DECK");
        if (keep == 0 && envDeck != null)
        {
            var e = Decks.ToList().FindIndex(d => d.Name == envDeck);
            if (e >= 0) idx = e;
        }
        SelectedDeck = Decks[idx];
        _deckSwitching = false;
    }

    // 会话只在真正入库时写库；未分类固定共用一个会话，不带时间戳。
    private void PrepareSession()
    {
        var deck = SelectedDeck;
        var unclassified = deck == null || deck.Id == 0;
        _sessionName = unclassified
            ? RecordStore.UnclassifiedSession
            : deck!.Name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _sessionId = null;
        _sessionStarted = DateTime.Now;
        _runSaved = 0;
        CurrentSessionName = _sessionName;
        SessionPending = true;
        Session?.Invoke(_sessionName, true);
        SessionLine = unclassified ? "未分类 · 本次第 0 局 · 不计累计" : $"会话 {_sessionStarted:HH:mm:ss} · 第 0 局";
        SetStatus(unclassified ? "未分类：共用一个会话，不计先后手累计；首次入库时写入" : "会话待入库：" + _sessionName);
    }

    private void EnsureSession()
    {
        if (_sessionId != null || _ctx.Store == null) return;
        var deck = SelectedDeck;
        _sessionId = deck == null || deck.Id == 0
            ? _ctx.Store.GetOrCreateSession(RecordStore.UnclassifiedSession)
            : _ctx.Store.CreateSession(_sessionName);
        SessionPending = false;
        Session?.Invoke(CurrentSessionName, false);
    }

    private int MissCount()
    {
        if (_lastResult == null) return 0;
        var miss = 0;
        for (var i = 0; i < _ctx.Cfg.Slots; i++)
        {
            var cid = _corrected[i] ?? (i < _lastResult.Slots.Length ? _lastResult.Slots[i]?.Card?.Cid : null);
            if (cid == null) miss++;
        }
        return miss;
    }

    // 五张全部命中卡表（含人工纠正）才写库；否则只留在内存等纠正。
    private void TryCommit()
    {
        var store = _ctx.Store;
        if (store == null || _lastResult == null || _saved) return;
        PendingCount = MissCount();
        if (PendingCount > 0)
        {
            CommitText = "未入库";
            CommitBrush = CommitAmber;
            SetStatus($"有 {PendingCount} 张未识别 · 本手未入库，逐槽点「改」补齐后自动入库", _pendingImage);
            return;
        }
        try
        {
            var deck = SelectedDeck;
            var deckId = deck == null || deck.Id == 0 ? null : (long?)deck.Id;
            EnsureSession();
            if (_sessionId == null) return;
            var id = store.AddSnapshot(_sessionId.Value, _pendingImage, _lastCapturePath,
                (int)_lastResult.Elapsed.TotalMilliseconds, _lastTurn, _lastResult.Slots, deckId);
            for (var i = 0; i < _corrected.Length; i++)
            {
                if (_corrected[i] is not int cid) continue;
                var raw = i < _lastResult.Slots.Length ? _lastResult.Slots[i]?.RawText ?? "" : "";
                store.Correct(id, i + 1, cid, raw);
            }
            _lastSnapshotId = id;
            _saved = true;
            _runSaved++;
            PendingCount = 0;
            CommitText = "已入库";
            CommitBrush = CommitGreen;
            SetStatus($"已保存并入库（{_lastTurn}·{deck?.Name ?? "未分类"}）", _pendingImage);
            CacheChanged?.Invoke();
        }
        catch (Exception ex)
        {
            CommitText = "入库失败";
            CommitBrush = CommitAmber;
            SetStatus("入库失败：" + ex.Message);
        }
    }

    private async Task CaptureAsync(string turn)
    {
        var rec = _ctx.Rec;
        if (rec == null || SelectedWindow is not WindowItem win) return;
        var hwnd = win.Hwnd;
        if (NativeMethods.IsIconic(hwnd))
        {
            SetStatus("目标窗口已最小化，请先还原");
            return;
        }
        var rect = NativeMethods.ClientRectOnScreen(hwnd);
        if (rect.Width < 100 || rect.Height < 100)
        {
            SetStatus("无法获取窗口客户区");
            return;
        }
        _busy = true;
        Raise(nameof(CaptureEnabled));
        CaptureFirstCommand.RaiseCanExecuteChanged();
        CaptureSecondCommand.RaiseCanExecuteChanged();
        FixEnabled = false;
        Preview = null;
        Slots.Clear();
        HasResult = false;
        _lastSnapshotId = null;
        _saved = false;
        PendingCount = 0;
        CommitText = "识别中";
        CommitBrush = CommitDim;
        Array.Clear(_corrected);
        SetTurn(turn);
        SetStatus(!_saved && _lastResult != null ? "上一手未识别齐全未入库，已放弃；截图中…" : "截图中…");
        var mode = Environment.GetEnvironmentVariable("YGO_CAPTURE");
        var path = "printwindow";
        var shot = mode == "wgc" ? null : await Task.Run(() => NativeMethods.CaptureWindow(hwnd, rect.Size));
        if (shot == null && mode != "pw")
        {
            path = "wgc";
            shot = await Task.Run(() => WgcCapture.CaptureAsync(hwnd));
        }
        if (shot == null)
        {
            SetStatus("截图失败：窗口可能最小化、独占全屏或不支持离屏渲染");
            _busy = false;
            Raise(nameof(CaptureEnabled));
            CaptureFirstCommand.RaiseCanExecuteChanged();
            CaptureSecondCommand.RaiseCanExecuteChanged();
            return;
        }
        Preview = ToAvalonia(shot);
        PreviewMeta = $"{shot.Width} × {shot.Height} · {(path == "wgc" ? "WGC" : "PrintWindow")} · {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        var dbg = Environment.GetEnvironmentVariable("YGO_DEBUGSHOT");
        if (dbg != null)
        {
            shot.Save(Path.Combine(dbg, "last_shot.png"), ImageFormat.Png);
            File.AppendAllText(Path.Combine(dbg, "capture.log"), $"path={path} size={shot.Width}x{shot.Height}\n");
        }
        SetStatus($"识别中… {shot.Width}x{shot.Height} ({path}) @ {rect.X},{rect.Y}");
        ApplyDeckConstraint(rec);
        var hr = await Task.Run(() => rec.Recognize(shot));
        _lastResult = hr;
        _lastCapturePath = path;
        _lastTurn = turn;
        var dir = ShotCache.Dir(_ctx.Root);
        Directory.CreateDirectory(dir);
        var stem = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var file = Path.Combine(dir, stem + ".png");
        for (var n = 2; File.Exists(file); n++) file = Path.Combine(dir, $"{stem}_{n}.png");
        shot.Save(file, ImageFormat.Png);
        shot.Dispose();
        _pendingImage = file;
        TryCommit();
        RenderNames(hr);
        FixEnabled = true;
        _busy = false;
        Raise(nameof(CaptureEnabled));
        CaptureFirstCommand.RaiseCanExecuteChanged();
        CaptureSecondCommand.RaiseCanExecuteChanged();
    }

    private void ApplyDeckConstraint(HandRecognizer rec)
    {
        var deckId = SelectedDeck?.Id ?? 0;
        if (deckId <= 0 || _ctx.Store == null) { rec.DeckCounts = null; return; }
        var counts = new Dictionary<int, int>();
        foreach (var c in _ctx.Store.GetDeckCards(deckId))
            if (c.Section == "Main" && c.Cid != null)
                counts[c.Cid.Value] = counts.GetValueOrDefault(c.Cid.Value) + c.Count;
        rec.DeckCounts = counts.Count > 0 ? counts : null;
    }

    private void RenderNames(HandResult hr)
    {
        DeckStats? stats = null;
        var mainTotal = 0;
        var mainCount = new Dictionary<int, int>();
        var deckId = SelectedDeck?.Id ?? 0;
        if (deckId > 0 && _ctx.Store != null)
        {
            foreach (var c in _ctx.Store.GetDeckCards(deckId))
                if (c.Section == "Main" && c.Cid != null)
                {
                    mainTotal += c.Count;
                    mainCount[c.Cid.Value] = mainCount.GetValueOrDefault(c.Cid.Value) + c.Count;
                }
            stats = HandStats.Compute(_sessionId is long sid
                ? _ctx.Store.ListSnapshots(sid, deckId)
                : new List<SnapshotRow>());
        }
        string TotalSuffix(long? cid)
        {
            if (cid == null || stats == null) return "合 -";
            var r = stats.Rate((int)cid.Value, "");
            return r == null ? "合 -" : $"合 {r:0.0}%";
        }
        string StatSuffix(int cid)
        {
            var theo = HandStats.Theoretical(mainTotal, mainCount.GetValueOrDefault(cid), _ctx.Cfg.Slots);
            var suffix = $"理论{theo:0.0}%";
            foreach (var (label, bucket) in new[] { ("合", ""), ("先", "先手"), ("后", "后手") })
            {
                var r = stats!.Rate(cid, bucket);
                suffix += r == null ? $" · {label}-" : $" · {label}{r:0.0}%{HandStats.Mark(r.Value, theo)}";
            }
            return suffix;
        }
        Slots.Clear();
        for (var i = 0; i < _ctx.Cfg.Slots; i++)
        {
            var s = i < hr.Slots.Length ? hr.Slots[i] : null;
            var cid = _corrected[i] ?? s?.Card?.Cid;
            var name = cid != null && _ctx.Db.TryByCid(cid.Value, out var card) ? card.DisplayName : null;
            var raw = s?.RawText ?? "";
            var idx = i;
            var slot = new SlotViewModel
            {
                Index = i + 1,
                CardId = cid != null && _ctx.Db.TryByCid(cid.Value, out var c2) ? c2.Id : null,
                Name = name ?? "未识别",
                Miss = name == null,
                Raw = s?.RawText ?? "-",
                ShowSim = s != null,
                SimText = s != null ? s.MatchSim.ToString("0.00") : "",
                Warn = s == null || s.MatchSim < 0.90,
                Corrected = _corrected[i] != null,
                Stats = name == null ? (raw.Length > 0 ? $"raw={raw} · 无匹配卡" : "raw=（空 · OCR 无输出）") : (cid != null && stats != null ? StatSuffix(cid.Value) : ""),
                TotalText = name == null ? "" : TotalSuffix(cid),
                FixCommand = new RelayCommand(() => FixSlot(idx)),
            };
            if (s != null)
            {
                var (bg, border, fg) = SlotViewModel.SimColors(name == null ? 0 : s.MatchSim);
                slot.SimBg = bg;
                slot.SimBorder = border;
                slot.SimFg = fg;
            }
            Slots.Add(slot);
            if (slot.CardId != null) _ = LoadThumbAsync(slot);
        }
        HasResult = true;
        ResultMeta = $"{hr.Slots.Count(s => s != null)} 张 · {hr.Elapsed.TotalMilliseconds:0} ms";
        SetTurn(_lastTurn);
        DeckName = SelectedDeck?.Name ?? "未分类";
        PendingCount = _saved ? 0 : MissCount();
        if (_saved)
        {
            CommitText = "已入库";
            CommitBrush = CommitGreen;
        }
        else
        {
            CommitText = PendingCount > 0 ? "未入库" : "待入库";
            CommitBrush = PendingCount > 0 ? CommitAmber : CommitDim;
        }
        if (deckId == 0)
            SessionLine = $"未分类 · 本次第 {_runSaved} 局 · 不计累计";
        else
        {
            var count = _ctx.Store != null && _sessionId is long sid2 ? _ctx.Store.ListSnapshots(sid2).Count : 0;
            SessionLine = $"会话 {_sessionStarted:HH:mm:ss} · 第 {count} 局";
        }
    }

    private void SetTurn(string turn)
    {
        TurnZh = turn;
        TurnEn = turn == "先手" ? "1st" : "2nd";
        First = turn == "先手";
        HasTurn = true;
        TurnWash = Wash(turn == "先手" ? "#3F9FFF" : "#C8102E");
    }

    private static IBrush Wash(string hex)
    {
        var c = Color.Parse(hex);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.FromArgb(0x24, c.R, c.G, c.B), 0),
                new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1),
            },
        };
    }

    private async Task LoadThumbAsync(SlotViewModel slot)
    {
        var bmp = await _images.GetAsync(slot.CardId!.Value);
        slot.Thumb = bmp;
    }

    private void FixSlot(int idx)
    {
        var store = _ctx.Store;
        var rec = _ctx.Rec;
        if (_lastResult == null) return;
        var s = idx < _lastResult.Slots.Length ? _lastResult.Slots[idx] : null;
        IReadOnlyList<Candidate> candidates = Array.Empty<Candidate>();
        if (rec != null && s != null && s.RawText.Length > 0 && (s.Card == null || s.MatchSim < 0.90))
            candidates = rec.CandidatesFor(s.RawText);
        var raw = s?.RawText ?? "";
        var pick = new CardPickViewModel(_ctx.Db, _images, candidates, idx + 1, raw);
        pick.Done += card =>
        {
            Pick = null;
            if (card == null) return;
            _corrected[idx] = card.Cid;
            if (store != null)
            {
                if (_lastSnapshotId is long id) store.Correct(id, idx + 1, card.Cid, raw);
                else store.UpsertAlias(raw, card.Cid);
            }
            var wasSaved = _saved;
            TryCommit();
            RenderNames(_lastResult);
            var aliasNote = raw.Trim().Length > 0 ? "，并写入 OcrAlias" : "";
            SetStatus($"槽{idx + 1} 已纠正为 {card.DisplayName}{aliasNote}" + (!wasSaved && _saved ? " · 五张齐全已入库" : ""));
        };
        Pick = pick;
    }

    private static AvBitmap ToAvalonia(Sd.Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        return new AvBitmap(ms);
    }
}