using System.Collections.ObjectModel;
using Avalonia.Media;
using Ygo.Records;

namespace YgoMDStartingHandRecorder.ViewModels;

// P13 属性 chip：名称/卡数/理论(可切换超几何≥1或期望)/合先后上手;点击 chip 进编辑态，详情按钮看 0~5 分布。
public sealed class TagChipViewModel : ViewModelBase
{
    private string _theoText = "";
    private string _totalText = "";
    private string _meanText = "";
    private bool _isEditing;

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public int CardCount { get; set; }
    public int CopyCount { get; set; }
    public string CountText => $"{CardCount}种/{CopyCount}张";

    public string TheoText { get => _theoText; set => Set(ref _theoText, value); }
    public string TotalText { get => _totalText; set => Set(ref _totalText, value); }
    public string MeanText { get => _meanText; set => Set(ref _meanText, value); }
    public IBrush TotalBrush { get; set; } = Brushes.Gray;

    public bool IsEditing
    {
        get => _isEditing;
        set { if (Set(ref _isEditing, value)) Raise(nameof(ChipEdge)); }
    }

    public IBrush ChipEdge => IsEditing
        ? new SolidColorBrush(Color.Parse("#B8933A"))
        : new SolidColorBrush(Color.Parse("#28314A"));
}

public sealed class ComboTypeChipViewModel : ViewModelBase
{
    private string _totalText = "";
    private string _firstText = "";
    private string _secondText = "";

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public int EntryCount { get; set; }
    public string TheoText { get; set; } = "";
    public string TotalText { get => _totalText; set => Set(ref _totalText, value); }
    public string FirstText { get => _firstText; set => Set(ref _firstText, value); }
    public string SecondText { get => _secondText; set => Set(ref _secondText, value); }
    public IBrush TotalBrush { get; set; } = Brushes.Gray;
    public IBrush FirstBrush { get; set; } = Brushes.Gray;
    public IBrush SecondBrush { get; set; } = Brushes.Gray;
}

public sealed class DistRowViewModel
{
    public int K { get; init; }
    public string TotalText { get; init; } = "";
    public string FirstText { get; init; } = "";
    public string SecondText { get; init; } = "";
}

public sealed class TagDetailViewModel
{
    public string Title { get; init; } = "";
    public string TheoText { get; init; } = "";
    public string SummaryText { get; init; } = "";
    public string MeanText { get; init; } = "";
    public ObservableCollection<DistRowViewModel> Rows { get; } = new();
    public RelayCommand CloseCommand { get; set; } = new(() => { });
    public event Action? Done;
    public void Close() => Done?.Invoke();
}

public sealed class ComboEntryStatViewModel
{
    public string Name { get; init; } = "";
    public string CardsText { get; init; } = "";
    public string TheoText { get; init; } = "";
    public string TotalText { get; init; } = "";
    public string FirstText { get; init; } = "";
    public string SecondText { get; init; } = "";
    public IBrush TotalBrush { get; init; } = Brushes.Gray;
    public IBrush FirstBrush { get; init; } = Brushes.Gray;
    public IBrush SecondBrush { get; init; } = Brushes.Gray;
}

public sealed class ComboDetailViewModel
{
    public string Title { get; init; } = "";
    public string SummaryText { get; init; } = "";
    public string TheoText { get; init; } = "";
    public ObservableCollection<ComboEntryStatViewModel> Entries { get; } = new();
    public RelayCommand CloseCommand { get; set; } = new(() => { });
    public event Action? Done;
    public void Close() => Done?.Invoke();
}

// 组合管理：左类型（增删/选中），右条目（增删）;新增条目从主卡组卡列表勾选。
public sealed class ComboManageViewModel : ViewModelBase
{
    private string _newTypeName = "";
    private string _newEntryName = "";
    private ComboTypeItem? _selectedType;

    public ComboManageViewModel()
    {
        AddTypeCommand = new RelayCommand(AddType);
        DeleteTypeCommand = new RelayCommand(p => { if (p is long id) DeleteType(id); });
        SelectTypeCommand = new RelayCommand(p => { if (p is long id) SelectType(id); });
        AddEntryCommand = new RelayCommand(AddEntry);
        DeleteEntryCommand = new RelayCommand(p => { if (p is long id) DeleteEntry(id); });
        CloseCommand = new RelayCommand(() => Done?.Invoke());
    }

    public ObservableCollection<ComboTypeItem> Types { get; } = new();
    public ObservableCollection<ComboEntryItem> Entries { get; } = new();
    public ObservableCollection<PickCardItem> ReqCards { get; } = new();
    public ObservableCollection<PickCardItem> OptCards { get; } = new();

    public string NewTypeName { get => _newTypeName; set => Set(ref _newTypeName, value); }
    public string NewEntryName { get => _newEntryName; set => Set(ref _newEntryName, value); }

    public ComboTypeItem? SelectedType
    {
        get => _selectedType;
        set => Set(ref _selectedType, value);
    }

    public RelayCommand AddTypeCommand { get; }
    public RelayCommand DeleteTypeCommand { get; }
    public RelayCommand SelectTypeCommand { get; }
    public RelayCommand AddEntryCommand { get; }
    public RelayCommand DeleteEntryCommand { get; }
    public RelayCommand CloseCommand { get; }

    public event Action? Done;
    public event Action? Changed;

    internal Func<string, long>? CreateType;
    internal Action<long>? RemoveType;
    internal Func<long, string, IReadOnlyList<int>, IReadOnlyList<int>, long>? CreateEntry;
    internal Action<long>? RemoveEntry;
    internal Action<long>? ReloadEntries;

    private void AddType()
    {
        var name = NewTypeName.Trim();
        if (name.Length == 0 || CreateType == null) return;
        var id = CreateType(name);
        NewTypeName = "";
        Changed?.Invoke();
        SelectType(id);
    }

    private void DeleteType(long id)
    {
        RemoveType?.Invoke(id);
        Changed?.Invoke();
    }

    private void SelectType(long id)
    {
        SelectedType = Types.FirstOrDefault(t => t.Id == id);
        ReloadEntries?.Invoke(id);
    }

    private void AddEntry()
    {
        if (SelectedType == null || CreateEntry == null) return;
        var req = ReqCards.Where(c => c.IsChecked).Select(c => c.Cid).ToList();
        var opt = OptCards.Where(c => c.IsChecked).Select(c => c.Cid).ToList();
        if (req.Count == 0) return;
        var name = NewEntryName.Trim();
        if (name.Length == 0)
        {
            name = string.Join(" + ", ReqCards.Where(c => c.IsChecked).Select(c => c.Name));
            if (opt.Count > 0) name += " + 任选[" + string.Join("·", OptCards.Where(c => c.IsChecked).Select(c => c.Name)) + "]";
        }
        CreateEntry(SelectedType.Id, name, req, opt);
        NewEntryName = "";
        foreach (var c in ReqCards) c.IsChecked = false;
        foreach (var c in OptCards) c.IsChecked = false;
        Changed?.Invoke();
        ReloadEntries?.Invoke(SelectedType.Id);
    }

    private void DeleteEntry(long id)
    {
        RemoveEntry?.Invoke(id);
        Changed?.Invoke();
        if (SelectedType != null) ReloadEntries?.Invoke(SelectedType.Id);
    }
}

public sealed class ComboTypeItem : ViewModelBase
{
    private bool _isSelected;
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

public sealed class ComboEntryItem
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string CardsText { get; init; } = "";
}

public sealed class PickCardItem : ViewModelBase
{
    private bool _isChecked;
    public int Cid { get; init; }
    public string Name { get; init; } = "";
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
}