using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using YgoMDStartingHandRecorder.ViewModels;

namespace YgoMDStartingHandRecorder.Views;

public partial class CardPickOverlay : UserControl
{
    public CardPickOverlay()
    {
        InitializeComponent();
        Focusable = true;
        // 隧道阶段 intercept：候选 ListBox 聚焦时会吞掉 Enter（WinForms AcceptButton 语义要求任意焦点下 Enter=确定）
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Loaded += (_, _) => Focus();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CardPickViewModel vm) return;
        if (e.Key == Key.Escape) { vm.CancelCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Enter) { vm.Confirm(); e.Handled = true; }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is CardPickViewModel vm) vm.Confirm();
    }
}