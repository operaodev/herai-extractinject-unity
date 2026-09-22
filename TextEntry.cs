using System.Text.Json.Serialization;

public class TextEntry
{
    [JsonPropertyName("assetName")]
    public string AssetName { get; set; } = "";

    [JsonPropertyName("assetClass")]
    public string AssetClass { get; set; } = "";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}
