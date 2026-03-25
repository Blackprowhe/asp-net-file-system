using System.Text.Json.Serialization;

namespace TestApp.Models;

public class FileItemDto
{
    [JsonPropertyName("created")]
    public string Created { get; set; } = "";

    [JsonPropertyName("changed")]
    public string Changed { get; set; } = "";

    [JsonPropertyName("file")]
    public bool File { get; set; }

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("extension")]
    public string? Extension { get; set; }

    [JsonPropertyName("content")]
    public Dictionary<string, FileItemDto>? Content { get; set; }
}

public class FileEventDto
{
     [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
}

    