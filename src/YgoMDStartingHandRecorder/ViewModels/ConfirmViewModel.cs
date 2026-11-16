namespace YgoMDStartingHandRecorder.ViewModels;

public sealed class ConfirmViewModel : ViewModelBase
{
    public string Title { get; }
    public string Message { get; }
    public string OkText { get; }

    public event Action<bool>? Done;

    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    public ConfirmViewModel(string title, string message, string okText)
    {
        Title = title;
        Message = message;
        OkText = okText;
        ConfirmCommand = new RelayCommand(() => Done?.Invoke(true));
        CancelCommand = new RelayCommand(() => Done?.Invoke(false));
    }
}