using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ygo.CardLib;
using Ygo.Records;
using YgoMDStartingHandRecorder.Services;

namespace YgoMDStartingHandRecorder.ViewModels;

public sealed class DeckTileViewModel : ViewModelBase
{
    private Bitmap? _cover;

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public bool IsImport { get; init; }
    public string CountText { get; init; } = "";
    public string CompText { get; init; } = "";
    public string SampleText { get; init; } = "";
    public string ImportedAt { get; init; } = "";

    public Bitmap? Cover
    {
        get => _cover;
        set
        {
            if (Set(ref _cover, value)) Raise(nameof(HasCover));
        }
    }

    public bool HasCover => _cover != null;
}

public sealed class DeckCardCellViewModel : ViewModelBase
{
    private static readonly IBrush UpBrush = new SolidColorBrush(Color.Parse("#46C08A"));
    private static readonly IBrush DownBrush = new SolidColorBrush(Color.Parse("#E63946"));
    private static readonly IBrush FlatBrush = new SolidColorBrush(Color.Parse("#5C6579"));
    private static readonly IBrush MissBrush = new SolidColorBrush(Color.Parse("#E63946"));
    private static readonly IBrush NormalEdge = new SolidColorBrush(Color.Parse("#28314A"));
    private static readonly IBrush MissEdge = new SolidColorBrush(Color.Parse("#80E63946"));
    private static readonly IBrush EditEdge = new SolidColorBrush(Color.Parse("#B8933A"));

    private Bitmap? _thumb;
    private bool _editHit;

    public int? Cid { get; init; }
    public string Name { get; init; } = "";
    public string CountText { get; init; } = "";
    public string TheoText { get; init; } = "";
    public string TotalCount { get; init; } = "";
    public string TotalRate { get; init; } = "";
    public string TotalMark { get; init; } = "";
    public string FirstCount { get; init; } = "";
    public string FirstRate { get; init; } = "";
    public string FirstMark { get; init; } = "";
    public string SecondCount { get; init; } = "";
    public string SecondRate { get; init; } = "";
    public string SecondMark { get; init; } = "";
    public bool Miss { get; init; }
    public bool IsExtra { get; init; }

    public Bitmap? Thumb
    {
        get => _thumb;
        set
        {
            if (Set(ref _thumb, value)) Raise(nameof(HasThumb));
        }
    }

    public bool HasThumb => _thumb != null;
    public bool EditHit { get => _editHit; set { if (Set(ref _editHit, value)) Raise(nameof(CellEdge)); } }
    public IBrush CellEdge => EditHit ? EditEdge : EdgeBrush;
    public IBrush NameBrush => Miss ? MissBrush : new SolidColorBrush(Color.Parse("#E9EDF5"));
    public IBrush EdgeBrush => Miss ? MissEdge : NormalEdge;
    public IBrush TotalRateBrush => BrushFor(TotalMark);
    public IBrush FirstRateBrush => BrushFor(FirstMark);
    public IBrush SecondRateBrush => BrushFor(SecondMark);
    public IBrush ObsBrush => IsExtra ? FlatBrush : new SolidColorBrush(Color.Parse("#CFD6E4"));

    private static IBrush BrushFor(string mark) => mark == "↑" ? UpBrush : mark == "↓" ? DownBrush : FlatBrush;
}

public sealed class DecksViewModel : ViewModelBase
{
    private readonly RecorderContext _ctx;
    private readonly CardImageCache _images;
    private bool _loading;
    private bool _showDetail;
    private long? _detailId;
    private long? _lastSavedId;
    private string _listMeta = "0 个卡组";
    private string _detailName = "";
    private string _detailComp = "";
    private string _sampleText = "0 局";
    private string _sampleSplit = "先0/后0";
    private string _mainMeta = "";
    private string _extraMeta = "";
    private SessionItem? _selectedScope;
    private long? _editingTagId;
    private string _newTagName = "";
    private string _newComboName = "";

