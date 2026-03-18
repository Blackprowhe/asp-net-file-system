namespace TestApp.Models;

public class FileContentResult
{
    public byte[] Bytes { get; set; } = [];
    public string ContentType { get; set; } = "application/octet-stream";
    public string Name { get; set; } = "";
}