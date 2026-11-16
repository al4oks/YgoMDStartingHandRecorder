using System.Collections.ObjectModel;
using Ygo.CardLib;
using Ygo.Records;

namespace YgoMDStartingHandRecorder.ViewModels;

public sealed class AliasItemViewModel
{
    public string RawText { get; init; } = "";
    public string Display { get; init; } = "";
}

public sealed class AliasViewModel : ViewModelBase
{
    private readonly RecordStore _store;
    private AliasItemViewModel? _selected;

    public ObservableCollection<AliasItemViewModel> Items { get; } = new();

    public event Action? Done;

    public RelayCommand DeleteCommand { get; }
    public RelayCommand CloseCommand { get; }

    public AliasViewModel(RecordStore store, CardDatabase db)
    {
        _store = store;
        DeleteCommand = new RelayCommand(DeleteSelected, () => _selected != null);
        CloseCommand = new RelayCommand(() => Done?.Invoke());
        foreach (var a in store.ListAliases())
        {
            var name = db.TryByCid(a.CardCid, out var c) ? c.DisplayName : $"cid{a.CardCid}";
            Items.Add(new AliasItemViewModel { RawText = a.RawText, Display = $"{a.RawText} → {name}  (hits {a.Hits})" });
        }
    }

    public AliasItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value)) DeleteCommand.RaiseCanExecuteChanged();
        }
    }

    private void DeleteSelected()
    {
        if (_selected == null) return;
        _store.DeleteAlias(_selected.RawText);
        Items.Remove(_selected);
        Selected = null;
    }
}