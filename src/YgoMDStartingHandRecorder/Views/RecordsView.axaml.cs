using Avalonia.Controls;
using Avalonia.Interactivity;
using YgoMDStartingHandRecorder.ViewModels;

namespace YgoMDStartingHandRecorder.Views;

public partial class RecordsView : UserControl
{
    public RecordsView()
    {
        InitializeComponent();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not RecordsViewModel vm) return;
        var ids = RowList.SelectedItems?.OfType<RecordRowViewModel>().Select(r => r.Id).ToList() ?? new List<long>();
        var current = e.AddedItems.OfType<RecordRowViewModel>().LastOrDefault()?.Id
                      ?? ids.LastOrDefault();
        vm.SetSelection(ids, ids.Count == 0 ? null : current);
    }
}