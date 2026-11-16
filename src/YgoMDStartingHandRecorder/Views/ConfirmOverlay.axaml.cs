using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using YgoMDStartingHandRecorder.ViewModels;

namespace YgoMDStartingHandRecorder.Views;

public partial class ConfirmOverlay : UserControl
{
    public ConfirmOverlay()
    {
        InitializeComponent();
        Focusable = true;
        KeyDown += OnKey;
        Loaded += (_, _) => Focus();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ConfirmViewModel vm) return;
        if (e.Key == Key.Escape) { vm.CancelCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Enter) { vm.ConfirmCommand.Execute(null); e.Handled = true; }
    }
}