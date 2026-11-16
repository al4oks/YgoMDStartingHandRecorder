using Avalonia.Media;
using Avalonia.Media.Imaging;
using YgoMDStartingHandRecorder.Controls;

namespace YgoMDStartingHandRecorder.ViewModels;

public sealed class SlotViewModel : ViewModelBase
{
    private static readonly IBrush HiBg = new SolidColorBrush(Color.Parse("#2946C08A"));
    private static readonly IBrush HiBorder = new SolidColorBrush(Color.Parse("#7346C08A"));
    private static readonly IBrush HiFg = new SolidColorBrush(Color.Parse("#46C08A"));
    private static readonly IBrush MidBg = new SolidColorBrush(Color.Parse("#24E8A33D"));
    private static readonly IBrush MidBorder = new SolidColorBrush(Color.Parse("#73E8A33D"));
    private static readonly IBrush MidFg = new SolidColorBrush(Color.Parse("#E8A33D"));
    private static readonly IBrush LoBg = new SolidColorBrush(Color.Parse("#24E63946"));
    private static readonly IBrush LoBorder = new SolidColorBrush(Color.Parse("#80E63946"));
    private static readonly IBrush LoFg = new SolidColorBrush(Color.Parse("#E63946"));

    private Bitmap? _thumb;

    public int Index { get; init; }
    public long? CardId { get; init; }
    public string Name { get; init; } = "？";
    public bool Miss { get; init; }
    public string Raw { get; init; } = "";
    public string SimText { get; init; } = "";
    public bool ShowSim { get; init; }
    public IBrush SimBg { get; set; } = HiBg;
    public IBrush SimBorder { get; set; } = HiBorder;
    public IBrush SimFg { get; set; } = HiFg;
    public bool Warn { get; init; }
    public bool Corrected { get; init; }
    public string Stats { get; init; } = "";
    // P16 悬浮窗只展示合计上手率（不含理论/先/后/置信度）
    public string TotalText { get; init; } = "";
    public RelayCommand FixCommand { get; init; } = null!;

    public Bitmap? Thumb
    {
        get => _thumb;
        set
        {
            if (Set(ref _thumb, value)) Raise(nameof(HasThumb));
        }
    }

    public bool HasThumb => _thumb != null;

    public string RawLine => !ShowSim ? "" : Miss ? (Raw.Length > 0 ? $"raw={Raw} · 无匹配卡" : "raw=（空 · OCR 无输出）") : $"raw={Raw}  sim={SimText}";

    public string BadgeText => Miss && Raw.Length > 0 ? "MISS" : SimText;

    public IBrush NameBrush => Miss ? new SolidColorBrush(Color.Parse("#8D97AD")) : new SolidColorBrush(Color.Parse("#FFFFFF"));

    public IBrush EdgeBrush => Miss ? new SolidColorBrush(Color.Parse("#8CE63946")) : Warn ? new SolidColorBrush(Color.Parse("#A6E8A33D")) : new SolidColorBrush(Color.Parse("#28314A"));
    public BoxShadows EdgeShadow => Miss ? BoxShadows.Parse("inset 3 0 0 0 #E63946, 0 0 12 0 #1FE63946") : Warn ? BoxShadows.Parse("inset 3 0 0 0 #E8A33D, 0 0 12 0 #1FE8A33D") : default;
    public IBrush IdxBg => Warn ? new SolidColorBrush(Color.Parse("#E8A33D")) : new SolidColorBrush(Color.Parse("#2A3349"));
    public IBrush IdxFg => Warn ? new SolidColorBrush(Color.Parse("#241A05")) : new SolidColorBrush(Color.Parse("#AEB8CF"));

    public static (IBrush Bg, IBrush Border, IBrush Fg) SimColors(double sim) =>
        sim >= 0.90 ? (HiBg, HiBorder, HiFg) : sim >= 0.50 ? (MidBg, MidBorder, MidFg) : (LoBg, LoBorder, LoFg);
}