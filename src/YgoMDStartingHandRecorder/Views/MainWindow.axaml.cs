using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using YgoMDStartingHandRecorder.Services;
using YgoMDStartingHandRecorder.ViewModels;

namespace YgoMDStartingHandRecorder.Views;

public partial class MainWindow : Window
{
    private readonly RecorderContext _ctx;
    private readonly CaptureViewModel _capture;
    private readonly RecordsViewModel _records;
    private readonly DecksViewModel _decks;
    private readonly StorageViewModel _storage;
    private MiniWindow? _mini;
    private int _tab;
    private string _captureStatus = "就绪";
    private string _capturePath = "";
    private string _recordsStatus = "";
    private string _decksStatus = "";
    private string _storageStatus = "";

    public MainWindow(string root)
    {
        InitializeComponent();
        ScanOverlay.Fill = Controls.MdBrushes.Scanline();
        PropertyChanged += OnWinPropertyChanged;

        _ctx = new RecorderContext(root, App.Embedded);
        var images = new CardImageCache(_ctx.CardImagesDir);
        _capture = new CaptureViewModel(_ctx, images);
        ViewCapture.DataContext = _capture;
        _capture.Status += (text, path) =>
        {
            _captureStatus = text;
            _capturePath = path ?? "";
            if (_tab == 0) ApplyStatus();
        };
        ViewCapture.MiniRequested += ToggleMini;
        _capture.Session += ApplySession;
        ApplySession(_capture.CurrentSessionName, _capture.SessionPending);
        _capture.CacheChanged += UpdateCache;
        _capture.PickChanged += OnPick;

        _records = new RecordsViewModel(_ctx, images);
        ViewRecords.DataContext = _records;
        _records.Status += text =>
        {
            _recordsStatus = text;
            if (_tab == 1) ApplyStatus();
        };
        _records.CacheChanged += UpdateCache;
        _records.PickChanged += OnPick;
        _records.AliasChanged += OnAlias;
        _records.ConfirmAsync = ShowConfirm;
        _records.SaveFileAsync = PickSaveFile;

        _decks = new DecksViewModel(_ctx, images);
        ViewDecks.DataContext = _decks;
        _decks.Status += text =>
        {
            _decksStatus = text;
            if (_tab == 2) ApplyStatus();
        };
        _decks.ConfirmAsync = ShowConfirm;
        _decks.ImportChanged += OnImport;
        _decks.TagDetailChanged += vm => SetOverlay(vm == null ? null : new TagDetailOverlay { DataContext = vm });
        _decks.ComboDetailChanged += vm => SetOverlay(vm == null ? null : new ComboDetailOverlay { DataContext = vm });
        _decks.ComboManageChanged += vm => SetOverlay(vm == null ? null : new ComboManageOverlay { DataContext = vm });
        _decks.ReadClipboardAsync = async () => await Clipboard!.TryGetTextAsync();
        _decks.WriteClipboardAsync = async t => await Clipboard!.SetTextAsync(t);

        _storage = new StorageViewModel(_ctx, images);
        ViewStorage.DataContext = _storage;
        _storage.Status += text =>
        {
            _storageStatus = text;
            if (_tab == 3) ApplyStatus();
        };
        _storage.PickFolderAsync = PickFolder;
        _storage.CacheChanged += UpdateCache;

        UpdateCache();
        Closed += (_, _) =>
        {
            _mini?.Close();
            _ctx.Dispose();
        };
        SelectTab(0);
        _ = PromptCardsAsync();
    }

    private void ToggleMini()
    {
        if (_mini != null)
        {
            _mini.Close();
            _mini = null;
            return;
        }
        _mini = new MiniWindow { DataContext = _capture };
        _mini.Closed += (_, _) => _mini = null;
        var wa = Screens.Primary?.WorkingArea;
        if (wa != null)
            _mini.Position = new PixelPoint(wa.Value.X + 12, wa.Value.Y + 120);
        _mini.Show();
    }

    private async Task PromptCardsAsync()
    {
        if (_ctx.HasCards) return;
        var ok = await ShowConfirm("缺少卡片数据",
            "程序不再自带卡表。现在从百鸽数据源下载？（zip 约 2.4MB，解包后约 13.9MB；也可稍后到「存储」页下载）", "下载");
        if (ok)
        {
            SelectTab(3);
            await _storage.UpdateAsync();
        }
    }

