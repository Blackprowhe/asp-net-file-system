namespace Tuss.Server.Models;

public record BulkMoveRequest(string[] paths, string targetFolder);

