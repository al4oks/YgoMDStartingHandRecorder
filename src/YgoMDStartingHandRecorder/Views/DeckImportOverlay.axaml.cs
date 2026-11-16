using Avalonia.Controls;
using Avalonia.Input;
using YgoMDStartingHandRecorder.ViewModels;

namespace YgoMDStartingHandRecorder.Views;

public partial class DeckImportOverlay : UserControl
{
    public DeckImportOverlay()
    {
        InitializeComponent();
        Focusable = true;
        KeyDown += OnKeyDown;
        Loaded += (_, _) => Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not DeckImportViewModel vm) return;
        if (e.Key == Key.Escape)
        {
            vm.CloseCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && vm.SaveCommand.CanExecute(null))
        {
            vm.SaveCommand.Execute(null);
            e.Handled = true;
        }
    }
}