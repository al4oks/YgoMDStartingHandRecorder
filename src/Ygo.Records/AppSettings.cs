using System.Text.Json;

namespace Ygo.Records;

// 运行时存储设置（settings.json，落在 root/exe 目录）：起手截图与卡片数据两个可改路径。
// 环境变量 YGO_AUTOSAVE / YGO_CARD_DATA 优先（自动化隔离用）。
public sealed class AppSettings
{
    public string? CapturesDir { get; set; }
    public string? CardDataDir { get; set; }

    public static string PathFor(string root) => Path.Combine(root, "settings.json");

    public static AppSettings Load(string root)
    {
        try
        {
            var p = PathFor(root);
            if (File.Exists(p)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(p),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
        }
        catch (Exception)
        {
            // 设置损坏时回退默认路径，不阻断启动
        }
        return new AppSettings();
    }

    public void Save(string root) =>
        File.WriteAllText(PathFor(root), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    // P17：默认落系统文档目录 Documents\YgoHand\{captures,carddata};
    // 开发回退：repo 内已有 data/cards.json 时卡表仍用 repo/data（离线开发/核验）。
    public static string DefaultBase() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "YgoHand");

    public string Captures(string root) =>
        Environment.GetEnvironmentVariable("YGO_AUTOSAVE")
        ?? (CapturesDir is { Length: > 0 } c ? c : Path.Combine(DefaultBase(), "captures"));

    public string CardData(string root) =>
        Environment.GetEnvironmentVariable("YGO_CARD_DATA")
        ?? (CardDataDir is { Length: > 0 } d ? d
            : File.Exists(Path.Combine(root, "data", "cards.json")) ? Path.Combine(root, "data")
            : Path.Combine(DefaultBase(), "carddata"));
}
