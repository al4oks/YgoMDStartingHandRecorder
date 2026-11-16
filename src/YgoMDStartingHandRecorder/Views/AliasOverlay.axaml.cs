using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using YgoMDStartingHandRecorder.ViewModels;

namespace YgoMDStartingHandRecorder.Views;

public partial class AliasOverlay : UserControl
{
    public AliasOverlay()
    {
        InitializeComponent();
        Focusable = true;
        KeyDown += OnKey;
        Loaded += (_, _) => Focus();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is not AliasViewModel vm) return;
        if (e.Key == Key.Escape) { vm.CloseCommand.Execute(null); e.Handled = true; }
    }
}