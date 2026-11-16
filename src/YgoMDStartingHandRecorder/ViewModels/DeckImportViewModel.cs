using System.Collections.ObjectModel;
using Avalonia.Media;
using Ygo.CardLib;
using Ygo.Records;

namespace YgoMDStartingHandRecorder.ViewModels;

public sealed class ImportRowViewModel
{
    public string SecDisplay { get; init; } = "";
    public bool IsMain { get; init; }
    public string Name { get; init; } = "";
    public string CountText { get; init; } = "";
    public string MatchText { get; init; } = "";
    public bool Miss { get; init; }
    public IBrush NameBrush => Miss ? new SolidColorBrush(Color.Parse("#E63946")) : new SolidColorBrush(Color.Parse("#E9EDF5"));
    public IBrush MatchBrush => Miss ? new SolidColorBrush(Color.Parse("#E63946")) : new SolidColorBrush(Color.Parse("#46C08A"));
    public IBrush SecBg => IsMain ? new SolidColorBrush(Color.Parse("#2EE6C15C")) : new SolidColorBrush(Color.Parse("#29B6C1D3"));
    public IBrush SecFg => IsMain ? new SolidColorBrush(Color.Parse("#E6C15C")) : new SolidColorBrush(Color.Parse("#D5DDEA"));
}

public sealed class DeckImportViewModel : ViewModelBase
{
    private static readonly IBrush StepIdleBg = new SolidColorBrush(Color.Parse("#99181E2E"));
    private static readonly IBrush StepOnBg = new SolidColorBrush(Color.Parse("#29B6C1D3"));
    private static readonly IBrush StepDoneBg = new SolidColorBrush(Color.Parse("#99181E2E"));
    private static readonly IBrush StepIdleFg = new SolidColorBrush(Color.Parse("#8D97AD"));
    private static readonly IBrush StepOnFg = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush StepDoneFg = new SolidColorBrush(Color.Parse("#CFD6E4"));
    private static readonly IBrush NumIdleBg = new SolidColorBrush(Color.Parse("#2A3349"));
    private static readonly IBrush NumOnBg = new SolidColorBrush(Color.Parse("#4C5A73"));
    private static readonly IBrush NumDoneBg = new SolidColorBrush(Color.Parse("#46C08A"));

    private readonly RecordStore _store;
    private readonly CardDatabase _db;
    private readonly Func<Task<string?>> _read;
    private List<DeckCardRow> _rows = new();
    private string _name = "";
    private string _pstatus = "先在浏览器复制 My Deck 卡组页文本，再点「读剪贴板」";
    private string _footer = "";
    private bool _canSave;
    private bool _parsed;

    public ObservableCollection<ImportRowViewModel> Rows { get; } = new();

    public event Action? Done;
    public event Action<long>? Saved;

    public RelayCommand ReadCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CloseCommand { get; }

    public DeckImportViewModel(RecordStore store, CardDatabase db, Func<Task<string?>> read)
    {
        _store = store;
        _db = db;
        _read = read;
        ReadCommand = new RelayCommand(() => _ = ReadAsync());
        SaveCommand = new RelayCommand(Save, () => _canSave);
        CloseCommand = new RelayCommand(() => Done?.Invoke());
    }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string PStatus
    {
        get => _pstatus;
        private set => Set(ref _pstatus, value);
    }

    public string Footer
    {
        get => _footer;
        private set => Set(ref _footer, value);
    }

    public string Step1Num => _parsed ? "✓" : "1";
    public string Step2Num => _parsed ? "✓" : "2";
    public IBrush Step1Bg => _parsed ? StepDoneBg : StepOnBg;
    public IBrush Step2Bg => _parsed ? StepDoneBg : StepIdleBg;
    public IBrush Step3Bg => _parsed ? StepOnBg : StepIdleBg;
    public IBrush Step1Fg => _parsed ? StepDoneFg : StepOnFg;
    public IBrush Step2Fg => _parsed ? StepDoneFg : StepIdleFg;
    public IBrush Step3Fg => _parsed ? StepOnFg : StepIdleFg;
    public IBrush Num1Bg => _parsed ? NumDoneBg : NumOnBg;
    public IBrush Num2Bg => _parsed ? NumDoneBg : NumIdleBg;
    public IBrush Num3Bg => _parsed ? NumOnBg : NumIdleBg;

    private async Task ReadAsync()
    {
        var text = await _read();
        if (string.IsNullOrWhiteSpace(text))
        {
            PStatus = "剪贴板为空：先在浏览器卡组页 Ctrl+A / Ctrl+C";
            return;
        }
        DeckParseResult parsed;
        try
        {
            parsed = DeckTextParser.Parse(text);
        }
        catch (Exception ex)
        {
            PStatus = "解析失败：" + ex.Message;
            SetCanSave(false);
            return;
        }
        _rows = new List<DeckCardRow>();
        Rows.Clear();
        int total = 0, matched = 0;
        foreach (var (sec, display, entries) in new[]
                 {
                     ("Main", "主卡组", parsed.Main), ("Extra", "额外", parsed.Extra), ("Side", "侧卡组", parsed.Side),
                 })
        {
            foreach (var e in entries)
            {
                total += e.Count;
                int? cid = _db.TryExact(e.Name, out var card) ? card.Cid : null;
                if (cid != null) matched += e.Count;
                _rows.Add(new DeckCardRow(sec, cid, e.Name, e.Count));
                Rows.Add(new ImportRowViewModel
                {
                    SecDisplay = display,
                    IsMain = sec == "Main",
                    Name = e.Name,
                    CountText = e.Count.ToString(),
                    MatchText = cid != null ? $"cid {cid}" : "MISS（未命中卡表）",
                    Miss = cid == null,
                });
            }
        }
        if (parsed.Name.Length > 0) Name = parsed.Name;
        PStatus = $"共 {total} 张 · 命中 {matched} 张 · {_rows.Count} 种" + (matched < total ? "（红色为未命中，仍可入库）" : "");
        Footer = $"共 {total} 张 · 命中 {matched} 张 · {_rows.Count} 种";
        SetCanSave(true);
        _parsed = true;
        RaiseSteps();
    }

    private void Save()
    {
        var name = Name.Trim();
        if (name.Length == 0)
        {
            PStatus = "先填写卡组名";
            return;
        }
        var id = _store.AddDeck(name, _rows);
        PStatus = $"已入库卡组「{name}」（{_rows.Sum(r => r.Count)} 张）";
        SetCanSave(false);
        Saved?.Invoke(id);
    }

    private void SetCanSave(bool value)
    {
        _canSave = value;
        SaveCommand.RaiseCanExecuteChanged();
    }

    private void RaiseSteps()
    {
        Raise(nameof(Step1Num));
        Raise(nameof(Step2Num));
        Raise(nameof(Step1Bg));
        Raise(nameof(Step2Bg));
        Raise(nameof(Step3Bg));
        Raise(nameof(Step1Fg));
        Raise(nameof(Step2Fg));
        Raise(nameof(Step3Fg));
        Raise(nameof(Num1Bg));
        Raise(nameof(Num2Bg));
        Raise(nameof(Num3Bg));
    }
}