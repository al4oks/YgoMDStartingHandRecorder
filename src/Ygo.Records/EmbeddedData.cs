namespace Ygo.Records;

// 仅配置内嵌（ROI 等，190B）;卡表自 P12 起不再内嵌，由 CardDataUpdater 下载。
public static class EmbeddedData
{
    public static Stream Config() => Open("config.json");

    private static Stream Open(string name) =>
        typeof(EmbeddedData).Assembly.GetManifestResourceStream("Ygo.Records.data." + name)
        ?? throw new InvalidDataException("embedded " + name + " missing");
}