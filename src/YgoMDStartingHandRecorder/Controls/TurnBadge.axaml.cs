using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace YgoMDStartingHandRecorder.Controls;

public partial class TurnBadge : UserControl
{
    public static readonly StyledProperty<string> ZhProperty =
        AvaloniaProperty.Register<TurnBadge, string>(nameof(Zh), "先手");

    public static readonly StyledProperty<string> EnProperty =
        AvaloniaProperty.Register<TurnBadge, string>(nameof(En), "1st");

    public static readonly StyledProperty<bool> FirstProperty =
        AvaloniaProperty.Register<TurnBadge, bool>(nameof(First), true);

    // 尚未截图：不预设先手，显示中性钢银「未定」
    public static readonly StyledProperty<bool> BlankProperty =
        AvaloniaProperty.Register<TurnBadge, bool>(nameof(Blank));

    public string Zh { get => GetValue(ZhProperty); set => SetValue(ZhProperty, value); }
    public string En { get => GetValue(EnProperty); set => SetValue(EnProperty, value); }
    public bool First { get => GetValue(FirstProperty); set => SetValue(FirstProperty, value); }
    public bool Blank { get => GetValue(BlankProperty); set => SetValue(BlankProperty, value); }

    public TurnBadge()
    {
        InitializeComponent();
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ZhProperty || change.Property == EnProperty
            || change.Property == FirstProperty || change.Property == BlankProperty)
            Refresh();
    }

    private void Refresh()
    {
        if (HexPath == null) return;
        ZhText.Text = Blank ? "未定" : Zh;
        EnText.Text = Blank ? "TURN" : En;
        var res = Application.Current!;
        HexPath.Fill = (IBrush)res.FindResource(Blank ? "HeadSteel" : First ? "HeadBlue" : "HeadRed")!;
        HexPath.Stroke = Blank ? (IBrush)res.FindResource("SteelEdgeBrush")! : null;
        HexPath.StrokeThickness = Blank ? 1 : 0;
        ZhText.Foreground = (IBrush)res.FindResource(Blank ? "SteelInkBrush" : "TextBrush")!;
        EnText.Foreground = Blank ? (IBrush)res.FindResource("SteelInkDimBrush")! : new SolidColorBrush(Color.Parse("#CCFFFFFF"));
    }
}