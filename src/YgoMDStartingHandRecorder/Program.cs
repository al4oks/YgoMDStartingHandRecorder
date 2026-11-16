using Avalonia;
using Ygo.CardLib;

namespace YgoMDStartingHandRecorder;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var resolved = args.Length > 0 ? args[0] : DataRoot.Resolve();
        App.Embedded = args.Length == 0 && resolved == null;
        var root = resolved ?? Path.GetDirectoryName(Environment.ProcessPath)!;
        App.Root = root;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}