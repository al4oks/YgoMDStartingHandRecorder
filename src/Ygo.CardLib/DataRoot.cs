namespace Ygo.CardLib;

public static class DataRoot
{
    public static string? Resolve()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "data", "cards.json")))
                    return dir.FullName;
        }
        return null;
    }
}