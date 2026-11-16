using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace YgoMDStartingHandRecorder.Controls;

// MD 切角面板/按钮：随尺寸重算 Clip。Slant=true 时为 tab 的平行四边形斜切。
public class CutBorder : Border
{
    public static readonly StyledProperty<double> CutProperty =
        AvaloniaProperty.Register<CutBorder, double>(nameof(Cut), 8);

    public static readonly StyledProperty<bool> SlantProperty =
        AvaloniaProperty.Register<CutBorder, bool>(nameof(Slant), false);

    public double Cut
    {
        get => GetValue(CutProperty);
        set => SetValue(CutProperty, value);
    }

    public bool Slant
    {
        get => GetValue(SlantProperty);
        set => SetValue(SlantProperty, value);
    }

    public CutBorder()
    {
        SizeChanged += (_, _) => ApplyClip();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CutProperty || change.Property == SlantProperty) ApplyClip();
    }

    private void ApplyClip()
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var c = Math.Min(Cut, Math.Min(w, h) / 2);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(c, 0), true);
            ctx.LineTo(new Point(w, 0));
            if (Slant)
            {
                ctx.LineTo(new Point(w - c, h));
                ctx.LineTo(new Point(0, h));
            }
            else
            {
                ctx.LineTo(new Point(w, h - c));
                ctx.LineTo(new Point(w - c, h));
                ctx.LineTo(new Point(0, h));
                ctx.LineTo(new Point(0, c));
            }
            ctx.EndFigure(true);
        }
        Clip = geo;
    }
}