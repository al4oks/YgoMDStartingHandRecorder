using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using YgoMDStartingHandRecorder.ViewModels;

namespace YgoMDStartingHandRecorder.Views;

public partial class CaptureView : UserControl
{
    private CaptureViewModel? _vm;

    public event Action? MiniRequested;

    private void OnMiniWindow(object? sender, RoutedEventArgs e) => MiniRequested?.Invoke();

    public CaptureView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContext;
        PreviewBody.SizeChanged += (_, _) => LayoutRoi();
    }

    private void OnDataContext(object? sender, EventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVm;
        _vm = DataContext as CaptureViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnVm;
            RoiTagEmpty.Text = RoiTagText.Text = $"识别区域 · 手牌 {_vm.SlotCount} 槽";
        }
        LayoutRoi();
    }

    private void OnVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CaptureViewModel.Preview)) LayoutRoi();
    }

    // ROI 配置为全图比例；预览按 Uniform 缩放，需换算到实际显示矩形再叠加。
    private void LayoutRoi()
    {
        LayoutEmpty();
        if (_vm?.Preview == null) return;
        var src = _vm.Preview.PixelSize;
        var body = PreviewBody.Bounds;
        double bw = body.Width - 28, bh = body.Height - 28;
        if (bw <= 0 || bh <= 0 || src.Width <= 0 || src.Height <= 0) return;
        var scale = Math.Min(bw / src.Width, bh / src.Height);
        double dw = src.Width * scale, dh = src.Height * scale;
        double ox = 14 + (bw - dw) / 2, oy = 14 + (bh - dh) / 2;
        var r = _vm.HandRoi;
        double left = ox + r.X * dw, top = oy + r.Y * dh;
        RoiBox.Margin = new Thickness(left, top, 0, 0);
        RoiBox.Width = r.W * dw;
        RoiBox.Height = r.H * dh;
        RoiTag.Margin = new Thickness(left, Math.Max(0, top - 22), 0, 0);
    }

    // 空态示意图按 16:9 等比内接于预览容器，ROI 位置与真实截图一致。
    private void LayoutEmpty()
    {
        var body = PreviewBody.Bounds;
        double bw = body.Width - 28, bh = body.Height - 28;
        if (bw <= 0 || bh <= 0) return;
        var scale = Math.Min(bw / 16.0, bh / 9.0);
        double dw = 16 * scale, dh = 9 * scale;
        EmptyFrame.Margin = new Thickness(14 + (bw - dw) / 2, 14 + (bh - dh) / 2, 0, 0);
        EmptyFrame.Width = dw;
        EmptyFrame.Height = dh;
        double rx = 0.26, ry = 0.83;
        if (_vm != null)
        {
            rx = _vm.HandRoi.X;
            ry = _vm.HandRoi.Y;
        }
        RoiTagEmptyBox.Margin = new Thickness(rx * dw, Math.Max(0, ry * dh - 22), 0, 0);
    }
}