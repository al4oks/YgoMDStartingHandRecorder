using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ygo.CardLib;

public sealed record Roi(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("w")] double W,
    [property: JsonPropertyName("h")] double H);

public sealed record HandConfig(
    [property: JsonPropertyName("handRoi")] Roi HandRoi,
    [property: JsonPropertyName("upscale")] int Upscale,
    [property: JsonPropertyName("slots")] int Slots,
    [property: JsonPropertyName("labelSeparator")] string LabelSeparator,
    [property: JsonPropertyName("nameSeparator")] string NameSeparator,
    [property: JsonPropertyName("skipPrefix")] string SkipPrefix,
    [property: JsonPropertyName("unknownSlot")] string UnknownSlot)
{
    public static HandConfig Load(string path) => Load(File.OpenRead(path));

    public static HandConfig Load(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<HandConfig>(reader.ReadToEnd())
            ?? throw new InvalidDataException("config.json empty");
    }
}