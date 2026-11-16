using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace YgoMDStartingHandRecorder.Views;

public partial class MiniWindow : Window
{
    public MiniWindow()
    {
        InitializeComponent();
        TitleBar.PointerPressed += (_, e) => BeginMoveDrag(e);
    }

    private void OnPin(object? sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinBtn.Content = Topmost ? "取消置顶" : "置顶";
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}