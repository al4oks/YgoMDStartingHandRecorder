using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using YgoMDStartingHandRecorder.Views;

namespace YgoMDStartingHandRecorder;

public partial class App : Application
{
    public static string Root { get; set; } = "";
    public static bool Embedded { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(Root);
        base.OnFrameworkInitializationCompleted();
    }
}