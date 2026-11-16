using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;

namespace YgoMDStartingHandRecorder.ViewModels;

public sealed class ThumbViewModel : ViewModelBase
{
    private Bitmap? _thumb;

    public bool Miss { get; init; }

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

public sealed class RecordRowViewModel : ViewModelBase
{
    private bool _isChecked;

    public long Id { get; init; }
    public string Time { get; init; } = "";
    public string SessionNote { get; init; } = "";
    public string HandTip { get; init; } = "";
    public bool Fixed { get; init; }
    public int Ms { get; init; }
    public string Turn { get; init; } = "";
    public ObservableCollection<ThumbViewModel> Thumbs { get; } = new();

    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }

    public bool IsFirst => Turn == "先手";
    public bool IsSecond => Turn == "后手";
}