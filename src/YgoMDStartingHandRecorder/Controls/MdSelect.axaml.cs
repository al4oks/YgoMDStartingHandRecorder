using System.Collections;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace YgoMDStartingHandRecorder.Controls;

public partial class MdSelect : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<MdSelect, string>(nameof(Label), "");

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<MdSelect, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<MdSelect, object?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> GoldProperty =
        AvaloniaProperty.Register<MdSelect, bool>(nameof(Gold));

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public bool Gold { get => GetValue(GoldProperty); set => SetValue(GoldProperty, value); }

    private bool _syncing;

    public MdSelect()
    {
        InitializeComponent();
        LabelProperty.Changed.AddClassHandler<MdSelect>((s, _) =>
        {
            s.LabelText.Text = s.Label;
            s.OpenBtn.SetValue(AutomationProperties.NameProperty, s.Label + "选择");
        });
        GoldProperty.Changed.AddClassHandler<MdSelect>((s, _) => s.ApplyValueColor());
        ItemsSourceProperty.Changed.AddClassHandler<MdSelect>((s, _) => s.List.ItemsSource = s.ItemsSource);
        SelectedItemProperty.Changed.AddClassHandler<MdSelect>((s, _) => s.SyncFromItem());
    }

    private void ApplyValueColor() =>
        ValueText.Foreground = Gold ? (IBrush)Application.Current!.FindResource("GoldBrush")!
                                    : (IBrush)Application.Current!.FindResource("TextBrush")!;

    private void SyncFromItem()
    {
        if (_syncing) return;
        _syncing = true;
        ValueText.Text = SelectedItem?.ToString() ?? "—";
        if (List.SelectedItem != SelectedItem) List.SelectedItem = SelectedItem;
        _syncing = false;
    }

    private void OnOpen(object? sender, RoutedEventArgs e)
    {
        Drop.MinWidth = Box.Bounds.Width;
        _syncing = true;
        List.SelectedItem = SelectedItem;
        _syncing = false;
        Drop.IsOpen = true;
    }

    private void OnPick(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        if (List.SelectedItem == null) return;
        _syncing = true;
        SelectedItem = List.SelectedItem;
        ValueText.Text = SelectedItem?.ToString() ?? "—";
        _syncing = false;
        Drop.IsOpen = false;
    }
}