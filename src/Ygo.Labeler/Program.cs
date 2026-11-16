using Ygo.CardLib;

namespace Ygo.Labeler;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var root = args.Length > 0 ? args[0] : DataRoot.Resolve();
        if (root == null || !File.Exists(Path.Combine(root, "data", "cards.json")))
        {
            MessageBox.Show(
                "找不到 data/cards.json。先运行 python tools/fetch_cards.py，或以仓库根目录作为第一个参数启动。",
                "Ygo.Labeler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        var cfg = HandConfig.Load(Path.Combine(root, "data", "config.json"));
        var db = CardDatabase.Load(Path.Combine(root, "data", "cards.json"));
        Application.Run(new MainForm(root, cfg, db, args.Length > 1 ? args[1] : null));
    }
}