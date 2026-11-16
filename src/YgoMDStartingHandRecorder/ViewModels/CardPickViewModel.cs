using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Ygo.CardLib;
using Ygo.Recognition;
using YgoMDStartingHandRecorder.Services;

namespace YgoMDStartingHandRecorder.ViewModels;

public sealed class PickItemViewModel : ViewModelBase
{
    private Bitmap? _thumb;

    public Card Card { get; init; } = null!;
    public double Sim { get; init; }
    public string Name => Card.DisplayName;
    public string Sub => $"#{Card.Id}";
    public bool ShowSim => Sim > 0;
    public string SimText => Sim.ToString("0.00");
    public IBrush SimBg { get; init; } = null!;
    public IBrush SimBorder { get; init; } = null!;
    public IBrush SimFg { get; init; } = null!;

    public Bitmap? Thumb
    {
        get => _thumb;
        set
        {
            if (Set(ref _thumb, value)) Raise(nameof(HasThumb));
        }
    }

    public bool HasThumb => _thumb != null;
}

public sealed class CardPickViewModel : ViewModelBase
{
    private readonly CardDatabase _db;
    private readonly CardImageCache _images;
    private readonly IReadOnlyList<Candidate> _candidates;
    private string _query = "";
    private PickItemViewModel? _selected;

    public int SlotIndex { get; }
    public string Raw { get; }
    public ObservableCollection<PickItemViewModel> Items { get; } = new();

    public event Action<Card?>? Done;

    public CardPickViewModel(CardDatabase db, CardImageCache images, IReadOnlyList<Candidate> candidates, int slotIndex, string raw)
    {
        _db = db;
        _images = images;
        _candidates = candidates;
        SlotIndex = slotIndex;
        Raw = raw;
        ConfirmCommand = new RelayCommand(() => Done?.Invoke(Selected?.Card));
        CancelCommand = new RelayCommand(() => Done?.Invoke(null));
        _query = raw;
        Fill(raw);
    }

    public string Query
    {
        get => _query;
        set
        {
            if (Set(ref _query, value)) Fill(value);
        }
    }

    public PickItemViewModel? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    public void Confirm() => Done?.Invoke(Selected?.Card);

    private void Fill(string query)
    {
        Items.Clear();
        Selected = null;
        if (query.Trim().Length == 0)
        {
            foreach (var c in _candidates) Items.Add(Make(c.Card, c.Sim));
        }
        else
        {
            foreach (var c in _db.Search(query, 30)) Items.Add(Make(c, 0));
        }
        if (Items.Count > 0) Selected = Items[0];
        foreach (var item in Items) _ = LoadThumbAsync(item);
    }

    private PickItemViewModel Make(Card card, double sim)
    {
        var (bg, border, fg) = SlotViewModel.SimColors(sim);
        return new PickItemViewModel { Card = card, Sim = sim, SimBg = bg, SimBorder = border, SimFg = fg };
    }

    private async Task LoadThumbAsync(PickItemViewModel item)
    {
        var bmp = await _images.GetAsync(item.Card.Id);
        item.Thumb = bmp;
    }
}