    private async Task<string?> PickFolder(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private void ApplySession(string name, bool pending)
    {
        SessionName.Text = name;
        SessionState.Text = pending ? "待入库" : "已入库";
        SessionDot.Fill = (IBrush)Application.Current!.FindResource(pending ? "AmberBrush" : "GreenBrush")!;
    }

    private void ApplyStatus()
    {
        if (_tab == 0)
        {
            StatusText.Text = _captureStatus;
            StatusPath.Text = _capturePath;
            var brush = (IBrush)Application.Current!.FindResource(_capture.HasPending ? "AmberBrush" : "GreenBrush")!;
            StatusDot.Fill = brush;
            StatusText.Foreground = brush;
        }
        else if (_tab == 1)
        {
            StatusText.Text = _recordsStatus;
            StatusPath.Text = "";
        }
        else if (_tab == 2)
        {
            StatusText.Text = _decksStatus;
            StatusPath.Text = "";
        }
        else if (_tab == 3)
        {
            StatusText.Text = _storageStatus;
            StatusPath.Text = "";
        }
    }

    private void UpdateCache() => StatusCache.Text = ShotCache.Format(ShotCache.Stats(ShotCache.Dir(_ctx.Root)));

    private void OnPick(CardPickViewModel? pick)
    {
        if (pick == null)
        {
            OverlayHost.IsVisible = false;
            OverlayHost.Content = null;
        }
        else
        {
            OverlayHost.Content = new CardPickOverlay { DataContext = pick };
            OverlayHost.IsVisible = true;
        }
    }

    private void OnAlias(AliasViewModel? alias)
    {
        if (alias == null)
        {
            OverlayHost.IsVisible = false;
            OverlayHost.Content = null;
            return;
        }
        alias.Done += () =>
        {
            OverlayHost.IsVisible = false;
            OverlayHost.Content = null;
        };
        OverlayHost.Content = new AliasOverlay { DataContext = alias };
        OverlayHost.IsVisible = true;
    }

    private void OnImport(DeckImportViewModel? import)
    {
        if (import == null)
        {
            OverlayHost.IsVisible = false;
            OverlayHost.Content = null;
            return;
        }
        OverlayHost.Content = new DeckImportOverlay { DataContext = import };
        OverlayHost.IsVisible = true;
    }

    private void SetOverlay(object? content)
    {
        OverlayHost.Content = content;
        OverlayHost.IsVisible = content != null;
    }

    private Task<bool> ShowConfirm(string title, string message, string okText)
    {
        var tcs = new TaskCompletionSource<bool>();
        var vm = new ConfirmViewModel(title, message, okText);
        vm.Done += result =>
        {
            OverlayHost.IsVisible = false;
            OverlayHost.Content = null;
            tcs.SetResult(result);
        };
        OverlayHost.Content = new ConfirmOverlay { DataContext = vm };
        OverlayHost.IsVisible = true;
        return tcs.Task;
    }

    private async Task<string?> PickSaveFile(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } },
        });
        return file?.TryGetLocalPath();
    }

    private void OnWinPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == OffScreenMarginProperty) Root.Margin = OffScreenMargin;
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnTabCapture(object? sender, RoutedEventArgs e) => SelectTab(0);
    private void OnTabRecords(object? sender, RoutedEventArgs e) => SelectTab(1);
    private void OnTabDecks(object? sender, RoutedEventArgs e) => SelectTab(2);
    private void OnTabStorage(object? sender, RoutedEventArgs e) => SelectTab(3);

    private void SelectTab(int index)
    {
        _tab = index;
        SetOn(TabCapture, index == 0);
        SetOn(TabRecords, index == 1);
        SetOn(TabDecks, index == 2);
        SetOn(TabStorage, index == 3);
        ViewCapture.IsVisible = index == 0;
        ViewRecords.IsVisible = index == 1;
        ViewDecks.IsVisible = index == 2;
        ViewStorage.IsVisible = index == 3;
        SessionChip.IsVisible = index == 0;
        if (index == 0) _capture.RefreshDecksOnTab();
        if (index == 1) _records.RefreshOnTab();
        if (index == 2) _decks.RefreshOnTab();
        if (index == 3) _storage.Refresh();
        ApplyStatus();
    }

    private static void SetOn(Button button, bool on)
    {
        button.Classes.Remove("md-tab-on");
        if (on) button.Classes.Add("md-tab-on");
    }
}