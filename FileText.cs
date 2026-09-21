using System.Text.Json.Serialization;

public class FileText
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("class")]
    public string Class { get; set; } = "";

    [JsonPropertyName("textFields")]
    public List<TextField> TextFields { get; set; } = new();

    // Compatibility alias with Go TextAsset.Entries
    [JsonPropertyName("entries")]
    public List<TextField>? Entries
    {
        get => TextFields;
        set
        {
            if (value != null) TextFields = value;
        }
    }
}

public class TextField
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}
