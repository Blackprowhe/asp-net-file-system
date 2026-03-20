namespace Tuss.Server.Models;

/// <summary>
/// En sparad version av en fil.
/// Varje gång en fil skapas eller ersätts med PUT skapas en ny rad här.
/// </summary>
public class FileVersion
{
    public long   Id         { get; set; }
    public string FileName   { get; set; } = "";
    public int    Version    { get; set; }
    public string DiskPath   { get; set; } = "";
    public string CreatedAt  { get; set; } = "";
    public long   Bytes      { get; set; }
}

