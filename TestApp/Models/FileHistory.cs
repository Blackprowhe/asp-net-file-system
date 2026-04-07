public class FileHistory
{
    public int Id { get; set; }

    public string FilePath { get; set; } = "";

    public int version { get; set; }

    public string Content { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}