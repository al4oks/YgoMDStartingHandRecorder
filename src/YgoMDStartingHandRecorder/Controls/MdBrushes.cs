using Avalonia;
using Avalonia.Media;

namespace YgoMDStartingHandRecorder.Controls;

// Avalonia 12 的 TileBrush 用 SourceRect/DestinationRect(RelativeRect)，
// 绝对像素平铺在代码里构造最稳妥。
public static class MdBrushes
{
    private static IBrush? _scan;
    private static IBrush? _stripe;

    // 供 XAML x:Static 引用（DataTemplate 内无法代码赋值）。
    public static IBrush ScanlineTile => _scan ??= Scanline();
    public static IBrush StripeTile => _stripe ??= Stripe();

    public static IBrush Scanline() => Tile(1, 5, new GeometryDrawing
    {
        Brush = new SolidColorBrush(Color.Parse("#0D8CAAEE")),
        Geometry = new RectangleGeometry(new Rect(0, 0, 1, 1)),
    });

    public static IBrush Stripe() => Tile(8, 8, new DrawingGroup
    {
        Children =
        {
            new GeometryDrawing
            {
                Brush = new SolidColorBrush(Color.Parse("#101522")),
                Geometry = new RectangleGeometry(new Rect(0, 0, 8, 8)),
            },
            new GeometryDrawing
            {
                Brush = new SolidColorBrush(Color.Parse("#141A29")),
                Geometry = StreamGeometry.Parse("M -2,2 L 2,-2 L 6,-2 L -2,6 Z M 2,10 L 10,2 L 10,6 L 6,10 Z"),
            },
        },
    });

    private static IBrush Tile(double w, double h, Drawing drawing) => new DrawingBrush
    {
        TileMode = TileMode.Tile,
        Stretch = Stretch.None,
        SourceRect = new RelativeRect(0, 0, w, h, RelativeUnit.Absolute),
        DestinationRect = new RelativeRect(0, 0, w, h, RelativeUnit.Absolute),
        Drawing = drawing,
    };
}