    public ObservableCollection<DeckTileViewModel> Tiles { get; } = new();
    public ObservableCollection<SessionItem> Scopes { get; } = new();
    public ObservableCollection<DeckCardCellViewModel> MainCells { get; } = new();
    public ObservableCollection<DeckCardCellViewModel> ExtraCells { get; } = new();
    public ObservableCollection<TagChipViewModel> Tags { get; } = new();
    public ObservableCollection<ComboTypeChipViewModel> ComboTypes { get; } = new();

    public event Action<string>? Status;
    public event Action<DeckImportViewModel?>? ImportChanged;
    public event Action<TagDetailViewModel?>? TagDetailChanged;
    public event Action<ComboDetailViewModel?>? ComboDetailChanged;
    public event Action<ComboManageViewModel?>? ComboManageChanged;
    public Func<string, string, string, Task<bool>>? ConfirmAsync;
    public Func<Task<string?>>? ReadClipboardAsync;
    public Func<string, Task>? WriteClipboardAsync;

    public const string DbUrl = "https://www.db.yugioh-card.com/yugiohdb";

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand OpenTileCommand { get; }
    public RelayCommand CopyDbUrlCommand { get; }
    public RelayCommand AddTagCommand { get; }
    public RelayCommand AddComboTypeCommand { get; }
    public RelayCommand ExitEditCommand { get; }
    public RelayCommand ToggleEditCommand { get; }
    public RelayCommand OpenTagDetailCommand { get; }
    public RelayCommand DeleteTagCommand { get; }
    public RelayCommand CardClickCommand { get; }
    public RelayCommand OpenComboDetailCommand { get; }
    public RelayCommand ManageComboCommand { get; }
    public RelayCommand DeleteComboTypeCommand { get; }

    public DecksViewModel(RecorderContext ctx, CardImageCache images)
    {
        _ctx = ctx;
        _images = images;
        RefreshCommand = new RelayCommand(RefreshAll);
        ImportCommand = new RelayCommand(Import);
        DeleteCommand = new RelayCommand(() => _ = DeleteAsync());
        BackCommand = new RelayCommand(Back);
        OpenTileCommand = new RelayCommand(p => { if (p is long id) { if (id < 0) Import(); else OpenDeck(id); } });
        CopyDbUrlCommand = new RelayCommand(() => _ = CopyDbUrlAsync());
        AddTagCommand = new RelayCommand(AddTag);
        AddComboTypeCommand = new RelayCommand(AddComboType);
        ExitEditCommand = new RelayCommand(() => SetEditing(null));
        ToggleEditCommand = new RelayCommand(p => { if (p is long id) SetEditing(_editingTagId == id ? null : id); });
        OpenTagDetailCommand = new RelayCommand(p => { if (p is long id) OpenTagDetail(id); });
        DeleteTagCommand = new RelayCommand(p => { if (p is long id) DeleteTag(id); });
        CardClickCommand = new RelayCommand(p => { if (p is int cid) CardClick(cid); });
        OpenComboDetailCommand = new RelayCommand(p => { if (p is long id) OpenComboDetail(id); });
        ManageComboCommand = new RelayCommand(p => ManageCombo(p as long?));
        DeleteComboTypeCommand = new RelayCommand(p => { if (p is long id) { _ctx.Store?.DeleteComboType(id); RefreshAgg(); } });
        RefreshAll();
    }

    private async Task CopyDbUrlAsync()
    {
        if (WriteClipboardAsync == null) return;
        await WriteClipboardAsync(DbUrl);
        SetStatus("已复制官方卡库地址 " + DbUrl);
    }

    public bool ShowDetail
    {
        get => _showDetail;
        private set
        {
            if (Set(ref _showDetail, value)) Raise(nameof(ShowList));
        }
    }

    public bool ShowList => !_showDetail;
    public bool HasTiles => Tiles.Count > 1;

