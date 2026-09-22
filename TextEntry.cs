using System.Text.Json.Serialization;

public class TextEntry
{
    [JsonPropertyName("assetname")]
    public string AssetName { get; set; } = "";

    [JsonPropertyName("assetclass")]
    public string AssetClass { get; set; } = "";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}
