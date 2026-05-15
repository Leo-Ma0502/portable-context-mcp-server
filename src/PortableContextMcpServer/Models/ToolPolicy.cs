namespace PortableContextMcpServer.Models;

public sealed class ToolPolicy
{
    public string ToolId { get; init; } = string.Empty;
    public string[] AllowedCategories { get; init; } = Array.Empty<string>();
    public string[] AllowedTags { get; init; } = Array.Empty<string>();

    public bool IsCategoryAllowed(string category)
    {
        return AllowedCategories.Length == 0 || AllowedCategories.Contains(category, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsTagAllowed(string tag)
    {
        return AllowedTags.Length == 0 || AllowedTags.Contains(tag, StringComparer.OrdinalIgnoreCase);
    }
}
