namespace Tuss.Server.Models;

public class StoredFile
{
    public string Name { get; set; } = "";
    public string DiskPath { get; set; } = "";
    public string Created { get; set; } = "";
    public string Changed { get; set; } = "";
    public bool IsFile { get; set; } = true;
    public long Bytes { get; set; }
    public string Extension { get; set; } = "";
}