    public string ListMeta
    {
        get => _listMeta;
        private set => Set(ref _listMeta, value);
    }

    public string DetailName
    {
        get => _detailName;
        private set => Set(ref _detailName, value);
    }

    public string DetailComp
    {
        get => _detailComp;
        private set => Set(ref _detailComp, value);
    }

    public string SampleText
    {
        get => _sampleText;
        private set => Set(ref _sampleText, value);
    }

    public string SampleSplit
    {
        get => _sampleSplit;
        private set => Set(ref _sampleSplit, value);
    }

    public string MainMeta
    {
        get => _mainMeta;
        private set => Set(ref _mainMeta, value);
    }

    public string ExtraMeta
    {
        get => _extraMeta;
        private set => Set(ref _extraMeta, value);
    }

    public SessionItem? SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (_loading || !Set(ref _selectedScope, value)) return;
            if (_detailId is long id) LoadCells(id);
            RefreshAgg();
        }
    }

    // ===== P13 聚合标注 / 组合 =====

    public long? EditingTagId => _editingTagId;
    public bool IsEditing => _editingTagId != null;
    public string EditingTagName => Tags.FirstOrDefault(t => t.Id == _editingTagId)?.Name ?? "";

    public string NewComboName
    {
        get => _newComboName;
        set => Set(ref _newComboName, value);
    }

    public string NewTagName
    {
        get => _newTagName;
        set => Set(ref _newTagName, value);
    }

    private IReadOnlyList<SnapshotRow> ScopeSnaps()
    {
        var store = _ctx.Store;
        if (store == null || _detailId is not long id) return new List<SnapshotRow>();
        var sessionId = _selectedScope is { Id: > 0 } s ? s.Id : (long?)null;
        return store.ListSnapshots(sessionId, id);
    }

    private int TagCopies(IReadOnlyList<int> cids)
    {
        if (_ctx.Store == null || _detailId is not long id) return 0;
        var set = new HashSet<int>(cids);
        return _ctx.Store.GetDeckCards(id).Where(c => c.Section == "Main" && c.Cid != null && set.Contains(c.Cid.Value)).Sum(c => c.Count);
    }

    private Dictionary<int, int> MainCopies()
    {
        var d = new Dictionary<int, int>();
        if (_ctx.Store == null || _detailId is not long id) return d;
        foreach (var c in _ctx.Store.GetDeckCards(id))
            if (c.Section == "Main" && c.Cid != null) d[c.Cid.Value] = d.GetValueOrDefault(c.Cid.Value) + c.Count;
        return d;
    }

    private int MainTotal()
    {
        if (_ctx.Store == null || _detailId is not long id) return 0;
        return Sum(_ctx.Store.GetDeckCards(id), "Main");
    }

    public void RefreshAgg()
    {
        var store = _ctx.Store;
        if (store == null || _detailId is not long id)
        {
            Tags.Clear();
            ComboTypes.Clear();
            return;
        }
        var snaps = ScopeSnaps();
        var mainTotal = MainTotal();
        Tags.Clear();
        foreach (var t in store.ListTags(id))
        {
            var agg = DeckAgg.TagStats(snaps, t.Cids);
            var copies = TagCopies(t.Cids);
            var chip = new TagChipViewModel { Id = t.Id, Name = t.Name, CardCount = t.Cids.Count, CopyCount = copies };
            FillChip(chip, agg, mainTotal, copies);
            chip.IsEditing = t.Id == _editingTagId;
            Tags.Add(chip);
        }
        ComboTypes.Clear();
        var copiesDict = MainCopies();
        foreach (var ct in store.ListComboTypes(id))
        {
            var specs = ct.Entries.Select(e => new DeckAgg.EntrySpec(e.Cids, e.OptCids)).ToList();
            var agg = DeckAgg.ComboStats(snaps, specs);
            var theory = DeckAgg.ComboTheory(mainTotal, copiesDict, specs, _ctx.Cfg.Slots);
            var chip = new ComboTypeChipViewModel { Id = ct.Id, Name = ct.Name, EntryCount = ct.Entries.Count };
            chip.TheoText = theory == null ? "—" : $"{theory:0.0}%";
            chip.TotalText = Fmt(agg.Total);
            chip.FirstText = Fmt(agg.First);
            chip.SecondText = Fmt(agg.Second);
            chip.TotalBrush = ComboBrush;
            chip.FirstBrush = ComboBrush;
            chip.SecondBrush = ComboBrush;
            ComboTypes.Add(chip);
        }
        UpdateCellEditHits();
        Raise(nameof(IsEditing));
        Raise(nameof(EditingTagName));
    }

    private void FillChip(TagChipViewModel chip, DeckAgg.AggResult agg, int mainTotal, int copies)
    {
        chip.TheoText = $"{HandStats.Theoretical(mainTotal, copies, _ctx.Cfg.Slots):0.0}%";
        chip.TotalText = Fmt(agg.Total);
        chip.MeanText = agg.Total.Mean == null ? "-" : $"{agg.Total.Mean:0.00} 张";
        var theoPct = HandStats.Theoretical(mainTotal, copies, _ctx.Cfg.Slots);
        chip.TotalBrush = RateBrush(agg.Total, theoPct);
    }

    private static string Fmt(DeckAgg.Bucket b) =>
        b.Hands == 0 ? "-" : $"{b.Hits}/{b.Hands} {b.Rate:0.0}%";

    private static IBrush RateBrush(DeckAgg.Bucket b, double theoPct)
    {
        if (b.Hands == 0 || b.Rate == null) return new SolidColorBrush(Color.Parse("#5C6579"));
        var mark = HandStats.Mark(b.Rate.Value, theoPct);
        return mark == "↑" ? new SolidColorBrush(Color.Parse("#46C08A"))
            : mark == "↓" ? new SolidColorBrush(Color.Parse("#E63946"))
            : new SolidColorBrush(Color.Parse("#5C6579"));
    }

    private static readonly IBrush ComboBrush = new SolidColorBrush(Color.Parse("#CFD6E4"));

    private void UpdateCellEditHits()
    {
        var store = _ctx.Store;
        if (store == null || _detailId is not long id) return;
        var editing = store.ListTags(id).FirstOrDefault(t => t.Id == _editingTagId);
        foreach (var cell in MainCells)
            cell.EditHit = cell.Cid != null && editing != null && editing.Cids.Contains(cell.Cid.Value);
    }

    private void SetEditing(long? id)
    {
        _editingTagId = id;
        foreach (var t in Tags) t.IsEditing = t.Id == id;
        UpdateCellEditHits();
        Raise(nameof(IsEditing));
        Raise(nameof(EditingTagName));
        SetStatus(id == null
            ? "已退出属性编辑"
            : $"编辑「{EditingTagName}」：连点卡牌加入/移除该属性，再点该属性或「退出编辑」结束");
    }

    private void AddTag()
    {
        var name = NewTagName.Trim();
        if (name.Length == 0 || _ctx.Store == null || _detailId is not long id) return;
        _ctx.Store.AddTag(id, name);
        NewTagName = "";
        RefreshAgg();
        SetStatus($"已添加类型「{name}」，点它进入编辑后连点卡牌标注");
    }

    private void AddComboType()
    {
        var name = NewComboName.Trim();
        if (name.Length == 0 || _ctx.Store == null || _detailId is not long id) return;
        _ctx.Store.AddComboType(id, name);
        NewComboName = "";
        RefreshAgg();
        SetStatus($"已添加组合类型「{name}」，点它编辑条目");
    }

    private void DeleteTag(long id)
    {
        if (_editingTagId == id) SetEditing(null);
        _ctx.Store?.DeleteTag(id);
        RefreshAgg();
    }

    private void CardClick(int cid)
    {
        if (_editingTagId is not long tagId || _ctx.Store == null) return;
        var added = _ctx.Store.ToggleTagCard(tagId, cid);
        RefreshAgg();
        SetStatus($"「{EditingTagName}」{(added ? "加入" : "移除")}该卡；继续连点或退出编辑");
    }

    private void OpenTagDetail(long id)
    {
        var store = _ctx.Store;
        if (store == null || _detailId is not long deckId) return;
        var tag = store.ListTags(deckId).FirstOrDefault(t => t.Id == id);
        if (tag == null) return;
        var snaps = ScopeSnaps();
        var agg = DeckAgg.TagStats(snaps, tag.Cids);
        var mainTotal = MainTotal();
        var copies = TagCopies(tag.Cids);
        var vm = new TagDetailViewModel
        {
            Title = $"类型「{tag.Name}」· 上手张数分布",
            TheoText = $"超几何≥1 {HandStats.Theoretical(mainTotal, copies, _ctx.Cfg.Slots):0.0}% · 期望 {DeckAgg.Expected(mainTotal, copies, _ctx.Cfg.Slots):0.00} 张 · 标注 {tag.Cids.Count} 种 / {copies} 张",
            SummaryText = $"合 {Fmt(agg.Total)} · 先 {Fmt(agg.First)} · 后 {Fmt(agg.Second)}",
            MeanText = $"实际平均每局上手：合 {Mean(agg.Total)} · 先 {Mean(agg.First)} · 后 {Mean(agg.Second)}",
        };
        for (var k = 0; k <= 5; k++)
        {
            vm.Rows.Add(new DistRowViewModel
            {
                K = k,
                TotalText = DistText(agg.Total, k),
                FirstText = DistText(agg.First, k),
                SecondText = DistText(agg.Second, k),
            });
        }
        vm.Done += () => TagDetailChanged?.Invoke(null);
        vm.CloseCommand = new RelayCommand(vm.Close);
        TagDetailChanged?.Invoke(vm);
    }

    private static string Mean(DeckAgg.Bucket b) => b.Mean == null ? "-" : $"{b.Mean:0.00} 张";

    private static string DistText(DeckAgg.Bucket b, int k) =>
        b.Hands == 0 ? "-" : $"{b.Dist[k]} 局 ({100.0 * b.Dist[k] / b.Hands:0.0}%)";

    private void OpenComboDetail(long id)
    {
        var store = _ctx.Store;
        if (store == null || _detailId is not long deckId) return;
        var ct = store.ListComboTypes(deckId).FirstOrDefault(t => t.Id == id);
        if (ct == null) return;
        var snaps = ScopeSnaps();
        var specs = ct.Entries.Select(e => new DeckAgg.EntrySpec(e.Cids, e.OptCids)).ToList();
        var agg = DeckAgg.ComboStats(snaps, specs);
        var copiesDict = MainCopies();
        var theory = DeckAgg.ComboTheory(MainTotal(), copiesDict, specs, _ctx.Cfg.Slots);
        var vm = new ComboDetailViewModel
        {
            Title = $"组合「{ct.Name}」· 任一条目成立即上手",
            SummaryText = $"合 {Fmt(agg.Total)} · 先 {Fmt(agg.First)} · 后 {Fmt(agg.Second)}",
            TheoText = theory == null ? "理论：—（条目过多）" : $"理论（任一条目成立）：{theory:0.0}%",
        };
        foreach (var e in ct.Entries)
        {
            var spec = new DeckAgg.EntrySpec(e.Cids, e.OptCids);
            var ea = DeckAgg.EntryStats(snaps, spec);
            var et = DeckAgg.EntryTheory(MainTotal(), copiesDict, spec, _ctx.Cfg.Slots);
            var names = e.Cids.Select(c => _ctx.Db.TryByCid(c, out var card) ? card.DisplayName : $"cid{c}").ToList();
            var optNames = e.OptCids.Select(c => _ctx.Db.TryByCid(c, out var card) ? card.DisplayName : $"cid{c}").ToList();
            var cardsText = string.Join(" + ", names);
            if (optNames.Count > 0) cardsText += " + 任选[" + string.Join("·", optNames) + "]";
            vm.Entries.Add(new ComboEntryStatViewModel
            {
                Name = e.Name.Length > 0 ? e.Name : cardsText,
                CardsText = cardsText,
                TheoText = et == null ? "—" : $"{et:0.0}%",
                TotalBrush = ComboBrush,
                FirstBrush = ComboBrush,
                SecondBrush = ComboBrush,
            });
        }
        vm.Done += () => ComboDetailChanged?.Invoke(null);
        vm.CloseCommand = new RelayCommand(vm.Close);
        ComboDetailChanged?.Invoke(vm);
    }

    private void ManageCombo(long? preselect)
    {
        var store = _ctx.Store;
        if (store == null || _detailId is not long deckId) return;
        var vm = new ComboManageViewModel();
        vm.CreateType = name => store.AddComboType(deckId, name);
        vm.RemoveType = tid => store.DeleteComboType(tid);
        vm.CreateEntry = (tid, name, req, opt) => store.AddComboEntry(tid, name, req, opt);
        vm.RemoveEntry = eid => store.DeleteComboEntry(eid);
        vm.ReloadEntries = tid =>
        {
            vm.Entries.Clear();
            var ct = store.ListComboTypes(deckId).FirstOrDefault(t => t.Id == tid);
            if (ct == null) return;
            foreach (var e in ct.Entries)
            {
                var names = e.Cids.Select(c => _ctx.Db.TryByCid(c, out var card) ? card.DisplayName : $"cid{c}").ToList();
                var optNames = e.OptCids.Select(c => _ctx.Db.TryByCid(c, out var card) ? card.DisplayName : $"cid{c}").ToList();
                var cardsText = string.Join(" + ", names);
                if (optNames.Count > 0) cardsText += " + 任选[" + string.Join("·", optNames) + "]";
                vm.Entries.Add(new ComboEntryItem { Id = e.Id, Name = e.Name.Length > 0 ? e.Name : cardsText, CardsText = cardsText });
            }
        };
        vm.ReqCards.Clear();
        vm.OptCards.Clear();
        foreach (var c in store.GetDeckCards(deckId).Where(c => c.Section == "Main" && c.Cid != null))
        {
            var name = _ctx.Db.TryByCid(c.Cid.Value, out var card) ? card.DisplayName : c.RawName;
            vm.ReqCards.Add(new PickCardItem { Cid = c.Cid!.Value, Name = name });
            vm.OptCards.Add(new PickCardItem { Cid = c.Cid!.Value, Name = name });
        }
        RebuildManageTypes(vm);
        vm.Changed += () => { RefreshAgg(); RebuildManageTypes(vm); };
        vm.Done += () => ComboManageChanged?.Invoke(null);
        ComboManageChanged?.Invoke(vm);
        if (preselect is long pid) vm.SelectTypeCommand.Execute(pid);
    }

    private void RebuildManageTypes(ComboManageViewModel vm)
    {
        var keep = vm.SelectedType?.Id;
        vm.Types.Clear();
        if (_ctx.Store == null || _detailId is not long deckId) return;
        foreach (var t in _ctx.Store.ListComboTypes(deckId))
            vm.Types.Add(new ComboTypeItem { Id = t.Id, Name = t.Name, IsSelected = t.Id == keep });
        vm.SelectedType = vm.Types.FirstOrDefault(t => t.Id == keep) ?? vm.Types.FirstOrDefault();
        if (vm.SelectedType != null) vm.ReloadEntries?.Invoke(vm.SelectedType.Id);
    }

    public void RefreshOnTab() => RefreshAll();

    public void RefreshAll()
    {
        var store = _ctx.Store;
        if (store == null)
        {
            SetStatus(_ctx.StoreError ?? "记录库不可用");
            return;
        }
        if (_showDetail && _detailId is long keep && store.ListDecks().Any(d => d.Id == keep))
        {
            LoadDetail(keep);
            return;
        }
        ShowDetail = false;
        _detailId = null;
        Tiles.Clear();
        foreach (var d in store.ListDecks())
        {
            var cards = store.GetDeckCards(d.Id);
            var stats = HandStats.Compute(store.ListSnapshots(null, d.Id));
            var tile = new DeckTileViewModel
            {
                Id = d.Id,
                Name = d.Name,
                CountText = $"{d.TotalCards} 张",
                CompText = $"主 {Sum(cards, "Main")} · 额外 {Sum(cards, "Extra")}",
                SampleText = stats.Total == 0 ? "0 局" : $"{stats.Total} 局（先 {stats.First} / 后 {stats.Second}）",
                ImportedAt = d.ImportedAt.Length >= 16 ? d.ImportedAt[..16] : d.ImportedAt,
            };
            Tiles.Add(tile);
            if (d.CoverCid is long cover) _ = LoadCoverAsync(tile, cover);
        }
        Tiles.Add(new DeckTileViewModel { Id = -1, Name = "导入新卡组", IsImport = true });
        Raise(nameof(HasTiles));
        ListMeta = $"{Tiles.Count - 1} 个卡组";
        SetStatus($"共 {Tiles.Count - 1} 个卡组");
    }

    public void OpenDeck(long id)
    {
        _editingTagId = null;
        _detailId = id;
        ShowDetail = true;
        LoadDetail(id);
    }

    private void Back()
    {
        _editingTagId = null;
        _detailId = null;
        ShowDetail = false;
        RefreshAll();
    }

    private void LoadDetail(long id)
    {
        var store = _ctx.Store;
        var deck = store?.ListDecks().FirstOrDefault(d => d.Id == id);
        if (store == null || deck == null)
        {
            Back();
            return;
        }
        var cards = store.GetDeckCards(id);
        DetailName = deck.Name;
        DetailComp = $"{deck.TotalCards} 张 · 主 {Sum(cards, "Main")} / 额外 {Sum(cards, "Extra")} · 导入 {(deck.ImportedAt.Length >= 16 ? deck.ImportedAt[..16] : deck.ImportedAt)}";
        _loading = true;
        var keepScope = _selectedScope?.Id ?? 0;
        Scopes.Clear();
        Scopes.Add(new SessionItem(0, "全部会话合并"));
        foreach (var s in store.ListSessions(id)) Scopes.Add(new SessionItem(s.Id, s.Note));
        _selectedScope = Scopes.FirstOrDefault(s => s.Id == keepScope) ?? Scopes[0];
        _loading = false;
        Raise(nameof(SelectedScope));
        LoadCells(id);
        RefreshAgg();
    }

    private void LoadCells(long id)
    {
        var store = _ctx.Store;
        if (store == null) return;
        var cards = store.GetDeckCards(id);
        var mainTotal = Sum(cards, "Main");
        var sessionId = _selectedScope is { Id: > 0 } s ? s.Id : (long?)null;
        var stats = HandStats.Compute(store.ListSnapshots(sessionId, id));
        MainCells.Clear();
        ExtraCells.Clear();
        foreach (var c in cards)
        {
            if (c.Section == "Side") continue;
            var isExtra = c.Section == "Extra";
            var name = c.Cid != null && _ctx.Db.TryByCid(c.Cid.Value, out var card) ? card.DisplayName : c.RawName;
            double? theo = isExtra ? null : HandStats.Theoretical(mainTotal, c.Count, _ctx.Cfg.Slots);
            var (tc, tr, tm) = Bucket(stats, c.Cid, "", theo, isExtra);
            var (fc, fr, fm) = Bucket(stats, c.Cid, "先手", theo, isExtra);
            var (sc, sr, sm) = Bucket(stats, c.Cid, "后手", theo, isExtra);
            var cell = new DeckCardCellViewModel
            {
                Cid = c.Cid,
                Name = name,
                CountText = $"×{c.Count}",
                TheoText = theo != null ? $"{theo:0.0}%" : "—",
                TotalCount = tc,
                TotalRate = tr,
                TotalMark = tm,
                FirstCount = fc,
                FirstRate = fr,
                FirstMark = fm,
                SecondCount = sc,
                SecondRate = sr,
                SecondMark = sm,
                Miss = c.Cid == null,
                IsExtra = isExtra,
            };
            (isExtra ? ExtraCells : MainCells).Add(cell);
            if (c.Cid != null) _ = LoadCellThumbAsync(cell, c.Cid.Value);
        }
        MainMeta = $"{mainTotal} 张 · {MainCells.Count} 种";
        ExtraMeta = $"{Sum(cards, "Extra")} 张 · {ExtraCells.Count} 种";
        SampleText = $"{stats.Total} 局";
        SampleSplit = $"先{stats.First}/后{stats.Second}";
        var scopeName = _selectedScope == null || _selectedScope.Id == 0 ? "全部会话合并" : _selectedScope.Note;
        SetStatus($"{DetailName} · 上手率口径：{scopeName} · 样本 {stats.Total} 局（先 {stats.First} / 后 {stats.Second}）");
    }

    private static (string Count, string Rate, string Mark) Bucket(DeckStats stats, int? cid, string bucket, double? theo, bool isExtra)
    {
        if (isExtra || cid == null) return ("—", "—", "");
        var n = stats.Count(bucket);
        var rate = stats.Rate(cid.Value, bucket);
        return ($"{stats.Hits(cid.Value, bucket)}/{n}",
            rate != null ? $"{rate:0.0}%" : "-",
            rate != null && theo != null ? HandStats.Mark(rate.Value, theo.Value) : "");
    }

    private async Task LoadCoverAsync(DeckTileViewModel tile, long cid)
    {
        if (!_ctx.Db.TryByCid((int)cid, out var card)) return;
        var bmp = await _images.GetAsync(card.Id, "!art") ?? await _images.GetAsync(card.Id);
        if (bmp != null) tile.Cover = bmp;
    }

    private async Task LoadCellThumbAsync(DeckCardCellViewModel cell, long cid)
    {
        if (!_ctx.Db.TryByCid((int)cid, out var card)) return;
        var bmp = await _images.GetAsync(card.Id);
        if (bmp != null) cell.Thumb = bmp;
    }

    private async Task DeleteAsync()
    {
        var store = _ctx.Store;
        if (store == null || _detailId is not long id || ConfirmAsync == null) return;
        var deck = store.ListDecks().FirstOrDefault(d => d.Id == id);
        if (deck == null) return;
        if (!await ConfirmAsync("删除卡组", $"删除卡组「{deck.Name}」？已关联的记录会回到未分类。", "删除")) return;
        store.DeleteDeck(id);
        Back();
    }

    private void Import()
    {
        var store = _ctx.Store;
        if (store == null || ReadClipboardAsync == null) return;
        var vm = new DeckImportViewModel(store, _ctx.Db, ReadClipboardAsync);
        vm.Saved += id => _lastSavedId = id;
        vm.Done += () =>
        {
            ImportChanged?.Invoke(null);
            RefreshAll();
            if (_lastSavedId is long nid)
            {
                _lastSavedId = null;
                OpenDeck(nid);
            }
        };
        ImportChanged?.Invoke(vm);
    }

    private static int Sum(IReadOnlyList<DeckCardRow> cards, string sec) => cards.Where(c => c.Section == sec).Sum(c => c.Count);

    private void SetStatus(string text) => Status?.Invoke(text);
}